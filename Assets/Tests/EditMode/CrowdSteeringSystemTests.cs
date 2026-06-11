using NUnit.Framework;
using Unity.Mathematics;
using Unity.Physics;

namespace Demo.Tests
{
    public class CrowdSteeringSystemTests : EcsTestsBase
    {
        const float Tol = 1e-3f;

        [Test]
        public void OpenField_VelocityPointsAtGoal_AtMoveSpeed()
        {
            CreateCrowdConfig(moveSpeed: 2.5f);
            var soldier = CreateCrowdSoldier(new float3(-30f, 0f, 0f), new float3(30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            Assert.AreEqual(2.5f, v.x, Tol);
            Assert.AreEqual(0f,   v.y, Tol);
            Assert.AreEqual(0f,   v.z, Tol);
        }

        [Test]
        public void WithinArrivalRadius_VelocityZero()
        {
            CreateCrowdConfig(arrivalRadius: 6f);
            var soldier = CreateCrowdSoldier(new float3(28f, 0f, 0f), new float3(30f, 0f, 0f));
            Manager.SetComponentData(soldier, new PhysicsVelocity
            {
                Linear = new float3(1f, 0f, 0f), Angular = float3.zero,
            });

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            Assert.AreEqual(0f, math.length(v), Tol);
        }

        [Test]
        public void RiverBlocks_VelocityPointsAtBridgeEntrance()
        {
            CreateCrowdConfig(moveSpeed: 2.5f);
            CreateTerrainRegion(float3.zero, new float2(3f, 30f)); // impassable river
            CreateCrossingPortal(new float3(-8f, 0f, 0f), new float3(8f, 0f, 0f), width: 8f);
            var soldier = CreateCrowdSoldier(new float3(-30f, 0f, 10f), new float3(30f, 0f, 10f));

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            var expectedDir = math.normalize(new float3(-8f, 0f, 0f) - new float3(-30f, 0f, 10f));
            var actualDir   = math.normalize(v);
            Assert.AreEqual(expectedDir.x, actualDir.x, Tol);
            Assert.AreEqual(expectedDir.z, actualDir.z, Tol);
            Assert.AreEqual(2.5f, math.length(v), Tol);
        }
    }
}
