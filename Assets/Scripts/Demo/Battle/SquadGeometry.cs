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
    }
}
