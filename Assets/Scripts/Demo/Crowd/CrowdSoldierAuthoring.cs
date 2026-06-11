using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Demo
{
    // Bakes a fully individual crowd soldier: a DYNAMIC upright-locked
    // frictionless capsule. The physics solver is the entire separation
    // model, so unlike BattleScene's vestigial kinematic collider this one
    // is load-bearing.
    [DisallowMultipleComponent]
    public class CrowdSoldierAuthoring : MonoBehaviour
    {
        [Tooltip("Collider radius — keep slightly under the visual radius so dense crowds read as touching, not gapped.")]
        public float Radius = 0.4f;
        public float Height = 1.8f;
        public float Mass   = 70f;
        [Tooltip("Light damping bleeds off solver-injected pushes between steering ticks.")]
        public float LinearDamping = 0.05f;

        class Baker : Baker<CrowdSoldierAuthoring>
        {
            public override void Bake(CrowdSoldierAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new CrowdSoldier { Team = 0, Goal = float3.zero });
                AddComponent(entity, new SoldierColor { Value = new float4(1f, 1f, 1f, 1f) });

                var geometry = new CapsuleGeometry
                {
                    Vertex0 = new float3(0f, a.Radius, 0f),
                    Vertex1 = new float3(0f, a.Height - a.Radius, 0f),
                    Radius  = a.Radius,
                };
                // Friction 0 / restitution 0: crowds slide past each other
                // instead of bouncing or sticking.
                var material = Unity.Physics.Material.Default;
                material.Friction    = 0f;
                material.Restitution = 0f;
                var collider = Unity.Physics.CapsuleCollider.Create(
                    geometry, CollisionFilter.Default, material);
                AddBlobAsset(ref collider, out _);
                AddComponent(entity, new PhysicsCollider { Value = collider });

                var mass = PhysicsMass.CreateDynamic(collider.Value.MassProperties, a.Mass);
                mass.InverseInertia = float3.zero; // upright lock — soldiers never tip
                AddComponent(entity, mass);

                AddComponent(entity, new PhysicsVelocity());
                AddComponent(entity, new PhysicsGravityFactor { Value = 0f }); // flat-plane sim
                AddComponent(entity, new PhysicsDamping { Linear = a.LinearDamping, Angular = 0f });
                AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }
        }
    }
}
