using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    // On an owned soldier (client-only): reference to its ground-ring entity.
    public struct OwnershipRingRef : IComponentData
    {
        public Entity Ring;
    }

    // Client-only presentation. For each locally-owned soldier (GhostOwnerIsLocal)
    // without a ring, instantiate the OwnershipRing prefab, parent it at the soldier's
    // feet, and link it for despawn. Runs AFTER HealthBarSpawnSystem and APPENDS to the
    // LinkedEntityGroup so it does not clobber the health-bar link (AddBuffer replaces).
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(HealthBarSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct OwnershipRingSpawnSystem : ISystem
    {
        EntityQuery _needsRing;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();

            _needsRing = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Soldier, LocalTransform, GhostOwnerIsLocal>()
                .WithNone<OwnershipRingRef>()
                .Build(ref state);
            state.RequireForUpdate(_needsRing);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            if (config.OwnershipRingPrefab == Entity.Null) return;

            var em = state.EntityManager;
            using var soldiers = _needsRing.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < soldiers.Length; i++)
            {
                var soldier = soldiers[i];

                var ring = em.Instantiate(config.OwnershipRingPrefab);
                em.AddComponentData(ring, new Parent { Value = soldier });
                em.SetComponentData(ring, LocalTransform.FromPosition(
                    new float3(0f, config.RingHeightOffset, 0f)));

                em.AddComponentData(soldier, new OwnershipRingRef { Ring = ring });

                // Append to the existing LinkedEntityGroup (HealthBarSpawnSystem may
                // have created it). AddBuffer would replace and drop the bar link.
                DynamicBuffer<LinkedEntityGroup> group;
                if (em.HasBuffer<LinkedEntityGroup>(soldier))
                {
                    group = em.GetBuffer<LinkedEntityGroup>(soldier);
                }
                else
                {
                    group = em.AddBuffer<LinkedEntityGroup>(soldier);
                    group.Add(new LinkedEntityGroup { Value = soldier });
                }
                group.Add(new LinkedEntityGroup { Value = ring });
            }
        }
    }
}
