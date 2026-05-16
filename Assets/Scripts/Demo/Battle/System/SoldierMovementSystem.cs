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
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct SoldierMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var dt = SystemAPI.Time.DeltaTime;
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SoldierStepJob
            {
                MoveSpeed   = config.MoveSpeed,
                AttackRange = config.AttackRange,
                Dt          = dt,
                XformLookup = xformLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SoldierStepJob : IJobEntity
    {
        public float MoveSpeed;
        public float AttackRange;
        public float Dt;
        [ReadOnly] public ComponentLookup<LocalTransform> XformLookup;

        public void Execute(ref LocalTransform xform, in Target target)
        {
            if (target.Value == Entity.Null) return;
            if (!XformLookup.HasComponent(target.Value)) return;

            var to = XformLookup[target.Value].Position - xform.Position;
            float dist = math.length(to);
            if (dist <= AttackRange || dist < 1e-4f) return;

            var dir = to / dist;
            xform.Position += dir * (MoveSpeed * Dt);
        }
    }
}
