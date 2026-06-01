using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    public class OwnershipRingSpawnSystemTests : EcsTestsBase
    {
        Entity SetupConfigWithRing()
        {
            var ring = CreateOwnershipRingStub();
            return CreateBattleConfig(ownershipRingPrefab: ring);
        }

        [Test]
        public void OwnedSoldier_GetsExactlyOneRing()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero);
            Manager.AddComponent<GhostOwnerIsLocal>(soldier); // enabled by default

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            Assert.IsTrue(Manager.HasComponent<OwnershipRingRef>(soldier),
                "owned soldier should have an OwnershipRingRef");
            var ring = Manager.GetComponentData<OwnershipRingRef>(soldier).Ring;
            Assert.AreNotEqual(Entity.Null, ring);
            Assert.AreEqual(soldier, Manager.GetComponentData<Parent>(ring).Value);

            var group = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(2, group.Length, "LinkedEntityGroup = soldier + ring");
            Assert.AreEqual(soldier, group[0].Value);
            Assert.AreEqual(ring, group[1].Value);
        }

        [Test]
        public void UnownedSoldier_GetsNoRing()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero); // no GhostOwnerIsLocal

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            Assert.IsFalse(Manager.HasComponent<OwnershipRingRef>(soldier));
        }

        [Test]
        public void Ring_AppendsToExistingLinkedGroup_FromHealthBar()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero);
            Manager.AddComponent<GhostOwnerIsLocal>(soldier);

            // Simulate HealthBarSpawnSystem having already linked a bar.
            var bar = Manager.CreateEntity(typeof(LocalTransform));
            var group = Manager.AddBuffer<LinkedEntityGroup>(soldier);
            group.Add(new LinkedEntityGroup { Value = soldier });
            group.Add(new LinkedEntityGroup { Value = bar });

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            var after = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(3, after.Length, "soldier + bar + ring; existing links preserved");
            Assert.AreEqual(soldier, after[0].Value);
            Assert.AreEqual(bar, after[1].Value);
        }
    }
}
