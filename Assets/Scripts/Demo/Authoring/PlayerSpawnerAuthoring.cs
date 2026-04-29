using Unity.Entities;
using UnityEngine;

namespace Demo
{

    public struct PlayerSpawner : IComponentData
    {
        public Entity Prefab;
    }


    // Place one of these in the EcsDemoSub subscene and drag the
    // PlayerCapsule prefab into the Prefab field. The baker bakes the
    // prefab into an Entity reference and creates the PlayerSpawner singleton.
    public class PlayerSpawnerAuthoring : MonoBehaviour
    {
        [Tooltip("PlayerCapsule prefab — must have a GhostAuthoringComponent.")]
        public GameObject Prefab;

        class Baker : Baker<PlayerSpawnerAuthoring>
        {
            public override void Bake(PlayerSpawnerAuthoring authoring)
            {
                // The authoring entity itself isn't important; we attach the
                // singleton component to it so the baker stays self-contained.
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerSpawner
                {
                    Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
