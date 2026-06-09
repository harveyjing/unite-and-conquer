using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadTargetingSystem))]
    [UpdateBefore(typeof(SquadMovementSystem))]
    public partial struct SquadNavigationSystem : ISystem
    {
        // Distance at which the squad anchor is considered "arrived" at a waypoint.
        // Invariant: must exceed one advance step (SquadAdvanceSpeed * maxDt) so a squad
        // can't sail past a waypoint in a single tick without triggering its transition.
        const float ArriveThreshold = 1.0f;

        EntityQuery _squadQuery;
        EntityQuery _regionQuery;
        EntityQuery _portalQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _squadQuery = SystemAPI.QueryBuilder()
                .WithAll<Squad, SquadNav, SquadMoveGoal, SquadTarget, LocalTransform, SquadMember>()
                .Build();
            state.RequireForUpdate(_squadQuery);
            _regionQuery = SystemAPI.QueryBuilder().WithAll<TerrainRegion>().Build();
            _portalQuery = SystemAPI.QueryBuilder().WithAll<CrossingPortal>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var regions = _regionQuery.ToComponentDataArray<TerrainRegion>(Allocator.TempJob);
            var portals = _portalQuery.ToComponentDataArray<CrossingPortal>(Allocator.TempJob);

            state.Dependency = new SquadNavJob
            {
                Regions         = regions,
                Portals         = portals,
                XformLookup     = SystemAPI.GetComponentLookup<LocalTransform>(true),
                HealthLookup    = SystemAPI.GetComponentLookup<Health>(true),
                ArriveThreshold = ArriveThreshold,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = regions.Dispose(state.Dependency);
            state.Dependency = portals.Dispose(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct SquadNavJob : IJobEntity
    {
        [Unity.Collections.ReadOnly] public NativeArray<TerrainRegion>  Regions;
        [Unity.Collections.ReadOnly] public NativeArray<CrossingPortal> Portals;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> XformLookup;
        [Unity.Collections.ReadOnly] public ComponentLookup<Health> HealthLookup;
        public float ArriveThreshold;

        public void Execute(
            ref Squad squad,
            ref SquadNav nav,
            ref SquadMoveGoal goal,
            in SquadTarget target,
            in LocalTransform xform,
            in DynamicBuffer<SquadMember> members)
        {
            float3 pos = xform.Position;

            switch (nav.State)
            {
                case NavState.ApproachPortal:
                {
                    goal.Position = nav.Entrance;
                    goal.Engage   = 0;
                    if (math.distance(pos, nav.Entrance) <= ArriveThreshold)
                    {
                        int alive = CountAlive(members);
                        int narrowCols = SquadGeometry.NarrowColsForWidth(nav.PortalWidth, squad.Spacing);
                        squad.Cols = narrowCols;
                        squad.Rows = SquadGeometry.RowsForAliveCount(alive, narrowCols);
                        nav.State  = NavState.Crossing;
                        goal.Position = nav.Exit;
                    }
                    return;
                }
                case NavState.Crossing:
                {
                    goal.Position = nav.Exit;
                    goal.Engage   = 0;
                    if (math.distance(pos, nav.Exit) <= ArriveThreshold)
                    {
                        int alive = CountAlive(members);
                        squad.Cols = nav.BaseCols;
                        squad.Rows = SquadGeometry.RowsForAliveCount(alive, nav.BaseCols);
                        nav.State  = NavState.Pursue;
                    }
                    return;
                }
                default: // Pursue
                {
                    if (target.Value == Entity.Null || !XformLookup.HasComponent(target.Value))
                    {
                        goal.Position = pos;
                        goal.Engage   = 0;
                        return;
                    }
                    float3 targetPos = XformLookup[target.Value].Position;
                    if (PathBlocked(pos, targetPos)
                        && TryPickPortal(pos, out float3 entrance, out float3 exit, out float width))
                    {
                        nav.State       = NavState.ApproachPortal;
                        nav.Entrance    = entrance;
                        nav.Exit        = exit;
                        nav.PortalWidth = width;
                        nav.BaseCols    = squad.Cols;
                        goal.Position   = entrance;
                        goal.Engage     = 0;
                    }
                    else
                    {
                        goal.Position = targetPos;
                        goal.Engage   = 1;
                    }
                    return;
                }
            }
        }

        int CountAlive(in DynamicBuffer<SquadMember> members)
        {
            int n = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var e = members[i].Value;
                if (e == Entity.Null) continue;
                // Single hash-map probe; skips destroyed (no Health) and dead soldiers.
                if (HealthLookup.TryGetComponent(e, out var h) && h.Current > 0f)
                    n++;
            }
            return n;
        }

        bool PathBlocked(float3 from, float3 to)
        {
            for (int i = 0; i < Regions.Length; i++)
            {
                var r = Regions[i];
                if (r.Passable != 0) continue;
                if (SquadGeometry.SegmentIntersectsBox(from, to, r.Center, r.HalfExtents, r.Yaw))
                    return true;
            }
            return false;
        }

        bool TryPickPortal(float3 pos, out float3 entrance, out float3 exit, out float width)
        {
            // Nearest portal by Euclidean distance to either endpoint; direction and
            // navigability are not considered, so authoring must place only valid
            // crossings (see the spec's portal-not-linked-to-region note).
            entrance = default; exit = default; width = 0f;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Portals.Length; i++)
            {
                var p = Portals[i];
                float dA = math.distance(pos, p.Entrance);
                float dB = math.distance(pos, p.Exit);
                float near = math.min(dA, dB);
                if (near < best)
                {
                    best = near;
                    found = true;
                    if (dA <= dB) { entrance = p.Entrance; exit = p.Exit; }
                    else          { entrance = p.Exit;     exit = p.Entrance; }
                    width = p.Width;
                }
            }
            return found;
        }
    }
}
