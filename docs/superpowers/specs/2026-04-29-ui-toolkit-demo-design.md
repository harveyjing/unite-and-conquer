# UI Toolkit Demo Design

**Date:** 2026-04-29
**Status:** Approved (pending implementation plan)

## Purpose

Add a basic UI Toolkit HUD to the existing NetCode demo scene. The work is intentionally small. Its value is as a **reference implementation** of the two core ECS↔UI integration patterns we will reuse for every later UI in the project (resource bars, unit rosters, equipment slots, tech trees, alliance panels, etc.):

1. **ECS → UI (read-only display):** read state from the client world each frame and surface it in a UI Toolkit `UIDocument`.
2. **UI → ECS (UI triggers ECS work):** translate a button click into a client-side RPC entity that the server consumes.

Out of scope for this pass: per-entity worldspace UI (health bars, name tags), connect/disconnect lifecycle UI, polished art, localization, runtime data binding API. Those are deliberately deferred — the goal is one minimal example that establishes the patterns.

## Context

The current demo (see [`CLAUDE.md`](../../../CLAUDE.md)) has a working NetCode for Entities loop: `ClientServerBootstrap` spins up both worlds on Play, `GoInGame` RPC marks the connection in-game, the server spawns a predicted player ghost, and the client drives it with WASD. A MonoBehaviour camera (`CameraFollowMono`) bridges ECS world state to a Unity Camera. This UI work follows the same bridge pattern — one MonoBehaviour reading ECS singletons/queries and writing into a Unity-side system.

Relevant existing code:
- [`Assets/Scripts/Demo/CameraFollowMono.cs`](../../../Assets/Scripts/Demo/CameraFollowMono.cs) — the bridge pattern this design mirrors.
- [`Assets/Scripts/Demo/Bootstrap/GoInGame.cs`](../../../Assets/Scripts/Demo/Bootstrap/GoInGame.cs) — the RPC client/server pattern this design extends.
- [`Assets/Scripts/Demo/Authoring/PrefabSpawnerAuthoring.cs`](../../../Assets/Scripts/Demo/Authoring/PrefabSpawnerAuthoring.cs) — the singleton holding the `ObstaclePrefab` reused by the new spawn-obstacle handler.
- [`Assets/Scripts/Demo/Authoring/PlayerInputAuthoring.cs`](../../../Assets/Scripts/Demo/Authoring/PlayerInputAuthoring.cs) — defines `PlayerTag`, used to query the local player.

## Decisions

| Decision | Choice | Reason |
|---|---|---|
| UI framework | **UI Toolkit** (`com.unity.modules.uielements`) | Unity 6's strategic direction; scales to data-dense panels (inventory, tech tree); plays better with frequent ECS state updates. |
| Patterns covered | ECS → UI (read) **and** UI → ECS (write) | Together these cover ~80% of real game UI. Worldspace and lifecycle UI are deferred. |
| Bridge form | One `MonoBehaviour` controller per `UIDocument` | Mirrors `CameraFollowMono`; keeps the entire ECS↔UI seam in one file per panel; avoids the more advanced runtime data-binding API. |
| RPC pattern | Mirror `GoInGame.cs`: `IRpcCommand` struct + server `ISystem` per file | Existing house style; client side is a button handler instead of a system, which is the only difference. |
| Code organization | Add `Assets/Scripts/Demo/UI/` and `Assets/UI/`. No new `.asmdef`. | Matches current `Assembly-CSharp` setup called out in `CLAUDE.md`. |

## Architecture

```
┌─────────────────────── SampleScene (regular scene) ─────────────────────┐
│                                                                         │
│   GameObject "DemoHud"                                                  │
│   ├── UIDocument         (Source Asset = Assets/UI/DemoHud.uxml)        │
│   └── DemoHudController  (MonoBehaviour bridge)                         │
│           │                                                             │
│           ├── reads:  client world singletons + queries                 │
│           │           → 4 labels (connection, position, ghosts, tick)   │
│           │                                                             │
│           └── writes: button clicks create RPC entities in client world │
│                       (RespawnRequest, SpawnObstacleRequest)            │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                   │
                                   │ NetCode replication
                                   ▼
┌──────────────────── Server world (existing) ────────────────────────────┐
│                                                                         │
│   New: RespawnRequestServerSystem                                       │
│        SpawnObstacleRequestServerSystem                                 │
│                                                                         │
│   Both follow the GoInGameServerSystem pattern:                         │
│   query [ReceiveRpcCommandRequest + RequestTag], handle, destroy req.   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

The controller is the **only** new MonoBehaviour and the **only** code that crosses the GameObject ↔ ECS boundary. This isolation is intentional — every later UI (inventory panel, alliance window, etc.) gets its own controller, and the boundary is always in one obvious place.

## Components

### 1. `Assets/UI/DemoHud.uxml` (UI structure)

Two panels under a single root that ignores pointer events (so clicks on empty space pass to the game):

```xml
<UXML xmlns:ui="UnityEngine.UIElements">
  <ui:VisualElement name="root" picking-mode="Ignore">

    <ui:VisualElement name="status-panel" class="panel status">
      <ui:Label name="connection-label"  text="Disconnected" />
      <ui:Label name="position-label"    text="Pos: -" />
      <ui:Label name="ghost-count-label" text="Ghosts: 0" />
      <ui:Label name="tick-label"        text="Tick: -" />
    </ui:VisualElement>

    <ui:VisualElement name="action-panel" class="panel actions">
      <ui:Button name="respawn-btn"        text="Respawn" />
      <ui:Button name="spawn-obstacle-btn" text="Spawn Obstacle" />
    </ui:VisualElement>

  </ui:VisualElement>
</UXML>
```

### 2. `Assets/UI/DemoHud.uss` (styling)

```css
#root           { flex-grow: 1; }
.panel          { position: absolute; padding: 8px; background-color: rgba(0,0,0,0.6); border-radius: 4px; }
.status         { top: 12px;    left: 12px;  min-width: 200px; }
.actions        { bottom: 12px; left: 50%;   translate: -50% 0; flex-direction: row; }
.actions Button { margin: 0 4px; padding: 6px 14px; }
.panel Label    { color: white; font-size: 14px; -unity-font-style: bold; margin: 2px 0; }
```

UXML and USS are split (rather than inline styles) to match Unity's documented best practice and to enable re-skinning without recompiling — the same pattern future complex UIs (inventory, tech tree) will use.

### 3. `Assets/Scripts/Demo/UI/DemoHudController.cs` (the bridge)

`MonoBehaviour` with `[RequireComponent(typeof(UIDocument))]`. Sits on the `DemoHud` GameObject in `SampleScene` (regular scene, not subscene — `UIDocument` has no baker).

Responsibilities, in order:

1. **`OnEnable`**: get `UIDocument.rootVisualElement`, cache references to the four labels and two buttons via `Q<Label>("connection-label")` etc., wire `RegisterCallback<ClickEvent>` on each button.
2. **Lazy-find client world** in `Update` — same pattern as `CameraFollowMono`. Cached on first frame; recovered if the world is recreated (stop and restart Play).
3. **Cache ECS queries once**:
   - Local player query: `(LocalTransform, GhostOwnerIsLocal, PlayerTag)`.
   - Ghost-count query: `GhostInstance`.
   - Singleton lookups: `NetworkId` (on the connection entity), `NetworkTime`.
4. **`Update` — read & display:**
   - **Position label:** every frame from the local-player query.
   - **Ghost count, tick, connection:** every ~10 frames via a `_throttle++ % 10` counter. This demonstrates the mobile-friendly "stagger UI work across frames" pattern called out in `CLAUDE.md`.
5. **Button click handlers** create an entity in the client world's `EntityManager` with the RPC component + `SendRpcCommandRequest` (target = `Entity.Null` → sends to the server connection set up by `AutoConnectPort`):

   ```csharp
   var em  = _clientWorld.EntityManager;
   var req = em.CreateEntity();
   em.AddComponentData(req, new RespawnRequest());
   em.AddComponentData(req, new SendRpcCommandRequest()); // null target = server
   ```

6. **`OnDisable`** unregisters callbacks and disposes queries.

**Why a controller class rather than runtime data binding:** the explicit poll-and-set form makes the data flow visible to a reader who is new to ECS. The data-binding API can replace this in a later refactor once the patterns are familiar.

### 4. `Assets/Scripts/Demo/UI/Respawn.cs`

```csharp
public struct RespawnRequest : IRpcCommand { }

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct RespawnRequestServerSystem : ISystem
{
    // OnCreate: RequireForUpdate query of [ReceiveRpcCommandRequest, RespawnRequest].
    //
    // OnUpdate, for each request:
    //   - read SourceConnection -> NetworkId.Value
    //   - find the player ghost where GhostOwner.NetworkId == that value
    //   - set its LocalTransform.Position = float3.zero
    //   - destroy the request entity
}
```

Server-authoritative teleport: writing `LocalTransform.Position` on the server propagates via the next snapshot, and the predicted client reconciles automatically — no client-side teleport needed.

### 5. `Assets/Scripts/Demo/UI/SpawnObstacle.cs`

```csharp
public struct SpawnObstacleRequest : IRpcCommand { }

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct SpawnObstacleRequestServerSystem : ISystem
{
    // Holds Unity.Mathematics.Random as a field, like GoInGameServerSystem.
    // OnUpdate: for each RPC, instantiate PrefabSpawner.ObstaclePrefab
    // at a random (x, 0.5, z) and destroy the request.
}
```

Reuses the existing `PrefabSpawner` singleton lookup pattern — no new authoring component required. Coexists cleanly with the existing `ObstacleSpawnSystem` initial-20 dump.

## File layout

```
Assets/
├── UI/                                   ← new
│   ├── DemoHud.uxml
│   └── DemoHud.uss
└── Scripts/Demo/UI/                      ← new
    ├── DemoHudController.cs
    ├── Respawn.cs
    └── SpawnObstacle.cs
```

Existing files are untouched.

## Naming

- RPC structs end in `Request` (matches `GoInGameRequest`).
- Server-side handler systems end in `RequestServerSystem` (matches `GoInGameServerSystem`).
- The HUD MonoBehaviour ends in `Controller` (it's runtime, not authoring — it has no baker).

## Scene wiring

In `Assets/Scenes/SampleScene.unity` (the regular scene, not the `EcsDemoSub` subscene), add a top-level GameObject named **`DemoHud`** with:

- `UIDocument` — Source Asset = `Assets/UI/DemoHud.uxml`; Panel Settings = a new or default `PanelSettings` asset that references `DemoHud.uss` in its Theme Style Sheet list.
- `DemoHudController` — added automatically via `[RequireComponent(typeof(UIDocument))]`.

The existing `Main Camera` (with `CameraFollowMono`) is unchanged.

## Things that explicitly do NOT happen in this work

- No `UIDocument` in the subscene — subscenes are baked into entities and `UIDocument` is a runtime-only MonoBehaviour with no baker; it would vanish.
- No new `.asmdef` files — the codebase still compiles into `Assembly-CSharp`. Splitting assemblies waits until there's a real need.
- No changes to `PlayerMovementSystem`, `PlayerInputSystem`, `ObstacleSpawnSystem`, `CameraFollowSystem`, or `CameraFollowMono`.
- No connect/disconnect UI, host migration handling, or worldspace per-entity UI.
- No runtime data-binding API; the controller polls and sets explicitly.

## Success criteria

The demo is complete when, on Play:

1. The HUD appears: top-left status panel, bottom-center action bar.
2. **Connection label** reads `"Connected as Client #N"` where N is the local `NetworkId.Value`.
3. **Position label** updates every frame and matches the visible player position.
4. **Ghost count label** updates ~3× per second and reflects the entities replicated to the client (player + obstacles).
5. **Tick label** updates ~3× per second and shows a monotonically increasing server tick.
6. **Respawn** button moves the player back to world origin (visible rubber-band reconciliation is acceptable for a demo).
7. **Spawn Obstacle** button adds a new obstacle at a random position on each click; existing obstacles remain.
8. No obvious GC spikes visible in the Profiler during steady-state HUD updates — only the unavoidable allocations from label-text reassignment when values actually change.

## Risks and considerations

- **Ghost-count query in a MonoBehaviour:** creating an `EntityQuery` from outside an `ISystem` requires careful disposal in `OnDisable`. The controller does this explicitly.
- **World recreation on Play stop/start:** the cached client world reference must be re-acquired if `_world.IsCreated` becomes false — same as `CameraFollowMono` already handles.
- **Mobile constraints:** label string allocations on every `Update` are the easy footgun. Throttling non-position updates to every ~10 frames keeps allocation volume low; position uses a small `string.Format` once per frame, which is acceptable but is the first thing to optimize if the Profiler flags it.
- **RPC ordering:** `SendRpcCommandRequest` from a MonoBehaviour writes to the client world's `EntityManager` on the main thread outside system updates. This is the same shape as `GoInGameClientSystem` but driven by a click instead of a query; the new RPC entity is consumed by Netcode's RPC send pipeline on the next client tick. Verify with Context7 (`/websites/unity3d_packages_com_unity_netcode_1_10_api`) during implementation if any unexpected behavior shows up.

## References

- [`CLAUDE.md`](../../../CLAUDE.md) — project conventions, mobile constraints, required tools.
- [`docs/superpowers/specs/2026-04-25-netcode-demo-design.md`](2026-04-25-netcode-demo-design.md) — the netcode demo this UI sits on top of.
- Unity UI Toolkit runtime documentation (verify against Context7's Unity packages library before implementation).
- Netcode for Entities `IRpcCommand` / `SendRpcCommandRequest` / `ReceiveRpcCommandRequest` (verify against `/websites/unity3d_packages_com_unity_netcode_1_10_api`).
