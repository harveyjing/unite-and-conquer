# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**Squad-formation two-army battle working end-to-end, with per-soldier health bars.** Unity 6000.4.1f1 / URP.

- **SampleScene** — original Netcode demo: `ClientServerBootstrap` (`GameBootstrap`) + `GoInGame` RPC handshake, predicted player ghost (WASD), `DemoHudController` UI Toolkit HUD with ECS↔UI data binding.
- **BattleScene** — squad-based melee battle. Soldiers are organized into **Squad** entities (rectangular `Rows × Cols` formations); squads target the nearest enemy squad, advance/rotate as a unit, and soldiers follow assigned slots. As soldiers die the formation **compacts** (shrinks rows, reassigns slots). Each soldier shows a client-side health bar. `BattleHudController` counts ghost soldiers per team client-side and shows a winner banner. `BattleCameraMono` provides scroll-wheel zoom and middle-mouse pan.

## Unity project

- **Editor:** 6000.4.1f1 · **URP:** 17.4.0 · **Input System:** 1.19.0 · **API:** .NET Standard 2.1
- **Pinned DOTS packages:** `com.unity.entities.graphics` 6.4.0 (Entities 1.4.x) · `com.unity.netcode` 1.13.1 · `com.unity.physics` 1.4.6
- **Open via** Unity Hub → Add → select repo root. Do not use `unity-editor` CLI — use Unity MCP instead.
- **Assemblies:** code lives in the `Demo` assembly (`Assets/Scripts/Demo/Demo.asmdef`); tests in `Demo.Tests.EditMode` (`Assets/Tests/EditMode/`). Adding a new `using` for a Unity package may require adding it to `Demo.asmdef`'s `references`.
- **Tests:** Window → General → Test Runner → EditMode (or run via Unity MCP). Tests subclass `EcsTestsBase`, which spins up a bare `World` per test and **deliberately avoids the `Unity.Entities.Tests` package** — it provides `CreateBattleConfig`/`CreateSquad`/`CreateSoldier` builders and `CreateAndUpdateSystem<T>()` (ticks one system and completes its job dependency so assertions see written data). To run a single test, filter by class/method name in the Test Runner. Systems are unit-tested in isolation (no full netcode world).

## Design vision

**Large-scale Three Kingdoms strategy MMO** inspired by 热血三国 (Re Xue San Guo). See [docs/basic-idea.md](docs/basic-idea.md) for the original brief.

**Core differentiator (non-negotiable):** every soldier, mount, siege engine, and supply wagon is an individual simulated entity — *not* an abstract stack count. This is why DOTS is the stack. **Every architectural decision must answer**: *does this scale to tens of thousands of individually simulated entities per battle, replicated to mobile clients?*

Reject: MonoBehaviour-per-soldier, GameObject-heavy hierarchies, per-entity managed allocations, single-threaded per-entity update loops.

**Target:** mobile-first MMO (iOS/Android primary). Server-authoritative; clients are thin renderers + input + prediction. Treat the client as untrusted. Design for cellular networks and 1–4 GB devices from day one.

Reference mechanics (anchor points, not a spec): four resources (wood/grain/stone/iron + gold), ~12 unit classes with rock-paper-scissors counters, formation cyclic-advantage chain, multi-tier general gacha (~16 equipment slots), persistent 500×500 grid world with terrain/tile occupation, alliances with shared tech and siege warfare.

## Tech stack

- **Unity Entities (ECS)** — simulation backbone; **Burst + Jobs** — hot paths; **Subscenes + Baking** — authoring → runtime
- **Netcode for Entities 1.11.0** (pinned) — server-authoritative replication. Lockstep rejected: Burst/SIMD floats are not bit-deterministic across ARM vs x86.
- **Server orchestration intent:** containerized dedicated servers on Kubernetes via Agones. Zone-authoritative servers; AOI replication via `GhostDistanceImportance` + spatial hash jobs; per-client relevancy sets with capped snapshot history and `MaxSendEntities`.

## Mobile constraints to honor

- 30 Hz tick at 30/60 fps render; wider interpolation windows on low-end devices.
- Schedule work across frames (`frameCount % interval`, chunk-per-frame jobs, staggered spawns).
- Coalesce ghost component fields; use `InternalBufferCapacity(0)` on usually-empty dynamic buffers.
- Prefer Vulkan on Android; keep a Burst feature-flag fallback for problematic iOS devices.

## Current code structure

All code lives under `Assets/Scripts/Demo/` (`Demo` namespace):

- **`Bootstrap/`** — `ClientServerBootstrap` subclass, `GoInGame` RPC handshake
- **`Authoring/`** — MonoBehaviour authoring components and their bakers (SampleScene)
- **`System/`** — Burst `ISystem` implementations (SampleScene player systems)
- **`UI/`** — `DemoHudViewModel`, `DemoHudController`, `RespawnRequest`, `SpawnObstacleRequest`
- **`CameraFollowMono.cs`** / **`BattleCameraMono.cs`** — MonoBehaviour cameras; `CameraFollowMono` bridges ECS→camera (SampleScene); `BattleCameraMono` provides scroll-zoom + middle-mouse pan (BattleScene)
- **`Battle/Authoring/`** — `SoldierAuthoring` (bakes `Soldier`, `Team`, `SoldierColor`, `Health`, `AttackStats`, `SquadMembership`, kinematic `PhysicsCollider`), `BattleConfigAuthoring` (singleton `BattleConfig` that drives every battle system — squad shape, behavior, combat tuning, colors, health-bar prefab), `HealthBarAuthoring`, `SquadComponents.cs` (the `Squad`, `SquadTarget`, `SquadMember` buffer, `SquadMembership` components — all server-only)
- **`Battle/SquadGeometry.cs`** — static Burst math (slot offsets, engagement distance, rows-for-alive-count) shared by spawn/movement/follow/compaction systems; no entity access, unit-tested directly
- **`Battle/System/`** — server battle pipeline + client-only health-bar systems (see Battle system pipeline below)
- **`Battle/UI/`** — `BattleHudController`, `BattleHudViewModel` (same pattern as `DemoHudController`)
- **`Assets/Tests/EditMode/`** — EditMode unit tests for the squad systems and `SquadGeometry`

Scenes: `Assets/Scenes/SampleScene.unity` + subscene `EcsDemoSub.unity`; `Assets/Scenes/BattleScene.unity` + subscene `BattleSub.unity`.

UI assets: `Assets/UI/DemoHud.{uxml,uss}`, `Assets/UI/BattleHud.{uxml,uss}`.

Pattern: **tag/component → authoring+baker → Burst `ISystem`**. Ghost prefab in `Assets/Prefabs/`. NetCode settings in `ProjectSettings/`.

## Battle system pipeline

The simulation is **squad-driven**: a `Squad` entity carries the formation shape (`Rows × Cols`, `Spacing`, `Team`) and a `SquadMember` buffer (one slot per formation position, `Entity.Null` = empty). Each soldier holds a `SquadMembership` (`Squad` + `SlotIndex`). Squads pick targets and move; individual soldiers just chase their assigned slot's world position. There is no per-soldier targeting.

Server execution order (all `ServerSimulation`, `SimulationSystemGroup`):

1. **`SquadTargetingSystem`** — throttled to every `TargetRefreshIntervalTicks` server ticks. Snapshots all squads into a `NativeArray<SquadSnapshot>` (`SnapshotJob`), then `AssignTargetJob` sets each squad's `SquadTarget` to the nearest enemy **squad** by squared distance. O(squads²), cheap because squad count is small. Requires `NetworkTime`.
2. **`SquadMovementSystem`** (`UpdateAfter(SquadTargetingSystem)`) — rotates each squad toward its target (`SquadRotationSpeed`) and advances it (`SquadAdvanceSpeed`) until front ranks are within `SquadGeometry.EngagementDistance`.
3. **`SoldierSlotFollowSystem`** (`UpdateAfter(SquadMovementSystem)`) — moves each soldier toward its slot's world position (`SquadGeometry.SlotLocalOffset` transformed by the squad's `LocalTransform`) at `SoldierStepSpeed`.
4. **`MeleeDamageSystem`** (`UpdateAfter(SoldierSlotFollowSystem)`) — scatter/gather: `WriteDamageJob` (`IJobChunk`) finds nearby enemies via the squads' `SquadMember` buffers (`BufferLookup<SquadMember>`) and writes damage into a `NativeStream`; `ReduceDamageJob` (serial `IJob`) drains the stream and decrements `Health`. Stream avoids concurrent writes to the same victim.
5. **`DeathSystem`** (`UpdateAfter(MeleeDamageSystem)`) — destroys entities with `Health.Current <= 0` via ECB (and clears their `SquadMember` slot).
6. **`SquadCompactionSystem`** (`UpdateAfter(DeathSystem)`) — throttled by `CompactionIntervalTicks`. Reclaims dead slots: shrinks `Squad.Rows` to `SquadGeometry.RowsForAliveCount`, repacks survivors into low slot indices, and rewrites each survivor's `SquadMembership.SlotIndex`. Requires `NetworkTime`.

**`BattleSpawnSystem`** runs once on the first frame (no ordering attribute needed — gated by `RequireForUpdate<BattleConfig>`): creates `2 * SquadsPerTeam` `Squad` entities laid in a line per team, bulk-spawns soldiers via `EntityManager.Instantiate`, then wires `SquadMembership` + `SquadMember` buffers and initializes per-soldier data with parallel jobs. Sets `state.Enabled = false` afterward. ECB-per-entity would cost hundreds of ms at scale.

Client-only (both `ClientSimulation`, `PresentationSystemGroup`):

- **`HealthBarSpawnSystem`** — instantiates the `HealthBarPrefab` for each ghost soldier that lacks a bar.
- **`HealthBarUpdateSystem`** (`UpdateAfter(HealthBarSpawnSystem)`) — positions each bar above its soldier (`HealthBarHeightOffset`) and drives the `HealthBarFill` material property from replicated `Health.Current` / `BattleConfig.MaxHealth`.

**Physics note:** `SoldierAuthoring` still bakes a kinematic `PhysicsCollider` (`Soldier.Layer = 1u << 1`, zero `PhysicsVelocity`, `PhysicsMass.CreateKinematic`, `PhysicsWorldIndex(0)`). **No battle system currently queries the physics world** — squad-level distance targeting replaced the old `PhysicsWorldSingleton`/`NearestEnemyCollector` broadphase approach, so the collider is presently vestigial (the `Soldier.Layer` doc comment is stale). Don't reintroduce a physics-broadphase dependency without re-checking this.

## UI Toolkit conventions

Runtime data binding in Unity 6000.4.1f1:
- Interface is **`INotifyBindablePropertyChanged`** (`INotifyBindingPropertyChanged` does not exist).
- Properties need `[CreateProperty]` from `Unity.Properties`.
- `PanelSettings.themeUss` only accepts `.tss`; load USS via `<Style src="DemoHud.uss" />` inside `<ui:UXML>`.
- `DemoHudController` is the template for future panels: lazy client-world find, `EntityQuery` cache in `Update`, short-circuit setters on the view model.

## DOTS conventions

- Authoring MonoBehaviours suffixed `Authoring`; `Baker<T>` lives alongside.
- GameObject content in **Subscenes**, not legacy Scenes.
- Prefer **`ISystem`** (Burst-compatible) over `SystemBase` unless managed data is required.
- Explicit `SystemGroup` ordering — never rely on defaults for simulation-critical work.
- **`IComponentData`** (unmanaged) by default; **`IBufferElementData`** for per-entity dynamic arrays; tag components for state flags.
- Hot loops via **`IJobEntity` / `IJobChunk`** + Burst; no managed allocations in per-frame code.

## Known risks

- **Cross-arch nondeterminism** (ARM vs x86) → server-authoritative only, no lockstep.
- **Netcode for Entities host-migration** limitations (prespawned ghost sync errors, ID reallocation) — design failover paths; don't assume seamless migration.
- **Burst on iOS** has historical crash reports — keep a feature-flag fallback.
- No public benchmark for Netcode for Entities serving 1k–10k entities to mobile cellular clients — build incremental load tests early.

## Unity Editor operations via MCP

**All Editor interactions are performed via Unity MCP (`mcp__unity-mcp__*`) — never the `unity-editor` CLI, no manual clicks in the Inspector, no menu navigation, no manual validation.**

After every code change or Editor command, call `Unity_GetConsoleLogs` and confirm zero errors before reporting done. For visual changes, capture the scene view via MCP to confirm the result.

At session start, call `Unity_GetConsoleLogs` and confirm `"success": true` before any other work. Surface failures immediately — the Editor is likely not running.

## Required tools

Verify these at session start; surface failures before other work.

| Tool | Purpose |
|------|---------|
| **Unity MCP** (`mcp__unity-mcp__*`) | All Editor operations and validation — see section above |
| **Context7** (`mcp__plugin_context7_context7__*`) | Up-to-date Unity / Entities / Burst / Netcode API docs — use before answering from memory |
| **Firecrawl** (Firecrawl skills) | Web research: MMO architecture, benchmarks, Three Kingdoms references |

**Context7 pinned library IDs:**

| Package | Library ID |
|---------|-----------|
| Unity Entities | `/needle-mirror/com.unity.entities` |
| Netcode for Entities | `/websites/unity3d_packages_com_unity_netcode_1_10_api` (snapshot of 1.10; installed package is **1.13.1** — re-verify version-specific APIs) |
| Unity Burst | `/needle-mirror/com.unity.burst` |
| Unity Collections | `/websites/unity3d_packages_com_unity_collections_2_6` |
| Unity Mathematics | `/websites/unity3d_packages_com_unity_mathematics_1_3` |

Use Context7 for any Unity/Entities/Burst/Netcode API question. Use Firecrawl for anything outside library docs. Use Unity MCP to inspect live Editor state rather than guessing.

**Scope:** Do not write ECS systems or invent file layout without explicit user request. When adding code, read existing patterns first and extend them.

## References

- [docs/basic-idea.md](docs/basic-idea.md) — original brief
- [docs/research/2026-04-08-rxsg-gameplay-mechanics.md](docs/research/2026-04-08-rxsg-gameplay-mechanics.md) — 热血三国 system breakdown
- [docs/research/2026-04-08-dots-netcode-mobile-mmo.md](docs/research/2026-04-08-dots-netcode-mobile-mmo.md) — DOTS + Netcode for mobile MMOs (late 2025); re-verify version-specific claims before relying on them
- `docs/superpowers/specs/` + `docs/superpowers/plans/` — design specs and implementation plans per feature. Most recent: `2026-05-19-squad-formation-battle-design.md` (squad architecture) and `2026-05-24-per-soldier-health-bar-design.md` (health bars) — read these before changing battle systems.
