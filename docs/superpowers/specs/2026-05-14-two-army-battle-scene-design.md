# Two-Army Battle Scene Design

**Date:** 2026-05-14
**Status:** Draft (pending implementation plan)

## Purpose

Add a second top-level scene (`BattleScene`) that spawns two opposing armies — 10,000 red soldiers vs 10,000 blue soldiers — which immediately march toward each other and resolve melee combat until one side is wiped out. This is a **DOTS scale stress-test**, not yet a game feature. Its value is:

1. **Proves the "every soldier is an individual entity" thesis from CLAUDE.md** at 20k entities, server-authoritative with ghost replication on a single machine.
2. **Establishes the spatial-query pattern** (Unity.Physics broadphase) we will later use for AoE, vision, AI targeting, etc.
3. **Establishes the parallel-write-then-reduce pattern** (`NativeStream<T>`) for any per-entity scatter operation (damage, healing, debuffs).
4. **Establishes a battle HUD** (alive counts, winner banner) parallel to the existing player HUD.

Out of scope: ranged units, armor/damage types, formations beyond a static grid, vision/fog, AI commands, retreat, win/lose meta state, scene loading UI, mobile-target perf optimization. Those are deliberately deferred — the goal is one playable battle.

## Context

The existing demo (see [`CLAUDE.md`](../../../CLAUDE.md)) runs a `ClientServerBootstrap` that spins up both server and client worlds on Play, connects via `GoInGame`, spawns a predicted player ghost, and bridges ECS to a UI Toolkit HUD via `DemoHudController`. The bootstrap, RPC handshake, and HUD-bridge patterns are reused as-is for the battle scene.

Verified package state (`Packages/packages-lock.json`):
- `com.unity.entities` 1.4.4 (transitive via netcode)
- `com.unity.netcode` 1.13.0
- `com.unity.physics` **1.4.6 — already in manifest**, no install needed
- `com.unity.entities.graphics` 6.4.0
- `com.unity.collections` 6.4.0

(CLAUDE.md still says NetCode 1.11.0 — minor doc drift, fix in a separate change.)

Relevant existing code:
- [`Assets/Scripts/Demo/Bootstrap/GameBootstrap.cs`](../../../Assets/Scripts/Demo/Bootstrap/GameBootstrap.cs) — `ClientServerBootstrap` subclass with `AutoConnectPort = 7979`. Reused unchanged.
- [`Assets/Scripts/Demo/Bootstrap/GoInGame.cs`](../../../Assets/Scripts/Demo/Bootstrap/GoInGame.cs) — connection handshake. Reused unchanged.
- [`Assets/Scripts/Demo/Authoring/PrefabSpawnerAuthoring.cs`](../../../Assets/Scripts/Demo/Authoring/PrefabSpawnerAuthoring.cs) — singleton-via-baker pattern. Mirrored by new `BattleConfigAuthoring`.
- [`Assets/Scripts/Demo/System/PlayerSpawnSystem.cs`](../../../Assets/Scripts/Demo/System/PlayerSpawnSystem.cs) — needs a one-line guard (see § "Touch outside Battle folder").
- [`Assets/Scripts/Demo/UI/DemoHudController.cs`](../../../Assets/Scripts/Demo/UI/DemoHudController.cs) and [`DemoHudViewModel.cs`](../../../Assets/Scripts/Demo/UI/DemoHudViewModel.cs) — MonoBehaviour ↔ client-world bridge. Mirrored by new `BattleHudController` / `BattleHudViewModel`. The `FindClientWorld()` helper is duplicated rather than factored, matching existing repo style.

## Decisions

| Decision | Choice | Reason |
|---|---|---|
| Army size | 10k per side, 20k total | The scale at which the "individual entity" thesis is meaningful. Smaller numbers don't exercise the relevant DOTS patterns. |
| Combat model | Melee only — walk to nearest enemy, deal DPS in contact | Simplest model that requires every component (targeting, movement, damage, death). Ranged adds projectile entities — defer. |
| Networking posture | `ClientServerBootstrap` loopback; soldiers are interpolated ghosts | Same as existing demo. Future-proofs the architectural pattern. Real bandwidth ceiling on loopback is GhostSendSystem CPU, not the wire. |
| Nearest-enemy lookup | Unity.Physics broadphase + custom `ICollector<DistanceHit>` | First-party, Burst-perfect, parallel build. True nearest neighbor with zero custom tree code. |
| Soldier body type | Dynamic + Kinematic + gravity-off; CollidesWith=0 (query-only) | Dynamic tree is incrementally maintained as bodies move; static tree would force a full rebuild every tick at 20k moving entities. |
| Damage application | `NativeStream<DamageEvent>` write from parallel `IJobChunk`, single-threaded reduce via `IJob` + `[NativeDisableParallelForRestriction] ComponentLookup<Health>` | Many attackers can target the same victim; naive parallel writes race. `IJobChunk` is required (not `IJobEntity`) so `BeginForEachIndex/EndForEachIndex` brackets each chunk's writes. |
| Targeting cadence | Every 5 server ticks per soldier (modulo `ServerTick % 5`) | Targets don't need to refresh every tick once a soldier is closing on one. Cuts physics-query cost ~5×. |
| Server-only components | `Health`, `AttackStats`, `Target` marked `[GhostComponent(PrefabType = GhostPrefabType.Server)]` | Client doesn't need per-soldier HP; saves bandwidth + per-ghost memory. |
| Replicated components | `Soldier` (tag) and `Team` (1 byte) marked `[GhostComponent(PrefabType = GhostPrefabType.All)]`; `Team.Value` has `[GhostField]` | HUD counts ghosts per team client-side; tag + team is all it needs. |
| Transform variant | Default 3D `LocalTransform` for v1 | Top-down 2D variant would save ~6 bytes/ghost but adds variant-registration code. Revisit only if profiling shows snapshot bandwidth is the bottleneck. |
| Bulk spawn | `EntityManager.Instantiate(prefab, NativeArray<Entity>)` + `IJobParallelFor` to fill values | The bulk path clones at chunk granularity. ECB-per-entity for 20k would be hundreds of ms. |
| Color per soldier | One shared `Soldier.prefab`, per-instance `URPMaterialPropertyBaseColor` set at spawn | Avoids two near-identical prefabs and a doubled ghost prefab table. URPMaterialPropertyBaseColor is recognized by entities.graphics 6.4 with no shader work. |
| Spawn trigger | Auto on scene load when `BattleConfig` baked singleton exists | Zero UI, immediate spectacle. Restart = reload scene. |
| Win condition | HUD shows winner banner client-side when one team's ghost count drops from >0 to 0 | No replicated battle-state singleton. Client-side derivation; one `bool _everSeenAlive` flag per team in the HUD controller. |
| Update group | Plain `SimulationSystemGroup` on server | Soldiers are interpolated (not predicted) — `PredictedSimulationSystemGroup` (used by `PlayerMovementSystem.cs:11`) is wrong here. |

## Architecture

```
┌─────────────────────── BattleScene (top-level) ─────────────────────────┐
│                                                                         │
│   Camera (fixed overhead, e.g. (0, 80, -40) looking at origin)          │
│   Directional Light                                                     │
│                                                                         │
│   GameObject "BattleHud"                                                │
│   ├── UIDocument         (Source Asset = Assets/UI/BattleHud.uxml)      │
│   └── BattleHudController (MonoBehaviour bridge)                        │
│         └── owns BattleHudViewModel (INotifyBindablePropertyChanged)    │
│                ├── RedAliveText, BlueAliveText                          │
│                ├── WinnerText (empty unless game over)                  │
│                └── TickText                                             │
│                                                                         │
│   SubScene (Assets/Scenes/BattleScene/BattleSub.unity)                  │
│   ├── Plane (ground, scaled)                                            │
│   └── GameObject "BattleConfig"                                         │
│         └── BattleConfigAuthoring                                       │
│               └── bakes BattleConfig singleton                          │
│                     { SoldierPrefab, CountPerSide=10000,                │
│                       RedCenter, BlueCenter, Spacing,                   │
│                       SearchRadius, MoveSpeed, AttackRange,             │
│                       Dps, MaxHealth, TargetRefreshIntervalTicks=5,     │
│                       RedColor=(1,0,0,1), BlueColor=(0,0.5,1,1) }       │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                   │
                                   │ ClientServerBootstrap (existing)
                                   │ AutoConnectPort 7979, loopback
                                   ▼
┌──────────────────── Server world ───────────────────────────────────────┐
│                                                                         │
│   SimulationSystemGroup ordering:                                       │
│     BattleSpawnSystem    (one-shot; instantiates 2×CountPerSide,        │
│                           IJobParallelFor fills components,             │
│                           state.Enabled = false at end)                 │
│   ↓                                                                     │
│     TargetingSystem      (IJobEntity, refreshes Target every 5 ticks    │
│                           via PhysicsWorldSingleton.CalculateDistance   │
│                           with NearestEnemyCollector that skips         │
│                           same-team via ComponentLookup<Team>)          │
│   ↓                                                                     │
│     SoldierMovementSystem(IJobEntity, step toward Target.Position by    │
│                           MoveSpeed*dt if dist > AttackRange)           │
│   ↓                                                                     │
│     MeleeDamageSystem    (IJobChunk write into NativeStream<DamageEvent>│
│                           keyed by unfilteredChunkIndex,                │
│                           then single-threaded IJob reduce that         │
│                           applies decrements to Health via              │
│                           [NativeDisableParallelForRestriction]         │
│                           ComponentLookup<Health>)                      │
│   ↓                                                                     │
│     DeathSystem          (destroy entities with Health<=0 via ECB;      │
│                           NetCode auto-despawns the ghost on client)    │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                   │
                                   │ snapshot stream
                                   ▼
┌──────────────────── Client world ───────────────────────────────────────┐
│                                                                         │
│   Receives Soldier+Team+LocalTransform ghosts only (interpolated).      │
│   No client-side simulation systems for battle.                         │
│                                                                         │
│   BattleHudController (MonoBehaviour, polls each Update):               │
│     - lazy-finds client world by name (mirrors                          │
│       DemoHudController.FindClientWorld at DemoHudController.cs:84)     │
│     - queries EntityQuery<Soldier, Team>, counts per team each frame    │
│     - tracks _redEverNonZero, _blueEverNonZero; sets WinnerText when    │
│       one team falls from >0 to 0                                       │
│     - writes formatted strings to BattleHudViewModel                    │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## Components & data layout

### Components on soldiers

```csharp
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct Soldier : IComponentData { }                  // tag, replicated

[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct Team : IComponentData
{
    [GhostField] public int Value;                          // 0=Red, 1=Blue
}

[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct Health : IComponentData { public float Current, Max; }

[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct AttackStats : IComponentData
{
    public float Range;
    public float Dps;
}

[GhostComponent(PrefabType = GhostPrefabType.Server)]
public struct Target : IComponentData { public Entity Value; }
```

Plus standard components: `LocalTransform` (replicated, interpolated, default variant), `PhysicsCollider`, `PhysicsVelocity` (zero, marks as dynamic-tree resident), `URPMaterialPropertyBaseColor` (instance color).

### Singleton

```csharp
public struct BattleConfig : IComponentData
{
    public Entity SoldierPrefab;
    public int    CountPerSide;
    public float3 RedCenter, BlueCenter;
    public float  Spacing;
    public float  SearchRadius;
    public float  MoveSpeed;
    public float  AttackRange;
    public float  Dps;
    public float  MaxHealth;
    public int    TargetRefreshIntervalTicks;
    public float4 RedColor, BlueColor;
}
```

Baked from `BattleConfigAuthoring` MonoBehaviour, same shape as existing `PrefabSpawnerAuthoring`.

### Cross-system shared type

```csharp
public struct DamageEvent { public Entity Victim; public float Amount; }
```

Defined at the top of `MeleeDamageSystem.cs`, following the file-local-types convention (cf. `PlayerCapsule` defined in [`PlayerSpawnSystem.cs:9`](../../../Assets/Scripts/Demo/System/PlayerSpawnSystem.cs)).

## File layout

```
Assets/Scripts/Demo/Battle/
├── Authoring/
│   ├── BattleConfigAuthoring.cs
│   └── SoldierAuthoring.cs        (defines Soldier, Team, Health,
│                                   AttackStats, Target + Baker)
├── System/
│   ├── BattleSpawnSystem.cs
│   ├── TargetingSystem.cs
│   ├── SoldierMovementSystem.cs   (named to distinguish from
│                                   existing PlayerMovementSystem)
│   ├── MeleeDamageSystem.cs       (also defines DamageEvent)
│   └── DeathSystem.cs
└── UI/
    ├── BattleHudController.cs
    └── BattleHudViewModel.cs

Assets/Prefabs/
└── Soldier.prefab                 (cube mesh ~0.3×0.6×0.3, URP-Lit,
                                    GhostAuthoringComponent,
                                    SoldierAuthoring,
                                    PhysicsShapeAuthoring (sphere ~0.3,
                                      BelongsTo=Soldier layer,
                                      CollidesWith=Nothing),
                                    PhysicsBodyAuthoring (Dynamic,
                                      gravity=0, IsKinematic=true))

Assets/UI/
├── BattleHud.uxml
├── BattleHud.uss
└── BattleHudPanelSettings.asset

Assets/Scenes/
├── BattleScene.unity              (top-level scene)
└── BattleScene/
    └── BattleSub.unity            (subscene with Plane + BattleConfig GO)
```

Namespace stays `Demo` for all new files. No `.asmdef` added (per `CLAUDE.md`, everything compiles into `Assembly-CSharp`).

## Touch outside Battle folder

One existing file requires a one-line guard. The current [`PlayerSpawnSystem`](../../../Assets/Scripts/Demo/System/PlayerSpawnSystem.cs) does `SystemAPI.GetSingleton<PrefabSpawner>()` which throws if `PrefabSpawner` is missing. The battle scene has no `PrefabSpawner`, but a client connection still triggers `GoInGameServerSystem` → enqueues a `PlayerCapsule` → `PlayerSpawnSystem` runs and throws.

Fix: add `state.RequireForUpdate<PrefabSpawner>()` to `PlayerSpawnSystem.OnCreate`. If the two RPC handlers (`Respawn.cs`, `SpawnObstacle.cs`) reference the same singleton, apply the same guard. No behavior change in `SampleScene`; battle scene no longer throws on client connect.

## NetCode specifics

### Soldier ghost prefab settings
- `DefaultGhostMode = Interpolated`
- `SupportedGhostModes = Interpolated` (forbid Predicted — keeps ghost type registry small)
- `OptimizationMode = Dynamic`
- `HasOwner = false`
- `UsePreSerialization = true` — caches per-ghost serialized blob per tick; defensive setting for future multi-client scaling.
- `Importance = 1`

### CollisionFilter
- Soldier body: `BelongsTo = 1u<<1`, `CollidesWith = 0`. Non-zero `BelongsTo` is **required** or the broadphase skips the body entirely.
- Targeting query input: `CollidesWith = 1u<<1`.

### Snapshot bandwidth — known risk
20k ghosts × `LocalTransform.Position` (3×16-bit quantized) + `Team` (8-bit) + per-ghost overhead ≈ 240 KB/snapshot at 30 Hz on loopback. NetCode will fragment across packets. On loopback the limit is `GhostSendSystem` CPU, not wire. **Likely to peg one core**; mitigation (importance scaling, lower tick rate, position-only variant) is deferred until profiling confirms a problem.

## Spawn algorithm

In `BattleSpawnSystem.OnUpdate` (one-shot):

1. Read `BattleConfig` singleton.
2. Allocate `NativeArray<Entity> reds = new(CountPerSide, Allocator.TempJob)`, same for blues.
3. `state.EntityManager.Instantiate(config.SoldierPrefab, reds)` — bulk clone.
4. Schedule `InitSoldierJob : IJobParallelFor` over `reds.Length` that writes `LocalTransform`, `Team{0}`, `Health`, `URPMaterialPropertyBaseColor=RedColor` via `ComponentLookup<T>` (with `[NativeDisableParallelForRestriction]`). Position from grid index: `RedCenter + (col*Spacing, 0, row*Spacing)` where `row = i / GridSide`, `col = i % GridSide`, `GridSide = ceil(sqrt(CountPerSide))`.
5. Repeat for blues (mirror Z, BlueColor).
6. `Dispose` the temp arrays as a dependency of the jobs.
7. `state.Enabled = false`.

`AttackStats` and `Target` carry their default-baked values; no need to per-entity write them at spawn.

## Verification

End-to-end happy path:

1. Open `Assets/Scenes/BattleScene.unity` in Editor → Press Play.
2. Console: `Unity_GetConsoleLogs` reports no errors. Expect `BattleSpawnSystem` log: spawned 10000 red + 10000 blue.
3. Editor Hierarchy / Entities Hierarchy window: 20,000 Soldier entities on server world, ~20,000 ghost entities on client world (with replication lag).
4. Camera shows two grid blocks converging.
5. HUD shows RedAlive and BlueAlive counts decreasing over time.
6. One side reaches 0 → HUD displays winner banner.
7. Profiler: `GhostSendSystem` CPU spike is the expected hot spot.

Edge cases to verify:

- Connect a second client (PlayMode Tools → +1 Thin Client): both clients see the same battle (server-authoritative).
- Re-enter Play mode mid-battle: subscene re-bakes, scene reloads cleanly, no leaked entities.
- Tiny config (CountPerSide=10) for fast iteration during development.

Stretch / nice-to-have (not in scope for v1, listed for awareness): a HUD button to "Restart" by re-enabling `BattleSpawnSystem` would let us iterate without scene reloads. Skip for now.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| `GhostSendSystem` saturates one core at 20k ghosts on loopback | High | Profile; consider lower snapshot rate (15 Hz) or position-only variant if CPU-bound. |
| Mass instantiation hitches the frame (50-200ms one-shot) | Medium | Acceptable for stress test; if intolerable, spawn in chunks across frames. |
| `URPMaterialPropertyBaseColor` not picked up if prefab material lacks `[MaterialProperty]` plumbing | Low | Verify by inspecting an existing prefab; add the component explicitly in the baker if needed. |
| Targets that die before `MeleeDamageSystem` runs lead to no-op damage (acceptable race) | Low | Already handled: damage emit checks `LocalTransform` existence, reduce checks `Health` existence on victim. |
| Soldier static-tree rebuild if Kinematic body misconfigured | Low | Verify body lands in dynamic tree via `PhysicsWorld.NumDynamicBodies` log line on first tick. |

## References

- Plan agent output retained in conversation context (chunk-write/single-reduce pattern, `NearestEnemyCollector` shape, broadphase filter semantics).
- [`CLAUDE.md`](../../../CLAUDE.md) — project conventions and DOTS scope.
- [`Assets/Scripts/Demo/UI/DemoHudController.cs`](../../../Assets/Scripts/Demo/UI/DemoHudController.cs) — HUD-bridge reference.
- [`Assets/Scripts/Demo/System/PlayerSpawnSystem.cs`](../../../Assets/Scripts/Demo/System/PlayerSpawnSystem.cs) — file-local component type convention (`PlayerCapsule`).
