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

        [Test]
        public void ClearPath_StaysPursue_EngageGoalAtTarget()
        {
            CreateBattleConfig();
            var self   = CreateSquad(0, 2, 2, 1.5f, new float3(-5, 0, 0), quaternion.identity);
            var target = CreateSquad(1, 2, 2, 1.5f, new float3( 5, 0, 0), quaternion.identity);
            Manager.SetComponentData(self, new SquadTarget { Value = target });

            CreateAndUpdateSystem<SquadNavigationSystem>();

            var nav  = Manager.GetComponentData<SquadNav>(self);
            var goal = Manager.GetComponentData<SquadMoveGoal>(self);
            Assert.AreEqual(NavState.Pursue, nav.State);
            Assert.AreEqual((byte)1, goal.Engage);
            Assert.AreEqual(5f, goal.Position.x, 1e-3f);
        }

        [Test]
        public void BlockedPath_EntersApproachPortal_GoalAtNearEntrance()
        {
            CreateBattleConfig();
            var self   = CreateSquad(0, 2, 2, 1.5f, new float3(-5, 0, 0), quaternion.identity);
            var target = CreateSquad(1, 2, 2, 1.5f, new float3( 5, 0, 0), quaternion.identity);
            Manager.SetComponentData(self, new SquadTarget { Value = target });

            // Impassable wall straddling the straight path: thin in x, long in z.
            CreateTerrainRegion(float3.zero, new float2(1f, 5f), yaw: 0f,
                passable: 0, kind: TerrainKind.River);
            // Bridge north of the wall: endpoints at z = 8 (outside the wall's z span).
            CreateCrossingPortal(
                entrance: new float3(-1, 0, 8), exit: new float3(1, 0, 8), width: 2f);

            CreateAndUpdateSystem<SquadNavigationSystem>();

            var nav  = Manager.GetComponentData<SquadNav>(self);
            var goal = Manager.GetComponentData<SquadMoveGoal>(self);
            Assert.AreEqual(NavState.ApproachPortal, nav.State);
            Assert.AreEqual((byte)0, goal.Engage);
            // Self at x=-5 is nearer the (-1,0,8) endpoint -> that becomes the entrance.
            Assert.AreEqual(-1f, goal.Position.x, 1e-3f);
            Assert.AreEqual( 8f, goal.Position.z, 1e-3f);
            Assert.AreEqual(2f, nav.PortalWidth, 1e-3f);
            Assert.AreEqual(2, nav.BaseCols);
        }
    }
}
