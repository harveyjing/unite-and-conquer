using Unity.Entities;
using UnityEngine;

public class CapsuleDemoAuthoring : MonoBehaviour
{
    class Baker : Baker<CapsuleDemoAuthoring>
    {
        public override void Bake(CapsuleDemoAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RotateTag>(entity);
        }
    }
}
