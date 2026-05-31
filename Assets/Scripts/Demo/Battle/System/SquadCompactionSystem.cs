using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathSystem))]
    public partial struct SquadCompactionSystem : ISystem
    {
        // Monotonic per-update counter used to stagger and throttle compaction.
        // Deliberately NOT NetworkTime.ServerTick: the server-observed tick is
        // parity-constrained, and `(tick + squadIndex) % interval` then starves
        // half the squads of compaction forever (their dead front rows linger,
        // freezing the battle). A counter that advances by exactly 1 per update
        // visits every residue, so every squad compacts every `interval` updates.
        uint _phase;
        EntityQuery _squadQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            // Cache the query in OnCreate so the system registers Squad (RW) +
            // SquadMember access up front. That registration is what makes the
            // automatic `state.Dependency` include prior Squad readers — notably
            // SquadStepJob in SquadMovementSystem (reads Squad via ComponentLookup)
            // — so CompactJob, which writes Squad, correctly waits on it.
            _squadQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<Squad>(),
                ComponentType.ReadWrite<SquadMember>());
            state.RequireForUpdate(_squadQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            int interval = config.CompactionIntervalTicks;
            if (interval <= 0) return;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            // Thread the incoming dependency through the job (waits on prior
            // Squad readers), then complete before the structural ECB playback.
            state.Dependency = new CompactJob
            {
                Phase            = _phase,
                Interval         = (uint)interval,
                MembershipLookup = SystemAPI.GetComponentLookup<SquadMembership>(false),
                HealthLookup     = SystemAPI.GetComponentLookup<Health>(true),
                Ecb              = ecb,
            }.Schedule(_squadQuery, state.Dependency);

            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            _phase++;
        }
    }

    [BurstCompile]
    public partial struct CompactJob : IJobEntity
    {
        public uint Phase;
        public uint Interval;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SquadMembership>      MembershipLookup;
        [Unity.Collections.ReadOnly] public ComponentLookup<Health> HealthLookup;
        public EntityCommandBuffer                   Ecb;

        public void Execute(Entity squadEntity,
                            ref Squad squad,
                            ref DynamicBuffer<SquadMember> buf)
        {
            uint squadHash = (uint)squadEntity.Index;
            if (((Phase + squadHash) % Interval) != 0u) return;

            int original = buf.Length;
            var alive = new NativeList<Entity>(original, Allocator.Temp);
            for (int i = 0; i < original; i++)
            {
                var e = buf[i].Value;
                if (e == Entity.Null) continue;
                if (!HealthLookup.HasComponent(e)) continue;
                if (HealthLookup[e].Current <= 0f) continue;
                alive.Add(e);
            }

            int aliveCount = alive.Length;
            if (aliveCount == 0)
            {
                buf.Clear();
                Ecb.DestroyEntity(squadEntity);
                alive.Dispose();
                return;
            }

            int newRows = SquadGeometry.RowsForAliveCount(aliveCount, squad.Cols);

            buf.ResizeUninitialized(aliveCount);
            for (int i = 0; i < aliveCount; i++)
            {
                var e = alive[i];
                buf[i] = new SquadMember { Value = e };
                if (MembershipLookup.HasComponent(e))
                {
                    var m = MembershipLookup[e];
                    m.SlotIndex = i;
                    MembershipLookup[e] = m;
                }
            }
            squad.Rows = newRows;

            alive.Dispose();
        }
    }
}
