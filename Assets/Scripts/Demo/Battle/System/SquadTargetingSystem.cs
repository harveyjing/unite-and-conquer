using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SquadTargetingSystem : ISystem
    {
        EntityQuery _squadQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<NetworkTime>();
            _squadQuery = SystemAPI.QueryBuilder()
                .WithAll<Squad, SquadTarget, LocalTransform>()
                .Build();
            state.RequireForUpdate(_squadQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var tick   = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            if ((tick % (uint)config.TargetRefreshIntervalTicks) != 0u) return;

            int squadCount = _squadQuery.CalculateEntityCount();
            var snapshot = new NativeArray<SquadSnapshot>(squadCount, Allocator.TempJob);

            state.Dependency = new SnapshotJob
            {
                Snapshot = snapshot,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = new AssignTargetJob
            {
                Snapshot = snapshot,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = snapshot.Dispose(state.Dependency);
        }
    }

    public struct SquadSnapshot
    {
        public Entity Entity;
        public int    Team;
        public float3 Position;
    }

    [BurstCompile]
    public partial struct SnapshotJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<SquadSnapshot> Snapshot;

        public void Execute([Unity.Entities.EntityIndexInQuery] int index,
                            Entity entity,
                            in Squad squad,
                            in LocalTransform xform)
        {
            Snapshot[index] = new SquadSnapshot
            {
                Entity   = entity,
                Team     = squad.Team,
                Position = xform.Position,
            };
        }
    }

    [BurstCompile]
    public partial struct AssignTargetJob : IJobEntity
    {
        [Unity.Collections.ReadOnly] public NativeArray<SquadSnapshot> Snapshot;

        public void Execute(in Squad squad,
                            in LocalTransform xform,
                            ref SquadTarget target)
        {
            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;
            float3 self = xform.Position;

            for (int i = 0; i < Snapshot.Length; i++)
            {
                var s = Snapshot[i];
                if (s.Team == squad.Team) continue;
                float d = math.distancesq(self, s.Position);
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestEntity = s.Entity;
                }
            }

            target.Value = bestEntity;
        }
    }
}
