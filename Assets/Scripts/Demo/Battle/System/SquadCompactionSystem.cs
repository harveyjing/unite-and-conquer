using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathSystem))]
    public partial struct SquadCompactionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<NetworkTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            uint tick  = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            int interval = config.CompactionIntervalTicks;
            if (interval <= 0) return;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            new CompactJob
            {
                Tick             = tick,
                Interval         = (uint)interval,
                MembershipLookup = SystemAPI.GetComponentLookup<SquadMembership>(false),
                HealthLookup     = SystemAPI.GetComponentLookup<Health>(true),
                Ecb              = ecb,
            }.Run();

            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    public partial struct CompactJob : IJobEntity
    {
        public uint Tick;
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
            if (((Tick + squadHash) % Interval) != 0u) return;

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
