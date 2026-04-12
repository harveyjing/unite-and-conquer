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
        RequireForUpdate<PlayerInputData>();
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
