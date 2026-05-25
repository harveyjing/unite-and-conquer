using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    // Client-only presentation. Each frame, writes
    // saturate(Health.Current / BattleConfig.MaxHealth) into the linked
    // HealthBarFill so the shader's _Health01 stays in sync with HP.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    // [UpdateAfter(typeof(HealthBarSpawnSystem))]  // re-enable in Task 8
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct HealthBarUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float maxHealth = SystemAPI.GetSingleton<BattleConfig>().MaxHealth;
            var fillLookup  = SystemAPI.GetComponentLookup<HealthBarFill>(false);

            state.Dependency = new WriteFillJob
            {
                MaxHealth  = maxHealth,
                FillLookup = fillLookup,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct WriteFillJob : IJobEntity
        {
            public float MaxHealth;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HealthBarFill> FillLookup;

            void Execute(in Health health, in HealthBarRef barRef)
            {
                if (barRef.Bar == Entity.Null) return;
                if (!FillLookup.HasComponent(barRef.Bar)) return;

                float ratio = MaxHealth > 0f
                    ? math.saturate(health.Current / MaxHealth)
                    : 0f;
                FillLookup[barRef.Bar] = new HealthBarFill { Value = ratio };
            }
        }
    }
}
