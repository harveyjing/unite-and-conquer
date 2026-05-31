using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SquadMovementSystemTests : EcsTestsBase
    {
        [Test]
        public void FarSquad_AdvancesTowardTarget()
        {
            CreateBattleConfig();
            // Red faces +X at origin, target at +10 on X. Engagement distance with
            // rows=2 spacing=1.5 attack=0.8 margin=0.1 is:
            //   (1*0.5*1.5)*2 + 0.8 - 0.1 = 1.5 + 0.8 - 0.1 = 2.2
            // dist = 10 > 2.2, so red must advance.
            var faceRedAtPlusX = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            var red  = CreateSquad(0, 2, 2, 1.5f, new float3( 0, 0, 0), faceRedAtPlusX);
            var blue = CreateSquad(1, 2, 2, 1.5f, new float3(10, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = blue });

            SetTime(elapsed: 0.0, dt: 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.Greater(pos.x, 0f);
            // SquadAdvanceSpeed=2, dt=0.1 → step ≈ 0.2 along forward (+X).
            Assert.LessOrEqual(pos.x, 0.21f);
        }

        [Test]
        public void SquadAtEngagementDistance_DoesNotAdvance()
        {
            CreateBattleConfig();
            var face = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            // Engagement distance is 2.2 with rows=2, spacing=1.5, range=0.8, margin=0.1.
            var red  = CreateSquad(0, 2, 2, 1.5f, new float3(0, 0, 0), face);
            var blue = CreateSquad(1, 2, 2, 1.5f, new float3(2.2f, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = blue });

            SetTime(0.0, 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.AreEqual(0f, pos.x, 1e-4f);
        }

        [Test]
        public void NullTarget_DoesNotMove()
        {
            CreateBattleConfig();
            var red = CreateSquad(0, 2, 2, 1.5f, new float3(0, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = Entity.Null });

            SetTime(0.0, 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.AreEqual(0f, pos.x, 1e-4f);
        }
    }
}
