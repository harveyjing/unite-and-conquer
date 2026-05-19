using Unity.Entities;

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
}
