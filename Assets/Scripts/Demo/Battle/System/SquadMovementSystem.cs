using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadNavigationSystem))]
    public partial struct SquadMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            float dt = SystemAPI.Time.DeltaTime;
            var squadLookup = SystemAPI.GetComponentLookup<Squad>(true);

            new SquadStepJob
            {
                AdvanceSpeed  = config.SquadAdvanceSpeed,
                RotationSpeed = config.SquadRotationSpeed,
                AttackRange   = config.AttackRange,
                ContactMargin = config.ContactMargin,
                Dt            = dt,
                SquadLookup   = squadLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SquadStepJob : IJobEntity
    {
        public float AdvanceSpeed;
        public float RotationSpeed;
        public float AttackRange;
        public float ContactMargin;
        public float Dt;

        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Squad> SquadLookup;

        public void Execute(in Squad self, in SquadMoveGoal goal,
                            in SquadTarget target, ref LocalTransform xform)
        {
            float3 toGoal = goal.Position - xform.Position;
            toGoal.y = 0f;
            float dist = math.length(toGoal);
            if (dist < 1e-4f) return;

            float3 desiredFwd = toGoal / dist;
            quaternion desiredRot = quaternion.LookRotationSafe(desiredFwd, math.up());
            float slerpT = math.saturate(RotationSpeed * Dt);
            xform.Rotation = math.slerp(xform.Rotation, desiredRot, slerpT);

            // Engagement stop applies only when chasing an enemy (Engage == 1).
            float stopDist = 0f;
            if (goal.Engage != 0
                && target.Value != Entity.Null
                && SquadLookup.HasComponent(target.Value))
            {
                int targetRows = SquadLookup[target.Value].Rows;
                stopDist = SquadGeometry.EngagementDistance(
                    self.Rows, targetRows, self.Spacing, AttackRange, ContactMargin);
            }

            if (dist <= stopDist) return;

            float3 fwd = math.mul(xform.Rotation, new float3(0, 0, 1));
            float step = AdvanceSpeed * Dt;
            float maxStep = dist - stopDist;
            step = math.min(step, maxStep);
            xform.Position += fwd * step;
        }
    }
}
