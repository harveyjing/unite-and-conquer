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
