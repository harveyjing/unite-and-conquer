using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TargetingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var tick   = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            if ((tick % (uint)config.TargetRefreshIntervalTicks) != 0u) return;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var teamLookup   = SystemAPI.GetComponentLookup<Team>(true);

            var queryFilter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = Soldier.Layer,
                GroupIndex   = 0,
            };

            new RefreshTargetJob
            {
                CollisionWorld = physicsWorld.CollisionWorld,
                TeamLookup     = teamLookup,
                Filter         = queryFilter,
                SearchRadius   = config.SearchRadius,
            }.ScheduleParallel();
        }
    }

    // For each Soldier, query the broadphase for the nearest enemy
    // (different Team) within SearchRadius. Write the result into Target.
    [BurstCompile]
    public partial struct RefreshTargetJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        public CollisionFilter Filter;
        public float SearchRadius;

        public void Execute(Entity entity, in LocalTransform xform, in Team team, ref Target target)
        {
            var collector = new NearestEnemyCollector
            {
                MaxFraction = SearchRadius,
                SelfTeam    = team.Value,
                SelfEntity  = entity,
                TeamLookup  = TeamLookup,
                Closest     = default,
                NumHits     = 0,
            };
            var input = new PointDistanceInput
            {
                Position    = xform.Position,
                MaxDistance = SearchRadius,
                Filter      = Filter,
            };
            CollisionWorld.CalculateDistance(input, ref collector);
            target.Value = collector.NumHits > 0 ? collector.Closest.Entity : Entity.Null;
        }
    }

    // Custom collector that keeps the single closest hit whose Team
    // differs from SelfTeam. Mutates MaxFraction so the broadphase
    // shrinks the search as closer candidates are found.
    public struct NearestEnemyCollector : ICollector<DistanceHit>
    {
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; set; }
        public int NumHits { get; set; }

        public DistanceHit Closest;
        public int SelfTeam;
        public Entity SelfEntity;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        public bool AddHit(DistanceHit hit)
        {
            if (hit.Entity == SelfEntity) return false;
            if (!TeamLookup.HasComponent(hit.Entity)) return false;
            if (TeamLookup[hit.Entity].Value == SelfTeam) return false;

            if (NumHits == 0 || hit.Fraction < Closest.Fraction)
            {
                Closest = hit;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
            return false;
        }
    }
}
