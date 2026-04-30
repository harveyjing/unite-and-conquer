using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ObstacleSpawnSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PrefabSpawner>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var obstaclePrefab = SystemAPI.GetSingleton<PrefabSpawner>().ObstaclePrefab;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var rng = Random.CreateFromIndex(42u);

            const int count = 20;
            const float range = 15f;

            for (int i = 0; i < count; i++)
            {
                var obstacle = ecb.Instantiate(obstaclePrefab);
                ecb.SetComponent(
                    obstacle,
                    LocalTransform.FromPosition(
                        new float3(rng.NextFloat(-range, range), 0.5f, rng.NextFloat(-range, range))
                    )
                );
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            state.Enabled = false;
        }
    }
}
