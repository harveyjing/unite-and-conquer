# Two-Army Battle Scene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `BattleScene` that auto-spawns two opposing armies (10k red vs 10k blue), simulates melee combat (target → move → damage → die) entirely server-side, replicates soldiers as interpolated ghosts to the loopback client, and shows alive-counts + winner banner in a UI Toolkit HUD.

**Architecture:** Server systems in `SimulationSystemGroup`: `BattleSpawnSystem` (one-shot bulk instantiation) → `TargetingSystem` (Unity.Physics broadphase nearest-enemy query every 5 ticks) → `SoldierMovementSystem` (step toward target) → `MeleeDamageSystem` (parallel `IJobChunk` write into `NativeStream<DamageEvent>`, then single-threaded reduce into `Health`) → `DeathSystem` (destroy when `Health<=0`; NetCode auto-despawns the ghost). Client has no battle simulation — only `BattleHudController` (MonoBehaviour mirroring `DemoHudController`) counts replicated ghosts per team and writes to a `BattleHudViewModel`.

**Tech Stack:** Unity 6000.4.1f1 · Entities 1.4.4 · Netcode for Entities 1.13.0 · Unity.Physics 1.4.6 · Entities.Graphics 6.4.0 · Collections 6.4.0 · Burst · URP 17.4.0 · UI Toolkit.

**Spec:** [`docs/superpowers/specs/2026-05-14-two-army-battle-scene-design.md`](../specs/2026-05-14-two-army-battle-scene-design.md)

**Branch:** `feat/two-army-battle` (already checked out)

---

## Verification model — read this before starting

No `.asmdef` test assemblies exist; the spec puts automated tests out of scope. Verification for every task is one of:

- **Compile check** (code-only tasks): save file, Unity recompiles on focus; user confirms console has no compile errors.
- **Editor task** (prefabs, scenes, UI assets): user performs steps in Unity Editor; the agent only verifies post-state via reading `.meta`/`.prefab`/`.unity` files where applicable.
- **PlayMode check** (after a system lands): user opens `BattleScene.unity`, presses Play, verifies expected behavior in DOTS Hierarchy + Inspector windows.

**`.meta` files: always commit them.** Every Unity asset (including each `.cs` file and every new directory under `Assets/`) has a sibling `<name>.meta` containing a GUID that other assets reference. If you commit `Foo.cs` without `Foo.cs.meta`, the GUID regenerates on clone and all references break. After creating any new file under `Assets/`, run `git status` and confirm the new `.meta` files are staged alongside their assets — including folder-level metas like `Battle.meta`, `Battle/System.meta`, etc. The git-add commands in each task below list only the primary file for brevity; you are responsible for adding the corresponding `.meta` files.

Unity MCP tools (`Unity_GetConsoleLogs`, `Unity_Camera_Capture`, etc.) **are currently unavailable** — they disconnected mid-session. The user is the verifier for runtime behavior.

The plan deliberately scales up at the very end (Task 14): every system through Task 13 is built and verified at `CountPerSide = 10` so spawn/movement/damage/death are observable entity-by-entity in the DOTS Inspector. Scaling to 10k only happens after all systems are correct.

---

## Required environment — confirm before Task 0

- Unity Editor open on `/Users/wjing/workspace/private/unite-and-conquer` using **6000.4.1f1**.
- Branch `feat/two-army-battle` checked out; spec commit `01d4a05` present.
- `Packages/manifest.json` shows `com.unity.physics: "1.4.6"` (uncommitted modification expected — will be folded into the Task 6 commit).
- `git status` shows only:
  - `M Packages/manifest.json`
  - `M Packages/packages-lock.json`
  - Untracked `.lscache` files (ignore — IDE artifacts).

---

## File Structure

```
Assets/
├── Scripts/Demo/Battle/                          ← new directory
│   ├── Authoring/
│   │   ├── BattleConfigAuthoring.cs             NEW (Task 2)
│   │   └── SoldierAuthoring.cs                  NEW (Task 1)
│   ├── System/
│   │   ├── BattleSpawnSystem.cs                 NEW (Task 6)
│   │   ├── TargetingSystem.cs                   NEW (Task 7)
│   │   ├── SoldierMovementSystem.cs             NEW (Task 8)
│   │   ├── MeleeDamageSystem.cs                 NEW (Task 9)
│   │   └── DeathSystem.cs                       NEW (Task 10)
│   └── UI/
│       ├── BattleHudViewModel.cs                NEW (Task 11)
│       └── BattleHudController.cs               NEW (Task 12)
├── Scripts/Demo/System/PlayerSpawnSystem.cs     MODIFIED (Task 3) — one-line guard
├── Prefabs/Soldier.prefab                        NEW (Task 4 — Editor)
├── UI/BattleHud.uxml                             NEW (Task 13 — Editor)
├── UI/BattleHud.uss                              NEW (Task 13 — Editor)
└── Scenes/
    ├── BattleScene.unity                         NEW (Task 5 — Editor)
    └── BattleScene/
        └── BattleSub.unity                       NEW (Task 5 — Editor)
```

No `.asmdef` files added. Everything continues to compile into `Assembly-CSharp`. PanelSettings for the BattleHud reuses the existing `Assets/UI/DemoHudPanelSettings.asset` — one shared PanelSettings is sufficient since we never show both HUDs simultaneously.

---

## Task 0: Verify clean baseline

**Files:** none.

- [ ] **Step 1: Confirm branch + commit state**

Run:
```bash
git status && git log --oneline -3
```

Expected:
- On branch `feat/two-army-battle`
- HEAD is `01d4a05 docs(specs): two-army battle scene design`
- `M Packages/manifest.json` and `M Packages/packages-lock.json` present (uncommitted)
- No staged changes

- [ ] **Step 2: Confirm package versions**

Run:
```bash
grep -E '"com\.unity\.(entities|netcode|physics|entities\.graphics)"' Packages/manifest.json
```

Expected output includes: `"com.unity.entities.graphics": "6.4.0"`, `"com.unity.netcode": "1.13.0"`, `"com.unity.physics": "1.4.6"`.

- [ ] **Step 3: Ask user to confirm Editor compiles cleanly**

User action: open Unity Editor, focus the project (triggers reload), open Console window (Window → General → Console). Confirm no red errors.

If errors appear about Unity.Physics — that means the package wasn't fully resolved. User runs Window → Package Manager → press refresh.

---

## Task 1: Define soldier components + SoldierAuthoring

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs`

This file defines all five soldier components AND the authoring MonoBehaviour with its Baker. Co-locating types that are only used together is the project convention (cf. `PlayerCapsule` defined in `Assets/Scripts/Demo/System/PlayerSpawnSystem.cs:9`).

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // Replicated to clients (used for HUD counts).
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct Soldier : IComponentData { }

    // Replicated as a single int per ghost. 0 = Red, 1 = Blue.
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct Team : IComponentData
    {
        [GhostField] public int Value;
    }

    // Server-only.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }

    // Server-only. Per-entity static after spawn.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct AttackStats : IComponentData
    {
        public float Range;
        public float Dps;
    }

    // Server-only. Refreshed by TargetingSystem every TargetRefreshIntervalTicks.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Target : IComponentData
    {
        public Entity Value;
    }

    // Authoring placed on the Soldier prefab GameObject (Task 4).
    // Adds all five components plus a query-only PhysicsCollider and a
    // URPMaterialPropertyBaseColor that BattleSpawnSystem overwrites per-team.
    //
    // PhysicsCollider is created programmatically (rather than via
    // PhysicsShapeAuthoring) to make BelongsTo / CollidesWith explicit in code.
    [DisallowMultipleComponent]
    public class SoldierAuthoring : MonoBehaviour
    {
        class Baker : Baker<SoldierAuthoring>
        {
            public override void Bake(SoldierAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new Target { Value = Entity.Null });
                AddComponent(entity, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });

                // Query-only sphere collider on layer 1.
                var filter = new CollisionFilter
                {
                    BelongsTo    = 1u << 1,
                    CollidesWith = 0u,
                    GroupIndex   = 0,
                };
                var collider = Unity.Physics.SphereCollider.Create(
                    new SphereGeometry
                    {
                        Center = float3.zero,
                        Radius = 0.3f,
                    },
                    filter);
                AddBlobAsset(ref collider, out _);
                AddComponent(entity, new PhysicsCollider { Value = collider });

                // PhysicsVelocity (zero) marks the body as dynamic-tree resident
                // so the broadphase is incrementally updated as soldiers move.
                AddComponent(entity, new PhysicsVelocity
                {
                    Linear  = float3.zero,
                    Angular = float3.zero,
                });

                // Kinematic mass means physics never integrates this body;
                // we only use it for distance queries.
                AddComponent(entity, PhysicsMass.CreateKinematic(MassProperties.UnitSphere));
            }
        }
    }
}
```

Note: `float3` and `float4` come from `Unity.Mathematics`. Add `using Unity.Mathematics;` near the top of the file (already covered by `using Unity.Transforms;` pulling it transitively in some Unity versions, but be explicit):

Replace the using block at the top with:

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
```

- [ ] **Step 2: Verify compiles**

Save file. Switch to Unity Editor (forces reload). Confirm Console has no errors related to `SoldierAuthoring.cs`, `Unity.Physics`, or `Unity.NetCode`.

If `Unity.Physics.Authoring` namespace fails to resolve, that namespace may not be needed — remove the `using Unity.Physics.Authoring;` line (the baker only uses `Unity.Physics` types, not authoring types).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs
git commit -m "$(cat <<'EOF'
feat(battle): soldier components and authoring baker

Defines Soldier (tag, replicated), Team (replicated 1-int ghost field),
Health/AttackStats/Target (server-only via GhostPrefabType.Server), plus
a SoldierAuthoring MonoBehaviour whose baker adds those components, a
query-only PhysicsCollider sphere on layer 1, zero PhysicsVelocity
(dynamic-tree resident), and a kinematic PhysicsMass.
EOF
)"
```

---

## Task 2: Define BattleConfig + BattleConfigAuthoring

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs`

Singleton holding all spawn + tuning parameters. Same pattern as `PrefabSpawnerAuthoring.cs`.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Singleton baked from BattleConfigAuthoring. Drives every battle system.
    public struct BattleConfig : IComponentData
    {
        public Entity SoldierPrefab;
        public int    CountPerSide;
        public float3 RedCenter;
        public float3 BlueCenter;
        public float  Spacing;
        public float  SearchRadius;
        public float  MoveSpeed;
        public float  AttackRange;
        public float  Dps;
        public float  MaxHealth;
        public int    TargetRefreshIntervalTicks;
        public float4 RedColor;
        public float4 BlueColor;
    }

    public class BattleConfigAuthoring : MonoBehaviour
    {
        [Tooltip("Soldier prefab — must have a GhostAuthoringComponent + SoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Army size")]
        [Tooltip("Soldiers per team. Start at 10 for verification; scale to 10000 at the end.")]
        public int CountPerSide = 10;
        public float Spacing = 1.5f;

        [Header("Spawn centers")]
        public Vector3 RedCenter  = new Vector3(-20f, 0f, 0f);
        public Vector3 BlueCenter = new Vector3( 20f, 0f, 0f);

        [Header("Combat tuning")]
        public float SearchRadius = 50f;
        public float MoveSpeed    = 2f;
        public float AttackRange  = 0.8f;
        public float Dps          = 25f;
        public float MaxHealth    = 50f;
        public int   TargetRefreshIntervalTicks = 5;

        [Header("Team colors (RGBA, linear)")]
        public Color RedColor  = new Color(1f, 0.1f, 0.1f, 1f);
        public Color BlueColor = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<BattleConfigAuthoring>
        {
            public override void Bake(BattleConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new BattleConfig
                {
                    SoldierPrefab = GetEntity(authoring.SoldierPrefab, TransformUsageFlags.Dynamic),
                    CountPerSide  = authoring.CountPerSide,
                    RedCenter     = authoring.RedCenter,
                    BlueCenter    = authoring.BlueCenter,
                    Spacing       = authoring.Spacing,
                    SearchRadius  = authoring.SearchRadius,
                    MoveSpeed     = authoring.MoveSpeed,
                    AttackRange   = authoring.AttackRange,
                    Dps           = authoring.Dps,
                    MaxHealth     = authoring.MaxHealth,
                    TargetRefreshIntervalTicks = authoring.TargetRefreshIntervalTicks,
                    RedColor      = new float4(authoring.RedColor.r,  authoring.RedColor.g,  authoring.RedColor.b,  authoring.RedColor.a),
                    BlueColor     = new float4(authoring.BlueColor.r, authoring.BlueColor.g, authoring.BlueColor.b, authoring.BlueColor.a),
                });
            }
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs
git commit -m "feat(battle): BattleConfig singleton and authoring component"
```

---

## Task 3: Apply PlayerSpawnSystem fix

**Files:**
- Modify: `Assets/Scripts/Demo/System/PlayerSpawnSystem.cs`

In `BattleScene` there is no `PrefabSpawner` singleton. When a client connects, `GoInGameServerSystem` enqueues a `PlayerCapsule` entity; `PlayerSpawnSystem` will then try `SystemAPI.GetSingleton<PrefabSpawner>()` and throw. Add `state.RequireForUpdate<PrefabSpawner>()` so the system silently skips in the battle scene.

(`SpawnObstacleRequestServerSystem` already has this guard at line 30; `RespawnRequestServerSystem` doesn't reference `PrefabSpawner`. No further changes needed.)

- [ ] **Step 1: Read current OnCreate**

File state before edit (lines 19-25):
```csharp
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<PlayerCapsule>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            state.RequireForUpdate<NetworkId>();
        }
```

- [ ] **Step 2: Apply the edit**

Add one line at the end of `OnCreate`:

```csharp
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<PlayerCapsule>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<PrefabSpawner>();
        }
```

- [ ] **Step 3: Verify compiles**

Save. Confirm no Console errors. SampleScene Play mode behavior is unchanged because `PrefabSpawner` exists in that scene.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/System/PlayerSpawnSystem.cs
git commit -m "$(cat <<'EOF'
fix(spawn): gate PlayerSpawnSystem on PrefabSpawner presence

In scenes that have no PrefabSpawner singleton (e.g. the upcoming
BattleScene), a connecting client still triggers GoInGameServerSystem
to enqueue a PlayerCapsule, after which PlayerSpawnSystem throws on
the missing singleton. Adding state.RequireForUpdate<PrefabSpawner>()
makes the system silently no-op in those scenes. SampleScene behavior
unchanged.
EOF
)"
```

---

## Task 4 (Editor): Create Soldier.prefab

**Files:**
- Create: `Assets/Prefabs/Soldier.prefab`

This is a Unity Editor task; the agent cannot author prefab YAML reliably (GUIDs, FileIDs). Instructions for the user:

- [ ] **Step 1: Create the GameObject**

In Unity Editor:
1. Hierarchy → right-click → 3D Object → Cube. Rename to `Soldier`.
2. Inspector → Transform → set Scale to `(0.3, 0.6, 0.3)`.
3. Remove the `BoxCollider` component (we use a `PhysicsCollider` via baking instead) — right-click the BoxCollider header → Remove Component.

- [ ] **Step 2: Material**

1. Project → `Assets/Materials/` (create if missing) → right-click → Create → Material. Name `SoldierMat`.
2. Set Shader to `Universal Render Pipeline/Lit`.
3. Inspector → tick **Enable GPU Instancing** (required for per-instance color via `URPMaterialPropertyBaseColor`).
4. Drag `SoldierMat` onto the Soldier MeshRenderer.

- [ ] **Step 3: Add Ghost authoring**

1. With `Soldier` selected, Inspector → Add Component → search `Ghost Authoring Component` (from `Unity.NetCode`).
2. Set fields:
   - Default Ghost Mode: **Interpolated**
   - Supported Ghost Modes: **Interpolated**
   - Optimization Mode: **Dynamic**
   - Has Owner: **unchecked**
   - Support Auto Command Target: **unchecked**
   - Use Pre Serialization: **checked**
   - Importance: `1`

- [ ] **Step 4: Add Soldier authoring**

Add Component → search `Soldier Authoring`. No fields to configure — baker does all the work.

- [ ] **Step 5: Save as prefab**

1. Drag `Soldier` GameObject from Hierarchy into `Assets/Prefabs/`. Confirm `Soldier.prefab` is created.
2. Delete the `Soldier` GameObject from the Hierarchy (we only need the prefab).

- [ ] **Step 6: Verify prefab structure**

Agent runs:
```bash
grep -E 'm_Name|GhostAuthoringComponent|SoldierAuthoring' Assets/Prefabs/Soldier.prefab | head -20
```

Expected output includes a `m_Name: Soldier` line, references to `GhostAuthoringComponent`, and to `SoldierAuthoring`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefabs/Soldier.prefab Assets/Prefabs/Soldier.prefab.meta Assets/Materials/SoldierMat.mat Assets/Materials/SoldierMat.mat.meta Assets/Materials/SoldierMat.mat.meta
git status   # verify only the intended files are staged
git commit -m "feat(battle): Soldier prefab with ghost + material"
```

(Adjust the `git add` paths if the user placed `SoldierMat.mat` in a different folder.)

---

## Task 5 (Editor): Create BattleScene + BattleSub

**Files:**
- Create: `Assets/Scenes/BattleScene.unity`
- Create: `Assets/Scenes/BattleScene/BattleSub.unity`

Editor task. Mirrors the existing `SampleScene` + `SampleScene/EcsDemoSub` structure.

- [ ] **Step 1: Create the top-level scene**

1. File → New Scene → choose "Standard (URP)" template → Create.
2. File → Save As → `Assets/Scenes/BattleScene.unity`.
3. In the new scene Hierarchy, you should have a default Camera and Directional Light.
4. Select **Main Camera**. Set Transform Position `(0, 80, -40)`, Rotation `(60, 0, 0)`. Camera component: Projection = Perspective, Field of View = 60. This frames the battlefield from a tilted overhead view.

- [ ] **Step 2: Create the subscene**

1. Hierarchy → right-click → New Sub Scene → Empty Scene. Save as `Assets/Scenes/BattleScene/BattleSub.unity`.
2. Double-click the SubScene in the Hierarchy to enter edit mode.

- [ ] **Step 3: Inside the subscene — ground plane**

1. Right-click in Hierarchy (inside subscene) → 3D Object → Plane. Position `(0, 0, 0)`, Scale `(10, 1, 10)`.
2. Remove the auto-added `MeshCollider` (we don't need ground physics for query-only soldiers).
3. (Optional) Assign a dark/neutral material to the plane for contrast.

- [ ] **Step 4: Inside the subscene — BattleConfig GameObject**

1. Right-click in Hierarchy (inside subscene) → Create Empty. Rename to `BattleConfig`.
2. Add Component → `Battle Config Authoring`.
3. Drag `Assets/Prefabs/Soldier.prefab` into the `Soldier Prefab` slot.
4. Leave other fields at defaults (CountPerSide = 10 for now). We bump to 10000 in Task 14.

- [ ] **Step 5: Close the subscene + verify**

1. Click "Save" on the subscene banner, then close subscene edit mode.
2. Save the BattleScene (Cmd+S).

Agent runs:
```bash
ls Assets/Scenes/BattleScene*
grep -c "BattleConfigAuthoring" Assets/Scenes/BattleScene/BattleSub.unity
```

Expected: `BattleScene.unity`, `BattleScene.unity.meta`, `BattleScene/BattleSub.unity`, `BattleScene/BattleSub.unity.meta` exist. The `grep` returns at least 1.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scenes/BattleScene.unity Assets/Scenes/BattleScene.unity.meta Assets/Scenes/BattleScene Assets/Scenes/BattleScene.meta
git commit -m "feat(battle): BattleScene + BattleSub with ground and BattleConfig"
```

---

## Task 6: BattleSpawnSystem

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs`

One-shot bulk spawner. Two `IJobParallelFor` passes (one per team) write per-entity components after a bulk `EntityManager.Instantiate`. Disables itself at the end.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot. Spawns CountPerSide soldiers per team in two opposing
    // grid blocks centered on RedCenter / BlueCenter, then disables
    // itself. Uses bulk EntityManager.Instantiate + IJobParallelFor to
    // initialize per-entity component values — ECB-per-entity would
    // cost hundreds of ms at 10k+10k.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct BattleSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var em = state.EntityManager;

            var reds  = em.Instantiate(config.SoldierPrefab, config.CountPerSide, Allocator.TempJob);
            var blues = em.Instantiate(config.SoldierPrefab, config.CountPerSide, Allocator.TempJob);

            var gridSide = (int)math.ceil(math.sqrt(config.CountPerSide));

            var xformLookup  = state.GetComponentLookup<LocalTransform>(false);
            var teamLookup   = state.GetComponentLookup<Team>(false);
            var healthLookup = state.GetComponentLookup<Health>(false);
            var attackLookup = state.GetComponentLookup<AttackStats>(false);
            var colorLookup  = state.GetComponentLookup<URPMaterialPropertyBaseColor>(false);

            var initRed = new InitSoldierJob
            {
                Entities       = reds,
                Origin         = config.RedCenter,
                GridSide       = gridSide,
                Spacing        = config.Spacing,
                TeamValue      = 0,
                TeamColor      = config.RedColor,
                MaxHealth      = config.MaxHealth,
                AttackRange    = config.AttackRange,
                Dps            = config.Dps,
                XformLookup    = xformLookup,
                TeamLookup     = teamLookup,
                HealthLookup   = healthLookup,
                AttackLookup   = attackLookup,
                ColorLookup    = colorLookup,
            };
            state.Dependency = initRed.Schedule(reds.Length, 64, state.Dependency);

            var initBlue = new InitSoldierJob
            {
                Entities       = blues,
                Origin         = config.BlueCenter,
                GridSide       = gridSide,
                Spacing        = config.Spacing,
                TeamValue      = 1,
                TeamColor      = config.BlueColor,
                MaxHealth      = config.MaxHealth,
                AttackRange    = config.AttackRange,
                Dps            = config.Dps,
                XformLookup    = xformLookup,
                TeamLookup     = teamLookup,
                HealthLookup   = healthLookup,
                AttackLookup   = attackLookup,
                ColorLookup    = colorLookup,
            };
            state.Dependency = initBlue.Schedule(blues.Length, 64, state.Dependency);

            state.Dependency = reds.Dispose(state.Dependency);
            state.Dependency = blues.Dispose(state.Dependency);

            Debug.Log($"BattleSpawnSystem: spawned {config.CountPerSide} red + {config.CountPerSide} blue soldiers.");
            state.Enabled = false;
        }

        [BurstCompile]
        struct InitSoldierJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            public float3 Origin;
            public int    GridSide;
            public float  Spacing;
            public int    TeamValue;
            public float4 TeamColor;
            public float  MaxHealth;
            public float  AttackRange;
            public float  Dps;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> XformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Team> TeamLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Health> HealthLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<AttackStats> AttackLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<URPMaterialPropertyBaseColor> ColorLookup;

            public void Execute(int i)
            {
                var e = Entities[i];
                int row = i / GridSide;
                int col = i % GridSide;
                var localOffset = new float3(
                    (col - GridSide * 0.5f) * Spacing,
                    0f,
                    (row - GridSide * 0.5f) * Spacing);
                var pos = Origin + localOffset;

                XformLookup[e]  = LocalTransform.FromPosition(pos);
                TeamLookup[e]   = new Team { Value = TeamValue };
                HealthLookup[e] = new Health { Current = MaxHealth, Max = MaxHealth };
                AttackLookup[e] = new AttackStats { Range = AttackRange, Dps = Dps };
                ColorLookup[e]  = new URPMaterialPropertyBaseColor { Value = TeamColor };
            }
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm Console has no errors.

- [ ] **Step 3: PlayMode verification (CountPerSide = 10)**

User actions:
1. Open `Assets/Scenes/BattleScene.unity`.
2. Verify the `BattleConfig` GameObject in the subscene has `Count Per Side = 10`.
3. Press Play.
4. Console: expect one log line `BattleSpawnSystem: spawned 10 red + 10 blue soldiers.`.
5. Open Window → Entities → Hierarchy. Filter the **ServerWorld**. Expect 20 entities with the `Soldier` component (plus a `BattleConfig` singleton entity).
6. Filter the **ClientWorld**. Expect ~20 ghost entities with `Soldier` and `Team` components (replication may take a few ticks).
7. Scene view: 20 cubes should be visible in two opposing rectangles centered on `(-20, 0, 0)` and `(20, 0, 0)`. Red cubes are on the -X side, blue on +X.

If cubes are uncolored (white), the URP material likely doesn't have GPU instancing enabled (Task 4 Step 2). Fix the material and retry.

- [ ] **Step 4: Commit (fold in package manifest changes)**

```bash
git add Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs Packages/manifest.json Packages/packages-lock.json
git commit -m "$(cat <<'EOF'
feat(battle): BattleSpawnSystem one-shot bulk spawn

Adds Unity.Physics 1.4.6 to the package manifest (required for
Task 7 broadphase queries) and a one-shot ServerSimulation system
that bulk-instantiates CountPerSide soldiers per team via
EntityManager.Instantiate + IJobParallelFor. Per-entity component
values (position grid, team, health, color) are filled in parallel,
then the system disables itself. Verified at CountPerSide=10.
EOF
)"
```

---

## Task 7: TargetingSystem

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/TargetingSystem.cs`

Refreshes `Target.Value` every `TargetRefreshIntervalTicks` ticks via Unity.Physics broadphase. Uses a custom `ICollector<DistanceHit>` that filters same-team hits via `ComponentLookup<Team>`.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TargetingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var tick   = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            if ((tick % (uint)config.TargetRefreshIntervalTicks) != 0u) return;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var teamLookup   = SystemAPI.GetComponentLookup<Team>(true);

            var queryFilter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = Soldier.Layer,
                GroupIndex   = 0,
            };

            new RefreshTargetJob
            {
                CollisionWorld = physicsWorld.CollisionWorld,
                TeamLookup     = teamLookup,
                Filter         = queryFilter,
                SearchRadius   = config.SearchRadius,
            }.ScheduleParallel();
        }
    }

    // For each Soldier, query the broadphase for the nearest enemy
    // (different Team) within SearchRadius. Write the result into Target.
    [BurstCompile]
    public partial struct RefreshTargetJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        public CollisionFilter Filter;
        public float SearchRadius;

        public void Execute(Entity entity, in LocalTransform xform, in Team team, ref Target target)
        {
            var collector = new NearestEnemyCollector
            {
                MaxFraction = SearchRadius,
                SelfTeam    = team.Value,
                SelfEntity  = entity,
                TeamLookup  = TeamLookup,
                Closest     = default,
                NumHits     = 0,
            };
            var input = new PointDistanceInput
            {
                Position    = xform.Position,
                MaxDistance = SearchRadius,
                Filter      = Filter,
            };
            CollisionWorld.CalculateDistance(input, ref collector);
            target.Value = collector.NumHits > 0 ? collector.Closest.Entity : Entity.Null;
        }
    }

    // Custom collector that keeps the single closest hit whose Team
    // differs from SelfTeam. Mutates MaxFraction so the broadphase
    // shrinks the search as closer candidates are found.
    public struct NearestEnemyCollector : ICollector<DistanceHit>
    {
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; set; }
        public int NumHits { get; set; }

        public DistanceHit Closest;
        public int SelfTeam;
        public Entity SelfEntity;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        public bool AddHit(DistanceHit hit)
        {
            if (hit.Entity == SelfEntity) return false;
            if (!TeamLookup.HasComponent(hit.Entity)) return false;
            if (TeamLookup[hit.Entity].Value == SelfTeam) return false;

            if (NumHits == 0 || hit.Fraction < Closest.Fraction)
            {
                Closest = hit;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: PlayMode verification**

1. Open `BattleScene`, press Play (still CountPerSide = 10).
2. Window → Entities → Hierarchy → ServerWorld. Pick any soldier entity, view its components.
3. Expect `Target.Value` to be a non-null Entity reference. Click through the reference: the referenced entity should be in the OPPOSITE team (Red soldiers target Blue, vice versa).
4. Verify by checking `Team.Value` on the source and target.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/TargetingSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): TargetingSystem with Unity.Physics broadphase

Every TargetRefreshIntervalTicks (default 5), each soldier queries the
PhysicsWorldSingleton.CollisionWorld via CalculateDistance + a custom
NearestEnemyCollector that filters same-team hits using
ComponentLookup<Team>. The collector shrinks MaxFraction as closer
candidates are found. Soldier colliders are query-only
(CollidesWith=0, BelongsTo=1) and live in the dynamic broadphase tree
courtesy of a zero PhysicsVelocity attached at bake time.
EOF
)"
```

---

## Task 8: SoldierMovementSystem

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs`

Step toward target each tick unless within attack range. Burst `IJobEntity`.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct SoldierMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var dt = SystemAPI.Time.DeltaTime;
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SoldierStepJob
            {
                MoveSpeed   = config.MoveSpeed,
                AttackRange = config.AttackRange,
                Dt          = dt,
                XformLookup = xformLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SoldierStepJob : IJobEntity
    {
        public float MoveSpeed;
        public float AttackRange;
        public float Dt;
        [ReadOnly] public ComponentLookup<LocalTransform> XformLookup;

        public void Execute(ref LocalTransform xform, in Target target)
        {
            if (target.Value == Entity.Null) return;
            if (!XformLookup.HasComponent(target.Value)) return;

            var to = XformLookup[target.Value].Position - xform.Position;
            float dist = math.length(to);
            if (dist <= AttackRange || dist < 1e-4f) return;

            var dir = to / dist;
            xform.Position += dir * (MoveSpeed * Dt);
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: PlayMode verification**

1. Open `BattleScene`, press Play.
2. Watch the Scene view: the two cube blocks should march toward each other along X and merge in the middle.
3. Soldiers should stop moving once they're in melee contact (within `AttackRange = 0.8`).

Note: at this point, no damage is applied — soldiers will simply pile up in the middle indefinitely. That's expected; Task 9 adds damage.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): SoldierMovementSystem

Each tick, soldiers step toward their current Target by MoveSpeed*dt
unless within AttackRange. Runs in SimulationSystemGroup after
TargetingSystem. Parallel IJobEntity, no allocations.
EOF
)"
```

---

## Task 9: MeleeDamageSystem

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs`

This is the most intricate system. Two-phase: parallel `IJobChunk` writes `DamageEvent`s into a `NativeStream` (one foreach-index per chunk), then a single-threaded `IJob` reduces the stream into `Health` via `[NativeDisableParallelForRestriction] ComponentLookup<Health>`.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    // Scatter-write per-attacker; gather-apply on victim's Health.
    public struct DamageEvent
    {
        public Entity Victim;
        public float  Amount;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SoldierMovementSystem))]
    public partial struct MeleeDamageSystem : ISystem
    {
        EntityQuery _attackerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _attackerQuery = SystemAPI.QueryBuilder()
                .WithAll<Soldier, AttackStats, Target, LocalTransform>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            int chunkCount = _attackerQuery.CalculateChunkCount();
            if (chunkCount == 0) return;

            var stream = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            state.Dependency = new WriteDamageJob
            {
                TargetHandle = SystemAPI.GetComponentTypeHandle<Target>(true),
                AttackHandle = SystemAPI.GetComponentTypeHandle<AttackStats>(true),
                XformHandle  = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                XformLookup  = xformLookup,
                DamageWriter = stream.AsWriter(),
                Dt           = dt,
            }.ScheduleParallel(_attackerQuery, state.Dependency);

            state.Dependency = new ReduceDamageJob
            {
                Reader       = stream.AsReader(),
                HealthLookup = SystemAPI.GetComponentLookup<Health>(false),
            }.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    struct WriteDamageJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Target> TargetHandle;
        [ReadOnly] public ComponentTypeHandle<AttackStats> AttackHandle;
        [ReadOnly] public ComponentTypeHandle<LocalTransform> XformHandle;
        [ReadOnly] public ComponentLookup<LocalTransform> XformLookup;
        public NativeStream.Writer DamageWriter;
        public float Dt;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DamageWriter.BeginForEachIndex(unfilteredChunkIndex);

            var targets = chunk.GetNativeArray(ref TargetHandle);
            var attacks = chunk.GetNativeArray(ref AttackHandle);
            var xforms  = chunk.GetNativeArray(ref XformHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var t = targets[i].Value;
                if (t == Entity.Null) continue;
                if (!XformLookup.HasComponent(t)) continue;

                float distSq = math.distancesq(xforms[i].Position, XformLookup[t].Position);
                float range  = attacks[i].Range;
                if (distSq <= range * range)
                {
                    DamageWriter.Write(new DamageEvent
                    {
                        Victim = t,
                        Amount = attacks[i].Dps * Dt,
                    });
                }
            }

            DamageWriter.EndForEachIndex();
        }
    }

    // Single-threaded reduce: read every event, decrement victim Health.
    // [NativeDisableParallelForRestriction] tells the safety system that
    // we will scatter-write to ComponentLookup<Health> from one thread;
    // since the job is IJob (not parallel), this is safe.
    [BurstCompile]
    struct ReduceDamageJob : IJob
    {
        public NativeStream.Reader Reader;
        [NativeDisableParallelForRestriction] public ComponentLookup<Health> HealthLookup;

        public void Execute()
        {
            int foreachCount = Reader.ForEachCount;
            for (int i = 0; i < foreachCount; i++)
            {
                int eventCount = Reader.BeginForEachIndex(i);
                for (int j = 0; j < eventCount; j++)
                {
                    var ev = Reader.Read<DamageEvent>();
                    if (HealthLookup.HasComponent(ev.Victim))
                    {
                        var h = HealthLookup[ev.Victim];
                        h.Current -= ev.Amount;
                        HealthLookup[ev.Victim] = h;
                    }
                }
                Reader.EndForEachIndex();
            }
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

Common error to watch for: if `NativeStream.AsWriter()` complains, ensure `using Unity.Collections;` is present. If `IJobChunk.Execute` signature errors, the `v128` parameter comes from `using Unity.Burst.Intrinsics;`.

- [ ] **Step 3: PlayMode verification**

1. Open `BattleScene`, press Play.
2. Watch the Scene view as armies converge.
3. Window → Entities → Hierarchy → ServerWorld. Pick a soldier currently in contact. Watch `Health.Current` decrease tick by tick.
4. With `Dps = 25`, `MaxHealth = 50`, two soldiers in contact mutually kill each other in ~1 second of simulated time. (No despawn yet — DeathSystem lands in Task 10. Soldiers with `Health.Current <= 0` will keep accumulating negative health.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): MeleeDamageSystem with parallel NativeStream reduce

Two-phase damage. WriteDamageJob is parallel IJobChunk:
each chunk calls NativeStream.Writer.BeginForEachIndex(chunkIndex),
writes DamageEvent { Victim, Amount } for every soldier whose
target is in range, then EndForEachIndex. ReduceDamageJob is a
single-threaded IJob reading the stream and decrementing Health
via [NativeDisableParallelForRestriction] ComponentLookup<Health>.
Stream allocated from state.WorldUpdateAllocator; auto-freed.
EOF
)"
```

---

## Task 10: DeathSystem

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/DeathSystem.cs`

Destroy entities with `Health.Current <= 0`. NetCode's `GhostSendSystem` auto-replicates the despawn to clients.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeDamageSystem))]
    public partial struct DeathSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (health, entity) in
                     SystemAPI.Query<RefRO<Health>>()
                              .WithAll<Soldier>()
                              .WithEntityAccess())
            {
                if (health.ValueRO.Current <= 0f)
                    ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: PlayMode verification**

1. Open `BattleScene`, press Play.
2. Watch armies converge, fight, and **dwindle**. Server-world Soldier count should drop over time, and client-world ghosts should despawn correspondingly.
3. With 10 vs 10 and roughly equal stats, expect mostly-mutual annihilation in ~2-4 seconds of sim time.
4. Open Window → Entities → Hierarchy → ServerWorld. Filter for `Soldier`. After ~5 seconds, count should approach 0 (or a small surviving handful).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/DeathSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): DeathSystem destroys soldiers with Health<=0

Runs after MeleeDamageSystem. ECB-based destroy; NetCode's
GhostSendSystem auto-replicates the despawn to interpolated
clients. WorldUpdateAllocator-backed ECB, auto-freed.
EOF
)"
```

---

## Task 11: BattleHudViewModel

**Files:**
- Create: `Assets/Scripts/Demo/Battle/UI/BattleHudViewModel.cs`

Mirrors `DemoHudViewModel.cs`. Three text properties.

- [ ] **Step 1: Write the file**

```csharp
using System;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Demo
{
    // Plain C# view model bound to BattleHud.uxml via runtime data binding.
    // Setters short-circuit on unchanged values; the controller can poll
    // every frame without churning the binding system.
    public class BattleHudViewModel : INotifyBindablePropertyChanged
    {
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        string _redAliveText  = "Red: -";
        string _blueAliveText = "Blue: -";
        string _winnerText    = string.Empty;

        [CreateProperty]
        public string RedAliveText
        {
            get => _redAliveText;
            set
            {
                if (_redAliveText == value) return;
                _redAliveText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(RedAliveText)));
            }
        }

        [CreateProperty]
        public string BlueAliveText
        {
            get => _blueAliveText;
            set
            {
                if (_blueAliveText == value) return;
                _blueAliveText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(BlueAliveText)));
            }
        }

        [CreateProperty]
        public string WinnerText
        {
            get => _winnerText;
            set
            {
                if (_winnerText == value) return;
                _winnerText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(WinnerText)));
            }
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/UI/BattleHudViewModel.cs
git commit -m "feat(battle): BattleHudViewModel for HUD data binding"
```

---

## Task 12: BattleHudController

**Files:**
- Create: `Assets/Scripts/Demo/Battle/UI/BattleHudController.cs`

Mirrors `DemoHudController.cs`. Lazy-finds client world, counts per-team ghosts each frame, derives winner.

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // MonoBehaviour bridge from the client ECS world to BattleHud.uxml.
    // Lazy-finds the client world by name (mirrors DemoHudController);
    // each Update, counts ghost soldiers per team and writes formatted
    // strings into the BattleHudViewModel. Winner detection: once a
    // team has been seen alive, dropping to zero triggers the banner.
    [RequireComponent(typeof(UIDocument))]
    public class BattleHudController : MonoBehaviour
    {
        BattleHudViewModel _viewModel;

        World _clientWorld;
        EntityQuery _soldierQuery;

        bool _redEverAlive;
        bool _blueEverAlive;
        int  _winnerTeam = -1;   // -1 = undecided, 0 = Red, 1 = Blue

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("BattleHudController: UIDocument rootVisualElement is null.");
                enabled = false;
                return;
            }

            _viewModel = new BattleHudViewModel();
            root.dataSource = _viewModel;
        }

        void OnDisable()
        {
            if (_soldierQuery != default) _soldierQuery.Dispose();
            _soldierQuery = default;
            _clientWorld = null;
        }

        static World FindClientWorld()
        {
            foreach (var w in World.All)
                if (w.Name.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        void Update()
        {
            if (_viewModel == null) return;

            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                if (_soldierQuery != default) _soldierQuery.Dispose();
                _soldierQuery = default;
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                _soldierQuery = _clientWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<Soldier>(),
                    ComponentType.ReadOnly<Team>());
            }

            int red = 0, blue = 0;
            using (var teams = _soldierQuery.ToComponentDataArray<Team>(Allocator.Temp))
            {
                for (int i = 0; i < teams.Length; i++)
                {
                    if (teams[i].Value == 0) red++;
                    else if (teams[i].Value == 1) blue++;
                }
            }

            if (red  > 0) _redEverAlive  = true;
            if (blue > 0) _blueEverAlive = true;

            _viewModel.RedAliveText  = string.Format(CultureInfo.InvariantCulture, "Red:  {0}", red);
            _viewModel.BlueAliveText = string.Format(CultureInfo.InvariantCulture, "Blue: {0}", blue);

            if (_winnerTeam == -1)
            {
                if (_redEverAlive  && red  == 0 && blue > 0) _winnerTeam = 1;
                if (_blueEverAlive && blue == 0 && red  > 0) _winnerTeam = 0;
                if (_redEverAlive  && _blueEverAlive && red == 0 && blue == 0) _winnerTeam = 2; // draw
            }

            _viewModel.WinnerText = _winnerTeam switch
            {
                0 => "RED WINS",
                1 => "BLUE WINS",
                2 => "DRAW",
                _ => string.Empty,
            };
        }
    }
}
```

- [ ] **Step 2: Verify compiles**

Save. Confirm no Console errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/UI/BattleHudController.cs
git commit -m "$(cat <<'EOF'
feat(battle): BattleHudController bridges client world to BattleHud

MonoBehaviour mirroring DemoHudController. Lazy-finds the client
world by name, queries EntityQuery<Soldier, Team>, counts per team
each Update, writes formatted strings to BattleHudViewModel via
short-circuit setters. Tracks whether each team was ever alive and
sets a winner banner on the falling edge to zero (handles draws).
EOF
)"
```

---

## Task 13 (Editor): UI assets + scene wiring

**Files:**
- Create: `Assets/UI/BattleHud.uxml`
- Create: `Assets/UI/BattleHud.uss`
- Modify: `Assets/Scenes/BattleScene.unity` — add BattleHud GameObject

PanelSettings: reuse the existing `Assets/UI/DemoHudPanelSettings.asset` (no need to create a new one).

- [ ] **Step 1: Create BattleHud.uss**

In Unity Editor: Project window → `Assets/UI/` → right-click → Create → UI Toolkit → StyleSheet. Name `BattleHud.uss`. Open and replace its contents with:

```css
.root {
    -unity-font-style: bold;
    color: rgb(230, 230, 230);
    padding: 12px;
}

.count {
    font-size: 22px;
    margin-bottom: 4px;
}

.count-red  { color: rgb(255, 90, 90); }
.count-blue { color: rgb(110, 170, 255); }

.winner {
    font-size: 48px;
    -unity-text-align: middle-center;
    color: rgb(255, 220, 60);
    margin-top: 32px;
}
```

- [ ] **Step 2: Create BattleHud.uxml**

In Unity Editor: Project window → `Assets/UI/` → right-click → Create → UI Toolkit → UI Document. Name `BattleHud.uxml`. Open and replace its contents with:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <Style src="BattleHud.uss" />

    <ui:VisualElement class="root" picking-mode="Ignore">
        <ui:Label name="red-alive"  class="count count-red"  text="Red:  -">
            <Bindings>
                <ui:DataBinding property="text" data-source-path="RedAliveText" />
            </Bindings>
        </ui:Label>
        <ui:Label name="blue-alive" class="count count-blue" text="Blue: -">
            <Bindings>
                <ui:DataBinding property="text" data-source-path="BlueAliveText" />
            </Bindings>
        </ui:Label>
        <ui:Label name="winner" class="winner" text="">
            <Bindings>
                <ui:DataBinding property="text" data-source-path="WinnerText" />
            </Bindings>
        </ui:Label>
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Step 3: Add HUD GameObject to BattleScene**

1. Open `Assets/Scenes/BattleScene.unity` (NOT the subscene — top-level).
2. Hierarchy → right-click → Create Empty. Rename to `BattleHud`.
3. Add Component → `UI Document`.
4. UI Document fields:
   - **Panel Settings**: drag `Assets/UI/DemoHudPanelSettings.asset` (reuse existing).
   - **Source Asset**: drag `Assets/UI/BattleHud.uxml`.
5. Add Component → `Battle Hud Controller` (the script from Task 12).
6. Save the scene (Cmd+S).

- [ ] **Step 4: Verify file existence**

Agent runs:
```bash
ls Assets/UI/BattleHud.uxml Assets/UI/BattleHud.uss
grep -c "BattleHud.uxml" Assets/Scenes/BattleScene.unity
grep -c "BattleHudController" Assets/Scenes/BattleScene.unity
```

Expected: both files exist; both greps return ≥ 1.

- [ ] **Step 5: PlayMode verification**

1. Open `Assets/Scenes/BattleScene.unity`, press Play (CountPerSide still 10).
2. Top-left of Game view: see two coloured labels "Red: 10" and "Blue: 10" decrementing as soldiers die.
3. Once one side reaches 0, the yellow "RED WINS" / "BLUE WINS" banner appears centered below the counts.
4. If counts stay at 0 throughout: BattleHudController found the client world too early or the EntityQuery isn't matching. Check: ghost soldiers should have both `Soldier` and `Team` on the client (verify in Entities Hierarchy → ClientWorld).

- [ ] **Step 6: Commit**

```bash
git add Assets/UI/BattleHud.uxml Assets/UI/BattleHud.uss Assets/UI/BattleHud.uxml.meta Assets/UI/BattleHud.uss.meta Assets/Scenes/BattleScene.unity
git commit -m "feat(battle): BattleHud UI assets and scene wiring"
```

---

## Task 14: Scale up to 10,000 per side

**Files:**
- Modify: `Assets/Scenes/BattleScene/BattleSub.unity` — set `Count Per Side = 10000`

This is the actual stress test. Everything must already work correctly at 10/side; this step only proves DOTS scale.

- [ ] **Step 1: Bump the count**

User action:
1. Open `Assets/Scenes/BattleScene.unity`.
2. Enter the subscene (`BattleSub`).
3. Select `BattleConfig` GameObject. Inspector → `Battle Config Authoring` → set **Count Per Side** to `10000`.
4. The grid will be `ceil(sqrt(10000)) = 100` rows × 100 cols. Default spacing `1.5` makes each block 150m × 150m. Adjust `RedCenter` / `BlueCenter` so the blocks don't overlap — recommended `(-90, 0, 0)` and `(90, 0, 0)` (a 180m gap centered on origin).
5. (Optional) Move the camera back: select `Main Camera` in the top-level scene → Position `(0, 200, -120)`, Rotation `(55, 0, 0)`.
6. Save subscene + scene.

- [ ] **Step 2: PlayMode verification**

1. Press Play. Expect a 1-3 second hitch as `BattleSpawnSystem` instantiates 20,000 entities.
2. Watch: 20,000 cubes spawn in two opposing blocks → march together → clash → cumulative casualties.
3. HUD counts should decrease (slowly at first, faster as combat density rises in the center).
4. Open Window → Analysis → Profiler. Watch the **CPU Usage** module. Expect `GhostSendSystem` to be a hot spot (server replication of 20k ghosts). If frame time is > 200 ms / frame consistently, consider reducing CountPerSide to 5000 and noting the result.

- [ ] **Step 3: Capture an observation note (optional but recommended)**

Append a one-paragraph observation to the spec doc covering: actual hitch on spawn, steady-state frame time, GhostSendSystem CPU share, and whether the battle converges to a winner in a reasonable time. This gives the next session real numbers to reason about.

- [ ] **Step 4: Final commit**

```bash
git add Assets/Scenes/BattleScene/BattleSub.unity
# If you edited the camera in BattleScene.unity, include it too:
git add Assets/Scenes/BattleScene.unity
git commit -m "$(cat <<'EOF'
feat(battle): scale up to 10000 per side

CountPerSide bumped from 10 (verification value) to 10000 — the
spec's stress-test target. Spawn centers spread to (-90,0,0) and
(90,0,0) so the 100x100 grid blocks don't overlap. Camera pulled
back to frame the battlefield.
EOF
)"
```

---

## Done

The branch `feat/two-army-battle` now contains:

1. Spec doc (Task 0 commit `01d4a05`)
2. Soldier components + authoring (Task 1)
3. BattleConfig + authoring (Task 2)
4. `PlayerSpawnSystem` guard (Task 3)
5. Soldier prefab (Task 4)
6. BattleScene + BattleSub (Task 5)
7. BattleSpawnSystem + Unity.Physics manifest (Task 6)
8. TargetingSystem (Task 7)
9. SoldierMovementSystem (Task 8)
10. MeleeDamageSystem with NativeStream reduce (Task 9)
11. DeathSystem (Task 10)
12. BattleHudViewModel (Task 11)
13. BattleHudController (Task 12)
14. UI assets + scene wiring (Task 13)
15. 10k-per-side stress test (Task 14)

Open follow-ups (not in this plan):
- Update `CLAUDE.md` package versions (NetCode is 1.13.0, not 1.11.0 as listed).
- A "Restart" HUD button so we don't reload the scene to iterate on tuning.
- Position-only `LocalTransform` ghost variant if profiling shows snapshot bandwidth is the bottleneck.
- `.gitignore` entry for `Assembly-CSharp*.csproj.lscache`.
