using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    public enum TerrainKind : byte { River = 0, Hills = 1, Mud = 2, HighGround = 3 }

    // Generic authored terrain region. v1 only authors impassable regions
    // (Passable = 0); MoveMultiplier is reserved for Slow terrain and the
    // combat modifier for High ground is intentionally not a field yet.
    public struct TerrainRegion : IComponentData
    {
        public float3      Center;        // world XZ center (Y ignored for nav)
        public float2      HalfExtents;   // box half-size on XZ (x, z)
        public float       Yaw;           // radians about Y
        public byte        Passable;      // 0 = impassable (v1), 1 = passable
        public float       MoveMultiplier;// reserved (Slow terrain); v1 = 1
        public TerrainKind Kind;
    }

    // A crossing through an impassable region. Entrance/Exit are symmetric —
    // a squad uses whichever endpoint is on its side as the entrance.
    public struct CrossingPortal : IComponentData
    {
        public float3 Entrance;
        public float3 Exit;
        public float  Width;     // usable corridor width (metres)
    }
}
