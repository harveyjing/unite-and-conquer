using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

namespace Demo
{
    // Replicated to clients (used for HUD counts).
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct Soldier : IComponentData
    {
        // CollisionFilter layer bit shared by soldier colliders and by the
        // TargetingSystem broadphase query filter. Define here so the
        // bit is named in exactly one place.
        public const uint Layer = 1u << 1;
    }

    // Replicated as a single int per ghost. 0 = Red, 1 = Blue.
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct Team : IComponentData
    {
        [GhostField] public int Value;
    }

    // Server-only.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }

    // Server-only. Per-entity static after spawn.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct AttackStats : IComponentData
    {
        public float Range;
        public float Dps;
    }

    // Server-only. Refreshed by TargetingSystem every TargetRefreshIntervalTicks.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Target : IComponentData
    {
        public Entity Value;
    }

    // Authoring placed on the Soldier prefab GameObject (Task 4).
    // Adds all five components plus a query-only PhysicsCollider and a
    // URPMaterialPropertyBaseColor that BattleSpawnSystem overwrites per-team.
    //
    // PhysicsCollider is created programmatically (rather than via
    // PhysicsShapeAuthoring) to make BelongsTo / CollidesWith explicit in code.
    [DisallowMultipleComponent]
    public class SoldierAuthoring : MonoBehaviour
    {
        class Baker : Baker<SoldierAuthoring>
        {
            public override void Bake(SoldierAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new Target { Value = Entity.Null });
                AddComponent(entity, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });

                // Query-only sphere collider on layer 1.
                var filter = new CollisionFilter
                {
                    BelongsTo    = Soldier.Layer,
                    CollidesWith = 0u,
                    GroupIndex   = 0,
                };
                var collider = Unity.Physics.SphereCollider.Create(
                    new SphereGeometry
                    {
                        Center = float3.zero,
                        Radius = 0.3f,
                    },
                    filter);
                AddBlobAsset(ref collider, out _);
                AddComponent(entity, new PhysicsCollider { Value = collider });

                // PhysicsVelocity (zero) marks the body as dynamic-tree resident
                // so the broadphase is incrementally updated as soldiers move.
                AddComponent(entity, new PhysicsVelocity
                {
                    Linear  = float3.zero,
                    Angular = float3.zero,
                });

                // Kinematic mass means physics never integrates this body;
                // we only use it for distance queries.
                AddComponent(entity, PhysicsMass.CreateKinematic(MassProperties.UnitSphere));
            }
        }
    }
}
