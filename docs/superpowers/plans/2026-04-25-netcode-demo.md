# Netcode Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the existing single-player capsule demo (`Assets/Scenes/SampleScene/EcsDemoSub.unity`) into a server-authoritative, predicted-ghost player using Netcode for Entities. Player ghost is server-spawned per connection; client predicts movement; camera follows the locally-owned ghost.

**Architecture:** A `ClientServerBootstrap` override creates client and server worlds based on the PlayMode Tools setting (defaults to ClientAndServer). The capsule moves out of the subscene into a prefab. On connect, a server-only system instantiates the prefab as a ghost, sets `GhostOwner = NetworkId`. Input lives on the ghost as `IInputComponentData`; movement runs in `PredictedSimulationSystemGroup` on both worlds with the same Burst code.

**Tech Stack:** Unity 6000.4.1f1 (worktree may be on 6000.4.2f1), DOTS Entities 1.4.x, Netcode for Entities 1.13.0, Burst, URP 17.4.0, Input System 1.19.0.

**Spec:** `docs/superpowers/specs/2026-04-25-netcode-demo-design.md`

**Verification model — read this before starting.** This project has no `.asmdef` test assemblies and no automated test framework wired up; the spec explicitly puts automated tests out of scope. The standard TDD cycle is replaced with: **edit code → trigger Editor recompile → check console via Unity MCP → manual acceptance in PlayMode for the final two tasks.** Each script task has a "verify" step that runs `Unity_GetConsoleLogs` and expects no compile errors. This is intentional, matches project state, and is reflected in CLAUDE.md.

**Important runtime caveat.** From Task 7 onward through Task 12, the demo will not function correctly in PlayMode — there will be no player capsule until the prefab and subscene are wired (Tasks 12–13). This is expected. Functional verification only happens at Task 14 onward. Each intermediate commit must compile cleanly, but it does not need to be runtime-functional.

**Required environment.** Confirm before Task 0:
- Unity Editor open on this project (`/Users/wjing/workspace/private/unite-and-conquer`).
- Unity MCP responding (`mcp__unity-mcp__Unity_GetConsoleLogs` returns `success: true`).
- Working tree clean *or* contains only the pre-existing pending changes from before this branch (package bumps, CLAUDE.md edits). Do not commit those alongside any task in this plan.

---

## File Structure

```
Assets/
├── Prefabs/
│   └── PlayerCapsule.prefab            NEW (Task 12) — capsule mesh + GhostAuthoringComponent + CapsuleDemoAuthoring
├── Scenes/SampleScene/EcsDemoSub.unity MODIFIED (Task 13) — capsule GameObject removed; PlayerSpawnerAuthoring added
└── Scripts/
    ├── Bootstrap/
    │   └── GameBootstrap.cs            NEW (Task 6)
    ├── Net/
    │   ├── PlayerSpawner.cs            NEW (Task 3)
    │   ├── PlayerSpawnerAuthoring.cs   NEW (Task 4)
    │   └── PlayerSpawnSystem.cs        NEW (Task 5)
    └── Demo/
        ├── PlayerInput.cs              NEW (Task 2) — IInputComponentData
        ├── PlayerInputData.cs          DELETED (Task 10)
        ├── PlayerInputSystem.cs        RENAMED → PlayerInputCollectionSystem.cs (Task 9)
        ├── PlayerInputCollectionSystem.cs  REPLACEMENT — fills PlayerInput on owned ghost (Task 9)
        ├── PlayerMovementData.cs       MODIFIED (Task 7) — Velocity becomes [GhostField]
        ├── PlayerMovementSystem.cs     MODIFIED (Task 8) — PredictedSimulationSystemGroup, reads input from entity
        ├── PlayerTag.cs                UNCHANGED
        ├── CameraTargetData.cs         UNCHANGED
        ├── CameraFollowSystem.cs       MODIFIED (Task 11) — client-only, GhostOwnerIsLocal
        ├── CameraFollowMono.cs         UNCHANGED
        └── CapsuleDemoAuthoring.cs     UNCHANGED — moves into the prefab in Task 12
```

No new `.asmdef` files. Everything continues to compile into `Assembly-CSharp`.

---

## Task 0: Verify clean baseline

**Files:** none.

- [ ] **Step 1: Confirm git baseline**

Run: `git status --short`
Expected: only pre-existing pending changes from before the branch (e.g. `M CLAUDE.md`, `M Packages/manifest.json`, etc. as established at branch start). No tracked changes from this plan yet.

- [ ] **Step 2: Confirm Unity console is clean**

Call `mcp__unity-mcp__Unity_GetConsoleLogs` with `logTypes: "Error,Exception"`, `maxEntries: 50`.
Expected: empty error list, `success: true`. If errors exist, surface them and stop — do not start the plan on a broken baseline.

- [ ] **Step 3: Confirm the existing demo runs**

This is a one-time sanity check. Open `Assets/Scenes/SampleScene.unity` in the Editor, ensure the `EcsDemoSub` subscene is loaded, hit Play, press W/A/S/D — capsule moves, camera follows. Stop Play. If broken, surface and stop.

No commit.

---

## Task 1: Verify Netcode 1.13 references compile

**Files:** none.

This task exists to catch a wrong package version before writing five files that depend on the API. We do this by adding a single throwaway `using` directive to a scratch file and removing it.

- [ ] **Step 1: Confirm `Unity.NetCode` namespace resolves**

Open `Assets/Scripts/Demo/PlayerTag.cs` and add `using Unity.NetCode;` at the top temporarily. Save. Wait for Editor reimport.

- [ ] **Step 2: Verify no compile error**

Call `mcp__unity-mcp__Unity_GetConsoleLogs` with `logTypes: "Error"`, `maxEntries: 20`.
Expected: no errors. If `Unity.NetCode` is missing, the netcode package isn't installed correctly — stop and fix the package state first.

- [ ] **Step 3: Revert**

Remove the `using Unity.NetCode;` line from `PlayerTag.cs`. Save. No commit.

---

## Task 2: Add `PlayerInput` (IInputComponentData)

**Files:**
- Create: `Assets/Scripts/Demo/PlayerInput.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Demo/PlayerInput.cs` with:

```csharp
using Unity.Mathematics;
using Unity.NetCode;

// Per-player input. Lives on the player ghost. Netcode auto-generates an
// InputBufferData<PlayerInput>, replicates client→server, and applies the
// per-tick value back into PlayerInput inside the prediction loop.
//
// MoveAxis is already isometric-projected (world-XZ direction). Projection
// happens client-side in PlayerInputCollectionSystem because the camera
// basis is a client concern; the server has no camera.
public struct PlayerInput : IInputComponentData
{
    public float2 MoveAxis;
}
```

- [ ] **Step 2: Verify compile**

Wait for Editor reimport. Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/PlayerInput.cs Assets/Scripts/Demo/PlayerInput.cs.meta
git commit -m "feat(net): add PlayerInput IInputComponentData"
```

---

## Task 3: Add `PlayerSpawner` singleton component

**Files:**
- Create: `Assets/Scripts/Net/PlayerSpawner.cs`

Note: `Assets/Scripts/Net/` does not exist yet. Create it before adding the file (Unity Editor: right-click `Assets/Scripts/` → Create → Folder → "Net"; or `mkdir -p Assets/Scripts/Net` in the terminal — the meta file will be generated by Unity on next import).

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Net/PlayerSpawner.cs` with:

```csharp
using Unity.Entities;

// Server-side singleton holding the baked Entity reference to the player
// ghost prefab. Created by PlayerSpawnerAuthoring's baker.
public struct PlayerSpawner : IComponentData
{
    public Entity Prefab;
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Net/
git commit -m "feat(net): add PlayerSpawner singleton component"
```

---

## Task 4: Add `PlayerSpawnerAuthoring`

**Files:**
- Create: `Assets/Scripts/Net/PlayerSpawnerAuthoring.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Net/PlayerSpawnerAuthoring.cs` with:

```csharp
using Unity.Entities;
using UnityEngine;

// Place one of these in the EcsDemoSub subscene and drag the
// PlayerCapsule prefab into the Prefab field. The baker bakes the
// prefab into an Entity reference and creates the PlayerSpawner singleton.
public class PlayerSpawnerAuthoring : MonoBehaviour
{
    [Tooltip("PlayerCapsule prefab — must have a GhostAuthoringComponent.")]
    public GameObject Prefab;

    class Baker : Baker<PlayerSpawnerAuthoring>
    {
        public override void Bake(PlayerSpawnerAuthoring authoring)
        {
            // The authoring entity itself isn't important; we attach the
            // singleton component to it so the baker stays self-contained.
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new PlayerSpawner
            {
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Net/PlayerSpawnerAuthoring.cs Assets/Scripts/Net/PlayerSpawnerAuthoring.cs.meta
git commit -m "feat(net): add PlayerSpawnerAuthoring + baker"
```

---

## Task 5: Add `PlayerSpawnSystem` (server-only)

**Files:**
- Create: `Assets/Scripts/Net/PlayerSpawnSystem.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Net/PlayerSpawnSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

// Server-only: for every new connection (NetworkId without
// NetworkStreamInGame), put the connection "in game" and instantiate
// one PlayerCapsule ghost owned by that connection.
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct PlayerSpawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerSpawner>();
        // Only run when there's at least one connection to consider.
        state.RequireForUpdate<NetworkId>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var prefab = SystemAPI.GetSingleton<PlayerSpawner>().Prefab;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (id, connectionEntity) in
                 SystemAPI.Query<RefRO<NetworkId>>()
                          .WithNone<NetworkStreamInGame>()
                          .WithEntityAccess())
        {
            ecb.AddComponent<NetworkStreamInGame>(connectionEntity);

            var player = ecb.Instantiate(prefab);
            ecb.SetComponent(player, new GhostOwner { NetworkId = id.ValueRO.Value });

            // Route this connection's input commands to the spawned ghost.
            ecb.SetComponent(connectionEntity, new CommandTarget { targetEntity = player });
        }

        ecb.Playback(state.EntityManager);
    }
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

If `CommandTarget` doesn't resolve, it lives in `Unity.NetCode` and is added to the connection entity automatically by the netcode package — no extra `using` needed beyond `Unity.NetCode`. If a real error appears, fix before committing.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Net/PlayerSpawnSystem.cs Assets/Scripts/Net/PlayerSpawnSystem.cs.meta
git commit -m "feat(net): server-side spawn-on-connect system"
```

---

## Task 6: Add `GameBootstrap`

**Files:**
- Create: `Assets/Scripts/Bootstrap/GameBootstrap.cs`

Note: `Assets/Scripts/Bootstrap/` does not exist yet — create the folder first.

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Bootstrap/GameBootstrap.cs` with:

```csharp
using Unity.Entities;
using Unity.NetCode;

// Custom bootstrap. Respects the PlayMode Tools window setting:
//   ClientAndServer → both worlds (default for hit-Play dev)
//   Server          → server only
//   Client          → client only
//
// AutoConnectPort is set so the client auto-connects to 127.0.0.1:7979
// and the server auto-listens on the same port — no manual RPC needed.
public class GameBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 7979;
        CreateDefaultClientServerWorlds();
        return true;
    }
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Bootstrap/
git commit -m "feat(net): add GameBootstrap respecting PlayMode tools"
```

---

## Task 7: Modify `PlayerMovementData` — Velocity becomes a `[GhostField]`

**Files:**
- Modify: `Assets/Scripts/Demo/PlayerMovementData.cs`

The `Velocity` field must replicate from server to client so prediction rollback restores a consistent state. `MoveSpeed` and `Acceleration` are bake-time constants — no need to replicate.

- [ ] **Step 1: Replace file contents**

Replace `Assets/Scripts/Demo/PlayerMovementData.cs` with:

```csharp
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct PlayerMovementData : IComponentData
{
    public float MoveSpeed;          // units/sec at full input
    public float Acceleration;       // velocity lerp rate (higher = snappier)
    [GhostField] public float2 Velocity;  // current smoothed XZ velocity; replicated for predicted ghost rollback
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/PlayerMovementData.cs
git commit -m "feat(net): mark PlayerMovementData.Velocity as GhostField"
```

---

## Task 8: Rewrite `PlayerMovementSystem` for predicted simulation

**Files:**
- Modify: `Assets/Scripts/Demo/PlayerMovementSystem.cs`

Moves the system into `PredictedSimulationSystemGroup` (runs on both client and server with the same Burst code), drops the singleton input dependency, and reads `PlayerInput` per-entity. The `Simulate` filter ensures the system only operates on entities Netcode has selected for this prediction tick.

- [ ] **Step 1: Replace file contents**

Replace `Assets/Scripts/Demo/PlayerMovementSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct PlayerMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (transform, movement, input) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRW<PlayerMovementData>, RefRO<PlayerInput>>()
                          .WithAll<PlayerTag, Simulate>())
        {
            float2 moveAxis = input.ValueRO.MoveAxis;
            float2 targetVelocity = moveAxis * movement.ValueRO.MoveSpeed;
            float t = 1f - math.exp(-movement.ValueRO.Acceleration * dt);
            float2 smoothedVelocity = math.lerp(movement.ValueRO.Velocity, targetVelocity, t);
            movement.ValueRW.Velocity = smoothedVelocity;

            float3 pos = transform.ValueRO.Position;
            pos.x += smoothedVelocity.x * dt;
            pos.z += smoothedVelocity.y * dt;

            transform.ValueRW = LocalTransform.FromPositionRotationScale(
                pos,
                transform.ValueRO.Rotation,
                transform.ValueRO.Scale
            );
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors. (Warnings about `PlayerInputData` no longer being used by `PlayerMovementSystem` are fine.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/PlayerMovementSystem.cs
git commit -m "feat(net): move PlayerMovementSystem to PredictedSimulationSystemGroup"
```

---

## Task 9: Replace `PlayerInputSystem` with `PlayerInputCollectionSystem`

**Files:**
- Rename: `Assets/Scripts/Demo/PlayerInputSystem.cs` → `Assets/Scripts/Demo/PlayerInputCollectionSystem.cs`
- Replace contents.

The rename must happen via the Unity Editor (right-click → Rename) so Unity preserves the `.meta` GUID and updates references. Do NOT use `mv` from the terminal — Unity's asset database may treat it as a delete-then-add and break references.

- [ ] **Step 1: Rename in Unity Editor**

In the Project window: right-click `Assets/Scripts/Demo/PlayerInputSystem.cs` → Rename → type `PlayerInputCollectionSystem.cs` → Enter. Wait for the reimport. The `.meta` file should be renamed automatically by Unity.

- [ ] **Step 2: Replace file contents**

Replace `Assets/Scripts/Demo/PlayerInputCollectionSystem.cs` with:

```csharp
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine.InputSystem;

// Client-only. Each tick, read the keyboard, isometric-project to a
// world-XZ direction, and write it onto the locally-owned player ghost's
// PlayerInput component. Netcode then auto-buffers and replicates to the
// server, and the prediction loop applies the value to the ghost's
// IInputComponentData on both worlds.
[UpdateInGroup(typeof(GhostInputSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class PlayerInputCollectionSystem : SystemBase
{
    // sqrt(2)/2 — the isometric projection constant
    const float k = 0.70711f;

    protected override void OnUpdate()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float rawX = 0f, rawY = 0f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) rawX += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)  rawX -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)    rawY += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)  rawY -= 1f;

        float2 moveAxis = float2.zero;
        if (rawX != 0f || rawY != 0f)
        {
            float2 raw = math.normalize(new float2(rawX, rawY));
            moveAxis = new float2(
                raw.x * k + raw.y * k,
                raw.x * -k + raw.y * k
            );
        }

        foreach (var input in
                 SystemAPI.Query<RefRW<PlayerInput>>()
                          .WithAll<GhostOwnerIsLocal>())
        {
            input.ValueRW.MoveAxis = moveAxis;
        }
    }
}
```

- [ ] **Step 3: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: only an error/warning about `PlayerInputData` no longer being created (because we no longer call `EntityManager.CreateSingleton<PlayerInputData>()`) — that's fine; we delete `PlayerInputData` next. **No other errors.**

If you see `The type 'PlayerInputData' does not exist`, it's because something stale references it. Search with `grep -r "PlayerInputData" Assets/Scripts` and fix.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/PlayerInputCollectionSystem.cs Assets/Scripts/Demo/PlayerInputCollectionSystem.cs.meta
git rm Assets/Scripts/Demo/PlayerInputSystem.cs Assets/Scripts/Demo/PlayerInputSystem.cs.meta 2>/dev/null || true
git commit -m "feat(net): replace PlayerInputSystem with client-only PlayerInputCollectionSystem"
```

(The `git rm` is a no-op if the rename already removed the old paths from the index. The `|| true` keeps the chain going either way.)

---

## Task 10: Delete `PlayerInputData.cs`

**Files:**
- Delete: `Assets/Scripts/Demo/PlayerInputData.cs`
- Delete: `Assets/Scripts/Demo/PlayerInputData.cs.meta`

The singleton is no longer created or read by anyone after Tasks 8 and 9.

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rn "PlayerInputData" Assets/Scripts`
Expected: empty output. If anything matches, stop and remove the reference first.

- [ ] **Step 2: Delete via Unity Editor**

Project window: right-click `Assets/Scripts/Demo/PlayerInputData.cs` → Delete → confirm. Unity removes the `.meta` file too.

- [ ] **Step 3: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Scripts/Demo/
git commit -m "feat(net): remove PlayerInputData singleton (replaced by IInputComponentData)"
```

---

## Task 11: Modify `CameraFollowSystem` — client-only, GhostOwnerIsLocal

**Files:**
- Modify: `Assets/Scripts/Demo/CameraFollowSystem.cs`

The camera should only follow the *local* player's ghost, not the server's authoritative copy of someone else's player (in future multi-player scenarios) and not the server-only ghost copy. `GhostOwnerIsLocal` is an enableable component Netcode flips on for ghosts owned by the local connection.

- [ ] **Step 1: Replace file contents**

Replace `Assets/Scripts/Demo/CameraFollowSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct CameraFollowSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<CameraTargetData>();
        state.RequireForUpdate<PlayerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var transform in
                 SystemAPI.Query<RefRO<LocalTransform>>()
                          .WithAll<PlayerTag, GhostOwnerIsLocal>())
        {
            SystemAPI.SetSingleton(new CameraTargetData
            {
                Position = transform.ValueRO.Position
            });
            return;
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Call `Unity_GetConsoleLogs` with `logTypes: "Error"`.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/CameraFollowSystem.cs
git commit -m "feat(net): client-only CameraFollowSystem follows owned ghost"
```

---

## Task 12: Editor — create `PlayerCapsule.prefab`

**Files:**
- Create: `Assets/Prefabs/PlayerCapsule.prefab` (+ `.meta`)

This is an Editor-driven task. Each step is a click sequence in the Unity Editor.

- [ ] **Step 1: Create the Prefabs folder**

In the Project window: right-click `Assets` → Create → Folder → "Prefabs". (Or `mkdir -p Assets/Prefabs` in the terminal — Unity will generate the meta on next focus.)

- [ ] **Step 2: Create the capsule GameObject in the main scene**

Open `Assets/Scenes/SampleScene.unity` (the main scene, *not* the subscene). Hierarchy: right-click → 3D Object → Capsule. This places a capsule with a MeshFilter, MeshRenderer, CapsuleCollider, and a default URP material.

- [ ] **Step 3: Strip the collider**

Select the capsule. In the Inspector, right-click `Capsule Collider` → Remove Component. (We don't use Unity physics for ECS; the collider would just confuse readers.)

- [ ] **Step 4: Add `CapsuleDemoAuthoring`**

Inspector: Add Component → search "CapsuleDemoAuthoring". Leave `moveSpeed = 5`, `acceleration = 10`.

- [ ] **Step 5: Add `GhostAuthoringComponent`**

Inspector: Add Component → search "Ghost Authoring Component". Set:
- **Name** (auto-fills) — leave as "PlayerCapsule".
- **Default Ghost Mode**: `OwnerPredicted` (the owning client predicts; others would interpolate — irrelevant here with one player but the right default).
- **Has Owner**: ✓ (adds `GhostOwner` to the ghost so the server can assign it to a connection).
- **Support Auto Command Target**: ✓ (so `PlayerInput` is auto-routed to the ghost via the owner connection's `CommandTarget`; matches the `SetComponent(connection, CommandTarget{...})` in `PlayerSpawnSystem`).
- **Importance**: 1.
- Leave the rest at defaults.

`LocalTransform` is automatically serialized by the default ghost variant Netcode applies to every ghost, so `Position` (and `Rotation`) replicate without any extra wiring on the prefab — there's nothing to check or toggle for that.

- [ ] **Step 6: Drag the capsule into `Assets/Prefabs/`**

Drag the Capsule GameObject from the Hierarchy into the `Assets/Prefabs/` folder in the Project window. Unity creates `PlayerCapsule.prefab`. The prefab variant icon should appear next to the GameObject in the Hierarchy.

- [ ] **Step 7: Delete the capsule from the scene**

In the Hierarchy, right-click the (now-blue) Capsule GameObject → Delete. The prefab on disk is unaffected.

- [ ] **Step 8: Save the scene**

`File → Save` (or Cmd+S). The scene change is just removing the temporary capsule we used as a prefab source.

- [ ] **Step 9: Verify compile + bake**

Call `Unity_GetConsoleLogs` with `logTypes: "Error,Exception"`.
Expected: no errors. Warnings about prefab baking are OK.

- [ ] **Step 10: Commit**

```bash
git add Assets/Prefabs Assets/Scenes/SampleScene.unity
git commit -m "feat(net): create PlayerCapsule ghost prefab"
```

(`SampleScene.unity` likely shows no real change after the delete — if `git diff` shows nothing for it, just commit `Assets/Prefabs`.)

---

## Task 13: Editor — modify `EcsDemoSub` subscene

**Files:**
- Modify: `Assets/Scenes/SampleScene/EcsDemoSub.unity`

- [ ] **Step 1: Open the subscene for editing**

Open `Assets/Scenes/SampleScene.unity`. In the Hierarchy, find the `EcsDemoSub` SubScene component. Click "Open" next to its asset reference in the Inspector (or set its open-checkbox in the SubScene component). The subscene contents now appear editable in the Hierarchy as nested entries.

- [ ] **Step 2: Delete the existing baked capsule**

Inside the EcsDemoSub subscene's hierarchy, find the GameObject with `CapsuleDemoAuthoring` attached (likely named "Capsule" or similar). Right-click → Delete.

- [ ] **Step 3: Add a `PlayerSpawner` GameObject**

Inside the subscene: right-click → Create Empty. Rename to `PlayerSpawner`. Inspector → Add Component → search "PlayerSpawnerAuthoring".

- [ ] **Step 4: Wire the prefab reference**

Drag `Assets/Prefabs/PlayerCapsule.prefab` from the Project window into the `Prefab` field of `PlayerSpawnerAuthoring` on the new GameObject.

- [ ] **Step 5: Close the subscene**

In the Hierarchy, the SubScene "open" toggle off / click "Close" in the Inspector. Unity re-bakes the subscene.

- [ ] **Step 6: Save**

Cmd+S — saves the parent scene; subscene asset is saved on close.

- [ ] **Step 7: Verify bake**

Call `Unity_GetConsoleLogs` with `logTypes: "Error,Exception"`.
Expected: no errors. Bake-time warnings (e.g. "ghost prefab found in subscene") are OK to read but should not prevent commit if the only errors are runtime expectations.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scenes/SampleScene/EcsDemoSub.unity
git commit -m "feat(net): replace baked capsule with PlayerSpawner in EcsDemoSub"
```

---

## Task 14: Acceptance — ClientAndServer mode

**Files:** none.

- [ ] **Step 1: Set PlayMode to ClientAndServer**

`Window → Multiplayer → PlayMode Tools` (or `Multiplayer → Window → PlayMode Tools` depending on Editor menu layout). Set "PlayMode Type" to **Client & Server**.

- [ ] **Step 2: Hit Play**

Open `Assets/Scenes/SampleScene.unity`. Press Play.

- [ ] **Step 3: Verify spawn + movement**

Expected:
- One capsule appears at world origin within ~1 second of Play start.
- W / A / S / D / arrow keys move the capsule in isometric projection (W moves up-and-right on screen, etc.).
- The Camera follows the capsule.
- Console (via Unity MCP) shows no errors or exceptions during PlayMode.

Run: `mcp__unity-mcp__Unity_GetConsoleLogs` with `logTypes: "Error,Exception"` while in PlayMode.
Expected: empty or only known-benign warnings. Real errors mean stop and debug.

- [ ] **Step 4: Capture a screenshot for the record**

Call `mcp__unity-mcp__Unity_SceneView_Capture2DScene` (or `Unity_Camera_Capture` for a Game-view shot) to confirm the capsule renders.

- [ ] **Step 5: Stop Play**

Press Play again to exit. No commit (no file changes).

---

## Task 15: Acceptance — Server-only mode

**Files:** none.

- [ ] **Step 1: Set PlayMode to Server**

PlayMode Tools window → "PlayMode Type" → **Server**.

- [ ] **Step 2: Hit Play**

- [ ] **Step 3: Verify**

Expected:
- No capsule visible (no client connected, no ghost replicated).
- Console shows the server listening (look for a log line containing "Listen" or "Listening" with port 7979).
- No errors.

Run: `mcp__unity-mcp__Unity_GetConsoleLogs` with `logTypes: "Error,Exception"`.
Expected: empty.

- [ ] **Step 4: Stop Play**

- [ ] **Step 5: Reset PlayMode for normal dev**

PlayMode Tools window → "PlayMode Type" → **Client & Server** (the dev default).

No commit. Implementation complete.

---

## Done

After Task 15:
- The demo subscene is functionally equivalent to before (one capsule the player drives), but the simulation is now server-authoritative with client prediction.
- The pattern is in place for every later replicated entity (units, generals, siege engines): an authoring + baker, an `IInputComponentData` if it takes player input, a server spawn path, and a `PredictedSimulationSystemGroup` system for movement/state.
- The user's pending package and CLAUDE.md edits remain in the worktree, untouched, for the user to commit separately.

**Out of scope (future work — not in this plan):**
- Multiplayer Play Mode for two-Editor-instance testing.
- AOI / `GhostDistanceImportance` partitioning.
- Snapshot history tuning, `MaxSendEntities`, adaptive `MaxSendRate`.
- CLAUDE.md netcode version line update (1.11.0 → 1.13.0).
- Automated tests (would need an `.asmdef` for `Unity.Entities.Tests`).
