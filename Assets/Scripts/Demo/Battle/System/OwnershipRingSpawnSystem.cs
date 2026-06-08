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

    // Client-only presentation. For each locally-owned soldier (its replicated
    // GhostOwner.NetworkId matches this client's NetworkId) without a ring,
    // instantiate the OwnershipRing prefab, parent it at the soldier's feet, and link
    // it for despawn. Runs AFTER HealthBarSpawnSystem and APPENDS to the
    // LinkedEntityGroup so it does not clobber the health-bar link (AddBuffer replaces).
    //
    // Ownership is detected by NetworkId comparison rather than the GhostOwnerIsLocal
    // enableable tag: Netcode does not add GhostOwnerIsLocal to these plain interpolated
    // soldier ghosts (no command/input setup), so that tag is never present at runtime.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(HealthBarSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct OwnershipRingSpawnSystem : ISystem
    {
        EntityQuery _needsRing;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<NetworkId>();

            _needsRing = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Soldier, LocalTransform, GhostOwner>()
                .WithNone<OwnershipRingRef>()
                .Build(ref state);
            state.RequireForUpdate(_needsRing);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            if (config.OwnershipRingPrefab == Entity.Null) return;
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var localNet)) return;
            int localId = localNet.Value;

            var em = state.EntityManager;
            using var soldiers = _needsRing.ToEntityArray(Allocator.Temp);
            using var owners   = _needsRing.ToComponentDataArray<GhostOwner>(Allocator.Temp);

            for (int i = 0; i < soldiers.Length; i++)
            {
                if (owners[i].NetworkId != localId) continue; // not ours
                var soldier = soldiers[i];

                var ring = em.Instantiate(config.OwnershipRingPrefab);
                em.AddComponentData(ring, new Parent { Value = soldier });
                em.SetComponentData(ring, LocalTransform.FromPosition(
                    new float3(0f, config.RingHeightOffset, 0f)));

                // Size the disc here rather than via the prefab's baked scale: changing
                // a subscene-referenced prefab does NOT reliably re-bake, so the baked
                // scale can't be trusted at runtime. This scale is relative to the
                // soldier (the ring is parented to it, and soldiers are baked ~0.3x), so
                // (1.5, 0.03, 1.5) renders as a snug disc a bit wider than the soldier.
                var ringShape = new PostTransformMatrix { Value = float4x4.Scale(1.5f, 0.03f, 1.5f) };
                if (em.HasComponent<PostTransformMatrix>(ring))
                    em.SetComponentData(ring, ringShape);
                else
                    em.AddComponentData(ring, ringShape);

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
