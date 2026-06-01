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
    }
}
