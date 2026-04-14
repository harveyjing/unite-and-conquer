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
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerInputData>();
        state.RequireForUpdate<PlayerTag>();
    }

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
