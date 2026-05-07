# UI Toolkit Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal UI Toolkit HUD on top of the existing NetCode demo that demonstrates both directions of ECS↔UI integration: read-only display of ECS state via Unity 6 runtime data binding (status panel: connection, player position, ghost count, server tick), and UI → ECS via two buttons that send `IRpcCommand` entities (Respawn, Spawn Obstacle).

**Architecture:** One `UIDocument` GameObject in `SampleScene` loads `DemoHud.uxml` + `DemoHud.uss`. A single MonoBehaviour `DemoHudController` lazy-finds the client world (same idiom as the existing `CameraFollowMono`), polls four ECS values each frame, and writes them through a `DemoHudViewModel` (`INotifyBindingPropertyChanged` + `[CreateProperty]`) whose setters short-circuit on unchanged values. UXML `<ui:DataBinding>` declarations route the view-model properties to the four `Label.text` fields. Buttons stay imperatively wired and create RPC entities directly in the client world's `EntityManager`. Two server-side `ISystem`s (mirroring `GoInGameServerSystem`) consume the RPCs.

**Tech Stack:** Unity 6000.4.1f1 · DOTS Entities 1.4.x · Netcode for Entities 1.13.0 · UI Toolkit (`com.unity.modules.uielements`) · Burst · URP 17.4.0 · `Unity.Properties` runtime data binding API.

**Spec:** [`docs/superpowers/specs/2026-04-29-ui-toolkit-demo-design.md`](../specs/2026-04-29-ui-toolkit-demo-design.md)

---

## Verification model — read this before starting

This project has no `.asmdef` test assemblies and no automated test framework wired up. The spec puts automated tests out of scope; the success criteria are runtime/visual. The verification cycle for every code task is therefore:

1. Save the file.
2. Trigger an Editor recompile (Unity reloads automatically on focus).
3. Run `mcp__unity-mcp__Unity_GetConsoleLogs` and confirm no compile errors and no NetCode source-generator warnings.
4. Final acceptance happens in Tasks 8–9 (scene wiring + PlayMode verification against spec success criteria).

Tasks 1–7 will compile cleanly but have **no runtime effect** until Task 8 wires the GameObject into the scene. That is expected.

---

## Required environment — confirm before Task 0

- Unity Editor open on this project (`/Users/wjing/workspace/private/unite-and-conquer`) using version **6000.4.1f1**.
- Unity MCP responding: `mcp__unity-mcp__Unity_GetConsoleLogs` returns `"success": true`.
- Working tree state: see Task 0 — `Assets/Scenes/SampleScene.unity` is currently modified from the prior session and must be either committed or stashed before starting Task 1.

---

## File Structure

```
Assets/
├── UI/                                            ← new directory
│   ├── DemoHud.uxml                               NEW (Task 1)
│   ├── DemoHud.uss                                NEW (Task 2)
│   └── DemoHudPanelSettings.asset                 NEW (Task 3)  — created in Editor
├── Scripts/Demo/UI/                               ← new directory
│   ├── DemoHudViewModel.cs                        NEW (Task 4)
│   ├── DemoHudController.cs                       NEW (Task 5)
│   ├── Respawn.cs                                 NEW (Task 6)
│   └── SpawnObstacle.cs                           NEW (Task 7)
└── Scenes/SampleScene.unity                       MODIFIED (Task 8) — adds DemoHud GameObject
```

No `.asmdef` files added. Everything continues to compile into `Assembly-CSharp`. Existing files (`PlayerMovementSystem`, `PlayerInputSystem`, `ObstacleSpawnSystem`, `CameraFollowSystem`, `CameraFollowMono`, `GoInGame.cs`, `GameBootstrap.cs`, `PrefabSpawnerAuthoring.cs`, `PlayerInputAuthoring.cs`, `PlayerSpawnSystem.cs`) are untouched.

The view model lives in its own file `DemoHudViewModel.cs` rather than inside the controller — the spec said "same file is fine for one panel," but splitting it keeps each file under one responsibility, which is the project convention from `CLAUDE.md` ("each file should have one clear responsibility"). The controller becomes shorter and easier to read.

---

## Task 0: Verify clean baseline

**Files:** none.

- [ ] **Step 1: Confirm working tree state**

Run:
```bash
git status
git log --oneline -3
```

Expected: branch is `feat/ui-demo`. The most recent commit is `docs: switch HUD spec to Unity 6 runtime data binding`. The only modification listed is `Assets/Scenes/SampleScene.unity`. If anything else is modified, stop and surface it before continuing.

- [ ] **Step 2: Stash the dirty SampleScene**

The dirty `SampleScene.unity` is from a prior session and unrelated to this plan; Task 8 modifies the same file, and we want a clean diff for that commit. Stash it now:

```bash
git stash push -m "pre-ui-demo SampleScene state" Assets/Scenes/SampleScene.unity
git status
```

Expected: working tree clean.

- [ ] **Step 3: Verify Unity MCP**

Run `mcp__unity-mcp__Unity_GetConsoleLogs` with no filters. Expected: `"success": true`. If `success: false`, surface the error and stop — every later task depends on this MCP working.

- [ ] **Step 4: Verify Editor compiles cleanly**

Inspect the console output from Step 3. Expected: no error-level entries. If errors exist, fix or surface them before starting; this plan assumes a clean baseline.

---

## Task 1: Create `Assets/UI/DemoHud.uxml`

**Files:**
- Create directory: `Assets/UI/`
- Create: `Assets/UI/DemoHud.uxml`

- [ ] **Step 1: Create the UI directory**

```bash
mkdir -p Assets/UI
```

- [ ] **Step 2: Write `DemoHud.uxml`**

Create `Assets/UI/DemoHud.uxml` with this exact content:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
  <ui:VisualElement name="root" picking-mode="Ignore" style="flex-grow: 1;">

    <ui:VisualElement name="status-panel" class="panel status">
      <ui:Label name="connection-label" text="Disconnected">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="ConnectionText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="position-label" text="Pos: -">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="PositionText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="ghost-count-label" text="Ghosts: 0">
        <Bindings>
          <ui:DataBinding property="text" data-source-path="GhostCountText" binding-mode="ToTarget" />
        </Bindings>
      </ui:Label>
      <ui:Label name="tick-label" text="Tick: -">
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
</ui:UXML>
```

Notes:
- `picking-mode="Ignore"` on the root keeps clicks falling through empty UI space to the game.
- The fallback `text="..."` attributes on the labels are what's shown if the data source isn't bound yet (e.g. before `OnEnable` runs).
- `binding-mode="ToTarget"` is one-way (data source → UI). The buttons are wired in C# instead of via bindings.

- [ ] **Step 3: Trigger Unity reimport and verify**

Switch focus to Unity. Then run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors. UXML import warnings about unrecognised binding attributes (if any) are addressed in Task 5 — leave them for now and proceed.

- [ ] **Step 4: Commit**

```bash
git add Assets/UI/DemoHud.uxml Assets/UI/DemoHud.uxml.meta Assets/UI Assets/UI.meta 2>/dev/null
git status --short
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHud.uxml with status + action panels

Defines the UI Toolkit document for the demo HUD: a top-left status
panel with four labels bound to a view model via <ui:DataBinding>, and
a bottom-center action bar with Respawn / Spawn Obstacle buttons.
Buttons wire imperatively in the controller; labels are pure data
binding.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

If `git status --short` shows any file outside `Assets/UI/` staged, unstage and surface it before committing.

---

## Task 2: Create `Assets/UI/DemoHud.uss`

**Files:**
- Create: `Assets/UI/DemoHud.uss`

- [ ] **Step 1: Write `DemoHud.uss`**

Create `Assets/UI/DemoHud.uss` with this exact content:

```css
#root {
    flex-grow: 1;
}

.panel {
    position: absolute;
    padding: 8px;
    background-color: rgba(0, 0, 0, 0.6);
    border-top-left-radius: 4px;
    border-top-right-radius: 4px;
    border-bottom-left-radius: 4px;
    border-bottom-right-radius: 4px;
}

.status {
    top: 12px;
    left: 12px;
    min-width: 200px;
}

.actions {
    bottom: 12px;
    left: 50%;
    translate: -50% 0;
    flex-direction: row;
}

.actions Button {
    margin-left: 4px;
    margin-right: 4px;
    padding-top: 6px;
    padding-bottom: 6px;
    padding-left: 14px;
    padding-right: 14px;
}

.panel Label {
    color: white;
    font-size: 14px;
    -unity-font-style: bold;
    margin-top: 2px;
    margin-bottom: 2px;
}
```

USS does not support the `border-radius` shorthand or `margin: 0 4px` shorthand — each side must be set explicitly. Same for `padding`.

- [ ] **Step 2: Verify Unity reimport**

Switch focus to Unity, then run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors. If USS warnings appear about unrecognised properties, fix the USS to use only fully-supported properties from the [USS reference](https://docs.unity3d.com/Manual/UIE-USS-SupportedProperties.html).

- [ ] **Step 3: Commit**

```bash
git add Assets/UI/DemoHud.uss Assets/UI/DemoHud.uss.meta
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHud.uss styling

Anchored translucent panels: status top-left, actions bottom-center.
Plain white bold labels. All shorthand properties expanded since USS
doesn't support border-radius/margin/padding shorthand.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Create `Assets/UI/DemoHudPanelSettings.asset` in the Editor

**Files:**
- Create: `Assets/UI/DemoHudPanelSettings.asset` (PanelSettings asset; created via Unity Editor menus, not text)

A `PanelSettings` asset is a `ScriptableObject` instance and cannot be hand-written reliably; it must be created from Unity's `Create` menu so all internal references are correct.

- [ ] **Step 1: Create the PanelSettings asset**

In the Unity Editor:

1. In the Project window, navigate to `Assets/UI/`.
2. Right-click → `Create` → `UI Toolkit` → `Panel Settings Asset`.
3. Rename the new asset to **`DemoHudPanelSettings`** (full path: `Assets/UI/DemoHudPanelSettings.asset`).

- [ ] **Step 2: Attach the USS as a Theme Style Sheet**

With `DemoHudPanelSettings` selected:

1. In the Inspector, locate the **Theme Style Sheet** field. (It will be empty by default; depending on Unity version it may be named "Theme" with a default Unity theme assigned.)
2. Either:
   - Drag `Assets/UI/DemoHud.uss` into the Theme Style Sheet field, **OR**
   - If the field requires a Theme Style Sheet asset (`*.tss`) rather than raw USS, leave the default Unity theme and instead attach `DemoHud.uss` directly to the `UIDocument` in Task 8 (the `UIDocument` component has its own Style Sheets list). Verify the field type — if it accepts USS directly, prefer attaching here so the styling lives with the panel settings.
3. Leave **Scale Mode** at its default (`Constant Pixel Size`) and **Reference Resolution** at the default. This is fine for the demo.

- [ ] **Step 3: Save the project**

In Unity: `File → Save Project` (Cmd+S).

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors.

- [ ] **Step 4: Verify the file exists**

```bash
ls -la Assets/UI/DemoHudPanelSettings.asset Assets/UI/DemoHudPanelSettings.asset.meta
```

Expected: both files exist. If the `.meta` file is missing, switch focus to Unity and wait for the import to complete.

- [ ] **Step 5: Commit**

```bash
git add Assets/UI/DemoHudPanelSettings.asset Assets/UI/DemoHudPanelSettings.asset.meta
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHudPanelSettings asset

PanelSettings asset for the demo HUD UIDocument. Default scale mode;
DemoHud.uss attached either here or on the UIDocument depending on
this Unity version's Theme Style Sheet field type.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Create `Assets/Scripts/Demo/UI/DemoHudViewModel.cs`

**Files:**
- Create directory: `Assets/Scripts/Demo/UI/`
- Create: `Assets/Scripts/Demo/UI/DemoHudViewModel.cs`

- [ ] **Step 1: Create the directory**

```bash
mkdir -p Assets/Scripts/Demo/UI
```

- [ ] **Step 2: Write `DemoHudViewModel.cs`**

Create `Assets/Scripts/Demo/UI/DemoHudViewModel.cs` with this exact content:

```csharp
using System;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Demo
{
    // View model for the demo HUD. Plain C# class implementing
    // INotifyBindingPropertyChanged so the UI Toolkit runtime data
    // binding system can observe property changes. Each setter
    // short-circuits when the new value equals the old value, so the
    // controller can call setters every frame without redundant binding
    // updates or text reassignment.
    public class DemoHudViewModel : INotifyBindingPropertyChanged
    {
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        string _connectionText = "Disconnected";
        string _positionText   = "Pos: -";
        string _ghostCountText = "Ghosts: 0";
        string _tickText       = "Tick: -";

        [CreateProperty]
        public string ConnectionText
        {
            get => _connectionText;
            set
            {
                if (_connectionText == value) return;
                _connectionText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(ConnectionText)));
            }
        }

        [CreateProperty]
        public string PositionText
        {
            get => _positionText;
            set
            {
                if (_positionText == value) return;
                _positionText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(PositionText)));
            }
        }

        [CreateProperty]
        public string GhostCountText
        {
            get => _ghostCountText;
            set
            {
                if (_ghostCountText == value) return;
                _ghostCountText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(GhostCountText)));
            }
        }

        [CreateProperty]
        public string TickText
        {
            get => _tickText;
            set
            {
                if (_tickText == value) return;
                _tickText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(TickText)));
            }
        }
    }
}
```

API verification note: this file uses `INotifyBindingPropertyChanged`, `BindablePropertyChangedEventArgs` (both from `UnityEngine.UIElements`), and `[CreateProperty]` (from `Unity.Properties`). If any of those types fail to resolve under Unity 6000.4.1f1, query Context7 (`/needle-mirror/com.unity.ui` or the Unity 6 manual page on "Bind with custom data sources") for the exact namespace/name in the current Unity version, fix the using directives, and re-run the console log check. The shape of the class (event + properties with attributes) is correct regardless of exact naming.

- [ ] **Step 3: Verify Unity recompile**

Switch focus to Unity. Wait ~3 seconds for recompile. Run `mcp__unity-mcp__Unity_GetConsoleLogs`.

Expected: no errors. If "type or namespace name X could not be found" appears, follow the API verification note above.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/UI/DemoHudViewModel.cs Assets/Scripts/Demo/UI/DemoHudViewModel.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHudViewModel with INotifyBindingPropertyChanged

Four string properties (ConnectionText, PositionText, GhostCountText,
TickText) with [CreateProperty] for the UI Toolkit binding system to
discover, and setters that short-circuit on unchanged values so the
controller can drive them every frame without redundant updates.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Create `Assets/Scripts/Demo/UI/DemoHudController.cs`

**Files:**
- Create: `Assets/Scripts/Demo/UI/DemoHudController.cs`

- [ ] **Step 1: Write the controller skeleton (compiles, OnEnable wiring only)**

Create `Assets/Scripts/Demo/UI/DemoHudController.cs` with this exact content:

```csharp
using System;
using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // Bridges the ECS client world to the demo HUD UIDocument.
    //
    // OnEnable: instantiate the view model, set it as the
    // rootVisualElement.dataSource, and wire button click callbacks.
    // Update: lazy-find the client world, cache four EntityQueries,
    // poll values each frame, and write formatted strings into the
    // view model. Setters short-circuit unchanged values, so the
    // bindings only push to the labels when state actually changes.
    // OnDisable: unregister callbacks, dispose queries.
    [RequireComponent(typeof(UIDocument))]
    public class DemoHudController : MonoBehaviour
    {
        DemoHudViewModel _viewModel;
        Button _respawnBtn;
        Button _spawnObstacleBtn;
        EventCallback<ClickEvent> _respawnHandler;
        EventCallback<ClickEvent> _spawnObstacleHandler;

        World _clientWorld;
        EntityQuery _localPlayerQuery;
        EntityQuery _ghostQuery;
        EntityQuery _networkIdQuery;
        EntityQuery _networkTimeQuery;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null) return;

            _viewModel = new DemoHudViewModel();
            root.dataSource = _viewModel;

            _respawnBtn       = root.Q<Button>("respawn-btn");
            _spawnObstacleBtn = root.Q<Button>("spawn-obstacle-btn");

            _respawnHandler       = _ => SendRpc<RespawnRequest>();
            _spawnObstacleHandler = _ => SendRpc<SpawnObstacleRequest>();

            _respawnBtn?.RegisterCallback(_respawnHandler);
            _spawnObstacleBtn?.RegisterCallback(_spawnObstacleHandler);
        }

        void OnDisable()
        {
            if (_respawnBtn != null && _respawnHandler != null)
                _respawnBtn.UnregisterCallback(_respawnHandler);
            if (_spawnObstacleBtn != null && _spawnObstacleHandler != null)
                _spawnObstacleBtn.UnregisterCallback(_spawnObstacleHandler);

            DisposeQueries();
            _clientWorld = null;
        }

        void DisposeQueries()
        {
            if (_localPlayerQuery  != default) _localPlayerQuery.Dispose();
            if (_ghostQuery        != default) _ghostQuery.Dispose();
            if (_networkIdQuery    != default) _networkIdQuery.Dispose();
            if (_networkTimeQuery  != default) _networkTimeQuery.Dispose();
            _localPlayerQuery = default;
            _ghostQuery = default;
            _networkIdQuery = default;
            _networkTimeQuery = default;
        }

        // Finds the first world whose name contains "client" (case-insensitive).
        // Returns null if no client world exists yet (e.g. before bootstrap).
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
                DisposeQueries();
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                var em = _clientWorld.EntityManager;
                _localPlayerQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                    ComponentType.ReadOnly<PlayerTag>());
                _ghostQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GhostInstance>());
                _networkIdQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                _networkTimeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            }

            // Connection
            if (!_networkIdQuery.IsEmpty)
            {
                var id = _networkIdQuery.GetSingleton<NetworkId>();
                _viewModel.ConnectionText = $"Connected as Client #{id.Value}";
            }
            else
            {
                _viewModel.ConnectionText = "Disconnected";
            }

            // Position (local player)
            if (!_localPlayerQuery.IsEmpty)
            {
                var t = _localPlayerQuery.GetSingleton<LocalTransform>();
                _viewModel.PositionText = string.Format(
                    CultureInfo.InvariantCulture,
                    "Pos: ({0:0.0}, {1:0.0}, {2:0.0})",
                    t.Position.x, t.Position.y, t.Position.z);
            }
            else
            {
                _viewModel.PositionText = "Pos: -";
            }

            // Ghost count
            _viewModel.GhostCountText = $"Ghosts: {_ghostQuery.CalculateEntityCount()}";

            // Server tick
            if (!_networkTimeQuery.IsEmpty)
            {
                var nt = _networkTimeQuery.GetSingleton<NetworkTime>();
                _viewModel.TickText = $"Tick: {nt.ServerTick.SerializedData}";
            }
            else
            {
                _viewModel.TickText = "Tick: -";
            }
        }

        void SendRpc<T>() where T : unmanaged, IRpcCommand
        {
            if (_clientWorld == null || !_clientWorld.IsCreated) return;
            var em = _clientWorld.EntityManager;
            var req = em.CreateEntity();
            em.AddComponentData(req, default(T));
            em.AddComponentData(req, new SendRpcCommandRequest()); // Entity.Null target = server
        }
    }
}
```

API verification notes for the implementer:

1. **`NetworkTime.ServerTick.SerializedData`** — `NetworkTick.SerializedData` is the underlying `uint`. If this property name doesn't exist in NetCode for Entities 1.13.0, the alternatives in order of preference are: `nt.ServerTick.TickIndexForValidTick` (guarded by `nt.ServerTick.IsValid`), or `nt.ServerTick.ToFixedString().ToString()`. Verify against `/websites/unity3d_packages_com_unity_netcode_1_10_api` via Context7 if the build fails on this line.
2. **`RespawnRequest` / `SpawnObstacleRequest`** are referenced via the generic `SendRpc<T>` — these types are defined in Tasks 6 and 7. The compiler will fail until those tasks are complete. Continue to Step 2 anyway to verify the rest of the file compiles aside from those expected errors.

- [ ] **Step 2: Verify expected compile errors**

Switch focus to Unity. Run `mcp__unity-mcp__Unity_GetConsoleLogs`.

Expected: exactly two errors: `The type or namespace name 'RespawnRequest' could not be found` and `The type or namespace name 'SpawnObstacleRequest' could not be found`. Any other error means something else is wrong (typo, missing using, wrong API name) — fix it before continuing.

- [ ] **Step 3: Commit (errors expected — they're fixed in Tasks 6 & 7)**

```bash
git add Assets/Scripts/Demo/UI/DemoHudController.cs Assets/Scripts/Demo/UI/DemoHudController.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHudController bridging ECS to UIDocument

MonoBehaviour with [RequireComponent(UIDocument)]. OnEnable creates
the view model and sets it as rootVisualElement.dataSource, then wires
button callbacks. Update lazy-finds the client world (CameraFollowMono
idiom), caches four EntityQueries, polls and writes formatted strings
into the view model. Buttons call SendRpc<T>() which creates an entity
in the client world's EntityManager with the RPC + SendRpcCommandRequest.

Compiles with two expected unresolved-symbol errors for RespawnRequest
and SpawnObstacleRequest; those types land in the next two tasks.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Create `Assets/Scripts/Demo/UI/Respawn.cs`

**Files:**
- Create: `Assets/Scripts/Demo/UI/Respawn.cs`

- [ ] **Step 1: Write `Respawn.cs`**

Create `Assets/Scripts/Demo/UI/Respawn.cs` with this exact content:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    public struct RespawnRequest : IRpcCommand { }

    // Server-side handler for RespawnRequest. Matches the
    // GoInGameServerSystem shape: query incoming RPCs, look up the
    // requesting connection's NetworkId, then walk the player ghosts
    // to find the one owned by that NetworkId and reset its position.
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct RespawnRequestServerSystem : ISystem
    {
        ComponentLookup<NetworkId> _networkIdLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RespawnRequest>()
                .WithAll<ReceiveRpcCommandRequest>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _networkIdLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var requesterIds = new NativeList<int>(8, Allocator.Temp);

            // Pass 1: collect requesting NetworkIds and destroy request entities.
            foreach (var (rpc, reqEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                              .WithAll<RespawnRequest>()
                              .WithEntityAccess())
            {
                var src = rpc.ValueRO.SourceConnection;
                if (_networkIdLookup.HasComponent(src))
                    requesterIds.Add(_networkIdLookup[src].Value);
                ecb.DestroyEntity(reqEntity);
            }

            // Pass 2: reset position on each player ghost whose owner matches.
            if (requesterIds.Length > 0)
            {
                foreach (var (owner, transform) in
                         SystemAPI.Query<RefRO<GhostOwner>, RefRW<LocalTransform>>()
                                  .WithAll<PlayerTag>())
                {
                    int ownerId = owner.ValueRO.NetworkId;
                    for (int i = 0; i < requesterIds.Length; i++)
                    {
                        if (ownerId == requesterIds[i])
                        {
                            transform.ValueRW.Position = float3.zero;
                            break;
                        }
                    }
                }
            }

            requesterIds.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
```

Notes:
- Two-pass design: collecting NetworkIds first avoids nesting one `SystemAPI.Query` enumerator inside another, which is an Entities-1.x footgun.
- `ComponentLookup<NetworkId>` is read-only (`true` argument).
- The position reset writes to `LocalTransform.Position` directly. NetCode replicates the change in the next snapshot; the predicted client reconciles automatically.

- [ ] **Step 2: Verify Unity recompile**

Switch focus to Unity. Wait ~5 seconds for recompile (NetCode source generators run for systems with `IRpcCommand`). Run `mcp__unity-mcp__Unity_GetConsoleLogs`.

Expected: one remaining error — `The type or namespace name 'SpawnObstacleRequest' could not be found` (still pending Task 7). The `RespawnRequest` error from Task 5 should be gone. No new errors. If new errors appear about ECS source generation or `IRpcCommand`, surface them and stop.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/UI/Respawn.cs Assets/Scripts/Demo/UI/Respawn.cs.meta
git commit -m "$(cat <<'EOF'
feat(net): add RespawnRequest IRpcCommand and server handler

Server-only ISystem mirroring GoInGameServerSystem: collect the
NetworkIds of inbound RespawnRequests, then walk PlayerTag ghosts
and zero LocalTransform.Position on the one whose GhostOwner matches.
Server-authoritative teleport — the predicted client reconciles via
the next snapshot, no client-side teleport call needed.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Create `Assets/Scripts/Demo/UI/SpawnObstacle.cs`

**Files:**
- Create: `Assets/Scripts/Demo/UI/SpawnObstacle.cs`

- [ ] **Step 1: Write `SpawnObstacle.cs`**

Create `Assets/Scripts/Demo/UI/SpawnObstacle.cs` with this exact content:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    public struct SpawnObstacleRequest : IRpcCommand { }

    // Server-side handler for SpawnObstacleRequest. Reuses the
    // PrefabSpawner singleton (set up by PrefabSpawnerAuthoring) for
    // the obstacle prefab. Coexists with the existing
    // ObstacleSpawnSystem, which dumps 20 obstacles once at startup
    // and disables itself.
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct SpawnObstacleRequestServerSystem : ISystem
    {
        Random _random;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SpawnObstacleRequest>()
                .WithAll<ReceiveRpcCommandRequest>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            state.RequireForUpdate<PrefabSpawner>();
            _random = Random.CreateFromIndex(7919u);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var obstaclePrefab = SystemAPI.GetSingleton<PrefabSpawner>().ObstaclePrefab;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            const float range = 15f;

            foreach (var (_, reqEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                              .WithAll<SpawnObstacleRequest>()
                              .WithEntityAccess())
            {
                var obstacle = ecb.Instantiate(obstaclePrefab);
                ecb.SetComponent(obstacle, LocalTransform.FromPosition(
                    new float3(
                        _random.NextFloat(-range, range),
                        0.5f,
                        _random.NextFloat(-range, range))));
                ecb.DestroyEntity(reqEntity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
```

Notes:
- The `Random` seed `7919u` is just a different prime from `ObstacleSpawnSystem`'s `42u` so the two systems generate distinct sequences.
- `_random` is mutated each call (statefully advances), which means subsequent obstacles after the first appear at different positions — different from the existing `ObstacleSpawnSystem` which seeds once and runs to completion.

- [ ] **Step 2: Verify Unity recompile**

Switch focus to Unity. Wait ~5 seconds. Run `mcp__unity-mcp__Unity_GetConsoleLogs`.

Expected: **no errors**. Both `RespawnRequest` and `SpawnObstacleRequest` are now defined; the controller from Task 5 should compile cleanly. If errors remain, surface them.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/UI/SpawnObstacle.cs Assets/Scripts/Demo/UI/SpawnObstacle.cs.meta
git commit -m "$(cat <<'EOF'
feat(net): add SpawnObstacleRequest IRpcCommand and server handler

Server-only ISystem that consumes inbound SpawnObstacleRequest RPCs,
instantiates PrefabSpawner.ObstaclePrefab at a random (-15..15, 0.5,
-15..15) position via the existing PrefabSpawner singleton, and
destroys the request. Coexists with ObstacleSpawnSystem's initial
20-obstacle dump.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Wire the `DemoHud` GameObject into `SampleScene`

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

This task is performed in the Unity Editor.

- [ ] **Step 1: Open SampleScene**

In Unity: File → Open Scene → `Assets/Scenes/SampleScene.unity`. Confirm the scene opens (you should see `Main Camera`, the `EcsDemoSub` Sub Scene, etc. in the Hierarchy).

- [ ] **Step 2: Create the DemoHud GameObject**

In the Hierarchy:

1. Right-click in empty space → `Create Empty` (top-level, not inside the subscene).
2. Rename the new GameObject to **`DemoHud`**.
3. With `DemoHud` selected, in the Inspector click **Add Component**.
4. Search for and add **UI Document** (`UnityEngine.UIElements.UIDocument`). The `[RequireComponent(typeof(UIDocument))]` on `DemoHudController` means adding the controller next will pull in the UIDocument automatically — adding it manually first ensures the order is clear.
5. Click **Add Component** again and search for **Demo Hud Controller**. Select it.

- [ ] **Step 3: Configure the UIDocument**

With `DemoHud` selected, in the UI Document component:

1. **Panel Settings:** drag `Assets/UI/DemoHudPanelSettings` into this slot.
2. **Source Asset (Visual Tree Asset):** drag `Assets/UI/DemoHud.uxml` into this slot.
3. **Sort Order:** leave at `0`.
4. If the UI Document has a separate **Style Sheets** list (depends on Unity 6 minor version) and the Panel Settings did not accept the USS directly in Task 3, add `Assets/UI/DemoHud.uss` to this list.

- [ ] **Step 4: Save the scene**

`File → Save` (Cmd+S).

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors. If you see `NullReferenceException` from the controller during the save, it usually means `Source Asset` wasn't set on the UIDocument — go back to Step 3.

- [ ] **Step 5: Verify the scene file changed**

```bash
git status
```

Expected: `Assets/Scenes/SampleScene.unity` is the only modified file.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "$(cat <<'EOF'
feat(ui): add DemoHud GameObject to SampleScene

Top-level DemoHud GameObject in SampleScene with UIDocument
(referencing DemoHudPanelSettings + DemoHud.uxml) and DemoHudController.
Existing Main Camera and EcsDemoSub subscene are unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Manual PlayMode verification

**Files:** none (verification only — no commits unless a fix is needed).

Walk through the spec's eight success criteria. For any that fail, debug and fix in a separate commit per fix.

- [ ] **Step 1: Enter PlayMode**

In Unity, click the Play button. Wait ~2 seconds for both worlds to spin up, the connection to establish, and the player ghost to spawn.

Run `mcp__unity-mcp__Unity_GetConsoleLogs` immediately after. Expected: no errors. The existing demo's `Debug.Log` lines about "setting connection X to in game" and "Spawning player for connection X" should appear once.

- [ ] **Step 2: Capture the Game view (success criterion #1)**

Run `mcp__unity-mcp__Unity_Camera_Capture` (or the equivalent screenshot command). Verify visually:

- A translucent dark panel sits in the **top-left** with four lines of white bold text.
- A row of two buttons sits at the **bottom-center**.
- Neither panel obstructs the player capsule in the middle.

If the layout is broken (panels overlapping, off-screen, all white), check that DemoHudPanelSettings is bound and the USS is reaching the panel — Task 3 Step 2 has the diagnosis.

- [ ] **Step 3: Verify connection and tick labels (success criteria #2 and #5)**

Read the status panel:

- **Connection label** should read `Connected as Client #1` (or `#2` if multiple clients are connected). Not `Disconnected`.
- **Tick label** should read `Tick: <some number>` and the number should be **changing rapidly** as you watch — server tick advances 30× per second.

If `Connected as Client #0` shows up, that means `NetworkId.Value == 0` was received, which is fine. If the label stays on `Disconnected`, the controller failed to find the client world or the NetworkId singleton isn't ready — check the console for messages from `GoInGameClientSystem`.

- [ ] **Step 4: Verify position label (success criterion #3)**

With the Game view focused, press WASD or arrow keys. The capsule should move and the **Position label** should update every frame to match (precision: one decimal).

If the position label stays `Pos: -`, either `GhostOwnerIsLocal` isn't being added to the player ghost (NetCode generates this automatically based on `GhostOwner.NetworkId`) or the local-player query is filtering wrong — re-check Task 5's query construction.

- [ ] **Step 5: Verify ghost count (success criterion #4)**

Read the **Ghost count label** — it should read approximately `Ghosts: 21` (1 player + 20 obstacles spawned by the existing `ObstacleSpawnSystem`). Acceptable range: 18–25 depending on initial replication state. Continue.

- [ ] **Step 6: Verify Respawn button (success criterion #6)**

1. Move the capsule away from origin using WASD.
2. Click the **Respawn** button.
3. The capsule should snap (or rubber-band) back to roughly `(0, *, 0)`. The Position label should update to reflect the reset.

If nothing happens, check the console — the most likely failure is an unhandled exception in `SendRpc<T>()` or `RespawnRequestServerSystem.OnUpdate`.

- [ ] **Step 7: Verify Spawn Obstacle button (success criterion #7)**

1. Click the **Spawn Obstacle** button several times.
2. New obstacle cubes should appear at random positions in the ground plane, one per click.
3. The Ghost count label should increase by 1 per click. Existing obstacles must still be present.

- [ ] **Step 8: Verify allocations are not catastrophic (success criterion #8)**

Open the Profiler (`Window → Analysis → Profiler`) while still in PlayMode:

1. In the CPU Usage track, look at the per-frame `GC.Alloc` total.
2. With the player **stationary** (no input), the per-frame GC allocation should be very low — well under 1 KB per frame from this HUD. The view-model setters short-circuit unchanged values, so most labels don't allocate.
3. With the player **moving**, position allocations rise (one `string.Format` per frame) — that's expected.
4. There should be no obvious large recurring spikes (kilobytes-per-frame) attributable to UI updates.

If the allocation profile looks pathological (megabytes per frame, GC pauses), it's almost always because `equals` comparison is failing on the view-model setters and every label is being rewritten every frame — verify the `if (_field == value) return;` guards are present.

- [ ] **Step 9: Exit PlayMode and commit nothing**

If all eight criteria pass, exit PlayMode. No commit — verification only.

If any criterion failed and you fixed it, commit the fix as a separate `fix(ui): …` commit before the plan is considered done.

- [ ] **Step 10: Restore the stashed SampleScene state from Task 0**

The dirty `SampleScene.unity` from before this plan started is still in the stash. Restore it to a separate branch or merge it back as the user prefers — confirm with the user before discarding:

```bash
git stash list
```

Surface the stash entry to the user and ask whether to apply, branch off, or drop. **Do not run `git stash drop` or `git stash pop` autonomously.**

---

## Self-review

I checked the plan against the spec section-by-section:

| Spec section | Covered by |
|---|---|
| Purpose: ECS→UI read | Tasks 4–5 (view model + controller poll/write) + Task 1 (UXML bindings) |
| Purpose: UI→ECS write | Task 5 (`SendRpc<T>` button handlers) + Tasks 6–7 (server handlers) |
| Decisions: UI Toolkit | Tasks 1–3 |
| Decisions: MonoBehaviour controller per UIDocument | Task 5 |
| Decisions: Runtime data binding (`dataSource`/`INotifyBindingPropertyChanged`/`[CreateProperty]`) | Tasks 1, 4, 5 |
| Decisions: RPC pattern mirrors GoInGame.cs | Tasks 6, 7 |
| Decisions: No new .asmdef | File Structure section |
| Architecture diagram | Tasks 1–8 collectively realize it |
| Component 1: DemoHud.uxml | Task 1 |
| Component 2: DemoHud.uss | Task 2 |
| Component 3a: DemoHudViewModel | Task 4 |
| Component 3b: DemoHudController | Task 5 |
| Component 4: Respawn.cs | Task 6 |
| Component 5: SpawnObstacle.cs | Task 7 |
| File layout | File Structure section + per-task `Files:` callouts |
| Naming conventions | Per-task type/file names match |
| Scene wiring | Task 8 |
| "Things that don't happen" — no UIDocument in subscene | Task 8 explicitly says top-level, not subscene |
| "Things that don't happen" — no .asmdef | Stated in File Structure |
| "Things that don't happen" — no changes to existing systems | No task touches existing files |
| Success criteria 1–8 | Task 9 walks each one |
| Risks: query disposal | Task 5 `DisposeQueries` + OnDisable |
| Risks: world recreation | Task 5 `_clientWorld == null \|\| !_clientWorld.IsCreated` re-acquisition |
| Risks: data binding API surface | API verification notes in Tasks 4 and 5 |
| Risks: mobile allocations | Task 9 Step 8 profiler check |
| Risks: RPC ordering | Task 5 `SendRpc<T>` matches the GoInGameClientSystem shape |

**Type consistency:** `RespawnRequest` and `SpawnObstacleRequest` used in Task 5 match definitions in Tasks 6 and 7. `DemoHudViewModel` properties (`ConnectionText`, `PositionText`, `GhostCountText`, `TickText`) match the UXML `data-source-path` strings in Task 1 character-for-character.

**Placeholder scan:** no TBD/TODO/"implement later" patterns. Every code step contains the full source. Manual editor steps (Tasks 3 and 8) name the exact menu items and field names rather than punting to "configure as needed."

**Known soft spot:** Task 3's PanelSettings asset and Task 8's scene wiring are interactive Editor steps; they are described prescriptively but cannot be unit-tested. The verification at Task 9 catches any wiring mistakes.
