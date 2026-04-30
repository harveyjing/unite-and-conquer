# UI Toolkit Demo Design

**Date:** 2026-04-29
**Status:** Approved (pending implementation plan)

## Purpose

Add a basic UI Toolkit HUD to the existing NetCode demo scene. The work is intentionally small. Its value is as a **reference implementation** of the two core ECS↔UI integration patterns we will reuse for every later UI in the project (resource bars, unit rosters, equipment slots, tech trees, alliance panels, etc.):

1. **ECS → UI (read-only display):** read state from the client world each frame and surface it in a UI Toolkit `UIDocument`.
2. **UI → ECS (UI triggers ECS work):** translate a button click into a client-side RPC entity that the server consumes.

Out of scope for this pass: per-entity worldspace UI (health bars, name tags), connect/disconnect lifecycle UI, polished art, and localization. Those are deliberately deferred — the goal is one minimal example that establishes the patterns.

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
| Bridge form | One `MonoBehaviour` controller per `UIDocument` | Mirrors `CameraFollowMono`; keeps the entire ECS↔UI seam in one file per panel. |
| View binding | Unity 6 **runtime data binding** (`dataSource` + `INotifyBindingPropertyChanged` + `[CreateProperty]`) | This is the modern, future-facing pattern and the right reference for the data-dense UIs the project will later need (inventory, tech tree, equipment slots, alliance panels). The controller still polls ECS — but it writes to a view model whose property setters only fire change notifications when values *actually* change, which makes UI updates allocation-light without manual throttling. |
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
│           ├── owns DemoHudViewModel (INotifyBindingPropertyChanged)     │
│           │     ├── ConnectionText, PositionText,                       │
│           │     │   GhostCountText, TickText  ([CreateProperty])        │
│           │     └── set as rootVisualElement.dataSource                 │
│           │                                                             │
│           ├── reads:  client world singletons + queries every frame,    │
│           │           writes formatted strings into view model;         │
│           │           UXML <DataBinding> elements push only-when-       │
│           │           changed values to the four Labels.                │
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

### 1. `Assets/UI/DemoHud.uxml` (UI structure + bindings)

Two panels under a single root that ignores pointer events. The status labels declare bindings to the view model's properties via UXML `<Bindings>`; the `data-source` for the view model is set from C# in the controller (UXML has no way to point at a runtime C# instance directly).

```xml
<UXML xmlns:ui="UnityEngine.UIElements">
  <ui:VisualElement name="root" picking-mode="Ignore">

    <ui:VisualElement name="status-panel" class="panel status">
      <ui:Label name="connection-label">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="ConnectionText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="position-label">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="PositionText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="ghost-count-label">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="GhostCountText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="tick-label">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="TickText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
    </ui:VisualElement>

    <ui:VisualElement name="action-panel" class="panel actions">
      <ui:Button name="respawn-btn"        text="Respawn" />
      <ui:Button name="spawn-obstacle-btn" text="Spawn Obstacle" />
    </ui:VisualElement>

  </ui:VisualElement>
</UXML>
```

The exact UXML schema for `<Bindings>` / `<ui:DataBinding>` should be verified against the current Unity 6 UI Toolkit docs during implementation — if UXML-side declaration is awkward in our Unity version, fall back to programmatic `label.SetBinding("text", new DataBinding { … })` calls in the controller's `OnEnable`. Either choice produces identical runtime behavior.

Buttons stay imperatively wired in C# — UI Toolkit's runtime data binding doesn't include a "command binding" idiom, and click handlers are clearer when explicit.

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

### 3. `Assets/Scripts/Demo/UI/DemoHudController.cs` + `DemoHudViewModel`

`MonoBehaviour` with `[RequireComponent(typeof(UIDocument))]`. Sits on the `DemoHud` GameObject in `SampleScene` (regular scene, not subscene — `UIDocument` has no baker). The view model can live in the same file for now (one panel, one model — splitting would be premature); when a second panel is added later, promote it to its own file.

#### View model

Plain C# class implementing `INotifyBindingPropertyChanged`. Each property has `[CreateProperty]` so the binding system can discover it, and the setter only raises the change event when the value actually differs:

```csharp
public class DemoHudViewModel : INotifyBindingPropertyChanged
{
    public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

    string _connectionText = "Disconnected";
    string _positionText   = "Pos: -";
    string _ghostCountText = "Ghosts: 0";
    string _tickText       = "Tick: -";

    [CreateProperty] public string ConnectionText
    {
        get => _connectionText;
        set { if (_connectionText == value) return;
              _connectionText = value;
              propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(ConnectionText))); }
    }
    // PositionText, GhostCountText, TickText follow the same shape.
}
```

This change-only-on-difference setter is the entire reason runtime data binding is allocation-light: the controller can call the setter every frame, but the binding system (and any string allocation) only fires when ECS state actually moves the value.

#### Controller

Responsibilities, in order:

1. **`OnEnable`:**
   - `var root = GetComponent<UIDocument>().rootVisualElement;`
   - Create the view model: `_viewModel = new DemoHudViewModel();`
   - `root.dataSource = _viewModel;` — this is what makes the UXML `data-source-path` references resolve.
   - Cache the two button references and `RegisterCallback<ClickEvent>` on each.
2. **Lazy-find client world** in `Update` — same pattern as `CameraFollowMono`. Cached on first frame; recovered if the world is recreated (stop and restart Play).
3. **Cache ECS queries once:**
   - Local player query: `(LocalTransform, GhostOwnerIsLocal, PlayerTag)`.
   - Ghost-count query: `GhostInstance`.
   - Singleton lookups: `NetworkId` (on the connection entity), `NetworkTime`.
4. **`Update` — poll and write to view model:**
   - Read the four ECS values, format strings, assign them to the view model properties.
   - No throttling counter is needed: the view model's setters short-circuit when the value is unchanged, so even every-frame writes don't push redundant updates to the labels. This is a key advantage of data binding over the manual `_throttle++ % 10` approach.
   - One small footgun: `string.Format` allocates regardless of whether the result equals the previous value. For the position label (which changes most frames during movement) this is unavoidable. For the other labels, formatting a value that hasn't changed still allocates a string before the equality check rejects it — acceptable for the demo, and the standard fix later is to track the last-formatted source value and skip the format step when unchanged.
5. **Button click handlers** create an entity in the client world's `EntityManager` with the RPC component + `SendRpcCommandRequest` (target = `Entity.Null` → sends to the server connection set up by `AutoConnectPort`):

   ```csharp
   var em  = _clientWorld.EntityManager;
   var req = em.CreateEntity();
   em.AddComponentData(req, new RespawnRequest());
   em.AddComponentData(req, new SendRpcCommandRequest()); // null target = server
   ```

6. **`OnDisable`** unregisters button callbacks and disposes ECS queries. The view model is GC-collected when the controller is destroyed; no explicit cleanup needed.

**Why this is the right reference:** every later UI in this project (resource bars, unit roster, equipment, tech tree, alliance window) will follow the same shape: an ECS-polling controller writes to a view model exposed via `[CreateProperty]`; UXML declares which property each visual element binds to. Replacing the four-label HUD with a 16-slot equipment grid is a UXML change, not a controller rewrite.

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

## Success criteria

The demo is complete when, on Play:

1. The HUD appears: top-left status panel, bottom-center action bar.
2. **Connection label** reads `"Connected as Client #N"` where N is the local `NetworkId.Value`.
3. **Position label** updates every frame and matches the visible player position.
4. **Ghost count label** reflects the entities replicated to the client (player + obstacles); changes are visible within one frame of the underlying value changing.
5. **Tick label** shows a monotonically increasing server tick.
6. **Respawn** button moves the player back to world origin (visible rubber-band reconciliation is acceptable for a demo).
7. **Spawn Obstacle** button adds a new obstacle at a random position on each click; existing obstacles remain.
8. No obvious GC spikes visible in the Profiler during steady-state HUD updates — only the unavoidable allocations from label-text reassignment when values actually change.

## Risks and considerations

- **Ghost-count query in a MonoBehaviour:** creating an `EntityQuery` from outside an `ISystem` requires careful disposal in `OnDisable`. The controller does this explicitly.
- **World recreation on Play stop/start:** the cached client world reference must be re-acquired if `_world.IsCreated` becomes false — same as `CameraFollowMono` already handles.
- **Runtime data binding API surface:** Unity 6's runtime data binding is relatively new (introduced in Unity 6 / 2023.2), and this project is on `6000.4.1f1`, so the API is available. The exact namespaces (`Unity.Properties`, `UnityEngine.UIElements`) and class names (`DataBinding`, `BindablePropertyChangedEventArgs`, `INotifyBindingPropertyChanged`, `[CreateProperty]`) should be re-verified against Unity 6 docs during implementation; Context7 currently indexes mostly the legacy `bindingPath` (SerializedObject) docs, so prefer Unity's official runtime data binding manual page or a current sample as the source of truth.
- **Mobile constraints:** the data-binding setter pattern (`if (value == _field) return; …`) makes string-text updates allocation-light by default — the binding fires only when state actually moves. The remaining cost is the `string.Format` call inside the controller's poll loop, which allocates regardless. For values that change every frame (player position) this is unavoidable and acceptable; for slower-changing values (tick, ghost count) the cheap optimization is to track the last-formatted source value and skip `string.Format` when unchanged. Defer that until the Profiler flags it.
- **RPC ordering:** `SendRpcCommandRequest` from a MonoBehaviour writes to the client world's `EntityManager` on the main thread outside system updates. This is the same shape as `GoInGameClientSystem` but driven by a click instead of a query; the new RPC entity is consumed by Netcode's RPC send pipeline on the next client tick. Verify with Context7 (`/websites/unity3d_packages_com_unity_netcode_1_10_api`) during implementation if any unexpected behavior shows up.

## References

- [`CLAUDE.md`](../../../CLAUDE.md) — project conventions, mobile constraints, required tools.
- [`docs/superpowers/specs/2026-04-25-netcode-demo-design.md`](2026-04-25-netcode-demo-design.md) — the netcode demo this UI sits on top of.
- Unity UI Toolkit runtime documentation (verify against Context7's Unity packages library before implementation).
- Netcode for Entities `IRpcCommand` / `SendRpcCommandRequest` / `ReceiveRpcCommandRequest` (verify against `/websites/unity3d_packages_com_unity_netcode_1_10_api`).
