using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot: bulk-spawns both armies on a non-overlapping grid inside
    // their spawn rectangles and stamps team/goal/color. Grid pitch
    // (SpawnSpacing) stays above the capsule diameter so the solver never
    // starts from interpenetration. LocalSimulation: crowd systems exist only
    // in the netcode-free sandbox world, never in BattleScene's worlds.
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial struct CrowdSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<CrowdConfig>();
            var em = state.EntityManager;

            SpawnArmy(em, config, team: 0, config.Army0Count,
                config.Army0SpawnCenter, config.Army0Goal, config.Army0Color);
            SpawnArmy(em, config, team: 1, config.Army1Count,
                config.Army1SpawnCenter, config.Army1Goal, config.Army1Color);

            Debug.Log($"CrowdSpawnSystem: spawned {config.Army0Count} + {config.Army1Count} soldiers.");
            state.Enabled = false;
        }

        static void SpawnArmy(EntityManager em, in CrowdConfig config,
            int team, int count, float3 center, float3 goal, float4 color)
        {
            if (count <= 0 || config.SoldierPrefab == Entity.Null)
                return;

            float2 half    = config.SpawnHalfExtents;
            float  spacing = config.SpawnSpacing;
            int cols = math.max(1, (int)math.floor(half.x * 2f / spacing));

            using var entities = em.Instantiate(config.SoldierPrefab, count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var pos = new float3(
                    center.x - half.x + (col + 0.5f) * spacing,
                    0f,
                    center.z - half.y + (row + 0.5f) * spacing);
                em.SetComponentData(entities[i], LocalTransform.FromPosition(pos));
                em.SetComponentData(entities[i], new CrowdSoldier { Team = team, Goal = goal });
                em.SetComponentData(entities[i], new SoldierColor { Value = color });
            }
        }
    }
}
