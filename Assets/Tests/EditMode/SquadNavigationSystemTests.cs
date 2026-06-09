using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SquadNavigationSystemTests : EcsTestsBase
    {
        [Test]
        public void CreateSquad_HasNavComponents_DefaultPursue()
        {
            var squad = CreateSquad(0, 5, 10, 1.5f, float3.zero, quaternion.identity);
            Assert.IsTrue(Manager.HasComponent<SquadNav>(squad));
            Assert.IsTrue(Manager.HasComponent<SquadMoveGoal>(squad));
            Assert.AreEqual(NavState.Pursue, Manager.GetComponentData<SquadNav>(squad).State);
        }
    }
}
