# Character Control — Design Spec

**Date:** 2026-04-12  
**Status:** Approved  
**Scope:** Demo capsule becomes a keyboard-controlled character using Unity ECS/DOTS

---

## Goal

Replace the auto-rotating capsule demo with a player-controlled character. The player presses WASD to move the capsule around the world. Movement is camera-relative (isometric fixed angle). The camera follows the capsule. All simulation logic uses the ECS stack (ISystem + Burst); managed code is strictly limited to input reading and camera positioning.

---

## Components

### `PlayerTag : IComponentData`
Empty tag struct. Marks the player entity. Follows the existing `RotateTag` pattern.

### `PlayerInputData : IComponentData`
Singleton. Written each frame by `PlayerInputSystem`.

```csharp
float2 MoveAxis   // normalized, camera-relative input direction
```

### `PlayerMovementData : IComponentData`
Lives on the player entity. Holds movement parameters and mutable state.

```csharp
float MoveSpeed      // units/sec at full throttle (default: 5)
float Acceleration   // velocity ramp rate (default: 10)
float2 Velocity      // current smoothed velocity (mutable, starts at zero)
```

### `CameraTargetData : IComponentData`
Singleton. Written each frame by `CameraFollowSystem`. Read by `CameraFollowMono`.

```csharp
float3 Position   // world position of the player entity this frame
```

---

## Systems

### `PlayerInputSystem : SystemBase` (managed — required for Input System)

**Responsibility:** Read raw keyboard input, project it into camera-relative world space, write to singleton.

**Logic:**
1. Read `Keyboard.current` for WASD / arrow keys → raw `float2`
2. If non-zero, normalize
3. Rotate by isometric camera yaw (45°) so screen-up maps to world diagonal
4. Write into `PlayerInputData` singleton (created in `OnCreate` via `EntityManager.CreateSingleton`)

**Group:** `SimulationSystemGroup` (before `PlayerMovementSystem`)

---

### `PlayerMovementSystem : ISystem` (Burst-compiled)

**Responsibility:** Apply smoothed velocity to the player's `LocalTransform`.

**Logic:**
1. Read `PlayerInputData` singleton
2. Query entities with `PlayerTag + PlayerMovementData + LocalTransform`
3. Per entity:
   - Target velocity = `MoveAxis * MoveSpeed`
   - `Velocity = math.lerp(Velocity, targetVelocity, Acceleration * dt)`
   - `Position.xz += Velocity * dt`  (Y is not affected — no gravity)

**Group:** `SimulationSystemGroup`

---

### `CameraFollowSystem : ISystem` (Burst-compiled)

**Responsibility:** Publish player world position to the `CameraTargetData` singleton.

**Logic:**
1. Query the single entity with `PlayerTag + LocalTransform`
2. Write `LocalTransform.Position` into `CameraTargetData` singleton (created in `OnCreate`)

**Group:** `PresentationSystemGroup` (after simulation, before rendering)

---

## MonoBehaviour Bridge

### `CameraFollowMono : MonoBehaviour`

Attached to the **Main Camera** GameObject in the legacy scene (not a subscene).

**Serialized fields:**
- `Vector3 offset` — inspector-set isometric offset (default: `(-10, 14, -10)`)

**Logic (`LateUpdate`):**
1. Get the default `World`
2. Read `CameraTargetData` singleton via `EntityManager`
3. Set `transform.position = target.Position + offset`

No ECS system is used for this — the camera is a managed GameObject and must be moved from managed code.

---

## Authoring Changes

### `CapsuleDemoAuthoring` (modified)

Remove `RotateTag`. Add `PlayerTag` and `PlayerMovementData` in the baker. Expose `moveSpeed` and `acceleration` as serialized float fields on the MonoBehaviour so they are tunable in the Inspector.

### `CapsuleRotateSystem` (retired)

Delete the file. The capsule no longer rotates autonomously.

---

## File Plan

| File | Action |
|---|---|
| `Assets/Scripts/Demo/CapsuleDemoAuthoring.cs` | Modify — swap RotateTag for PlayerTag + PlayerMovementData |
| `Assets/Scripts/Demo/CapsuleRotateSystem.cs` | Delete |
| `Assets/Scripts/Demo/RotateTag.cs` | Delete |
| `Assets/Scripts/Demo/PlayerTag.cs` | New — empty tag IComponentData |
| `Assets/Scripts/Demo/PlayerInputData.cs` | New — singleton IComponentData |
| `Assets/Scripts/Demo/PlayerMovementData.cs` | New — IComponentData with speed/accel/velocity |
| `Assets/Scripts/Demo/CameraTargetData.cs` | New — singleton IComponentData |
| `Assets/Scripts/Demo/PlayerInputSystem.cs` | New — managed SystemBase, reads Input System |
| `Assets/Scripts/Demo/PlayerMovementSystem.cs` | New — Burst ISystem, moves player |
| `Assets/Scripts/Demo/CameraFollowSystem.cs` | New — Burst ISystem, writes CameraTargetData |
| `Assets/Scripts/Demo/CameraFollowMono.cs` | New — MonoBehaviour on Main Camera |

---

## Constraints & Non-Goals

- **No Unity Physics package.** Movement is direct `LocalTransform` mutation. Collision is out of scope for this demo.
- **No netcode wiring yet.** `PlayerInputData` is a local singleton — designed to be replaced by a ghost component later without touching `PlayerMovementSystem`.
- **Y axis is locked.** The capsule stays on a flat plane; no gravity or jumping.
- **Single player only.** One entity with `PlayerTag`. Multi-player is not in scope for this demo.
