# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**Two-army battle simulation working end-to-end.** Unity 6000.4.1f1 / URP.

- **SampleScene** — original Netcode demo: `ClientServerBootstrap` + `GoInGame` RPC handshake, predicted player ghost (WASD), `DemoHudController` UI Toolkit HUD with ECS↔UI data binding.
- **BattleScene** — two-army melee battle: `BattleSpawnSystem` spawns N red vs N blue soldiers (server-only), `TargetingSystem` assigns nearest enemies via Physics broadphase, `SoldierMovementSystem` advances toward targets, `MeleeDamageSystem` deals damage, `DeathSystem` destroys at zero health. `BattleHudController` counts ghost soldiers per team client-side and shows a winner banner. `BattleCameraMono` provides scroll-wheel zoom and middle-mouse pan.

## Unity project

- **Editor:** 6000.4.1f1 · **URP:** 17.4.0 · **Input System:** 1.19.0 · **API:** .NET Standard 2.1
- **Pinned DOTS packages:** `com.unity.entities.graphics` 6.4.0 · `com.unity.netcode` 1.11.0
- **Open via** Unity Hub → Add → select repo root. Do not use `unity-editor` CLI — use Unity MCP instead.
- **Tests:** Window → General → Test Runner. ECS unit tests use `Unity.Entities.Tests` in an `EditMode` assembly. No `.asmdef` files yet — all code compiles into `Assembly-CSharp`.

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
- **`Battle/Authoring/`** — `SoldierAuthoring` (bakes `Soldier`, `Team`, `Health`, `AttackStats`, `Target`, `PhysicsCollider`; kinematic body for broadphase queries only), `BattleConfigAuthoring` (singleton `BattleConfig` that drives all battle systems)
- **`Battle/System/`** — five server-only systems in strict execution order (see Battle system pipeline below)
- **`Battle/UI/`** — `BattleHudController`, `BattleHudViewModel` (same pattern as `DemoHudController`)

Scenes: `Assets/Scenes/SampleScene.unity` + subscene `EcsDemoSub.unity`; `Assets/Scenes/BattleScene.unity` + subscene `BattleSub.unity`.

UI assets: `Assets/UI/DemoHud.{uxml,uss}`, `Assets/UI/BattleHud.{uxml,uss}`.

Pattern: **tag/component → authoring+baker → Burst `ISystem`**. Ghost prefab in `Assets/Prefabs/`. NetCode settings in `ProjectSettings/`.

## Battle system pipeline

Execution order (all `ServerSimulation`, `SimulationSystemGroup`):

1. **`TargetingSystem`** — throttled to every `TargetRefreshIntervalTicks` server ticks; uses `PhysicsWorldSingleton.CalculateDistance` with a custom `NearestEnemyCollector` (`ICollector<DistanceHit>`) to find the closest enemy per soldier. Requires `PhysicsWorldSingleton` and `NetworkTime` singletons.
2. **`SoldierMovementSystem`** (`UpdateAfter(TargetingSystem)`) — moves each soldier toward its target; stops when within `AttackRange`.
3. **`MeleeDamageSystem`** (`UpdateAfter(SoldierMovementSystem)`) — scatter/gather damage pattern: `WriteDamageJob` (parallel `IJobChunk`) writes `DamageEvent`s into a `NativeStream`; `ReduceDamageJob` (serial `IJob`) drains the stream and decrements `Health`. This avoids concurrent writes to the same victim.
4. **`DeathSystem`** (`UpdateAfter(MeleeDamageSystem)`) — destroys entities with `Health.Current <= 0` via ECB.

**`BattleSpawnSystem`** runs once on the first frame (before `TargetingSystem`, no ordering attribute needed): bulk `EntityManager.Instantiate` then `IJobParallelFor` to initialize per-entity data. Sets `state.Enabled = false` after spawning. ECB-per-entity would cost hundreds of ms at 10k+10k.

**Physics setup in `SoldierAuthoring`:** kinematic `PhysicsMass`, zero `PhysicsVelocity`, `PhysicsWorldIndex(0)`. The body lives in the broadphase dynamic tree (so positions update as soldiers move) but physics never integrates it. `Soldier.Layer` (`1u << 1`) is the single source of truth for the collision filter layer bit used by both the collider and `TargetingSystem`'s `CollisionFilter`.

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

**All Unity Editor interactions must go through Unity MCP (`mcp__unity-mcp__*`) — never the `unity-editor` CLI.**

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
| Netcode for Entities | `/websites/unity3d_packages_com_unity_netcode_1_10_api` |
| Unity Burst | `/needle-mirror/com.unity.burst` |
| Unity Collections | `/websites/unity3d_packages_com_unity_collections_2_6` |
| Unity Mathematics | `/websites/unity3d_packages_com_unity_mathematics_1_3` |

Use Context7 for any Unity/Entities/Burst/Netcode API question. Use Firecrawl for anything outside library docs. Use Unity MCP to inspect live Editor state rather than guessing.

**Scope:** Do not write ECS systems or invent file layout without explicit user request. When adding code, read existing patterns first and extend them.

## References

- [docs/basic-idea.md](docs/basic-idea.md) — original brief
- [docs/research/2026-04-08-rxsg-gameplay-mechanics.md](docs/research/2026-04-08-rxsg-gameplay-mechanics.md) — 热血三国 system breakdown
- [docs/research/2026-04-08-dots-netcode-mobile-mmo.md](docs/research/2026-04-08-dots-netcode-mobile-mmo.md) — DOTS + Netcode for mobile MMOs (late 2025); re-verify version-specific claims before relying on them
