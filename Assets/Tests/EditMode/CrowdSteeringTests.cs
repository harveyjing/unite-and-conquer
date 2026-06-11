using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Demo.Tests
{
    // Pure-math tests for the stateless per-soldier routing decision.
    // World fixture mirrors the CrowdScene layout: river box at x=0
    // (half extents 3 x 30), bridge portal entrance (-8,0,0) exit (8,0,0)
    // width 8. Red marches +x, blue marches -x.
    public class CrowdSteeringTests
    {
        const float Tol = 1e-4f;

        static readonly float3 RedGoal  = new float3( 30f, 0f, 0f);
        static readonly float3 BlueGoal = new float3(-30f, 0f, 0f);

        static NativeArray<TerrainRegion> River(Allocator alloc = Allocator.Temp)
        {
            var a = new NativeArray<TerrainRegion>(1, alloc);
            a[0] = new TerrainRegion
            {
                Center = float3.zero, HalfExtents = new float2(3f, 30f),
                Yaw = 0f, Passable = 0, MoveMultiplier = 1f, Kind = TerrainKind.River,
            };
            return a;
        }

        static NativeArray<CrossingPortal> Bridge(Allocator alloc = Allocator.Temp)
        {
            var a = new NativeArray<CrossingPortal>(1, alloc);
            a[0] = new CrossingPortal
            {
                Entrance = new float3(-8f, 0f, 0f),
                Exit     = new float3( 8f, 0f, 0f),
                Width    = 8f,
            };
            return a;
        }

        static void AssertWaypoint(float3 expected, float3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tol);
            Assert.AreEqual(expected.z, actual.z, Tol);
        }

        [Test]
        public void NoRegions_ReturnsGoal()
        {
            var regions = new NativeArray<TerrainRegion>(0, Allocator.Temp);
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(-30f, 0f, 5f), RedGoal, regions, portals);
            AssertWaypoint(RedGoal, w);
        }

        [Test]
        public void ClearPath_ReturnsGoal()
        {
            // Both endpoints on the east bank: segment never touches the river box.
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(12f, 0f, 0f), RedGoal, regions, portals);
            AssertWaypoint(RedGoal, w);
        }

        [Test]
        public void BlockedFarFromBridge_ReturnsNearSideEndpoint()
        {
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(-30f, 0f, 5f), RedGoal, regions, portals);
            AssertWaypoint(new float3(-8f, 0f, 0f), w); // entrance on the west side
        }

        [Test]
        public void BlockedFarFromBridge_OppositeArmy_ReturnsItsOwnSideEndpoint()
        {
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(30f, 0f, 5f), BlueGoal, regions, portals);
            AssertWaypoint(new float3(8f, 0f, 0f), w); // endpoints are symmetric
        }

        [Test]
        public void AtNearEndpoint_PushesToFarEndpoint()
        {
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(-8f, 0f, 0f), RedGoal, regions, portals);
            AssertWaypoint(new float3(8f, 0f, 0f), w);
        }

        [Test]
        public void MidBridge_PushesToFarEndpoint()
        {
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(0f, 0f, 0f), RedGoal, regions, portals);
            AssertWaypoint(new float3(8f, 0f, 0f), w);
        }

        [Test]
        public void PastNearEndpointButLaterallyOffCorridor_ReturnsNearEndpoint()
        {
            // x=-5 is past the entrance (t > 0) but z=12 is outside the
            // corridor width — heading for the exit from here walks into the
            // bank collider, so route back through the entrance point.
            var regions = River();
            var portals = Bridge();
            var w = CrowdSteering.PickWaypoint(new float3(-5f, 0f, 12f), RedGoal, regions, portals);
            AssertWaypoint(new float3(-8f, 0f, 0f), w);
        }

        [Test]
        public void BlockedButNoPortals_ReturnsGoal()
        {
            var regions = River();
            var portals = new NativeArray<CrossingPortal>(0, Allocator.Temp);
            var w = CrowdSteering.PickWaypoint(new float3(-30f, 0f, 5f), RedGoal, regions, portals);
            AssertWaypoint(RedGoal, w); // graceful fallback, no NaN
        }
    }
}
