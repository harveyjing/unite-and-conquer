using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot. Spawns CountPerSide soldiers per team in two opposing
    // grid blocks centered on RedCenter / BlueCenter, then disables
    // itself. Uses bulk EntityManager.Instantiate + IJobParallelFor to
    // initialize per-entity component values — ECB-per-entity would
    // cost hundreds of ms at 10k+10k.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct BattleSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var em = state.EntityManager;

            var reds  = em.Instantiate(config.SoldierPrefab, config.CountPerSide, Allocator.TempJob);
            var blues = em.Instantiate(config.SoldierPrefab, config.CountPerSide, Allocator.TempJob);

            var gridSide = (int)math.ceil(math.sqrt(config.CountPerSide));

            var xformLookup  = state.GetComponentLookup<LocalTransform>(false);
            var teamLookup   = state.GetComponentLookup<Team>(false);
            var healthLookup = state.GetComponentLookup<Health>(false);
            var attackLookup = state.GetComponentLookup<AttackStats>(false);
            var colorLookup  = state.GetComponentLookup<URPMaterialPropertyBaseColor>(false);

            var initRed = new InitSoldierJob
            {
                Entities       = reds,
                Origin         = config.RedCenter,
                GridSide       = gridSide,
                Spacing        = config.Spacing,
                TeamValue      = 0,
                TeamColor      = config.RedColor,
                MaxHealth      = config.MaxHealth,
                AttackRange    = config.AttackRange,
                Dps            = config.Dps,
                XformLookup    = xformLookup,
                TeamLookup     = teamLookup,
                HealthLookup   = healthLookup,
                AttackLookup   = attackLookup,
                ColorLookup    = colorLookup,
            };
            state.Dependency = initRed.Schedule(reds.Length, 64, state.Dependency);

            var initBlue = new InitSoldierJob
            {
                Entities       = blues,
                Origin         = config.BlueCenter,
                GridSide       = gridSide,
                Spacing        = config.Spacing,
                TeamValue      = 1,
                TeamColor      = config.BlueColor,
                MaxHealth      = config.MaxHealth,
                AttackRange    = config.AttackRange,
                Dps            = config.Dps,
                XformLookup    = xformLookup,
                TeamLookup     = teamLookup,
                HealthLookup   = healthLookup,
                AttackLookup   = attackLookup,
                ColorLookup    = colorLookup,
            };
            state.Dependency = initBlue.Schedule(blues.Length, 64, state.Dependency);

            state.Dependency = reds.Dispose(state.Dependency);
            state.Dependency = blues.Dispose(state.Dependency);

            Debug.Log($"BattleSpawnSystem: spawned {config.CountPerSide} red + {config.CountPerSide} blue soldiers.");
            state.Enabled = false;
        }

        [BurstCompile]
        struct InitSoldierJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            public float3 Origin;
            public int    GridSide;
            public float  Spacing;
            public int    TeamValue;
            public float4 TeamColor;
            public float  MaxHealth;
            public float  AttackRange;
            public float  Dps;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> XformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Team> TeamLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Health> HealthLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<AttackStats> AttackLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<URPMaterialPropertyBaseColor> ColorLookup;

            public void Execute(int i)
            {
                var e = Entities[i];
                int row = i / GridSide;
                int col = i % GridSide;
                var localOffset = new float3(
                    (col - GridSide * 0.5f) * Spacing,
                    0f,
                    (row - GridSide * 0.5f) * Spacing);
                var pos = Origin + localOffset;

                XformLookup[e]  = LocalTransform.FromPosition(pos);
                TeamLookup[e]   = new Team { Value = TeamValue };
                HealthLookup[e] = new Health { Current = MaxHealth, Max = MaxHealth };
                AttackLookup[e] = new AttackStats { Range = AttackRange, Dps = Dps };
                ColorLookup[e]  = new URPMaterialPropertyBaseColor { Value = TeamColor };
            }
        }
    }
}
