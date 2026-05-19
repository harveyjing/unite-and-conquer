using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class SquadCompactionSystemTests : EcsTestsBase
    {
        // Pick a tick that satisfies (tick + squad.Index) % interval == 0
        // for our default interval. We set interval = 1 in tests so any tick fires.
        const uint FireEveryTick = 1;

        [Test]
        public void RepacksBuffer_DropsDeadAndNulls_ReassignsSlotIndex()
        {
            CreateBattleConfig(compactionIntervalTicks: (int)FireEveryTick);
            CreateNetworkTime(tick: 1);

            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Build a 4-slot buffer: [alive, null, dead, alive]
            var buf = Manager.GetBuffer<SquadMember>(squad);
            buf.ResizeUninitialized(4);
            var a = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 30f);
            var c = CreateSoldier(squad, slot: 2, pos: float3.zero, health: 0f); // dead
            var d = CreateSoldier(squad, slot: 3, pos: float3.zero, health: 30f);
            buf[0] = new SquadMember { Value = a };
            buf[1] = new SquadMember { Value = Entity.Null };
            buf[2] = new SquadMember { Value = c };
            buf[3] = new SquadMember { Value = d };

            CreateAndUpdateSystem<SquadCompactionSystem>();

            var freshBuf = Manager.GetBuffer<SquadMember>(squad);
            Assert.AreEqual(2, freshBuf.Length, "buffer should be packed to alive count");
            Assert.AreEqual(a, freshBuf[0].Value);
            Assert.AreEqual(d, freshBuf[1].Value);

            var freshSquad = Manager.GetComponentData<Squad>(squad);
            Assert.AreEqual(1, freshSquad.Rows, "2 alive in cols=2 → 1 row");

            var membershipA = Manager.GetComponentData<SquadMembership>(a);
            var membershipD = Manager.GetComponentData<SquadMembership>(d);
            Assert.AreEqual(0, membershipA.SlotIndex);
            Assert.AreEqual(1, membershipD.SlotIndex);
        }

        [Test]
        public void AllDead_DestroysSquadEntity()
        {
            CreateBattleConfig(compactionIntervalTicks: (int)FireEveryTick);
            CreateNetworkTime(tick: 1);

            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            var buf = Manager.GetBuffer<SquadMember>(squad);
            buf.ResizeUninitialized(2);
            var dead1 = CreateSoldier(squad, 0, float3.zero, health: 0f);
            var dead2 = CreateSoldier(squad, 1, float3.zero, health: 0f);
            buf[0] = new SquadMember { Value = dead1 };
            buf[1] = new SquadMember { Value = dead2 };

            CreateAndUpdateSystem<SquadCompactionSystem>();

            Assert.IsFalse(Manager.Exists(squad), "Squad entity should be destroyed when alive count hits zero");
        }
    }
}
