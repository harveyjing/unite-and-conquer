using Unity.Entities;
using UnityEngine;

public class CameraFollowMono : MonoBehaviour
{
    [Tooltip("World-space offset from the player position to the camera.")]
    public Vector3 offset = new Vector3(-10f, 14f, -10f);

    EntityQuery _query;
    bool _queryCreated;

    void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        _query = world.EntityManager.CreateEntityQuery(typeof(CameraTargetData));
        _queryCreated = true;
    }

    void LateUpdate()
    {
        if (!_queryCreated || _query.IsEmpty) return;
        var target = _query.GetSingleton<CameraTargetData>();
        transform.position = new Vector3(
            target.Position.x + offset.x,
            target.Position.y + offset.y,
            target.Position.z + offset.z
        );
    }

    void OnDestroy()
    {
        if (_queryCreated) _query.Dispose();
    }
}
