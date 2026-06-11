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

        // Fills a squad's member buffer with `count` live soldiers in packed slots.
        void FillSquad(Entity squad, int count)
        {
            var buf = Manager.GetBuffer<SquadMember>(squad);
            buf.ResizeUninitialized(count);
            for (int i = 0; i < count; i++)
            {
                var s = CreateSoldier(squad, slot: i, pos: float3.zero, health: 30f);
                buf[i] = new SquadMember { Value = s };
            }
        }

        [Test]
        public void AtEntrance_ReshapesNarrow_EntersCrossing()
        {
            CreateBattleConfig();
            var self = CreateSquad(0, 5, 10, 1.5f, new float3(-1, 0, 8), quaternion.identity);
            FillSquad(self, 20); // 20 alive
            Manager.SetComponentData(self, new SquadNav
            {
                State       = NavState.ApproachPortal,
                Entrance    = new float3(-1, 0, 8),  // squad is AT the entrance
                Exit        = new float3( 1, 0, 8),
                PortalWidth = 2f,                     // narrowCols = floor(2/1.5)=1
                BaseCols    = 10,
            });

            CreateAndUpdateSystem<SquadNavigationSystem>();

            var nav   = Manager.GetComponentData<SquadNav>(self);
            var squad = Manager.GetComponentData<Squad>(self);
            var goal  = Manager.GetComponentData<SquadMoveGoal>(self);
            Assert.AreEqual(NavState.Crossing, nav.State);
            Assert.AreEqual(1, squad.Cols, "narrow cols = floor(2/1.5)");
            Assert.AreEqual(20, squad.Rows, "20 alive in 1 col -> 20 rows");
            Assert.AreEqual(1f, goal.Position.x, 1e-3f, "goal now points at the exit");
            Assert.AreEqual(8f, goal.Position.z, 1e-3f);
        }

        [Test]
        public void AtExit_ReExpands_ReturnsPursue()
        {
            CreateBattleConfig();
            // Already narrow (Cols=1) and standing AT the exit.
            var self = CreateSquad(0, 20, 1, 1.5f, new float3(1, 0, 8), quaternion.identity);
            FillSquad(self, 20);
            Manager.SetComponentData(self, new SquadNav
            {
                State       = NavState.Crossing,
                Entrance    = new float3(-1, 0, 8),
                Exit        = new float3( 1, 0, 8),
                PortalWidth = 2f,
                BaseCols    = 10,
            });

            CreateAndUpdateSystem<SquadNavigationSystem>();

            var nav   = Manager.GetComponentData<SquadNav>(self);
            var squad = Manager.GetComponentData<Squad>(self);
            Assert.AreEqual(NavState.Pursue, nav.State);
            Assert.AreEqual(10, squad.Cols, "restored to BaseCols");
            Assert.AreEqual(2, squad.Rows, "20 alive in 10 cols -> 2 rows");
        }
    }
}
