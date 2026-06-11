using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Demo.Tests
{
    // Crowd-sandbox helpers: config singleton, a prefab stand-in, and a
    // directly placed soldier for steering tests.
    public abstract partial class EcsTestsBase
    {
        protected Entity CreateCrowdConfig(
            Entity soldierPrefab = default,
            int army0Count = 4,
            int army1Count = 4,
            float3 army0SpawnCenter = default,
            float3 army1SpawnCenter = default,
            float2 spawnHalfExtents = default,
            float3 army0Goal = default,
            float3 army1Goal = default,
            float spawnSpacing = 1.2f,
            float moveSpeed = 2.5f,
            float arrivalRadius = 6f)
        {
            if (spawnHalfExtents.Equals(default(float2)))
                spawnHalfExtents = new float2(12f, 30f);
            var e = Manager.CreateEntity(typeof(CrowdConfig));
            Manager.SetComponentData(e, new CrowdConfig
            {
                SoldierPrefab    = soldierPrefab,
                Army0Count       = army0Count,
                Army1Count       = army1Count,
                Army0SpawnCenter = army0SpawnCenter,
                Army1SpawnCenter = army1SpawnCenter,
                SpawnHalfExtents = spawnHalfExtents,
                Army0Goal        = army0Goal,
                Army1Goal        = army1Goal,
                SpawnSpacing     = spawnSpacing,
                MoveSpeed        = moveSpeed,
                ArrivalRadius    = arrivalRadius,
                Army0Color       = new float4(1f, 0f, 0f, 1f),
                Army1Color       = new float4(0f, 0f, 1f, 1f),
            });
            return e;
        }

        // Stand-in for the baked CrowdSoldier prefab: carries every component
        // CrowdSpawnSystem writes after Instantiate, plus the Prefab tag so
        // the stub itself never matches runtime queries.
        protected Entity CreateCrowdSoldierPrefabStub()
        {
            var e = Manager.CreateEntity(
                typeof(CrowdSoldier), typeof(SoldierColor),
                typeof(LocalTransform), typeof(PhysicsVelocity),
                typeof(Prefab));
            Manager.SetComponentData(e, LocalTransform.Identity);

            // Models the prefab's Visual child: carries its own SoldierColor
            // (the one Entities.Graphics actually binds to _BaseColor).
            var child = Manager.CreateEntity(
                typeof(SoldierColor), typeof(LocalTransform), typeof(Prefab));
            Manager.SetComponentData(child, LocalTransform.Identity);

            // LinkedEntityGroup element 0 must be the root (Unity convention).
            var leg = Manager.AddBuffer<LinkedEntityGroup>(e);
            leg.Add(new LinkedEntityGroup { Value = e });
            leg.Add(new LinkedEntityGroup { Value = child });

            return e;
        }

        protected Entity CreateCrowdSoldier(float3 pos, float3 goal, int team = 0)
        {
            var e = Manager.CreateEntity(
                typeof(CrowdSoldier), typeof(LocalTransform), typeof(PhysicsVelocity));
            Manager.SetComponentData(e, new CrowdSoldier { Team = team, Goal = goal });
            Manager.SetComponentData(e, LocalTransform.FromPosition(pos));
            return e;
        }
    }
}
