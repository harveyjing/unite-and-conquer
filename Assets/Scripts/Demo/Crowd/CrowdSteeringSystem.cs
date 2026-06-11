using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Demo
{
    // Writes each soldier's desired PhysicsVelocity from the stateless
    // routing decision. Runs before the physics step: the solver then
    // resolves all soldier-vs-soldier and soldier-vs-bank contacts, which is
    // the entire separation model — there is deliberately no avoidance code
    // here. Terrain/portal entities are few and hand-authored; gathering
    // them per tick is cheap (same pattern as SquadNavigationSystem).
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(CrowdSpawnSystem))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial struct CrowdSteeringSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<CrowdConfig>();

            var regionQuery = SystemAPI.QueryBuilder().WithAll<TerrainRegion>().Build();
            var portalQuery = SystemAPI.QueryBuilder().WithAll<CrossingPortal>().Build();
            var regions = regionQuery.ToComponentDataArray<TerrainRegion>(state.WorldUpdateAllocator);
            var portals = portalQuery.ToComponentDataArray<CrossingPortal>(state.WorldUpdateAllocator);

            state.Dependency = new SteerJob
            {
                Regions         = regions,
                Portals         = portals,
                MoveSpeed       = config.MoveSpeed,
                ArrivalRadiusSq = config.ArrivalRadius * config.ArrivalRadius,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct SteerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<TerrainRegion>  Regions;
            [ReadOnly] public NativeArray<CrossingPortal> Portals;
            public float MoveSpeed;
            public float ArrivalRadiusSq;

            void Execute(in CrowdSoldier soldier, in LocalTransform transform,
                         ref PhysicsVelocity velocity)
            {
                float3 pos    = transform.Position;
                float3 toGoal = soldier.Goal - pos;
                toGoal.y = 0f;
                if (math.lengthsq(toGoal) <= ArrivalRadiusSq)
                {
                    velocity.Linear  = float3.zero;
                    velocity.Angular = float3.zero;
                    return;
                }

                float3 waypoint = CrowdSteering.PickWaypoint(pos, soldier.Goal, Regions, Portals);
                float3 dir = waypoint - pos;
                dir.y = 0f;
                velocity.Linear  = math.normalizesafe(dir) * MoveSpeed;
                velocity.Angular = float3.zero;
            }
        }
    }
}
