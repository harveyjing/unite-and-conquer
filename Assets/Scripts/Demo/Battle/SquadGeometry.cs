using Unity.Burst;
using Unity.Mathematics;

namespace Demo
{
    // Pure math used by BattleSpawnSystem, SoldierSlotFollowSystem,
    // SquadMovementSystem, and SquadCompactionSystem. All static, all
    // Burst-compatible, no allocations, no entity access.
    [BurstCompile]
    public static class SquadGeometry
    {
        // Offset of a slot in the squad's local frame.
        // Row 0 is the front rank, facing +Z. Cols centered on X=0.
        public static float3 SlotLocalOffset(int slot, int rows, int cols, float spacing)
        {
            int col = slot % cols;
            int row = slot / cols;
            float localX = (col - (cols - 1) * 0.5f) * spacing;
            float localZ = ((rows - 1) * 0.5f - row) * spacing;
            return new float3(localX, 0f, localZ);
        }

        // Anchor-to-anchor distance at which two facing squads' front ranks
        // are within `attackRange` of each other. `contactMargin` is subtracted
        // so the ranks settle just inside reach (gap = attackRange - margin),
        // not just beyond it.
        public static float EngagementDistance(
            int selfRows, int targetRows, float spacing,
            float attackRange, float contactMargin)
        {
            return (selfRows   - 1) * 0.5f * spacing
                 + (targetRows - 1) * 0.5f * spacing
                 + attackRange
                 - contactMargin;
        }

        // Row count required to hold `aliveCount` soldiers in `cols`-wide rows.
        public static int RowsForAliveCount(int aliveCount, int cols)
        {
            if (aliveCount <= 0) return 0;
            if (cols <= 0) return 0;
            return (aliveCount + cols - 1) / cols;
        }

        // True if the XZ segment p0->p1 intersects the oriented box (center,
        // halfExtents, yaw radians about Y). Y is ignored — terrain regions are
        // vertical prisms. Transforms the segment into the box's local frame and
        // runs a 2D slab test. Used by SquadNavigationSystem to decide whether a
        // squad's straight path to its target is blocked.
        public static bool SegmentIntersectsBox(
            float3 p0, float3 p1, float3 center, float2 halfExtents, float yaw)
        {
            // Undo yaw: rotate world deltas by -yaw about Y into the box-local frame
            // where the box is axis-aligned.
            float c = math.cos(-yaw);
            float s = math.sin(-yaw);
            float2 a = WorldToLocalXZ(p0, center, c, s);
            float2 b = WorldToLocalXZ(p1, center, c, s);
            float2 d = b - a;

            float tmin = 0f;
            float tmax = 1f;

            for (int axis = 0; axis < 2; axis++)
            {
                float origin = axis == 0 ? a.x : a.y;
                float dir    = axis == 0 ? d.x : d.y;
                float half   = axis == 0 ? halfExtents.x : halfExtents.y;

                if (math.abs(dir) < 1e-8f)
                {
                    // Segment parallel to this slab: reject if it lies outside.
                    if (origin < -half || origin > half) return false;
                }
                else
                {
                    float inv = 1f / dir;
                    float t1 = (-half - origin) * inv;
                    float t2 = ( half - origin) * inv;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tmin = math.max(tmin, t1);
                    tmax = math.min(tmax, t2);
                    if (tmin > tmax) return false;
                }
            }
            return true;
        }

        // Rotate a world point's XZ offset from `center` by an already-computed
        // (cos,sin) into the box-local frame. Returns (localX, localZ) as a float2.
        static float2 WorldToLocalXZ(float3 p, float3 center, float cosA, float sinA)
        {
            float dx = p.x - center.x;
            float dz = p.z - center.z;
            return new float2(dx * cosA - dz * sinA,
                              dx * sinA + dz * cosA);
        }
    }
}
