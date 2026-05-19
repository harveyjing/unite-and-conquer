using NUnit.Framework;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class SquadGeometryTests
    {
        const float Tol = 1e-4f;

        [Test]
        public void SlotLocalOffset_FirstSlot_FrontLeft()
        {
            // 2 rows × 3 cols, spacing 1.5 → cols are at -1.5, 0, +1.5;
            // rows are at +0.75 (front) and -0.75 (back).
            var p = SquadGeometry.SlotLocalOffset(slot: 0, rows: 2, cols: 3, spacing: 1.5f);
            Assert.AreEqual(-1.5f, p.x, Tol);
            Assert.AreEqual( 0.0f, p.y, Tol);
            Assert.AreEqual( 0.75f, p.z, Tol);
        }

        [Test]
        public void SlotLocalOffset_LastSlot_BackRight()
        {
            var p = SquadGeometry.SlotLocalOffset(slot: 5, rows: 2, cols: 3, spacing: 1.5f);
            Assert.AreEqual(+1.5f, p.x, Tol);
            Assert.AreEqual( 0.0f, p.y, Tol);
            Assert.AreEqual(-0.75f, p.z, Tol);
        }

        [Test]
        public void SlotLocalOffset_CenterColumnEvenCols_StraddlesZero()
        {
            // 1 row × 2 cols, spacing 1 → slots at x = -0.5, +0.5
            var p0 = SquadGeometry.SlotLocalOffset(0, 1, 2, 1f);
            var p1 = SquadGeometry.SlotLocalOffset(1, 1, 2, 1f);
            Assert.AreEqual(-0.5f, p0.x, Tol);
            Assert.AreEqual(+0.5f, p1.x, Tol);
        }

        [Test]
        public void EngagementDistance_Symmetric()
        {
            // Rows=5, spacing=1.5, AttackRange=0.8, margin=0.1.
            // (5-1) * 0.5 * 1.5 = 3.0 per side. 3.0 + 3.0 + 0.8 + 0.1 = 6.9.
            var d = SquadGeometry.EngagementDistance(5, 5, 1.5f, 0.8f, 0.1f);
            Assert.AreEqual(6.9f, d, Tol);
        }

        [Test]
        public void EngagementDistance_Asymmetric()
        {
            // self 3 rows, target 1 row, spacing 1.0, range 0.5, margin 0.
            // (3-1)*0.5*1 + (1-1)*0.5*1 + 0.5 + 0 = 1 + 0 + 0.5 = 1.5.
            var d = SquadGeometry.EngagementDistance(3, 1, 1f, 0.5f, 0f);
            Assert.AreEqual(1.5f, d, Tol);
        }

        [Test]
        public void RowsForAliveCount_Exact()
        {
            Assert.AreEqual(5, SquadGeometry.RowsForAliveCount(50, 10));
        }

        [Test]
        public void RowsForAliveCount_RoundsUp()
        {
            Assert.AreEqual(6, SquadGeometry.RowsForAliveCount(51, 10));
            Assert.AreEqual(1, SquadGeometry.RowsForAliveCount(1, 10));
            Assert.AreEqual(1, SquadGeometry.RowsForAliveCount(9, 10));
        }

        [Test]
        public void RowsForAliveCount_ZeroAlive_ReturnsZero()
        {
            Assert.AreEqual(0, SquadGeometry.RowsForAliveCount(0, 10));
        }
    }
}
