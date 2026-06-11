using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    // Server-only. One Squad entity per regiment per team.
    public struct Squad : IComponentData
    {
        public int   Team;     // 0 = red, 1 = blue
        public int   Rows;     // mutable — shrinks during compaction
        public int   Cols;     // fixed (line width stays constant)
        public float Spacing;
    }

    // Server-only. Set by SquadTargetingSystem.
    public struct SquadTarget : IComponentData
    {
        public Entity Value;   // enemy Squad entity, or Entity.Null
    }

    // Server-only buffer on each Squad entity, indexed by slot.
    // Stale references are tolerated until the next compaction.
    // Capacity 0: this buffer is always at least Rows*Cols (50+) elements,
    // so inline storage just wastes chunk space — keep it all on the heap.
    [InternalBufferCapacity(0)]
    public struct SquadMember : IBufferElementData
    {
        public Entity Value;   // soldier entity, or Entity.Null = empty slot
    }

    // Server-only on each soldier. Replaces the removed `Target` component.
    public struct SquadMembership : IComponentData
    {
        public Entity Squad;
        public int    SlotIndex;
    }

    // Server-only. The squad's terrain-navigation state machine.
    public enum NavState : byte { Pursue = 0, ApproachPortal = 1, Crossing = 2 }

    public struct SquadNav : IComponentData
    {
        public NavState State;
        public float3   Entrance;     // cached portal endpoint on our side
        public float3   Exit;         // cached portal endpoint on the far side
        public float    PortalWidth;  // cached, drives the narrow Cols
        public int      BaseCols;     // full-width Cols to restore after crossing
    }

    // Server-only. Written by SquadNavigationSystem, read by SquadMovementSystem.
    public struct SquadMoveGoal : IComponentData
    {
        public float3 Position;   // where the squad anchor should head this tick
        public byte   Engage;     // 1 = stop at EngagementDistance; 0 = walk fully there
    }
}
