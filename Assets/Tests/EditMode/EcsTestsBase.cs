using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    // Minimal per-test ECS world. Avoids the Unity.Entities.Tests package
    // dependency. Subclasses get a fresh World + EntityManager and helpers
    // for the four entity shapes our systems read.
    //
    // CreateBattleConfig is added in Task 4 once BattleConfig grows the
    // SquadSpacing / SoldierStepSpeed / SquadAdvanceSpeed / SquadRotationSpeed
    // / ContactMargin / CompactionIntervalTicks / SquadsPerTeam / SquadRows /
    // SquadCols fields it needs to reference.
    public abstract class EcsTestsBase
    {
        protected World         World;
        protected EntityManager Manager;

        [SetUp]
        public virtual void SetUp()
        {
            World   = new World("Test " + GetType().Name);
            Manager = World.EntityManager;
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (World != null && World.IsCreated)
                World.Dispose();
            World   = null;
            Manager = default;
        }

        protected Entity CreateNetworkTime(uint tick = 1)
        {
            var e = Manager.CreateEntity(typeof(NetworkTime));
            Manager.SetComponentData(e, new NetworkTime
            {
                ServerTick = new NetworkTick(tick),
            });
            return e;
        }

        protected Entity CreateSquad(
            int team, int rows, int cols, float spacing,
            float3 position, quaternion rotation)
        {
            var e = Manager.CreateEntity(
                typeof(Squad), typeof(SquadTarget), typeof(SquadMember),
                typeof(LocalTransform), typeof(LocalToWorld));
            Manager.SetComponentData(e, new Squad
            {
                Team = team, Rows = rows, Cols = cols, Spacing = spacing,
            });
            Manager.SetComponentData(e, new SquadTarget { Value = Entity.Null });
            Manager.SetComponentData(e, LocalTransform.FromPositionRotation(position, rotation));
            return e;
        }

        protected Entity CreateSoldier(
            Entity squad, int slot, float3 pos,
            float health = 50f, float attackRange = 0.8f, float dps = 25f)
        {
            var e = Manager.CreateEntity(
                typeof(Soldier), typeof(Team), typeof(Health), typeof(AttackStats),
                typeof(SquadMembership), typeof(LocalTransform));
            Manager.SetComponentData(e, new Team { Value = 0 });
            Manager.SetComponentData(e, new SquadMembership { Squad = squad, SlotIndex = slot });
            Manager.SetComponentData(e, new Health { Current = health, Max = health });
            Manager.SetComponentData(e, new AttackStats { Range = attackRange, Dps = dps });
            Manager.SetComponentData(e, LocalTransform.FromPosition(pos));
            return e;
        }

        // Advance the world's time so SystemAPI.Time.DeltaTime returns `dt`.
        protected void SetTime(double elapsed, float dt)
        {
            World.SetTime(new TimeData(elapsed, dt));
        }

        // Create system, tick once, return the SystemHandle.
        protected SystemHandle CreateAndUpdateSystem<T>() where T : unmanaged, ISystem
        {
            var handle = World.CreateSystem<T>();
            World.Unmanaged.GetUnsafeSystemRef<T>(handle).OnUpdate(
                ref World.Unmanaged.ResolveSystemStateRef(handle));
            return handle;
        }
    }
}
