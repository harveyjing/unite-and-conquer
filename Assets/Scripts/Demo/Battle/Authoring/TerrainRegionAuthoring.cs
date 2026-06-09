using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Authors a TerrainRegion from this GameObject's transform (position = center,
    // Y-rotation = yaw) plus inspector half-extents. Place long-and-thin for
    // rivers/passes. v1 uses impassable regions only.
    public class TerrainRegionAuthoring : MonoBehaviour
    {
        public Vector2     HalfExtents    = new Vector2(1f, 5f); // (x, z)
        public bool        Passable       = false;
        public float       MoveMultiplier = 1f;                  // reserved (Slow terrain)
        public TerrainKind Kind           = TerrainKind.River;

        class Baker : Baker<TerrainRegionAuthoring>
        {
            public override void Bake(TerrainRegionAuthoring a)
            {
                var t = a.transform;
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new TerrainRegion
                {
                    Center         = t.position,
                    HalfExtents    = new float2(a.HalfExtents.x, a.HalfExtents.y),
                    Yaw            = math.radians(t.rotation.eulerAngles.y),
                    Passable       = (byte)(a.Passable ? 1 : 0),
                    MoveMultiplier = a.MoveMultiplier,
                    Kind           = a.Kind,
                });
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Passable ? new Color(0.4f, 0.8f, 0.4f, 0.4f)
                                    : new Color(0.2f, 0.5f, 0.9f, 0.4f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(HalfExtents.x * 2f, 0.2f, HalfExtents.y * 2f));
        }
    }
}
