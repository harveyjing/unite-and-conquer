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
    [UpdateAfter(typeof(SquadTargetingSystem))]
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
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SquadStepJob
            {
                AdvanceSpeed  = config.SquadAdvanceSpeed,
                RotationSpeed = config.SquadRotationSpeed,
                AttackRange   = config.AttackRange,
                ContactMargin = config.ContactMargin,
                Dt            = dt,
                SquadLookup   = squadLookup,
                XformLookup   = xformLookup,
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

        [Unity.Collections.ReadOnly] public ComponentLookup<Squad> SquadLookup;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> XformLookup;

        public void Execute(in Squad self, in SquadTarget target, ref LocalTransform xform)
        {
            if (target.Value == Entity.Null) return;
            if (!XformLookup.HasComponent(target.Value)) return;
            if (!SquadLookup.HasComponent(target.Value)) return;

            float3 targetPos = XformLookup[target.Value].Position;
            float3 toTarget  = targetPos - xform.Position;
            toTarget.y = 0f;
            float dist = math.length(toTarget);
            if (dist < 1e-4f) return;

            float3 desiredFwd = toTarget / dist;
            quaternion desiredRot = quaternion.LookRotationSafe(desiredFwd, math.up());
            float slerpT = math.saturate(RotationSpeed * Dt);
            xform.Rotation = math.slerp(xform.Rotation, desiredRot, slerpT);

            int targetRows = SquadLookup[target.Value].Rows;
            float engageDist = SquadGeometry.EngagementDistance(
                self.Rows, targetRows, self.Spacing, AttackRange, ContactMargin);

            if (dist <= engageDist) return;

            float3 fwd = math.mul(xform.Rotation, new float3(0, 0, 1));
            float step = AdvanceSpeed * Dt;
            float maxStep = dist - engageDist;
            step = math.min(step, maxStep);
            xform.Position += fwd * step;
        }
    }
}
