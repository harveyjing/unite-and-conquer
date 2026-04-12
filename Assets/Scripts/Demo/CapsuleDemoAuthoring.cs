using Unity.Entities;
using UnityEngine;

public class CapsuleDemoAuthoring : MonoBehaviour
{
    [Tooltip("Units per second at full input.")]
    public float moveSpeed = 5f;

    [Tooltip("Velocity ramp rate. Higher = snappier response.")]
    public float acceleration = 10f;

    class Baker : Baker<CapsuleDemoAuthoring>
    {
        public override void Bake(CapsuleDemoAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
            AddComponent(entity, new PlayerMovementData
            {
                MoveSpeed    = authoring.moveSpeed,
                Acceleration = authoring.acceleration,
                Velocity     = default
            });
        }
    }
}
