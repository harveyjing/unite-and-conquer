# Character Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the auto-rotating capsule demo with a WASD-controlled character that moves in camera-relative isometric space, with a following camera, using the full ECS/Burst stack.

**Architecture:** A managed `SystemBase` reads keyboard input and writes to a `PlayerInputData` singleton; a Burst `ISystem` reads that singleton and applies smoothed velocity to `LocalTransform`; a second Burst `ISystem` publishes the player position to a `CameraTargetData` singleton consumed by a `MonoBehaviour` on the camera.

**Tech Stack:** Unity 6000.4.1f1 · Unity Entities (ECS) · Burst · Unity.InputSystem · URP

---

## File Map

| File | Action |
|---|---|
| `Assets/Scripts/Demo/PlayerTag.cs` | Create — empty `IComponentData` tag |
| `Assets/Scripts/Demo/PlayerInputData.cs` | Create — singleton `IComponentData` |
| `Assets/Scripts/Demo/PlayerMovementData.cs` | Create — `IComponentData` with speed / accel / velocity |
| `Assets/Scripts/Demo/CameraTargetData.cs` | Create — singleton `IComponentData` |
| `Assets/Scripts/Demo/PlayerInputSystem.cs` | Create — managed `SystemBase`, reads Input System |
| `Assets/Scripts/Demo/PlayerMovementSystem.cs` | Create — Burst `ISystem`, moves player |
| `Assets/Scripts/Demo/CameraFollowSystem.cs` | Create — Burst `ISystem`, writes `CameraTargetData` |
| `Assets/Scripts/Demo/CameraFollowMono.cs` | Create — `MonoBehaviour` on Main Camera |
| `Assets/Scripts/Demo/CapsuleDemoAuthoring.cs` | Modify — swap `RotateTag` for `PlayerTag` + `PlayerMovementData` |
| `Assets/Scripts/Demo/CapsuleRotateSystem.cs` | Delete |
| `Assets/Scripts/Demo/RotateTag.cs` | Delete |

---

## Task 1: Component Data Structs

**Files:**
- Create: `Assets/Scripts/Demo/PlayerTag.cs`
- Create: `Assets/Scripts/Demo/PlayerInputData.cs`
- Create: `Assets/Scripts/Demo/PlayerMovementData.cs`
- Create: `Assets/Scripts/Demo/CameraTargetData.cs`

- [ ] **Step 1: Create `PlayerTag.cs`**

```csharp
using Unity.Entities;

public struct PlayerTag : IComponentData { }
```

- [ ] **Step 2: Create `PlayerInputData.cs`**

```csharp
using Unity.Entities;
using Unity.Mathematics;

public struct PlayerInputData : IComponentData
{
    // Normalized, camera-relative move direction in world XZ.
    // x = world X axis contribution, y = world Z axis contribution.
    public float2 MoveAxis;
}
```

- [ ] **Step 3: Create `PlayerMovementData.cs`**

```csharp
using Unity.Entities;
using Unity.Mathematics;

public struct PlayerMovementData : IComponentData
{
    public float MoveSpeed;     // units/sec at full input
    public float Acceleration;  // velocity lerp rate (higher = snappier)
    public float2 Velocity;     // current smoothed XZ velocity (mutable)
}
```

- [ ] **Step 4: Create `CameraTargetData.cs`**

```csharp
using Unity.Entities;
using Unity.Mathematics;

public struct CameraTargetData : IComponentData
{
    public float3 Position; // world position of the player this frame
}
```

- [ ] **Step 5: Verify compilation**

Switch to the Unity Editor. Check the Console window (Window → General → Console). There must be **zero** compiler errors. Warnings are acceptable.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/PlayerTag.cs \
        Assets/Scripts/Demo/PlayerInputData.cs \
        Assets/Scripts/Demo/PlayerMovementData.cs \
        Assets/Scripts/Demo/CameraTargetData.cs
git commit -m "feat(ecs-demo): add player component data structs"
```

---

## Task 2: PlayerInputSystem

**Files:**
- Create: `Assets/Scripts/Demo/PlayerInputSystem.cs`

- [ ] **Step 1: Create `PlayerInputSystem.cs`**

This is a **managed** `SystemBase` — no `[BurstCompile]` — because `Keyboard.current` is a managed Unity Input System API.

The isometric projection: camera sits at offset `(-10, 14, -10)`, so camera-forward in XZ is `(1, 0, 1)` normalized and camera-right is `(1, 0, -1)` normalized. Multiplying raw input `(rawX, rawY)` (where rawX = D/A, rawY = W/S) by these vectors gives the world-space move direction.

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class PlayerInputSystem : SystemBase
{
    // sqrt(2)/2 — the isometric projection constant
    const float k = 0.70711f;

    protected override void OnCreate()
    {
        // Create the singleton so other systems can always find it
        EntityManager.CreateSingleton<PlayerInputData>();
    }

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
            // Project onto isometric camera axes
            // camRight = (k, 0, -k)  →  world X = raw.x * k + raw.y * k
            // camForward = (k, 0, k) →  world Z = raw.x * -k + raw.y * k
            moveAxis = new float2(
                raw.x * k + raw.y * k,
                raw.x * -k + raw.y * k
            );
        }

        SystemAPI.GetSingletonRW<PlayerInputData>().ValueRW.MoveAxis = moveAxis;
    }
}
```

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/PlayerInputSystem.cs
git commit -m "feat(ecs-demo): add PlayerInputSystem (camera-relative WASD)"
```

---

## Task 3: PlayerMovementSystem

**Files:**
- Create: `Assets/Scripts/Demo/PlayerMovementSystem.cs`

- [ ] **Step 1: Create `PlayerMovementSystem.cs`**

Burst-compiled `ISystem`. Reads the `PlayerInputData` singleton, lerps velocity, writes to `LocalTransform`. Y axis is intentionally untouched (no gravity).

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerInputSystem))]
public partial struct PlayerMovementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        float2 moveAxis = SystemAPI.GetSingleton<PlayerInputData>().MoveAxis;

        foreach (var (transform, movement) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRW<PlayerMovementData>>()
                          .WithAll<PlayerTag>())
        {
            float2 targetVelocity = moveAxis * movement.ValueRO.MoveSpeed;
            float t = math.saturate(movement.ValueRO.Acceleration * dt);
            movement.ValueRW.Velocity = math.lerp(movement.ValueRO.Velocity, targetVelocity, t);

            float3 pos = transform.ValueRO.Position;
            pos.x += movement.ValueRO.Velocity.x * dt;
            pos.z += movement.ValueRO.Velocity.y * dt;

            transform.ValueRW = LocalTransform.FromPositionRotationScale(
                pos,
                transform.ValueRO.Rotation,
                transform.ValueRO.Scale
            );
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/PlayerMovementSystem.cs
git commit -m "feat(ecs-demo): add PlayerMovementSystem (Burst, smoothed velocity)"
```

---

## Task 4: CameraFollowSystem

**Files:**
- Create: `Assets/Scripts/Demo/CameraFollowSystem.cs`

- [ ] **Step 1: Create `CameraFollowSystem.cs`**

Burst-compiled `ISystem`. Runs in `PresentationSystemGroup` (after simulation) so the camera reads the position after movement has already been applied this frame.

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton<CameraTargetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var transform in
                 SystemAPI.Query<RefRO<LocalTransform>>()
                          .WithAll<PlayerTag>())
        {
            SystemAPI.SetSingleton(new CameraTargetData
            {
                Position = transform.ValueRO.Position
            });
            return; // single player — exit after first match
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/CameraFollowSystem.cs
git commit -m "feat(ecs-demo): add CameraFollowSystem (Burst, publishes player pos)"
```

---

## Task 5: CameraFollowMono

**Files:**
- Create: `Assets/Scripts/Demo/CameraFollowMono.cs`

- [ ] **Step 1: Create `CameraFollowMono.cs`**

A `MonoBehaviour` attached to the Main Camera in the legacy scene. Caches the `EntityQuery` in `Start` to avoid per-frame allocation. Reads `CameraTargetData` singleton in `LateUpdate` (after all ECS systems have run).

```csharp
using Unity.Entities;
using UnityEngine;

public class CameraFollowMono : MonoBehaviour
{
    [Tooltip("World-space offset from the player position to the camera.")]
    public Vector3 offset = new Vector3(-10f, 14f, -10f);

    EntityQuery _query;
    bool _queryCreated;

    void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        _query = world.EntityManager.CreateEntityQuery(typeof(CameraTargetData));
        _queryCreated = true;
    }

    void LateUpdate()
    {
        if (!_queryCreated || _query.IsEmpty) return;
        var target = _query.GetSingleton<CameraTargetData>();
        transform.position = new Vector3(
            target.Position.x + offset.x,
            target.Position.y + offset.y,
            target.Position.z + offset.z
        );
    }

    void OnDestroy()
    {
        if (_queryCreated) _query.Dispose();
    }
}
```

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/CameraFollowMono.cs
git commit -m "feat(ecs-demo): add CameraFollowMono (camera tracks player via ECS singleton)"
```

---

## Task 6: Update CapsuleDemoAuthoring

**Files:**
- Modify: `Assets/Scripts/Demo/CapsuleDemoAuthoring.cs`

- [ ] **Step 1: Replace the file contents**

Expose `moveSpeed` and `acceleration` as Inspector-tunable fields. The baker now adds `PlayerTag` and `PlayerMovementData` instead of `RotateTag`.

```csharp
using Unity.Entities;
using UnityEngine;

public class CapsuleDemoAuthoring : MonoBehaviour
{
    [Tooltip("Units per second at full input.")]
    public float moveSpeed = 5f;

    [Tooltip("Velocity ramp rate. Higher = snappier response.")]
    public float acceleration = 10f;

    class Baker : Baker<CapsuleDemoAuthoring>
    {
        public override void Bake(CapsuleDemoAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
            AddComponent(entity, new PlayerMovementData
            {
                MoveSpeed    = authoring.moveSpeed,
                Acceleration = authoring.acceleration,
                Velocity     = default
            });
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/CapsuleDemoAuthoring.cs
git commit -m "feat(ecs-demo): update authoring to bake PlayerTag + PlayerMovementData"
```

---

## Task 7: Delete Retired Files

**Files:**
- Delete: `Assets/Scripts/Demo/CapsuleRotateSystem.cs`
- Delete: `Assets/Scripts/Demo/RotateTag.cs`
- Delete: `Assets/Scripts/Demo/CapsuleRotateSystem.cs.meta`
- Delete: `Assets/Scripts/Demo/RotateTag.cs.meta`

- [ ] **Step 1: Delete the files**

```bash
git rm Assets/Scripts/Demo/CapsuleRotateSystem.cs \
       Assets/Scripts/Demo/RotateTag.cs
```

(Unity's `.meta` files are tracked by git; `git rm` will remove both the `.cs` and any staged `.meta` if they exist. If the `.meta` files are untracked, also run `rm` on them and `git add -u`.)

- [ ] **Step 2: Verify compilation**

Unity Editor Console — zero errors. The capsule in the subscene is no longer rotating on its own.

- [ ] **Step 3: Commit**

```bash
git commit -m "refactor(ecs-demo): retire CapsuleRotateSystem and RotateTag"
```

---

## Task 8: Scene Wiring & Play Mode Verification

**No code changes — scene setup and manual testing only.**

- [ ] **Step 1: Add `CameraFollowMono` to the Main Camera**

In Unity Editor:
1. In the Hierarchy, select **Main Camera** (in the legacy scene, not the subscene).
2. In the Inspector, click **Add Component** → search for `CameraFollowMono` → add it.
3. Confirm the **Offset** field shows `(-10, 14, -10)`. Adjust if the scene's camera already has a different position — set offset to match the visual you want.

- [ ] **Step 2: Point the camera at the origin**

With Main Camera selected, set its **Rotation** to `(35, 45, 0)` in the Inspector Transform — this gives the standard isometric look direction that matches the `(-10, 14, -10)` offset.

- [ ] **Step 3: Enter Play Mode and test**

Press the Unity Play button. In Play Mode:

| Action | Expected result |
|---|---|
| Press **W** | Capsule moves toward upper-right on screen (world +X+Z diagonal) |
| Press **S** | Capsule moves toward lower-left |
| Press **A** | Capsule moves toward upper-left |
| Press **D** | Capsule moves toward lower-right |
| Release all keys | Capsule decelerates smoothly and stops |
| Move in any direction | Camera follows, keeping capsule roughly centered |
| Capsule does **not** rotate on its own | Confirmed: `CapsuleRotateSystem` is gone |

- [ ] **Step 4: Tune if needed**

If movement feels too slow/fast: select the capsule GameObject in the subscene → the `CapsuleDemoAuthoring` component exposes **Move Speed** and **Acceleration** in the Inspector. Adjust without code changes.

If the camera offset is wrong: select Main Camera → `CameraFollowMono` → adjust **Offset** field.

- [ ] **Step 5: Commit final state**

```bash
git add Assets/Scenes/
git commit -m "feat(ecs-demo): wire CameraFollowMono to Main Camera, capsule character control complete"
```

---

## Done

At this point:
- WASD moves the capsule in camera-relative isometric directions with smooth acceleration
- The camera follows the capsule at a fixed isometric angle
- All simulation runs through Burst `ISystem`s
- Input reading is quarantined to a single managed `SystemBase`
- The `PlayerInputData` singleton is the clean seam for future netcode integration
