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

        protected Entity CreateBattleConfig(
            int squadsPerTeam = 1,
            int rows = 2,
            int cols = 2,
            float spacing = 1.5f,
            float attackRange = 0.8f,
            float dps = 25f,
            float maxHealth = 50f,
            float soldierStepSpeed = 2f,
            float squadAdvanceSpeed = 2f,
            float squadRotationSpeed = 2f,
            float contactMargin = 0.1f,
            int compactionIntervalTicks = 10,
            int targetRefreshIntervalTicks = 1)
        {
            var e = Manager.CreateEntity(typeof(BattleConfig));
            Manager.SetComponentData(e, new BattleConfig
            {
                SquadsPerTeam              = squadsPerTeam,
                SquadRows                  = rows,
                SquadCols                  = cols,
                SquadSpacing               = spacing,
                SquadAdvanceSpeed          = squadAdvanceSpeed,
                SquadRotationSpeed         = squadRotationSpeed,
                ContactMargin              = contactMargin,
                CompactionIntervalTicks    = compactionIntervalTicks,
                AttackRange                = attackRange,
                Dps                        = dps,
                MaxHealth                  = maxHealth,
                SoldierStepSpeed           = soldierStepSpeed,
                TargetRefreshIntervalTicks = targetRefreshIntervalTicks,
                RedCenter                  = new float3(-5f, 0f, 0f),
                BlueCenter                 = new float3( 5f, 0f, 0f),
                RedColor                   = new float4(1f, 0f, 0f, 1f),
                BlueColor                  = new float4(0f, 0f, 1f, 1f),
                CountPerSide               = squadsPerTeam * rows * cols,
            });
            return e;
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

        // Create system, tick once, complete any in-flight jobs, return the SystemHandle.
        // Completing state.Dependency is essential: systems under test commonly schedule
        // parallel jobs but rely on a downstream system or end-of-frame to drain them.
        // In a single-system unit test there's no such drainer, so we do it here before
        // assertions read the data the job wrote.
        protected SystemHandle CreateAndUpdateSystem<T>() where T : unmanaged, ISystem
        {
            var handle = World.CreateSystem<T>();
            ref var stateRef = ref World.Unmanaged.ResolveSystemStateRef(handle);
            World.Unmanaged.GetUnsafeSystemRef<T>(handle).OnUpdate(ref stateRef);
            stateRef.Dependency.Complete();
            return handle;
        }
    }
}
