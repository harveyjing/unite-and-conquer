# Netcode demo — design

**Date:** 2026-04-25
**Scope:** Convert the existing single-player capsule demo (`Assets/Scenes/SampleScene/EcsDemoSub.unity`) into a server-authoritative, predicted-ghost player using Netcode for Entities. This establishes the input → command → predicted-server → snapshot → render loop that the rest of the game (units, generals, combat) will build on.

**Non-goals:**
- Multi-client testing in the same play session (no Multiplayer Play Mode package).
- AI-driven entities or scale stress.
- Automated tests (matches current project state — no `.asmdef` test assemblies exist).
- asmdef splitting for client/server code separation.

## Context

Demo today:
- `EcsDemoSub.unity` subscene contains one `CapsuleDemoAuthoring` GameObject baking into a `PlayerTag` + `PlayerMovementData` entity.
- `PlayerInputSystem` (managed, default world) reads `Keyboard.current`, isometric-projects WASD/arrows, writes a `PlayerInputData` singleton.
- `PlayerMovementSystem` (Burst `ISystem`) reads the singleton and integrates `LocalTransform` with exponential velocity smoothing.
- `CameraFollowSystem` writes a `CameraTargetData` singleton; `CameraFollowMono.LateUpdate` reads it, already prefers a world whose name contains `"client"` (architecture was anticipating this addition).

Packages installed:
- `com.unity.netcode` **1.13.0** (CLAUDE.md says 1.11.0 — note for an unrelated CLAUDE.md update).
- `com.unity.multiplayer.center` 1.0.1.
- NetCode `ProjectSettings` assets (`EntitiesClientSettings.asset`, `NetCodeClientAndServerSettings.asset`, `NetCodeServerSettings.asset`) are present.

## Decisions

1. **Topology:** server-authoritative single player, predicted ghost. *Reason:* smallest change that exercises the full Netcode-for-Entities loop; foundation every later entity will reuse.
2. **Spawn flow:** server-spawned on connect (one ghost per `NetworkId`). *Reason:* canonical Netcode pattern, what the MMO needs, sidesteps the pre-spawned-ghost host-migration risk CLAUDE.md flags.
3. **Run mode:** `ClientServerBootstrap` override that respects the `PlayMode Tools` window — defaults to ClientAndServer in one process via in-memory transport, but can flip to Server-only or Client-only without code changes.
4. **Prediction:** the player is a *predicted* ghost (not interpolated). Input-driven characters require prediction or they feel laggy.
5. **Input pattern:** `IInputComponentData` (Netcode 1.x modern path) on the player ghost — Netcode auto-generates the input buffer and replicates client→server.
6. **Camera:** `CameraFollowSystem` runs in the client world only and follows the entity with `PlayerTag` + `GhostOwnerIsLocal`. `CameraFollowMono` is unchanged.

## Architecture

Three Netcode-for-Entities worlds may exist depending on PlayMode mode:

- **Client world** — reads input → fills `IInputComponentData` on locally-owned ghost → predicts simulation → renders.
- **Server world** — accepts connections → spawns a player ghost per `NetworkId` → simulates authoritatively → ships snapshots.
- **ClientAndServer (default)** — both above in one process, in-memory transport.

`PlayerCapsule` is a predicted ghost. `EcsDemoSub.unity` stays as world content (lighting, ground); the capsule GameObject moves to a separate prefab asset that the server-side spawner references. The ghost prefab entity is baked into **both** client and server worlds (clients need it to materialize incoming ghosts); only the server calls `Instantiate`. The client-side spawn is handled automatically by Netcode's snapshot delivery.

## Components

**New (`IComponentData` unless noted):**

- `PlayerInput : IInputComponentData` — `float2 MoveAxis` (already isometric-projected, in world XZ). Replaces the `PlayerInputData` singleton. Lives on the player ghost. Netcode auto-generates the input buffer & replicates.
- `PlayerSpawner : IComponentData` — server-side singleton. Field: `Entity Prefab` (baked reference to the `PlayerCapsule` prefab). Created by `PlayerSpawnerAuthoring` placed in the subscene.

**Modified:**

- `PlayerMovementData` — fields unchanged (`MoveSpeed`, `Acceleration`, `Velocity`). `Velocity` becomes `[GhostField]` so client rollback restores it from server snapshots.
- `LocalTransform` on the player ghost — `[GhostField]` for `Position` (and `Rotation`, via the default ghost variant).

**Deleted:**

- `PlayerInputData` (singleton struct).
- `PlayerInputSystem`'s singleton creation in `OnCreate`. The system itself is replaced by `PlayerInputCollectionSystem` (see below).

## Data flow

**Per-frame (predicted single player):**

```
Client world                                      Server world
─────────────                                     ─────────────
PlayerInputCollectionSystem
  reads Keyboard.current,
  isometric-projects, writes
  PlayerInput on ghost where
  GhostOwnerIsLocal
        │
        ▼
[Netcode auto: command buffered & sent]──────────►[command applied to ghost]
        │                                                   │
        ▼                                                   ▼
PlayerMovementSystem                       PlayerMovementSystem
  (PredictedSimulationSystemGroup)           (PredictedSimulationSystemGroup)
  reads PlayerInput from buffer,             same code, same Burst struct,
  integrates LocalTransform,                 authoritative integration
  smooths Velocity
        │
        ▼
[Client snapshot received]◄───────────────[snapshot serialized w/ GhostFields]
        │
        ▼
[Client rolls back & re-predicts if mismatched]
        │
        ▼
CameraFollowSystem (PresentationSystemGroup, client-only)
  finds PlayerTag + GhostOwnerIsLocal, writes CameraTargetData
        │
        ▼
CameraFollowMono (LateUpdate)
  reads CameraTargetData from client world, moves Camera
```

**Server-spawn flow (one-shot per connect):**

```
Server: PlayerSpawnSystem (server-only)
  detects new entity with NetworkId + no NetworkStreamInGame tag
  → adds NetworkStreamInGame
  → instantiates PlayerSpawner.Prefab
  → sets GhostOwner = NetworkId on the new entity
  → (PlayerTag, PlayerMovementData already on the prefab via baker)
```

**Isometric projection placement:** stays in `PlayerInputCollectionSystem` (client-side). The camera basis is a client concern; the server should not know about cameras. The transmitted `PlayerInput.MoveAxis` is the already-projected world-XZ direction.

## File layout

```
Assets/
├── Prefabs/
│   └── PlayerCapsule.prefab            NEW — capsule mesh + GhostAuthoringComponent + CapsuleDemoAuthoring
├── Scenes/SampleScene/EcsDemoSub.unity MODIFIED — capsule GameObject removed; PlayerSpawnerAuthoring added
└── Scripts/
    ├── Bootstrap/
    │   └── GameBootstrap.cs            NEW — overrides ClientServerBootstrap, respects RequestedPlayType
    ├── Net/
    │   ├── PlayerSpawnerAuthoring.cs   NEW — bakes PlayerSpawner singleton holding the prefab Entity
    │   ├── PlayerSpawner.cs            NEW — IComponentData { Entity Prefab; }
    │   └── PlayerSpawnSystem.cs        NEW — [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)], spawns one ghost per new connection
    └── Demo/
        ├── PlayerInput.cs              NEW — IInputComponentData { float2 MoveAxis; }
        ├── PlayerInputCollectionSystem.cs  NEW (replaces PlayerInputSystem) — [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)], fills PlayerInput on owned ghost
        ├── PlayerMovementData.cs       MODIFIED — Velocity becomes [GhostField]
        ├── PlayerMovementSystem.cs     MODIFIED — [UpdateInGroup(typeof(PredictedSimulationSystemGroup))], reads input from entity (not singleton); runs in both client & server worlds
        ├── PlayerTag.cs                UNCHANGED
        ├── CameraTargetData.cs         UNCHANGED
        ├── CameraFollowSystem.cs       MODIFIED — [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)], query adds WithAll<GhostOwnerIsLocal>()
        ├── CameraFollowMono.cs         UNCHANGED
        ├── CapsuleDemoAuthoring.cs     UNCHANGED — moves into the prefab; baker still adds PlayerTag + PlayerMovementData
        └── PlayerInputData.cs          DELETED — singleton replaced by per-entity IInputComponentData
```

No `.asmdef` files added. Everything continues to compile into `Assembly-CSharp` (matches current project state).

## Error handling & known risks

- **Bootstrap fails to create a client world** → `CameraFollowMono` already falls back to `DefaultGameObjectInjectionWorld`, so the camera doesn't crash. Capsule won't move (visible symptom we want).
- **Player ghost not yet replicated to client** → `CameraFollowSystem` query is empty → `CameraTargetData` stays at zero → camera sits at offset from origin until the ghost arrives. Acceptable for a demo.
- **Pre-spawned ghost host-migration risk (CLAUDE.md)** → sidestepped by spawning per-connection.
- **Prediction mismatch / jitter** → with one player and in-memory transport in ClientAndServer, latency is ~0; nothing to mispredict. Becomes real when MPPM lands later.
- **Burst on iOS fallback (CLAUDE.md)** → `PlayerMovementSystem` is the only Burst code touched; pattern unchanged. No new exposure.

## Testing — manual acceptance

1. Open `EcsDemoSub.unity`. PlayMode Tools → "Client & Server". Hit Play.
   - Expect: capsule spawns, WASD/arrows move it, camera follows, console clean.
2. PlayMode Tools → "Server" only. Hit Play.
   - Expect: no capsule (no client to spawn for), console shows server listening on 7979, no errors.
3. (Out of scope for this PR) PlayMode Tools → "Client" only against a separate server.

After implementation, verify via Unity MCP:
- `Unity_GetConsoleLogs` — no compile or runtime errors.
- `Unity_SceneView_Capture2DScene` — capsule renders in PlayMode.

## Out of scope (future work)

- Multi-client testing via Multiplayer Play Mode package.
- AOI / `GhostDistanceImportance` partitioning.
- Snapshot-history tuning, `MaxSendEntities`, adaptive `MaxSendRate`.
- Server zone/region authority and handoff.
- Automated tests (would need an `.asmdef` for `Unity.Entities.Tests`).
- CLAUDE.md netcode version line update (1.11.0 → 1.13.0).
