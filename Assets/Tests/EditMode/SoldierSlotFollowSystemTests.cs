using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SoldierSlotFollowSystemTests : EcsTestsBase
    {
        [Test]
        public void SoldierFarFromSlot_AdvancesAtStepSpeed()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Slot 0 in 2×2 grid: local (-0.5, 0, +0.5), world is the same since identity rotation.
            // Soldier starts far behind at z = -10.
            var soldier = CreateSoldier(squad, slot: 0, pos: new float3(-0.5f, 0, -10f));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            // Moved 0.2 toward (-0.5, 0, 0.5) from (-0.5, 0, -10); only z changes.
            Assert.AreEqual(-0.5f, pos.x, 1e-4f);
            Assert.AreEqual(-9.8f, pos.z, 1e-3f);
        }

        [Test]
        public void SoldierWithinOneStep_SnapsToSlot()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Slot 0 world pos: (-0.5, 0, +0.5). Soldier within step distance (step = 0.2).
            var soldier = CreateSoldier(squad, slot: 0, pos: new float3(-0.5f, 0, 0.4f));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            Assert.AreEqual(-0.5f, pos.x, 1e-4f);
            Assert.AreEqual( 0.5f, pos.z, 1e-4f);
        }

        [Test]
        public void NullSquadMembership_DoesNotMove()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var soldier = CreateSoldier(Entity.Null, slot: -1, pos: new float3(7, 0, 7));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            Assert.AreEqual(7f, pos.x, 1e-4f);
            Assert.AreEqual(7f, pos.z, 1e-4f);
        }
    }
}
