using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;


public struct PlayerCapsule: IComponentData
{
    public int NetworkId;
    public int2 Location;
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct PlayerSpawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<PlayerCapsule>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
        state.RequireForUpdate<NetworkId>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var prefab = SystemAPI.GetSingleton<PlayerSpawner>().Prefab;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (playerCapsule, entity) in
                 SystemAPI.Query<RefRO<PlayerCapsule>>().WithEntityAccess())
        {
            var player = ecb.Instantiate(prefab);
            ecb.SetComponent(player, new GhostOwner { NetworkId = playerCapsule.ValueRO.NetworkId });

            ecb.DestroyEntity(entity);
            UnityEngine.Debug.Log($"Spawning player for connection '{playerCapsule.ValueRO.NetworkId}' at location {playerCapsule.ValueRO.Location}");
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
