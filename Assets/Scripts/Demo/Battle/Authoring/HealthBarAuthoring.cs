using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace Demo
{
    // On a soldier (client-only): reference to that soldier's bar entity.
    public struct HealthBarRef : IComponentData
    {
        public Entity Bar;
    }

    // On a bar entity (client-only): back-pointer to its owning soldier.
    // Not used in v1's hot path (HealthBarUpdateSystem indexes from the soldier
    // side), but kept for debug introspection and future systems.
    public struct HealthBarLink : IComponentData
    {
        public Entity Owner;
    }

    // Entities.Graphics material-property override. Drives the _Health01
    // shader uniform; value should be in [0, 1].
    [MaterialProperty("_Health01")]
    public struct HealthBarFill : IComponentData
    {
        public float Value;
    }

    // Authoring MonoBehaviour for the HealthBar prefab. Its baker attaches
    // a HealthBarFill component initialized to full so the shader's
    // _Health01 starts at 1.0 before HealthBarUpdateSystem first ticks.
    [DisallowMultipleComponent]
    public class HealthBarAuthoring : MonoBehaviour
    {
        class Baker : Baker<HealthBarAuthoring>
        {
            public override void Bake(HealthBarAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new HealthBarFill { Value = 1f });
            }
        }
    }
}
