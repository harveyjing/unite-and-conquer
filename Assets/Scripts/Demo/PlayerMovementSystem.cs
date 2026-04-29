using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct PlayerMovementSystem : ISystem
{
    // [BurstCompile]
    // public void OnCreate(ref SystemState state)
    // {
    //     state.RequireForUpdate<PlayerTag>();
    // }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var speed = SystemAPI.Time.DeltaTime * 4;

        foreach (var (input, trans) in
                 SystemAPI.Query<RefRO<PlayerInput>, RefRW<LocalTransform>>()
                          .WithAll<PlayerTag, Simulate>())
        {
            var moveInput = new float2(input.ValueRO.Horizontal, input.ValueRO.Vertical);
            moveInput = math.normalizesafe(moveInput) * speed;
            trans.ValueRW.Position += new float3(moveInput.x, 0, moveInput.y);
        }
    }
}
