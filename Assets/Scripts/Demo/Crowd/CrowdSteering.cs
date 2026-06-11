using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Demo
{
    // Pure stateless routing for individual soldiers (CrowdScene). No entity
    // access, no allocations — unit-tested directly, mirroring SquadGeometry.
    [BurstCompile]
    public static class CrowdSteering
    {
        // Where should a soldier at `pos` walk right now to reach `goal`?
        // - straight at the goal when no impassable region blocks the segment;
        // - otherwise via the nearest portal: its near-side endpoint first,
        //   then the far-side endpoint once the soldier is past the near one
        //   and laterally inside the corridor.
        // Re-derived from position every tick, so physics shoving can never
        // desync a stored navigation state.
        public static float3 PickWaypoint(
            float3 pos, float3 goal,
            NativeArray<TerrainRegion> regions,
            NativeArray<CrossingPortal> portals)
        {
            // No portals: nothing useful to route through, so walk at the goal
            // even if blocked — the physical terrain colliders are the backstop.
            // Otherwise go straight when no impassable region blocks the segment.
            if (portals.Length == 0 || !Blocked(pos, goal, regions))
                return goal;

            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < portals.Length; i++)
            {
                float3 mid = (portals[i].Entrance + portals[i].Exit) * 0.5f;
                float d = math.distancesq(pos.xz, mid.xz);
                if (d < bestSq) { bestSq = d; best = i; }
            }
            var portal = portals[best];

            // Endpoints are symmetric: the one closer to the goal is the far
            // side ("exit" for this soldier), the other is on its own bank.
            bool entranceIsFar =
                math.distancesq(portal.Entrance.xz, goal.xz) <
                math.distancesq(portal.Exit.xz,     goal.xz);
            float3 farEnd  = entranceIsFar ? portal.Entrance : portal.Exit;
            float3 nearEnd = entranceIsFar ? portal.Exit     : portal.Entrance;

            // Corridor frame: t = progress from nearEnd toward farEnd
            // (normalized), lateral = world-units offset off the axis.
            float2 axis  = farEnd.xz - nearEnd.xz;
            float lenSq  = math.lengthsq(axis);
            if (lenSq < 1e-8f)
                return farEnd;
            float2 rel     = pos.xz - nearEnd.xz;
            float t        = math.dot(rel, axis) / lenSq;
            float lateral  = math.length(rel - t * axis);

            // Intentionally generous: full Width (not Width/2) as the lateral
            // margin, so approaching soldiers funnel in early; the bank
            // colliders, not this check, are what actually keep them dry.
            if (t >= 0f && lateral <= portal.Width)
                return farEnd;
            return nearEnd;
        }

        static bool Blocked(float3 a, float3 b, NativeArray<TerrainRegion> regions)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                var r = regions[i];
                if (r.Passable == 0 &&
                    SquadGeometry.SegmentIntersectsBox(a, b, r.Center, r.HalfExtents, r.Yaw))
                    return true;
            }
            return false;
        }
    }
}
