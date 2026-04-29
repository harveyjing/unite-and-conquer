using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

public struct PlayerTag : IComponentData { }

public struct PlayerInput : IInputComponentData
{
    public int Horizontal;
    public int Vertical;
}

[DisallowMultipleComponent]
public class PlayerInputAuthoring : MonoBehaviour
{
    class Baker : Baker<PlayerInputAuthoring>
    {
        public override void Bake(PlayerInputAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
            AddComponent<PlayerInput>(entity);
        }
    }
}