using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    public struct DamageEvent
    {
        public Entity Victim;
        public float  Amount;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SoldierSlotFollowSystem))]
    public partial struct MeleeDamageSystem : ISystem
    {
        EntityQuery _attackerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _attackerQuery = SystemAPI.QueryBuilder()
                .WithAll<Soldier, AttackStats, SquadMembership, LocalTransform>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            int chunkCount = _attackerQuery.CalculateChunkCount();
            if (chunkCount == 0) return;

            var stream = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            state.Dependency = new WriteDamageJob
            {
                MembershipHandle = SystemAPI.GetComponentTypeHandle<SquadMembership>(true),
                AttackHandle     = SystemAPI.GetComponentTypeHandle<AttackStats>(true),
                XformHandle      = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                SquadLookup      = SystemAPI.GetComponentLookup<Squad>(true),
                TargetLookup     = SystemAPI.GetComponentLookup<SquadTarget>(true),
                BufferLookup     = SystemAPI.GetBufferLookup<SquadMember>(true),
                XformLookup      = SystemAPI.GetComponentLookup<LocalTransform>(true),
                HealthLookup     = SystemAPI.GetComponentLookup<Health>(true),
                DamageWriter     = stream.AsWriter(),
                Dt               = dt,
            }.ScheduleParallel(_attackerQuery, state.Dependency);

            state.Dependency = new ReduceDamageJob
            {
                Reader       = stream.AsReader(),
                HealthLookup = SystemAPI.GetComponentLookup<Health>(false),
            }.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    struct WriteDamageJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<SquadMembership> MembershipHandle;
        [ReadOnly] public ComponentTypeHandle<AttackStats>     AttackHandle;
        [ReadOnly] public ComponentTypeHandle<LocalTransform>  XformHandle;
        [ReadOnly] public ComponentLookup<Squad>               SquadLookup;
        [ReadOnly] public ComponentLookup<SquadTarget>         TargetLookup;
        [ReadOnly] public BufferLookup<SquadMember>            BufferLookup;
        [ReadOnly] public ComponentLookup<LocalTransform>      XformLookup;
        [ReadOnly] public ComponentLookup<Health>              HealthLookup;
        public NativeStream.Writer DamageWriter;
        public float Dt;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DamageWriter.BeginForEachIndex(unfilteredChunkIndex);

            var memberships = chunk.GetNativeArray(ref MembershipHandle);
            var attacks     = chunk.GetNativeArray(ref AttackHandle);
            var xforms      = chunk.GetNativeArray(ref XformHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var m = memberships[i];
                if (m.Squad == Entity.Null) continue;
                if (!SquadLookup.HasComponent(m.Squad)) continue;

                var selfSquad = SquadLookup[m.Squad];
                if (m.SlotIndex < 0 || m.SlotIndex >= selfSquad.Cols) continue;

                var targetSquadEntity = TargetLookup[m.Squad].Value;
                if (targetSquadEntity == Entity.Null) continue;
                if (!BufferLookup.HasBuffer(targetSquadEntity)) continue;
                if (!SquadLookup.HasComponent(targetSquadEntity)) continue;

                var enemyBuf   = BufferLookup[targetSquadEntity];
                var enemySquad = SquadLookup[targetSquadEntity];

                // Pair by physical proximity, not column index. Compaction
                // left-packs survivors into the lowest slots, so columns no
                // longer line up and the buffer can be shorter than Cols; an
                // index-based pairing then misfires and combat stalls. Instead
                // scan the enemy front row (slots 0..Cols-1, the only ranks in
                // reach) and damage the single nearest live enemy within Range.
                float3 selfPos = xforms[i].Position;
                float  range   = attacks[i].Range;

                Entity nearest    = Entity.Null;
                float  nearestSq  = range * range;
                int    frontCount = math.min(enemySquad.Cols, enemyBuf.Length);
                for (int c = 0; c < frontCount; c++)
                {
                    Entity enemy = enemyBuf[c].Value;
                    if (enemy == Entity.Null) continue;
                    if (!HealthLookup.HasComponent(enemy)) continue;
                    if (HealthLookup[enemy].Current <= 0f) continue;
                    if (!XformLookup.HasComponent(enemy)) continue;

                    float d = math.distancesq(selfPos, XformLookup[enemy].Position);
                    if (d <= nearestSq)
                    {
                        nearestSq = d;
                        nearest   = enemy;
                    }
                }

                if (nearest != Entity.Null)
                {
                    DamageWriter.Write(new DamageEvent
                    {
                        Victim = nearest,
                        Amount = attacks[i].Dps * Dt,
                    });
                }
            }

            DamageWriter.EndForEachIndex();
        }
    }

    [BurstCompile]
    struct ReduceDamageJob : IJob
    {
        public NativeStream.Reader Reader;
        [NativeDisableParallelForRestriction] public ComponentLookup<Health> HealthLookup;

        public void Execute()
        {
            int foreachCount = Reader.ForEachCount;
            for (int i = 0; i < foreachCount; i++)
            {
                int eventCount = Reader.BeginForEachIndex(i);
                for (int j = 0; j < eventCount; j++)
                {
                    var ev = Reader.Read<DamageEvent>();
                    if (HealthLookup.HasComponent(ev.Victim))
                    {
                        var h = HealthLookup[ev.Victim];
                        h.Current -= ev.Amount;
                        HealthLookup[ev.Victim] = h;
                    }
                }
                Reader.EndForEachIndex();
            }
        }
    }
}
