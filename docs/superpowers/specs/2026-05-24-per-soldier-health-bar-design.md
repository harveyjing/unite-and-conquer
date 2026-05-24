# Per-soldier health bar — design

## Goal

Render a small floating health bar above every soldier in BattleScene. Each bar
shows the soldier's exact current/max HP, updates per server tick, billboards to
the camera, and uses a green→yellow→red gradient so HP is readable at a glance.

## Constraints inherited from the project

- DOTS-first: no MonoBehaviour-per-soldier, no managed allocations in hot loops.
- Must scale toward the mobile-MMO target (CLAUDE.md): tens of thousands of
  individually simulated soldiers per battle. The v1 implementation needs to be
  benchmarkable at current scale (~100/side) and not paint into a corner at
  10k/side.
- Server is authoritative; clients render. Bars are pure presentation and must
  not exist in the server world.

## Decisions (locked during brainstorming)

| Question | Decision |
|---|---|
| Fidelity | Exact per-tick HP value |
| Rendering | World-space quads via Entities.Graphics |
| Visibility | Always on for visible soldiers |
| Placement | Floating above the head |
| Style | Single quad with shader-driven fill |
| Color | Green → yellow → red gradient on HP% |
| Spawn strategy | Dynamic client-side spawn (no bake into Soldier prefab) |

## Components & replication

Modify `Demo.Health` in [SoldierAuthoring.cs](Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs):

- Change `[GhostComponent(PrefabType = GhostPrefabType.Server)]` to
  `[GhostComponent(PrefabType = GhostPrefabType.All)]`.
- Add `[GhostField]` to `Current` only. `Max` stays a plain field — it's
  constant after spawn and shared by every soldier, so the client doesn't need
  it replicated; `HealthBarUpdateSystem` reads `BattleConfig.MaxHealth`
  instead. (When unit classes ship with diverging per-class max HP, revisit
  and add `[GhostField]` to `Max`.)

Add new components (in a new file `Assets/Scripts/Demo/Battle/Authoring/HealthBarComponents.cs`):

- `HealthBarRef { Entity Bar; }` — added to a soldier on the client when its
  bar is spawned. Never replicated.
- `HealthBarLink { Entity Owner; }` — on a bar entity, points back at the
  soldier.
- `HealthBarFill : IComponentData` with
  `[MaterialProperty("_Health01")] public float Value;` — Entities.Graphics
  material-property override that drives the shader fill.

Bandwidth note: at current scale (≤200 soldiers/side) this is well within
Netcode's budget. At 10k/side we will need to revisit and quantize `Current` to
a `[GhostField(Quantization = ...)]` or a 4-bit packed field. Not in v1.

## Bar asset & shader

**Prefab** `Assets/Prefabs/HealthBar.prefab`:
- Empty GameObject, `MeshFilter` referencing Unity built-in Quad,
  `MeshRenderer` with `HealthBar.mat`.
- `HealthBarAuthoring` MonoBehaviour with a `Baker` that adds
  `HealthBarFill { Value = 1f }`.
- Not a `GhostAuthoringComponent` — purely client-side art.

**Shader** `Assets/Shaders/HealthBar.shader` (URP HLSL, unlit, single pass):
- DOTS instancing enabled, alpha blend.
- **Vertex stage**: camera-facing billboard. Take object origin in world space,
  rebuild the quad basis from camera right/up extracted from `unity_MatrixV`,
  scale by per-shader constants `BarWidth`, `BarHeight`. Output clip position
  and pass through quad-local UV.
- **Fragment stage**: read per-instance `_Health01`. Compute
  `fill = step(uv.x, _Health01)`. Color the filled region with a
  gradient: `mix(red, yellow, saturate(_Health01 * 2))` then
  `mix(prev, green, saturate(_Health01 * 2 - 1))`. Unfilled region is a dark
  gray with reduced alpha for the background plate.

**Material** `HealthBar.mat`: references the shader, GPU instancing on, DOTS
instancing flag on.

**Tunables** added to `BattleConfig` ([BattleConfigAuthoring.cs](Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs)):
- `Entity HealthBarPrefab` (baked from a new
  `BattleConfigAuthoring.HealthBarPrefab : GameObject` field).
- `float HealthBarHeightOffset` (default 1.2, in world units).

`BarWidth` and `BarHeight` live as shader constants for v1 (0.8 and 0.1).

## Systems

Both new systems live in `Assets/Scripts/Demo/Battle/System/`, run in
`PresentationSystemGroup`, and are gated to the client world via
`[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]`. They use
`BeginPresentationEntityCommandBufferSystem` for structural changes.

### `HealthBarSpawnSystem`

- Query: `Soldier`, `LocalTransform`, **without** `HealthBarRef`.
- For each match:
  1. Instantiate `BattleConfig.HealthBarPrefab` via ECB.
  2. On the bar: `Parent { Value = soldier }`,
     `LocalTransform { Position = (0, HealthBarHeightOffset, 0), Rotation = identity, Scale = 1 }`,
     `HealthBarLink { Owner = soldier }`.
  3. On the soldier: add `HealthBarRef { Bar = bar }` and add a
     `LinkedEntityGroup` buffer with element 0 = soldier (required as the
     root) and element 1 = bar, so `DestroyEntity(soldier)` at ghost despawn
     cascades to the bar.

### `HealthBarUpdateSystem`

- `[UpdateAfter(typeof(HealthBarSpawnSystem))]`.
- Reads `BattleConfig` singleton in `OnUpdate`, captures `MaxHealth` once.
- Burst `IJobEntity` over soldiers with `HealthBarRef` + `Health`.
- For each soldier, look up the bar via
  `ComponentLookup<HealthBarFill>` (with `update = true`) and write
  `Value = math.saturate(MaxHealth > 0 ? Current / MaxHealth : 0)`.
- No structural changes; parallel-safe (each soldier writes to a distinct bar).

### Cleanup

No dedicated system. `LinkedEntityGroup` on the soldier ensures that when the
ghost despawns (`DeathSystem` on the server → ghost despawn replicated to the
client) the bar is destroyed in the same step.

## Testing & validation

EditMode tests in `Assets/Tests/EditMode/` (existing assembly pattern):

- **`HealthBarUpdateSystemTests`**
  - Soldier with `Health { Current = 25, Max = 50 }`, a `BattleConfig`
    singleton with `MaxHealth = 50`, and `HealthBarRef` pointing at a bar
    entity with `HealthBarFill { Value = 1 }`. Tick the system once. Assert
    `HealthBarFill.Value == 0.5f`.
  - Edge: `BattleConfig.MaxHealth == 0` → fill becomes 0 (no NaN).
  - Edge: `Current > MaxHealth` → fill clamped to 1.

- **`HealthBarSpawnSystemTests`**
  - Soldier with no `HealthBarRef`. Stand in a hand-built "prefab" entity
    carrying `HealthBarFill` for `BattleConfig.HealthBarPrefab`.
  - After one tick, assert (a) `HealthBarRef` exists on the soldier,
    (b) `LinkedEntityGroup` on soldier contains the bar entity,
    (c) `Parent` on bar points at the soldier,
    (d) `HealthBarLink.Owner` on bar points at the soldier.

In-Editor manual validation (Unity MCP):

1. Open BattleScene, enter Play mode.
2. `Unity_GetConsoleLogs` — expect zero errors.
3. `Unity_SceneView_Capture2DScene` — confirm bars are visible above soldiers,
   billboarded toward the camera, colored full green initially.
4. Let combat run a few seconds. Capture again — confirm bars shorten and shift
   yellow/red as HP drops, and disappear when soldiers die.

Performance sanity check (Profiler):

- At default config (2×5×10 = 100/side), `HealthBarUpdateSystem` should run in
  <0.1ms on a desktop editor. Capture once after implementation to set a
  baseline.

## Out of scope (explicit non-goals for v1)

- Quantized health replication (revisit when scaling past ~1k/side).
- "Recent damage" yellow trail.
- Hiding bars at full HP, hover-only bars, or selection-driven bars.
- Per-platform shader fallback for Vulkan/Metal differences.
- Custom bar width per unit class (all soldiers share constants for now).

## Files added / changed

**Added**
- `Assets/Scripts/Demo/Battle/Authoring/HealthBarComponents.cs`
- `Assets/Scripts/Demo/Battle/Authoring/HealthBarAuthoring.cs`
- `Assets/Scripts/Demo/Battle/System/HealthBarSpawnSystem.cs`
- `Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs`
- `Assets/Prefabs/HealthBar.prefab`
- `Assets/Shaders/HealthBar.shader`
- `Assets/Materials/HealthBar.mat` (created during prefab setup)
- `Assets/Tests/EditMode/HealthBarSpawnSystemTests.cs`
- `Assets/Tests/EditMode/HealthBarUpdateSystemTests.cs`

**Changed**
- `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` —
  `Health` becomes `GhostPrefabType.All` with `[GhostField]` on both fields.
- `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` —
  add `HealthBarPrefab` and `HealthBarHeightOffset`.
- `Assets/Scenes/BattleSub.unity` — assign `HealthBarPrefab` on the
  `BattleConfigAuthoring` instance.
