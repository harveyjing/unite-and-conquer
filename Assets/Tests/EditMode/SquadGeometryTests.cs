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
            // (5-1) * 0.5 * 1.5 = 3.0 per side. 3.0 + 3.0 + 0.8 - 0.1 = 6.7.
            // Margin is subtracted so front ranks settle inside attack reach.
            var d = SquadGeometry.EngagementDistance(5, 5, 1.5f, 0.8f, 0.1f);
            Assert.AreEqual(6.7f, d, Tol);
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

        [Test]
        public void SegmentIntersectsBox_CrossesThinWall_True()
        {
            // Box centered at origin, thin in x (half 1), long in z (half 5), no yaw.
            // Segment runs along x straight through it.
            bool hit = SquadGeometry.SegmentIntersectsBox(
                new float3(-3, 0, 0), new float3(3, 0, 0),
                float3.zero, new float2(1f, 5f), 0f);
            Assert.IsTrue(hit);
        }

        [Test]
        public void SegmentIntersectsBox_LateralMiss_False()
        {
            // Same box; segment at z = 8 is north of the box's z extent (half 5).
            bool hit = SquadGeometry.SegmentIntersectsBox(
                new float3(-3, 0, 8), new float3(3, 0, 8),
                float3.zero, new float2(1f, 5f), 0f);
            Assert.IsFalse(hit);
        }

        [Test]
        public void SegmentIntersectsBox_SegmentTooShort_False()
        {
            // Segment runs along x but ends at x = -2, before reaching the box's
            // x extent (half 1, i.e. left face at x = -1). tmax=1 clamping rejects it.
            bool hit = SquadGeometry.SegmentIntersectsBox(
                new float3(-5, 0, 0), new float3(-2, 0, 0),
                float3.zero, new float2(1f, 5f), 0f);
            Assert.IsFalse(hit);
        }

        [Test]
        public void SegmentIntersectsBox_ParallelOutside_False()
        {
            // Segment runs along z at x = 5, outside the box's x extent (half 1).
            bool hit = SquadGeometry.SegmentIntersectsBox(
                new float3(5, 0, -3), new float3(5, 0, 3),
                float3.zero, new float2(1f, 5f), 0f);
            Assert.IsFalse(hit);
        }

        [Test]
        public void SegmentIntersectsBox_EndpointInside_True()
        {
            bool hit = SquadGeometry.SegmentIntersectsBox(
                float3.zero, new float3(3, 0, 0),
                float3.zero, new float2(1f, 5f), 0f);
            Assert.IsTrue(hit);
        }

        [Test]
        public void SegmentIntersectsBox_RotatedBox_True()
        {
            // Same thin-in-x box rotated 90° about Y is now long-in-x / thin-in-z.
            // A segment along z through the origin now crosses it.
            bool hit = SquadGeometry.SegmentIntersectsBox(
                new float3(0, 0, -3), new float3(0, 0, 3),
                float3.zero, new float2(1f, 5f), math.radians(90f));
            Assert.IsTrue(hit);
        }

        [Test]
        public void NarrowColsForWidth_FitsWholeColumns()
        {
            // spacing 1.5: width 2 -> floor(1.33)=1 (single file, conservative margin)
            Assert.AreEqual(1, SquadGeometry.NarrowColsForWidth(2f, 1.5f));
            // width 4 -> floor(2.67)=2
            Assert.AreEqual(2, SquadGeometry.NarrowColsForWidth(4f, 1.5f));
            // spacing 1: width 3 -> 3
            Assert.AreEqual(3, SquadGeometry.NarrowColsForWidth(3f, 1f));
        }

        [Test]
        public void NarrowColsForWidth_NeverBelowOne()
        {
            Assert.AreEqual(1, SquadGeometry.NarrowColsForWidth(0.5f, 1f));
            Assert.AreEqual(1, SquadGeometry.NarrowColsForWidth(2f, 0f)); // bad spacing guard
        }
    }
}
