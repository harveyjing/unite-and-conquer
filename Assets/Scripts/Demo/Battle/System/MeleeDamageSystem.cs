using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    // Scatter-write per-attacker; gather-apply on victim's Health.
    public struct DamageEvent
    {
        public Entity Victim;
        public float  Amount;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SoldierMovementSystem))]
    public partial struct MeleeDamageSystem : ISystem
    {
        EntityQuery _attackerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _attackerQuery = SystemAPI.QueryBuilder()
                .WithAll<Soldier, AttackStats, Target, LocalTransform>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            int chunkCount = _attackerQuery.CalculateChunkCount();
            if (chunkCount == 0) return;

            var stream = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            state.Dependency = new WriteDamageJob
            {
                TargetHandle = SystemAPI.GetComponentTypeHandle<Target>(true),
                AttackHandle = SystemAPI.GetComponentTypeHandle<AttackStats>(true),
                XformHandle  = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                XformLookup  = xformLookup,
                DamageWriter = stream.AsWriter(),
                Dt           = dt,
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
        [ReadOnly] public ComponentTypeHandle<Target> TargetHandle;
        [ReadOnly] public ComponentTypeHandle<AttackStats> AttackHandle;
        [ReadOnly] public ComponentTypeHandle<LocalTransform> XformHandle;
        [ReadOnly] public ComponentLookup<LocalTransform> XformLookup;
        public NativeStream.Writer DamageWriter;
        public float Dt;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DamageWriter.BeginForEachIndex(unfilteredChunkIndex);

            var targets = chunk.GetNativeArray(ref TargetHandle);
            var attacks = chunk.GetNativeArray(ref AttackHandle);
            var xforms  = chunk.GetNativeArray(ref XformHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var t = targets[i].Value;
                if (t == Entity.Null) continue;
                if (!XformLookup.HasComponent(t)) continue;

                float distSq = math.distancesq(xforms[i].Position, XformLookup[t].Position);
                float range  = attacks[i].Range;
                if (distSq <= range * range)
                {
                    DamageWriter.Write(new DamageEvent
                    {
                        Victim = t,
                        Amount = attacks[i].Dps * Dt,
                    });
                }
            }

            DamageWriter.EndForEachIndex();
        }
    }

    // Single-threaded reduce: read every event, decrement victim Health.
    // [NativeDisableParallelForRestriction] tells the safety system that
    // we will scatter-write to ComponentLookup<Health> from one thread;
    // since the job is IJob (not parallel), this is safe.
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
