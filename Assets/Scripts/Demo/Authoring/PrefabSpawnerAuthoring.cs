using Unity.Entities;
using UnityEngine;

namespace Demo
{
    public struct PrefabSpawner : IComponentData
    {
        public Entity PlayerPrefab;
        public Entity ObstaclePrefab;
    }

    public class PrefabSpawnerAuthoring : MonoBehaviour
    {
        [Tooltip("PlayerCapsule prefab — must have a GhostAuthoringComponent.")]
        public GameObject PlayerPrefab;

        [Tooltip("Obstacle prefab — must have a GhostAuthoringComponent.")]
        public GameObject ObstaclePrefab;

        class Baker : Baker<PrefabSpawnerAuthoring>
        {
            public override void Bake(PrefabSpawnerAuthoring authoring)
            {
                // The authoring entity itself isn't important; we attach the
                // singleton component to it so the baker stays self-contained.
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(
                    entity,
                    new PrefabSpawner
                    {
                        PlayerPrefab = GetEntity(
                            authoring.PlayerPrefab,
                            TransformUsageFlags.Dynamic
                        ),
                        ObstaclePrefab = GetEntity(
                            authoring.ObstaclePrefab,
                            TransformUsageFlags.Renderable
                        ),
                    }
                );
            }
        }
    }
}
