using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    // Multi-tick battle integration harness. Lives as a partial of EcsTestsBase so
    // single-system unit tests keep using the original bare-World helpers unchanged.
    public abstract partial class EcsTestsBase
    {
        // An entity carrying the full soldier archetype, used as the prefab that
        // BattleSpawnSystem clones via EntityManager.Instantiate. Mirrors what
        // SoldierAuthoring bakes (minus physics, which no battle system reads).
        protected Entity CreateSoldierPrefabStub(
            float maxHealth = 50f, float attackRange = 0.8f, float dps = 25f)
        {
            var e = Manager.CreateEntity(
                typeof(Soldier), typeof(Team), typeof(SoldierColor), typeof(Health),
                typeof(AttackStats), typeof(SquadMembership),
                typeof(LocalTransform), typeof(LocalToWorld));
            Manager.SetComponentData(e, new Team { Value = 0 });
            Manager.SetComponentData(e, new SoldierColor { Value = new float4(1, 1, 1, 1) });
            Manager.SetComponentData(e, new Health { Current = maxHealth, Max = maxHealth });
            Manager.SetComponentData(e, new AttackStats { Range = attackRange, Dps = dps });
            Manager.SetComponentData(e, new SquadMembership { Squad = Entity.Null, SlotIndex = -1 });
            Manager.SetComponentData(e, LocalTransform.Identity);
            return e;
        }

        // Points BattleConfig at a freshly-created prefab stub, ticks BattleSpawnSystem
        // once to build the board, then destroys the stub so it is not counted as a
        // stray live soldier (Instantiate has already cloned it onto the real soldiers).
        protected void SpawnViaBattleSpawnSystem(Entity config)
        {
            var stub = CreateSoldierPrefabStub();
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.SoldierPrefab = stub;
            Manager.SetComponentData(config, bc);

            CreateAndUpdateSystem<BattleSpawnSystem>();

            Manager.DestroyEntity(stub);
        }

        // --- Server pipeline harness state ---
        SimulationSystemGroup _pipeline;
        Entity                _networkTime;
        uint                  _serverTick;
        double                _elapsed;

        // Creates the NetworkTime singleton SquadTargetingSystem requires, once.
        protected Entity EnsureNetworkTime()
        {
            if (_networkTime != Entity.Null && Manager.Exists(_networkTime))
                return _networkTime;
            _networkTime = CreateNetworkTime(0);
            return _networkTime;
        }

        // Builds the real server SimulationSystemGroup with the six continuous battle
        // systems and sorts them. SortSystems() honors each system's [UpdateAfter], so
        // the production execution order is exercised, not re-hardcoded here.
        protected SimulationSystemGroup CreateServerPipeline()
        {
            if (_pipeline != null) return _pipeline;
            var group = World.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(World.CreateSystem<SquadTargetingSystem>());
            group.AddSystemToUpdateList(World.CreateSystem<SquadMovementSystem>());
            group.AddSystemToUpdateList(World.CreateSystem<SoldierSlotFollowSystem>());
            group.AddSystemToUpdateList(World.CreateSystem<MeleeDamageSystem>());
            group.AddSystemToUpdateList(World.CreateSystem<DeathSystem>());
            group.AddSystemToUpdateList(World.CreateSystem<SquadCompactionSystem>());
            group.SortSystems();
            _pipeline = group;
            return group;
        }

        // Advances one tick: bumps ServerTick (by tickStride), advances Time, runs the
        // group, and drains scheduled jobs so assertions read settled data.
        void TickOnce(SimulationSystemGroup group, float dt, uint tickStride)
        {
            _serverTick += tickStride;
            Manager.SetComponentData(_networkTime,
                new NetworkTime { ServerTick = new NetworkTick(_serverTick) });
            _elapsed += dt;
            SetTime(_elapsed, dt);
            group.Update();
            Manager.CompleteAllTrackedJobs();
        }

        // Ticks the pipeline a fixed number of times.
        protected void RunBattle(int ticks, float dt = 0.1f, uint tickStride = 1)
        {
            EnsureNetworkTime();
            var group = CreateServerPipeline();
            for (int i = 0; i < ticks; i++)
                TickOnce(group, dt, tickStride);
        }

        // Ticks until one team has zero live soldiers, or the budget is exhausted.
        // Returns the tick count it took, or -1 if not resolved within maxTicks.
        protected int RunUntilResolved(int maxTicks, float dt = 0.1f, uint tickStride = 1)
        {
            EnsureNetworkTime();
            var group = CreateServerPipeline();
            for (int tick = 1; tick <= maxTicks; tick++)
            {
                TickOnce(group, dt, tickStride);
                if (CountLive(0) == 0 || CountLive(1) == 0)
                    return tick;
            }
            return -1;
        }

        // Live soldiers on a team: existing Soldier entities with positive Health.
        protected int CountLive(int team)
        {
            int count = 0;
            var q = Manager.CreateEntityQuery(typeof(Soldier), typeof(Team), typeof(Health));
            var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (Manager.GetComponentData<Team>(e).Value != team) continue;
                if (Manager.GetComponentData<Health>(e).Current > 0f) count++;
            }
            ents.Dispose();
            return count;
        }

        // Non-destroyed Squad entities on a team that still have at least one live member.
        protected int CountLiveSquads(int team)
        {
            int count = 0;
            var q = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadMember));
            var squads = q.ToEntityArray(Allocator.Temp);
            foreach (var sq in squads)
            {
                if (Manager.GetComponentData<Squad>(sq).Team != team) continue;
                var buf = Manager.GetBuffer<SquadMember>(sq);
                for (int i = 0; i < buf.Length; i++)
                {
                    var m = buf[i].Value;
                    if (m == Entity.Null) continue;
                    if (!Manager.Exists(m)) continue;
                    if (Manager.GetComponentData<Health>(m).Current > 0f) { count++; break; }
                }
            }
            squads.Dispose();
            return count;
        }

        // Overwrites the current health of every live soldier on a team (for handicaps).
        protected void SetTeamHealth(int team, float current)
        {
            var q = Manager.CreateEntityQuery(typeof(Soldier), typeof(Team), typeof(Health));
            var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (Manager.GetComponentData<Team>(e).Value != team) continue;
                var h = Manager.GetComponentData<Health>(e);
                h.Current = current;
                Manager.SetComponentData(e, h);
            }
            ents.Dispose();
        }

        // --- Invariant tracking (reset per RunUntilResolvedChecked call) ---
        int  _livenessInitialTotal;
        int  _livenessLastTotal;
        int  _livenessStaleTicks;
        bool _livenessReady;

        void ResetInvariants()
        {
            _livenessInitialTotal = CountLive(0) + CountLive(1);
            _livenessLastTotal    = _livenessInitialTotal;
            _livenessStaleTicks   = 0;
            _livenessReady        = true;
        }

        // Safety: no corrupt soldier/squad state. Cheap; called every tick.
        protected void AssertSafety()
        {
            var q = Manager.CreateEntityQuery(typeof(Soldier), typeof(Health), typeof(SquadMembership));
            var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                var h = Manager.GetComponentData<Health>(e);
                Assert.IsFalse(float.IsNaN(h.Current), "soldier health is NaN");

                if (h.Current <= 0f) continue; // dead-this-tick soldiers are cleaned up downstream

                var m = Manager.GetComponentData<SquadMembership>(e);
                if (m.Squad == Entity.Null) continue;
                Assert.IsTrue(Manager.Exists(m.Squad),
                    "live soldier references a destroyed squad");
                var buf = Manager.GetBuffer<SquadMember>(m.Squad);
                Assert.That(m.SlotIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(buf.Length),
                    "live soldier slot index is out of its squad buffer range");
            }
            ents.Dispose();
        }

        // Liveness (freeze detector): once combat has started (total < initial), the
        // total live count must strictly drop at least once per `window` ticks. A longer
        // stall means casualties stopped while both sides remain — i.e. a freeze.
        protected void AssertLiveness(int window)
        {
            int total = CountLive(0) + CountLive(1);

            if (total < _livenessLastTotal)
                _livenessStaleTicks = 0;
            else
                _livenessStaleTicks++;

            bool engaged = total < _livenessInitialTotal;
            bool bothAlive = CountLive(0) > 0 && CountLive(1) > 0;
            if (engaged && bothAlive)
            {
                Assert.LessOrEqual(_livenessStaleTicks, window,
                    $"battle stalled: no casualty for {_livenessStaleTicks} ticks " +
                    $"while both sides alive (total={total}) — freeze regression");
            }

            _livenessLastTotal = total;
        }

        // Like RunUntilResolved, but checks safety + liveness invariants every tick.
        protected int RunUntilResolvedChecked(
            int maxTicks, int livenessWindow = 60, float dt = 0.1f, uint tickStride = 1)
        {
            EnsureNetworkTime();
            var group = CreateServerPipeline();
            ResetInvariants();
            for (int tick = 1; tick <= maxTicks; tick++)
            {
                TickOnce(group, dt, tickStride);
                AssertSafety();
                AssertLiveness(livenessWindow);
                if (CountLive(0) == 0 || CountLive(1) == 0)
                    return tick;
            }
            return -1;
        }
    }
}
