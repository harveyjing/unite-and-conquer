# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**DOTS stack with player character control working.** Unity 6000.4.1f1 project with URP. The DOTS packages are installed, baking pipeline is functional, and a player-controlled capsule demo (`Assets/Scenes/SampleScene/EcsDemoSub.unity`) is running: WASD movement with isometric input projection, exponential-smoothed velocity, and a MonoBehaviour camera follower bridging ECS world data to the Unity Camera. Re-run `/init` once the first real game systems (combat, city, resources) begin landing.

## Unity project

- **Editor version:** 6000.4.1f1
- **Render pipeline:** URP 17.4.0
- **Scripting backend:** not yet set (will need IL2CPP for mobile builds)
- **API compatibility:** .NET Standard 2.1 (`apiCompatibilityLevel: 6`)
- **Input System:** 1.19.0 (new Input System package)
- **Pinned DOTS packages:** `com.unity.entities.graphics` 6.4.0 · `com.unity.netcode` 1.11.0

Opening the project: open Unity Hub → Add → select this repo root. Do not open via `unity-editor` CLI until a `-projectPath` wrapper script exists.

Running tests: Unity Test Runner (Window → General → Test Runner). ECS unit tests use `Unity.Entities.Tests` base classes inside an `EditMode` test assembly. No `.asmdef` files exist yet — all code compiles into the default `Assembly-CSharp` assembly.

## Design vision

The game is a **large-scale Three Kingdoms strategy MMO inspired by 热血三国 (Re Xue San Guo / "Passion Three Kingdoms")**: city building, generals/heroes, a four-resource economy, alliances, siege warfare, and a persistent grid world with tile-level territorial control. See [docs/basic-idea.md](docs/basic-idea.md) for the original brief.

**Core differentiator (non-negotiable):** every soldier, mount, siege engine, and supply wagon is an individual simulated entity with position, state, and behavior — *not* an abstract stack count as in the reference game. This single design choice is the reason Unity DOTS is the chosen stack, and **every architectural decision must be measured against**: *does this still scale to tens of thousands of individually simulated entities per battle, replicated to mobile clients?*

Mechanics inherited from the reference (anchor points, not a spec):

- Four resources: wood, grain, stone, iron, plus gold from city tax; population/tax interactions.
- ~12 unit classes (spear / shield / archer / light-heavy-archer cavalry / crossbow / battering ram / trebuchet / supply wagon / scouts) with rock-paper-scissors counter multipliers.
- Formation cyclic-advantage chain layered on top of unit counters.
- Multi-tier general (hero) gacha with ~16 equipment slots and set bonuses.
- Persistent grid world (reference uses 500×500, 13 provinces) with terrain types, tile occupation, raiding, and named-city tiers.
- Alliances with shared tech, resource transport, and coordinated siege warfare.

Reject any pattern that breaks the entity-scaling goal: MonoBehaviour-per-soldier, GameObject-heavy hierarchies, per-entity managed allocations, single-threaded per-entity update loops.

## Platform & target

- **Online MMO, mobile-first.** iOS and Android are the primary targets. Desktop and tablet are secondary at most.
- **Persistent server-authoritative world.** Clients are thin renderers + input + prediction. Treat the client as untrusted.
- Design for cellular networks, thermal throttling, and devices with **1–4 GB shared CPU/GPU memory** from day one — not as a late-stage optimization pass.

## Tech stack

- **Unity Entities (ECS)** — simulation backbone.
- **Burst + Jobs** — hot-path code.
- **Subscenes + Baking** — authoring → runtime entity conversion.
- **Netcode for Entities 1.11.0** — replication (pinned).
- **Server-authoritative model.** Lockstep is rejected: Burst/SIMD floating-point is *not* bit-deterministic across ARM (mobile) vs x86 (server).
- **Anticipated server orchestration:** containerized dedicated servers on Kubernetes via **Agones**. Planning assumption — confirm once networking layer lands.

## Server / world architecture intent

Guiding patterns for when code starts landing — not yet implemented:

- **Zone / region authoritative servers.** Each zone owns its entities; authority transfers to adjacent zones via compact handoff messages.
- **Area-of-interest replication** via `GhostDistanceImportance` + `GhostDistancePartitioningSystem`, on a tile/chunk grid sized to gameplay (precise tile size TBD).
- **Spatial partition** (spatial hash / voxel grid) implemented as Burst-compiled jobs for neighbor queries, culling, and AOI.
- **Per-client relevancy sets**, snapshot history limited (target 6–16 entries vs defaults), `MaxSendEntities` capped, delta compression on, adaptive `MaxSendRate` per client class.
- **Anti-cheat baseline:** server authority + selective replication / fog-of-war.

## Mobile constraints to honor in design

- **Tickrate slightly below render framerate** (e.g., 30 Hz tick at 30/60 fps render). Use wider interpolation windows on low-end devices.
- **Schedule work across frames** — `frameCount % interval`, chunk-per-frame jobs, staggered spawns. Never pile all the work into a single frame.
- **Coalesce frequently-updated fields** into compact ghost component structs; minimize the replicated component set per ghost.
- Use `InternalBufferCapacity(0)` on dynamic buffers when payloads are usually empty.
- Watch draw calls and batching aggressively; prefer Vulkan on Android where available.
- Plan a **Burst feature-flag fallback** for problematic iOS devices (historical Burst-related crashes have been reported).

## Current code structure

The only ECS code is a minimal rotation demo in `Assets/Scripts/Demo/`:

- `RotateTag.cs` — tag `IComponentData` (empty struct, marks entities to rotate)
- `CapsuleDemoAuthoring.cs` — `MonoBehaviour` + nested `Baker<T>` that adds `RotateTag` at bake time
- `CapsuleRotateSystem.cs` — `[BurstCompile] partial struct … : ISystem` that queries `LocalTransform` + `RotateTag` and rotates around X each frame

This trio is the established pattern: tag → authoring/baker → Burst `ISystem`. **All new code should follow this pattern and extend these files or sit alongside them** rather than introducing parallel conventions.

NetCode project settings landed automatically: `EntitiesClientSettings.asset`, `NetCodeClientAndServerSettings.asset`, and `NetCodeServerSettings.asset` live in `ProjectSettings/`.

## DOTS conventions to follow when code is added

- Authoring MonoBehaviours suffixed `Authoring`; their `Baker<T>` lives alongside.
- GameObject content lives in **Subscenes**, not legacy Scenes — ECS is incompatible with the legacy scene system for runtime data.
- Prefer **`ISystem`** (Burst-compatible) over `SystemBase` unless managed data is genuinely required.
- Group systems explicitly into `SystemGroup`s; do not rely on default ordering for simulation-critical work.
- Components are **`IComponentData`** (unmanaged) by default; **`IBufferElementData`** for per-entity dynamic arrays (e.g., a unit's order queue); tag components for state flags.
- Hot loops go through **`IJobEntity` / `IJobChunk`** with Burst; avoid managed allocations in per-frame system code.

## Known risks not to forget

- **Cross-architecture nondeterminism** (ARM vs x86 with Burst/SIMD) → rules out lockstep; server-authoritative only.
- **Netcode for Entities host-migration limitations** (prespawned ghost sync errors, ID reallocation) — design failover paths around these, do not assume seamless host migration.
- **Burst on iOS** has historical crash reports on some devices — keep a feature-flag fallback path.
- **No public benchmark** demonstrates Netcode for Entities serving 1k–10k actively updated entities per world to mobile cellular clients. Build incremental load tests early (synthetic clients, cellular emulation, AOI saturation).

## Scope discipline

- **Do not write ECS systems or invent file layout without explicit user request.** The user drives scaffolding decisions.
- When asked to add code, *first* read whatever exists at that time and extend existing patterns rather than introducing parallel ones.
- Use **`ctx7`** for Unity / Entities / Burst / Netcode for Entities API questions before answering.
- **Whenever you need information from the internet, use Tavily** (`tvly` CLI / Tavily skills) for broader web research — MMO architecture patterns, mobile performance, Three Kingdoms gameplay references, community benchmarks, anything outside library API docs.

## References

- [docs/basic-idea.md](docs/basic-idea.md) — the original brief.
- 热血三国 (Re Xue San Guo) — gameplay reference; see the Baidu Baike entry linked from `docs/basic-idea.md`.
- [docs/research/](docs/research/) — dated, point-in-time deep-research snapshots with cited sources. Treat as reference, not spec — re-verify before relying on version-specific claims. Currently includes:
  - [docs/research/2026-04-08-rxsg-gameplay-mechanics.md](docs/research/2026-04-08-rxsg-gameplay-mechanics.md) — full breakdown of 热血三国 systems (city, resources, generals, units, combat formulas, world map, alliances, monetization).
  - [docs/research/2026-04-08-dots-netcode-mobile-mmo.md](docs/research/2026-04-08-dots-netcode-mobile-mmo.md) — Unity DOTS + Netcode for Entities for mobile MMOs as of late 2025: server architecture, AOI, mobile constraints, known limitations, alternatives.
