using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class HealthBarSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void SpawnsBar_LinksFromSoldier_AndParentsToSoldier()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab, healthBarHeightOffset: 1.2f);

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform));
            Manager.SetComponentData(soldier, LocalTransform.FromPosition(new float3(3f, 0f, 7f)));

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            Assert.IsTrue(Manager.HasComponent<HealthBarRef>(soldier),
                "soldier should gain HealthBarRef");
            var barRef = Manager.GetComponentData<HealthBarRef>(soldier);
            Assert.AreNotEqual(Entity.Null, barRef.Bar);
            Assert.IsTrue(Manager.Exists(barRef.Bar));

            Assert.IsTrue(Manager.HasComponent<Parent>(barRef.Bar),
                "bar should have a Parent component");
            Assert.AreEqual(soldier, Manager.GetComponentData<Parent>(barRef.Bar).Value);

            Assert.IsTrue(Manager.HasComponent<HealthBarLink>(barRef.Bar));
            Assert.AreEqual(soldier, Manager.GetComponentData<HealthBarLink>(barRef.Bar).Owner);

            var localT = Manager.GetComponentData<LocalTransform>(barRef.Bar);
            Assert.AreEqual(new float3(0f, 1.2f, 0f), localT.Position);
        }

        [Test]
        public void AddsLinkedEntityGroup_SoDestroyingSoldierDestroysBar()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab);

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform));

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            Assert.IsTrue(Manager.HasBuffer<LinkedEntityGroup>(soldier));
            var group = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(2, group.Length);
            Assert.AreEqual(soldier, group[0].Value, "element 0 must be the root entity");

            var bar = Manager.GetComponentData<HealthBarRef>(soldier).Bar;
            Assert.AreEqual(bar, group[1].Value);

            Manager.DestroyEntity(soldier);
            Assert.IsFalse(Manager.Exists(bar), "bar should be cascaded-destroyed");
        }

        [Test]
        public void DoesNotRespawn_WhenSoldierAlreadyHasHealthBarRef()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab);

            var existingBar = Manager.CreateEntity(typeof(HealthBarFill));
            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = existingBar });

            // Count entities before
            var beforeQuery = Manager.CreateEntityQuery(typeof(HealthBarFill));
            int beforeCount = beforeQuery.CalculateEntityCount();
            beforeQuery.Dispose();

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            // The same single HealthBarFill entity (existingBar) should still be the only one.
            var afterQuery = Manager.CreateEntityQuery(typeof(HealthBarFill));
            int afterCount = afterQuery.CalculateEntityCount();
            afterQuery.Dispose();

            Assert.AreEqual(beforeCount, afterCount, "no new bar should be spawned");
            Assert.AreEqual(existingBar, Manager.GetComponentData<HealthBarRef>(soldier).Bar);
        }
    }
}
