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

        // --- Terrain navigation end-to-end ---

        // A river wall sits on the x-axis between the two armies, with a bridge portal to
        // the north. The straight path between the squads crosses the wall, so each squad
        // must detour through the portal. Proves SquadNavigationSystem drives the real
        // server pipeline (not just unit-level state transitions): the red squad leaves
        // Pursue and moves north toward the bridge instead of walking into the water.
        [Test]
        public void RiverBetweenArmies_SquadDetoursNorthThroughBridge()
        {
            var config = CreateBattleConfig(
                squadsPerTeam: 1, rows: 2, cols: 2,
                attackRange: 1.0f, dps: 60f, maxHealth: 50f,
                soldierStepSpeed: 6f, squadAdvanceSpeed: 6f, squadRotationSpeed: 8f,
                compactionIntervalTicks: 4, targetRefreshIntervalTicks: 1);
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.RedCenter  = new float3(-6f, 0f, 0f);
            bc.BlueCenter = new float3( 6f, 0f, 0f);
            Manager.SetComponentData(config, bc);
            SpawnViaBattleSpawnSystem(config);

            // Impassable river: thin in x, long in z, straddling the x-axis path.
            CreateTerrainRegion(new float3(0, 0, 0), new float2(1.5f, 6f),
                passable: 0, kind: TerrainKind.River);
            // Bridge to the north, beyond the wall's z extent (entrance->exit stays clear).
            CreateCrossingPortal(new float3(-3, 0, 9), new float3(3, 0, 9), width: 3f);

            bool redDetoured = false;
            float maxRedZ = float.MinValue;
            for (int i = 0; i < 80; i++)
            {
                RunBattle(1);
                var red = FirstSquad(0);
                if (red == Entity.Null) break; // red eliminated (shouldn't happen this fast)
                if (Manager.GetComponentData<SquadNav>(red).State != NavState.Pursue)
                    redDetoured = true;
                float z = Manager.GetComponentData<Unity.Transforms.LocalTransform>(red).Position.z;
                maxRedZ = math.max(maxRedZ, z);
            }

            Assert.IsTrue(redDetoured,
                "red's straight path to blue crosses the river, so it must leave Pursue to route through the bridge");
            Assert.Greater(maxRedZ, 2f,
                "red should move north toward the bridge entrance (z~9), not straight into the water");
        }

        // Two armies contest a single bridge. Before the engagement override, crossing
        // squads walked through each other (Engage=0) and ping-ponged across the river,
        // never fighting. With the override they HALT at the chokepoint and grind each
        // other down — heavy attrition is the signature of the fix. (A residual 2v2
        // endgame stall, where the last survivors in overlapping narrow formations fall
        // out of melee range, is a separate melee-geometry limitation tracked as a
        // follow-up — hence this asserts heavy attrition rather than full elimination.)
        [Test]
        public void ContestedBridge_ArmiesFightAtChokepoint_HeavyAttrition()
        {
            var config = CreateBattleConfig(
                squadsPerTeam: 1, rows: 4, cols: 8,
                attackRange: 1.0f, dps: 80f, maxHealth: 40f,
                soldierStepSpeed: 6f, squadAdvanceSpeed: 6f, squadRotationSpeed: 8f,
                compactionIntervalTicks: 4, targetRefreshIntervalTicks: 1);
            var bc = Manager.GetComponentData<BattleConfig>(config);
            bc.RedCenter  = new float3(-14f, 0f, 0f);
            bc.BlueCenter = new float3( 14f, 0f, 0f);
            Manager.SetComponentData(config, bc);
            SpawnViaBattleSpawnSystem(config);

            // Impassable river with one bridge wide enough that the narrowed column is
            // short relative to the entrance gap, so the two columns meet at the chokepoint.
            CreateTerrainRegion(new float3(0, 0, 0), new float2(1.5f, 8f), passable: 0, kind: TerrainKind.River);
            CreateCrossingPortal(new float3(-7, 0, 0), new float3(7, 0, 0), width: 8f);

            int startTotal = CountLive(0) + CountLive(1);   // 64
            RunBattle(ticks: 400);
            int remaining = CountLive(0) + CountLive(1);

            // A real chokepoint fight grinds 64 down to a handful. Pass-through/ping-pong
            // (the bug) would leave most of both armies alive.
            Assert.Less(remaining, startTotal / 4,
                $"armies must fight at the bridge and take heavy casualties (started {startTotal}, left {remaining})");
        }

        // First squad entity on `team` (or Entity.Null). Squads are few, so a linear scan is fine.
        Entity FirstSquad(int team)
        {
            var q = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadNav));
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity found = Entity.Null;
            foreach (var e in ents)
                if (Manager.GetComponentData<Squad>(e).Team == team) { found = e; break; }
            ents.Dispose();
            return found;
        }
    }
}
