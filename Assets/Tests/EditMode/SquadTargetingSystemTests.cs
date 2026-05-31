using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class SquadTargetingSystemTests : EcsTestsBase
    {
        [Test]
        public void NearestEnemySquad_IsChosen()
        {
            CreateBattleConfig(targetRefreshIntervalTicks: 1);
            CreateNetworkTime(tick: 1);

            // Red squad at origin. Two blue squads: one at +5, one at +20.
            var red   = CreateSquad(0, 2, 2, 1f, new float3( 0, 0, 0), quaternion.identity);
            var nearBlue = CreateSquad(1, 2, 2, 1f, new float3(+5, 0, 0), quaternion.identity);
            var farBlue  = CreateSquad(1, 2, 2, 1f, new float3(+20, 0, 0), quaternion.identity);

            CreateAndUpdateSystem<SquadTargetingSystem>();

            var t = Manager.GetComponentData<SquadTarget>(red);
            Assert.AreEqual(nearBlue, t.Value);

            // And from blue's POV: nearBlue's nearest is also the red squad (only enemy).
            var t2 = Manager.GetComponentData<SquadTarget>(nearBlue);
            Assert.AreEqual(red, t2.Value);

            // farBlue's nearest enemy is still red (only red is enemy).
            var t3 = Manager.GetComponentData<SquadTarget>(farBlue);
            Assert.AreEqual(red, t3.Value);
        }

        [Test]
        public void NoEnemySquad_LeavesTargetNull()
        {
            CreateBattleConfig(targetRefreshIntervalTicks: 1);
            CreateNetworkTime(tick: 1);

            // Only red squads exist.
            var redA = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            CreateSquad(0, 2, 2, 1f, new float3(5, 0, 0), quaternion.identity);

            CreateAndUpdateSystem<SquadTargetingSystem>();

            var t = Manager.GetComponentData<SquadTarget>(redA);
            Assert.AreEqual(Entity.Null, t.Value);
        }
    }
}
