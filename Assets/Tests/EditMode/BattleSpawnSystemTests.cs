using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Demo.Tests
{
    public class BattleSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void Spawns_WiresBuffersAndMembership_AndSelfDisables()
        {
            var config = CreateBattleConfig(squadsPerTeam: 1, rows: 2, cols: 2);
            var stub = CreateSoldierPrefabStub();
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.SoldierPrefab = stub;
            Manager.SetComponentData(config, bc);

            var handle = CreateAndUpdateSystem<BattleSpawnSystem>();

            // 2 * SquadsPerTeam squads exist.
            var squadQuery = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadMember));
            Assert.AreEqual(2, squadQuery.CalculateEntityCount(), "one red + one blue squad");

            // Every slot is wired to a soldier whose membership points back consistently.
            var squads = squadQuery.ToEntityArray(Allocator.Temp);
            foreach (var sq in squads)
            {
                var buf = Manager.GetBuffer<SquadMember>(sq);
                Assert.AreEqual(4, buf.Length, "rows*cols slots");
                for (int i = 0; i < buf.Length; i++)
                {
                    var soldier = buf[i].Value;
                    Assert.AreNotEqual(Entity.Null, soldier, "no empty slot after spawn");
                    var m = Manager.GetComponentData<SquadMembership>(soldier);
                    Assert.AreEqual(sq, m.Squad, "membership points at its squad");
                    Assert.AreEqual(i, m.SlotIndex, "slot index matches buffer position");
                }
            }
            squads.Dispose();

            // System disabled itself after the one-shot spawn.
            ref var stateRef = ref World.Unmanaged.ResolveSystemStateRef(handle);
            Assert.IsFalse(stateRef.Enabled, "BattleSpawnSystem disables itself after spawning");
        }

        [Test]
        public void Spawn_SquadsHaveNavComponents_Pursue()
        {
            var config = CreateBattleConfig(squadsPerTeam: 1, rows: 2, cols: 2);
            var stub = CreateSoldierPrefabStub();
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.SoldierPrefab = stub;
            Manager.SetComponentData(config, bc);

            CreateAndUpdateSystem<BattleSpawnSystem>();

            var squadQuery = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadNav), typeof(SquadMoveGoal));
            Assert.AreEqual(2, squadQuery.CalculateEntityCount(), "one red + one blue squad");

            using var squads = squadQuery.ToEntityArray(Allocator.Temp);
            foreach (var s in squads)
                Assert.AreEqual(NavState.Pursue, Manager.GetComponentData<SquadNav>(s).State);
        }
    }
}
