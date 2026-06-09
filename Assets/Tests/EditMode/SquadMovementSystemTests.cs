using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    // SquadMovementSystem advances a squad's anchor toward SquadMoveGoal.Position.
    // The engagement-distance stop applies only when goal.Engage == 1 (chasing an
    // enemy). SquadNavigationSystem writes the goal in production; here we set it
    // directly to test movement in isolation.
    public class SquadMovementSystemTests : EcsTestsBase
    {
        [Test]
        public void Engage0_WalksTowardGoal()
        {
            CreateBattleConfig(squadAdvanceSpeed: 2f, squadRotationSpeed: 100f);
            var squad = CreateSquad(0, 1, 1, 1.5f, float3.zero,
                quaternion.LookRotationSafe(new float3(1, 0, 0), math.up()));
            Manager.SetComponentData(squad, new SquadMoveGoal
            {
                Position = new float3(10, 0, 0), Engage = 0,
            });
            SetTime(1.0, 0.5f); // dt = 0.5 -> step = 1.0

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(squad).Position;
            Assert.Greater(p.x, 0.4f, "should advance toward +x goal");
        }

        [Test]
        public void Engage1_StopsInsideEngagementDistance()
        {
            CreateBattleConfig(squadAdvanceSpeed: 2f, squadRotationSpeed: 100f,
                attackRange: 0.8f, contactMargin: 0.1f);
            var self = CreateSquad(0, 1, 1, 1.5f, float3.zero,
                quaternion.LookRotationSafe(new float3(1, 0, 0), math.up()));
            // Target squad 0.5 away; engagement distance for 1x1 vs 1x1 is
            // attackRange - margin = 0.7, so 0.5 < 0.7 -> must NOT advance.
            var target = CreateSquad(1, 1, 1, 1.5f, new float3(0.5f, 0, 0), quaternion.identity);
            Manager.SetComponentData(self, new SquadTarget { Value = target });
            Manager.SetComponentData(self, new SquadMoveGoal
            {
                Position = new float3(0.5f, 0, 0), Engage = 1,
            });
            SetTime(1.0, 0.5f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(self).Position;
            Assert.AreEqual(0f, p.x, 1e-3f, "inside engagement range -> no advance");
        }

        [Test]
        public void Engage1_AdvancesTowardTarget_WhenBeyondEngagement()
        {
            CreateBattleConfig(); // advanceSpeed=2, rows/cols 2x2 spacing 1.5 by default
            var face = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            // Engagement distance for 2x2 vs 2x2 is (1*0.5*1.5)*2 + 0.8 - 0.1 = 2.2.
            var self  = CreateSquad(0, 2, 2, 1.5f, float3.zero, face);
            var blue  = CreateSquad(1, 2, 2, 1.5f, new float3(10, 0, 0), quaternion.identity);
            Manager.SetComponentData(self, new SquadTarget { Value = blue });
            Manager.SetComponentData(self, new SquadMoveGoal
            {
                Position = new float3(10, 0, 0), Engage = 1,
            });
            SetTime(0.0, 0.1f); // step ≈ advanceSpeed(2) * 0.1 = 0.2

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(self).Position;
            Assert.Greater(p.x, 0f, "dist 10 > engagement 2.2 -> advances");
            Assert.LessOrEqual(p.x, 0.21f, "one step is ~0.2");
        }

        [Test]
        public void AtGoal_DoesNotMove()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 2, 2, 1.5f, float3.zero, quaternion.identity);
            Manager.SetComponentData(squad, new SquadTarget { Value = Entity.Null });
            // Goal coincides with the anchor (what nav writes when there is no
            // reachable target): zero displacement -> no movement.
            Manager.SetComponentData(squad, new SquadMoveGoal
            {
                Position = float3.zero, Engage = 0,
            });
            SetTime(0.0, 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(squad).Position;
            Assert.AreEqual(0f, p.x, 1e-4f);
            Assert.AreEqual(0f, p.z, 1e-4f);
        }
    }
}
