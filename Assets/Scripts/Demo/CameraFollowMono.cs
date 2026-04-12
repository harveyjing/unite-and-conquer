using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class CameraFollowMono : MonoBehaviour
{
    [Tooltip("World-space offset from the player position to the camera.")]
    public Vector3 offset = new Vector3(-10f, 14f, -10f);

    EntityQuery _query;
    bool _queryCreated;

    void LateUpdate()
    {
        // Lazy-init: search all worlds for one that has the player entity.
        // Needed because NetCode creates multiple worlds (Client, Server) and
        // the subscene may not be loaded on the first frame.
        if (!_queryCreated)
        {
            foreach (var world in World.All)
            {
                var q = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
                if (!q.IsEmpty)
                {
                    _query = q;
                    _queryCreated = true;
                    break;
                }
                q.Dispose();
            }
            if (!_queryCreated) return;
        }

        if (_query.IsEmpty) return;
        var playerTransform = _query.GetSingleton<LocalTransform>();
        transform.position = new Vector3(
            playerTransform.Position.x + offset.x,
            playerTransform.Position.y + offset.y,
            playerTransform.Position.z + offset.z
        );
    }

    void OnDestroy()
    {
        if (_queryCreated) _query.Dispose();
    }
}
