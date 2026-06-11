using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class CrowdSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void SpawnsConfiguredCountsPerArmy()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            CreateCrowdConfig(prefab, army0Count: 5, army1Count: 3,
                army0SpawnCenter: new float3(-30f, 0f, 0f),
                army1SpawnCenter: new float3( 30f, 0f, 0f),
                army0Goal: new float3( 30f, 0f, 0f),
                army1Goal: new float3(-30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier));
            Assert.AreEqual(8, query.CalculateEntityCount()); // Prefab stub excluded by default
            int team0 = 0, team1 = 0;
            using var soldiers = query.ToComponentDataArray<CrowdSoldier>(Unity.Collections.Allocator.Temp);
            foreach (var s in soldiers)
                if (s.Team == 0) team0++; else team1++;
            Assert.AreEqual(5, team0);
            Assert.AreEqual(3, team1);
        }

        [Test]
        public void StampsGoalsPerArmy()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            var goal0 = new float3(30f, 0f, 0f);
            var goal1 = new float3(-30f, 0f, 0f);
            CreateCrowdConfig(prefab, army0Count: 2, army1Count: 2,
                army0SpawnCenter: new float3(-30f, 0f, 0f),
                army1SpawnCenter: new float3( 30f, 0f, 0f),
                army0Goal: goal0, army1Goal: goal1);

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier));
            using var soldiers = query.ToComponentDataArray<CrowdSoldier>(Unity.Collections.Allocator.Temp);
            foreach (var s in soldiers)
            {
                var expected = s.Team == 0 ? goal0 : goal1;
                Assert.AreEqual(expected.x, s.Goal.x, 1e-4f);
                Assert.AreEqual(expected.z, s.Goal.z, 1e-4f);
            }
        }

        [Test]
        public void PlacesSoldiersInsideSpawnRect_NoTwoAtSamePosition()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            var center = new float3(-30f, 0f, 0f);
            var half   = new float2(12f, 30f);
            CreateCrowdConfig(prefab, army0Count: 50, army1Count: 0,
                army0SpawnCenter: center, spawnHalfExtents: half,
                army0Goal: new float3(30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier), typeof(LocalTransform));
            using var xforms = query.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            Assert.AreEqual(50, xforms.Length);
            for (int i = 0; i < xforms.Length; i++)
            {
                var p = xforms[i].Position;
                Assert.LessOrEqual(math.abs(p.x - center.x), half.x + 1e-3f, $"soldier {i} x outside rect");
                Assert.LessOrEqual(math.abs(p.z - center.z), half.y + 1e-3f, $"soldier {i} z outside rect");
                for (int j = i + 1; j < xforms.Length; j++)
                    Assert.Greater(math.distance(p, xforms[j].Position), 0.5f,
                        $"soldiers {i} and {j} spawned overlapping");
            }
        }
    }
}
