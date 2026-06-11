using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Goes on the CrowdSoldier prefab's child Visual (the MeshRenderer
    // holder). SoldierColor must live on the entity that carries
    // MaterialMeshInfo for the _BaseColor override to bind; the root's copy
    // is inert. CrowdSpawnSystem writes the team color to every
    // LinkedEntityGroup member that has SoldierColor.
    [DisallowMultipleComponent]
    public class CrowdSoldierVisualAuthoring : MonoBehaviour
    {
        class Baker : Baker<CrowdSoldierVisualAuthoring>
        {
            public override void Bake(CrowdSoldierVisualAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new SoldierColor { Value = new float4(1f, 1f, 1f, 1f) });
            }
        }
    }
}
