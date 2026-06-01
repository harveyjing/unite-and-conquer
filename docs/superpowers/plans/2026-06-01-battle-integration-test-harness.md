# Battle Integration Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an EditMode test harness that drives the whole server battle pipeline across many ticks, so the emergent freeze/starvation bug class becomes a red test on the exact tick it breaks — plus close the `DeathSystem` and `BattleSpawnSystem` coverage holes.

**Architecture:** Extend `EcsTestsBase` (already a bare-`World` EditMode base) with a harness that creates the real `SimulationSystemGroup`, adds the six continuous server systems, sorts by their existing `[UpdateAfter]` attributes, and ticks the group while advancing `Time` and `NetworkTime.ServerTick`. Tests assert bounded properties: the battle *resolves within a budget* and *never stalls*. `BattleSpawnSystem` is ticked once (separately) to build realistic boards and gets its own coverage test.

**Tech Stack:** Unity 6000.4.1f1, Entities 1.4.x, Netcode for Entities 1.13.1, NUnit (EditMode), Burst. Tests run via Unity MCP Test Runner (EditMode); validate with `Unity_GetConsoleLogs`.

**Spec:** `docs/superpowers/specs/2026-06-01-battle-integration-test-harness-design.md`

---

## Conventions for every task

- **Running tests:** All tests are EditMode. Run them via Unity MCP — open Test Runner (EditMode) and filter to the named class, OR run all EditMode tests. After any run, call `Unity_GetConsoleLogs` and confirm zero errors/exceptions before treating a step as done. There is no `dotnet`/`pytest` CLI for this project — never use the `unity-editor` CLI.
- **Characterization vs TDD:** `DeathSystem`, `BattleSpawnSystem`, and the squad pipeline already exist and work. Their tests are *characterization/regression* tests — they are expected to **PASS on first run** (they document and lock in current behavior). The "red" phase here is a **compile failure** when a test references a harness helper not yet written; once the helper exists the test compiles and passes. Each task notes the expected state explicitly.
- **No asmdef changes:** `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef` already references `Demo, Unity.Entities, Unity.Burst, Unity.Mathematics, Unity.Collections, Unity.Transforms, Unity.NetCode` — everything the harness needs.
- **Commit message footer:** end every commit body with
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## File structure

| File | Responsibility | Tasks |
|------|----------------|-------|
| `Assets/Tests/EditMode/EcsTestsBase.cs` | Existing base — only change: add `partial` to the class declaration | 2 |
| `Assets/Tests/EditMode/EcsTestsBase.Battle.cs` | **New.** Harness partial: prefab stub, spawn helper, server-pipeline group, tick loop, counting helpers, invariants | 2, 3, 4 |
| `Assets/Tests/EditMode/DeathSystemTests.cs` | **New.** Unit tests for `DeathSystem` | 1 |
| `Assets/Tests/EditMode/BattleSpawnSystemTests.cs` | **New.** Unit test for `BattleSpawnSystem` | 2 |
| `Assets/Tests/EditMode/BattleIntegrationTests.cs` | **New.** Multi-tick integration tests | 3, 4, 5 |

---

## Task 1: DeathSystem unit tests

Closes a coverage hole and warms up the test runner. No harness needed — uses the existing `CreateAndUpdateSystem<T>()` / `CreateSoldier` helpers.

**Files:**
- Create: `Assets/Tests/EditMode/DeathSystemTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class DeathSystemTests : EcsTestsBase
    {
        [Test]
        public void DestroysSoldier_AtZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var dead = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 0f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsFalse(Manager.Exists(dead),
                "soldier at exactly 0 health must be destroyed");
        }

        [Test]
        public void DestroysSoldier_BelowZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var dead = CreateSoldier(squad, slot: 0, pos: float3.zero, health: -5f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsFalse(Manager.Exists(dead),
                "soldier below 0 health must be destroyed");
        }

        [Test]
        public void KeepsSoldier_AboveZeroHealth()
        {
            CreateBattleConfig();
            var squad = CreateSquad(0, 1, 1, 1f, float3.zero, quaternion.identity);
            var alive = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 1f);

            CreateAndUpdateSystem<DeathSystem>();

            Assert.IsTrue(Manager.Exists(alive),
                "soldier with positive health must survive");
        }
    }
}
```

- [ ] **Step 2: Run the tests**

Run via Unity MCP: Test Runner (EditMode), filter class `DeathSystemTests`. Then call `Unity_GetConsoleLogs`.
Expected: all 3 **PASS**, zero console errors. (Characterization — `DeathSystem` already destroys `Health.Current <= 0`.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/DeathSystemTests.cs
git commit -m "test(battle): cover DeathSystem destroy/keep behavior

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Prefab-stub helper + BattleSpawnSystem coverage

Introduces the harness partial file with just the spawn helpers, then tests `BattleSpawnSystem` end-to-end (squads created, buffers wired, membership slots correct, self-disables).

**Files:**
- Modify: `Assets/Tests/EditMode/EcsTestsBase.cs` (add `partial`)
- Create: `Assets/Tests/EditMode/EcsTestsBase.Battle.cs`
- Create: `Assets/Tests/EditMode/BattleSpawnSystemTests.cs`

- [ ] **Step 1: Make `EcsTestsBase` partial**

In `Assets/Tests/EditMode/EcsTestsBase.cs`, change the class declaration line:

```csharp
    public abstract partial class EcsTestsBase
```

(Only the `partial` keyword is added; the rest of the file is unchanged.)

- [ ] **Step 2: Create the harness partial with the spawn helpers**

Create `Assets/Tests/EditMode/EcsTestsBase.Battle.cs`:

```csharp
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
    }
}
```

- [ ] **Step 3: Write the BattleSpawnSystem test**

Create `Assets/Tests/EditMode/BattleSpawnSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Demo.Tests
{
    public class BattleSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void Spawns_WiresBuffersAndMembership_AndSelfDisables()
        {
            var config = CreateBattleConfig(squadsPerTeam: 1, rows: 2, cols: 2);
            var stub = CreateSoldierPrefabStub();
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.SoldierPrefab = stub;
            Manager.SetComponentData(config, bc);

            var handle = CreateAndUpdateSystem<BattleSpawnSystem>();

            // 2 * SquadsPerTeam squads exist.
            var squadQuery = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadMember));
            Assert.AreEqual(2, squadQuery.CalculateEntityCount(), "one red + one blue squad");

            // Every slot is wired to a soldier whose membership points back consistently.
            var squads = squadQuery.ToEntityArray(Allocator.Temp);
            foreach (var sq in squads)
            {
                var buf = Manager.GetBuffer<SquadMember>(sq);
                Assert.AreEqual(4, buf.Length, "rows*cols slots");
                for (int i = 0; i < buf.Length; i++)
                {
                    var soldier = buf[i].Value;
                    Assert.AreNotEqual(Entity.Null, soldier, "no empty slot after spawn");
                    var m = Manager.GetComponentData<SquadMembership>(soldier);
                    Assert.AreEqual(sq, m.Squad, "membership points at its squad");
                    Assert.AreEqual(i, m.SlotIndex, "slot index matches buffer position");
                }
            }
            squads.Dispose();

            // System disabled itself after the one-shot spawn.
            ref var stateRef = ref World.Unmanaged.ResolveSystemStateRef(handle);
            Assert.IsFalse(stateRef.Enabled, "BattleSpawnSystem disables itself after spawning");
        }
    }
}
```

- [ ] **Step 4: Run the test**

Run via Unity MCP: Test Runner (EditMode), filter class `BattleSpawnSystemTests`. Then `Unity_GetConsoleLogs`.
Expected: **PASS**, zero errors. (One `Debug.Log` "BattleSpawnSystem: spawned…" line is expected and is not an error.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/EditMode/EcsTestsBase.cs Assets/Tests/EditMode/EcsTestsBase.Battle.cs Assets/Tests/EditMode/BattleSpawnSystemTests.cs
git commit -m "test(battle): cover BattleSpawnSystem wiring + add prefab-stub harness helper

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Server-pipeline harness + lopsided resolution test

Adds the core tick loop (real `SimulationSystemGroup`, `ServerTick`/`Time` advance, job drain) and the counting helpers, then proves a battle *resolves* with the stronger side winning.

**Files:**
- Modify: `Assets/Tests/EditMode/EcsTestsBase.Battle.cs`
- Create: `Assets/Tests/EditMode/BattleIntegrationTests.cs`

- [ ] **Step 1: Add pipeline + loop + counting helpers to the harness**

Add these fields and methods inside the `EcsTestsBase` partial in `EcsTestsBase.Battle.cs` (after the existing `SpawnViaBattleSpawnSystem` method, before the closing braces):

```csharp
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
```

- [ ] **Step 2: Write the lopsided resolution test**

Create `Assets/Tests/EditMode/BattleIntegrationTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class BattleIntegrationTests : EcsTestsBase
    {
        // Spawns a small symmetric board with the squad centers close together and a
        // brisk advance speed so the armies engage quickly inside the tick budget.
        Entity SetupSmallBoard()
        {
            var config = CreateBattleConfig(
                squadsPerTeam: 1, rows: 2, cols: 2,
                attackRange: 1.0f, dps: 60f, maxHealth: 50f,
                soldierStepSpeed: 6f, squadAdvanceSpeed: 6f, squadRotationSpeed: 8f,
                compactionIntervalTicks: 4, targetRefreshIntervalTicks: 1);
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.RedCenter  = new float3(-3f, 0f, 0f);
            bc.BlueCenter = new float3( 3f, 0f, 0f);
            Manager.SetComponentData(config, bc);
            SpawnViaBattleSpawnSystem(config);
            return config;
        }

        [Test]
        public void LopsidedBattle_ResolvesWithStrongerSideWinning()
        {
            SetupSmallBoard();
            // Handicap blue to 1 HP so red wins decisively and fast.
            SetTeamHealth(1, 1f);

            int redBefore = CountLive(0);
            Assert.Greater(redBefore, 0, "sanity: red spawned with soldiers");

            int ticks = RunUntilResolved(maxTicks: 400);

            Assert.AreNotEqual(-1, ticks, "battle must resolve within the budget (no freeze)");
            Assert.AreEqual(0, CountLive(1), "the handicapped side (blue) is eliminated");
            Assert.Greater(CountLive(0), 0, "the stronger side (red) has survivors");
        }
    }
}
```

- [ ] **Step 3: Run the test**

Run via Unity MCP: Test Runner (EditMode), filter class `BattleIntegrationTests`. Then `Unity_GetConsoleLogs`.
Expected: **PASS**, zero errors. If it returns `-1` (didn't resolve), the armies likely never closed — bump `maxTicks` and/or `squadAdvanceSpeed`/`RedCenter`/`BlueCenter` in `SetupSmallBoard`; these are tuning knobs, the asserted property (resolves, stronger side wins) is what matters.

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/EcsTestsBase.Battle.cs Assets/Tests/EditMode/BattleIntegrationTests.cs
git commit -m "test(battle): add multi-tick server-pipeline harness + lopsided resolution test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Invariants + symmetric no-freeze test

Adds the per-tick safety and liveness invariants (the freeze detector) and the symmetric battle that must terminate without stalling.

**Files:**
- Modify: `Assets/Tests/EditMode/EcsTestsBase.Battle.cs`
- Modify: `Assets/Tests/EditMode/BattleIntegrationTests.cs`

- [ ] **Step 1: Add invariant state + methods to the harness**

Add these fields and methods inside the `EcsTestsBase` partial in `EcsTestsBase.Battle.cs` (alongside the other harness members):

```csharp
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
```

- [ ] **Step 2: Add the symmetric no-freeze and explicit-safety tests**

Append these two methods inside the `BattleIntegrationTests` class in `BattleIntegrationTests.cs` (before the closing brace):

```csharp
        [Test]
        public void SymmetricBattle_NeverFreezes_AndTerminates()
        {
            SetupSmallBoard(); // even fight, full health both sides

            int ticks = RunUntilResolvedChecked(maxTicks: 600, livenessWindow: 60);

            // RunUntilResolvedChecked throws on the exact tick if liveness/safety break.
            Assert.AreNotEqual(-1, ticks,
                "symmetric battle must reach a terminal state within the budget");
            Assert.IsTrue(CountLive(0) == 0 || CountLive(1) == 0,
                "exactly one side should be eliminated at resolution");
        }

        [Test]
        public void SafetyInvariants_HoldEveryTick()
        {
            SetupSmallBoard();
            // A medium run purely to document that AssertSafety holds throughout; it is
            // invoked every tick inside RunUntilResolvedChecked and throws on violation.
            int ticks = RunUntilResolvedChecked(maxTicks: 600, livenessWindow: 600);
            Assert.AreNotEqual(-1, ticks, "battle resolved with safety invariants intact");
        }
```

- [ ] **Step 3: Run the tests**

Run via Unity MCP: Test Runner (EditMode), filter class `BattleIntegrationTests`. Then `Unity_GetConsoleLogs`.
Expected: all `BattleIntegrationTests` **PASS**, zero errors. If `SymmetricBattle` trips liveness, that is a *real freeze regression* — debug the pipeline, not the test. If it returns `-1`, widen `maxTicks` (tuning only).

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/EcsTestsBase.Battle.cs Assets/Tests/EditMode/BattleIntegrationTests.cs
git commit -m "test(battle): add per-tick safety + liveness invariants and symmetric no-freeze test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: ServerTick-parity regression + compaction-over-ticks test

Guards the two remaining spec behaviors: the battle still resolves when `ServerTick` advances by 2 (the original parity trap), and compaction shrinks rows / left-packs survivors as casualties mount across ticks.

**Files:**
- Modify: `Assets/Tests/EditMode/BattleIntegrationTests.cs`

- [ ] **Step 1: Add the parity and compaction tests**

Append these two methods inside the `BattleIntegrationTests` class in `BattleIntegrationTests.cs` (before the closing brace). Note the `using Demo;` types `Squad`/`SquadGeometry` are already in the `Demo` namespace and visible via the test asmdef's `Demo` reference.

```csharp
        [Test]
        public void Battle_Resolves_WhenServerTickAdvancesByTwo()
        {
            SetupSmallBoard();

            // tickStride = 2 reproduces the parity-constrained server tick that used to
            // starve even-index squads of compaction (the freeze). With the _phase-based
            // compaction fix the battle must still resolve without stalling.
            int ticks = RunUntilResolvedChecked(
                maxTicks: 600, livenessWindow: 60, tickStride: 2);

            Assert.AreNotEqual(-1, ticks,
                "battle must resolve even when ServerTick is parity-constrained (stride 2)");
            Assert.IsTrue(CountLive(0) == 0 || CountLive(1) == 0,
                "one side eliminated under parity-constrained ticks");
        }

        [Test]
        public void Compaction_ShrinksRows_AndLeftPacksSurvivors_AsCasualtiesMount()
        {
            var config = SetupSmallBoard();
            // Handicap blue so casualties accumulate quickly and compaction fires.
            SetTeamHealth(1, 1f);

            // Run partway, not to resolution, so we can inspect mid-battle squad state.
            RunBattle(ticks: 120);

            int cols = Manager.GetComponentData<BattleConfig>(config).SquadCols;

            var q = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadMember));
            var squads = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var sq in squads)
            {
                var buf = Manager.GetBuffer<SquadMember>(sq);

                // Count live members and verify survivors are left-packed (no live member
                // sits after a hole left by a dead/destroyed one).
                int alive = 0;
                bool sawGap = false;
                for (int i = 0; i < buf.Length; i++)
                {
                    var m = buf[i].Value;
                    bool live = m != Entity.Null && Manager.Exists(m)
                                && Manager.GetComponentData<Health>(m).Current > 0f;
                    if (live)
                    {
                        Assert.IsFalse(sawGap,
                            "survivor found after a gap — compaction failed to left-pack");
                        alive++;
                    }
                    else
                    {
                        sawGap = true;
                    }
                }

                if (alive > 0)
                {
                    int expectedRows = SquadGeometry.RowsForAliveCount(alive, cols);
                    Assert.AreEqual(expectedRows, Manager.GetComponentData<Squad>(sq).Rows,
                        "squad Rows must match RowsForAliveCount(alive, cols) after compaction");
                }
            }
            squads.Dispose();

            // Losing side should be losing whole squads over time.
            Assert.LessOrEqual(CountLiveSquads(1), CountLiveSquads(0),
                "handicapped side should not have more live squads than the winner");
        }
```

- [ ] **Step 2: Run the tests**

Run via Unity MCP: Test Runner (EditMode), filter class `BattleIntegrationTests`. Then `Unity_GetConsoleLogs`.
Expected: all `BattleIntegrationTests` **PASS**, zero errors. The compaction test inspects state right after the harness drains jobs (`CompleteAllTrackedJobs`), so buffers are settled. If `Compaction_...` sees a transient gap, increase the pre-inspection `RunBattle(ticks: …)` so a compaction cycle (interval 4) has run after the latest deaths — but a *persistent* gap is a real compaction bug.

- [ ] **Step 3: Run the full EditMode suite**

Run via Unity MCP: Test Runner (EditMode), **all** tests. Then `Unity_GetConsoleLogs`.
Expected: every test (pre-existing + the new `DeathSystemTests`, `BattleSpawnSystemTests`, `BattleIntegrationTests`) **PASS**, zero console errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/BattleIntegrationTests.cs
git commit -m "test(battle): add ServerTick-parity regression + compaction-over-ticks test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review notes (for the implementer)

- **Spec coverage:** all 7 spec test cases map to tasks — Lopsided (T3), SymmetricNeverFreezes (T4), ServerTickByTwo (T5), CompactionShrinksRows (T5), SafetyInvariants (T4), DeathSystem (T1), BattleSpawnSystem (T2). All helper-API members in the spec are implemented across T2–T4 (`CreateSoldierPrefabStub`, `SpawnViaBattleSpawnSystem`, `CreateServerPipeline`, `RunBattle`, `RunUntilResolved`, `CountLive`, `CountLiveSquads`, `AssertSafety`, `AssertLiveness`). `SetTeamHealth`/`RunUntilResolvedChecked` are harness conveniences added to keep tests DRY.
- **Tuning is expected:** integration tests assert *bounded* properties (resolves within budget, no stall, stronger side wins). Exact tick counts are never asserted, so normal `BattleConfig` tuning won't flake them. The only knobs to adjust during execution are `maxTicks` and the `SetupSmallBoard` speeds/centers — never the asserted property.
- **Distinguish test failure from real bug:** a tripped `AssertLiveness` or a persistent compaction gap is a *real regression in production code*, not a flaky test — debug the pipeline (use `superpowers:systematic-debugging`). A `-1` (didn't resolve) under a generous budget usually means the armies never engaged — a setup-tuning issue.
```
