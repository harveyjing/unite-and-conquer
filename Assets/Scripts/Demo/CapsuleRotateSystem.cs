using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct CapsuleRotateSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        foreach (var transform in
                 SystemAPI.Query<RefRW<LocalTransform>>()
                          .WithAll<RotateTag>())
        {
            transform.ValueRW = transform.ValueRO.RotateX(dt * math.PI * 0.5f);
        }
    }
}
