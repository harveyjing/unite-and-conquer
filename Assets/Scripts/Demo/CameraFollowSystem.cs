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
