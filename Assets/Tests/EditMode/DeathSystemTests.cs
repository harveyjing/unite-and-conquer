using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class DeathSystemTests : EcsTestsBase
    {
        [Test]
        public void DestroysSoldier_AtZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var dead = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 0f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsFalse(Manager.Exists(dead),
                "soldier at exactly 0 health must be destroyed");
        }

        [Test]
        public void DestroysSoldier_BelowZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var dead = CreateSoldier(squad, slot: 0, pos: float3.zero, health: -5f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsFalse(Manager.Exists(dead),
                "soldier below 0 health must be destroyed");
        }

        [Test]
        public void KeepsSoldier_AboveZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var alive = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 1f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsTrue(Manager.Exists(alive),
                "soldier with positive health must survive");
        }
    }
}
