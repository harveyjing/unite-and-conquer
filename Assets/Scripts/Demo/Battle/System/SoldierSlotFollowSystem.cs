using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadMovementSystem))]
    public partial struct SoldierSlotFollowSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config      = SystemAPI.GetSingleton<BattleConfig>();
            float dt        = SystemAPI.Time.DeltaTime;
            var squadLookup = SystemAPI.GetComponentLookup<Squad>(true);
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SlotFollowJob
            {
                StepSpeed   = config.SoldierStepSpeed,
                Dt          = dt,
                SquadLookup = squadLookup,
                XformLookup = xformLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SlotFollowJob : IJobEntity
    {
        public float StepSpeed;
        public float Dt;

        [Unity.Collections.ReadOnly] public ComponentLookup<Squad> SquadLookup;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform>                     XformLookup;

        public void Execute(ref LocalTransform xform, in SquadMembership membership)
        {
            if (membership.Squad == Entity.Null) return;
            if (!SquadLookup.HasComponent(membership.Squad)) return;

            var squad   = SquadLookup[membership.Squad];
            var anchor  = XformLookup[membership.Squad];
            var local   = SquadGeometry.SlotLocalOffset(membership.SlotIndex, squad.Rows, squad.Cols, squad.Spacing);
            float3 target = anchor.Position + math.mul(anchor.Rotation, local);

            float3 toSlot = target - xform.Position;
            float  dist   = math.length(toSlot);
            float  step   = StepSpeed * Dt;

            if (dist <= step || dist < 1e-4f)
                xform.Position = target;
            else
                xform.Position += (toSlot / dist) * step;

            xform.Rotation = anchor.Rotation;
        }
    }
}
