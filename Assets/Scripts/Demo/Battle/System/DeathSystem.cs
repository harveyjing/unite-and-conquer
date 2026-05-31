using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeDamageSystem))]
    public partial struct DeathSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
            foreach (var (health, entity) in
                     SystemAPI.Query<RefRO<Health>>()
                              .WithAll<Soldier>()
                              .WithEntityAccess())
            {
                if (health.ValueRO.Current <= 0f)
                    ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
