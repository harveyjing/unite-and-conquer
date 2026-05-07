using Unity.Entities;
using UnityEngine;

namespace Demo
{
    public class CameraFollowMono : MonoBehaviour
    {
        [Tooltip("World-space offset from the player position to the camera.")]
        public Vector3 offset = new Vector3(-10f, 14f, -10f);

        EntityQuery _query;
        World _world;

        // Finds the first world whose name contains "client" (case-insensitive),
        // falling back to DefaultGameObjectInjectionWorld when no client world
        // exists (e.g. single-player / editor play-mode without NetCode).
        static World FindClientWorld()
        {
            foreach (var w in World.All)
                if (w.Name.IndexOf("client", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return World.DefaultGameObjectInjectionWorld;
        }

        void LateUpdate()
        {
            // Lazy-init: locate the client world and cache a query for the
            // CameraTargetData singleton written by CameraFollowSystem.
            if (_world == null || !_world.IsCreated)
            {
                _world = FindClientWorld();
                if (_world == null) return;

                _query = _world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<CameraTargetData>());
            }

            if (_query.IsEmpty) return;

            var target = _query.GetSingleton<CameraTargetData>();
            transform.position = new Vector3(
                target.Position.x + offset.x,
                target.Position.y + offset.y,
                target.Position.z + offset.z
            );
        }

        void OnDestroy()
        {
            if (_query != default && _world != null && _world.IsCreated)
                _query.Dispose();
        }
    }
}