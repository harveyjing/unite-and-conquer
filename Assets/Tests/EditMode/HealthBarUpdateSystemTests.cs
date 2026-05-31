using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class HealthBarUpdateSystemTests : EcsTestsBase
    {
        [Test]
        public void WritesFillRatio_FromCurrentOverConfigMaxHealth()
        {
            CreateBattleConfig(maxHealth: 50f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 1f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 25f, Max = 50f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(0.5f, fill.Value, 0.0001f);
        }

        [Test]
        public void ClampsToZero_WhenConfigMaxHealthIsZero()
        {
            CreateBattleConfig(maxHealth: 0f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 1f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 25f, Max = 0f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(0f, fill.Value, 0.0001f, "must not be NaN or > 0 when Max=0");
        }

        [Test]
        public void ClampsToOne_WhenCurrentExceedsMax()
        {
            CreateBattleConfig(maxHealth: 50f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 0f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 999f, Max = 50f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(1f, fill.Value, 0.0001f);
        }
    }
}
