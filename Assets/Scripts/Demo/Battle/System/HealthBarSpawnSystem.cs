using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    // Client-only presentation. For each Soldier without a HealthBarRef,
    // instantiates the HealthBar prefab, parents it to the soldier, and
    // registers the link via LinkedEntityGroup so ghost despawn cascades.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct HealthBarSpawnSystem : ISystem
    {
        EntityQuery _needsBar;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();

            _needsBar = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Soldier, LocalTransform>()
                .WithNone<HealthBarRef>()
                .Build(ref state);
            state.RequireForUpdate(_needsBar);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            if (config.HealthBarPrefab == Entity.Null) return;

            var em = state.EntityManager;
            using var soldiers = _needsBar.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < soldiers.Length; i++)
            {
                var soldier = soldiers[i];

                var bar = em.Instantiate(config.HealthBarPrefab);

                em.AddComponentData(bar, new Parent { Value = soldier });
                em.AddComponentData(bar, new HealthBarLink { Owner = soldier });
                em.SetComponentData(bar, LocalTransform.FromPosition(
                    new float3(0f, config.HealthBarHeightOffset, 0f)));

                em.AddComponentData(soldier, new HealthBarRef { Bar = bar });

                // LinkedEntityGroup: element 0 must be the root entity for
                // DestroyEntity cascades.
                var group = em.AddBuffer<LinkedEntityGroup>(soldier);
                group.Add(new LinkedEntityGroup { Value = soldier });
                group.Add(new LinkedEntityGroup { Value = bar });
            }
        }
    }
}
