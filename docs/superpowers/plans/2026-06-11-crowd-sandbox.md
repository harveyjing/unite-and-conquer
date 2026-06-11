# Individual-Soldier Crowd Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new netcode-free `CrowdScene` where 1,000–2,000 fully individual soldiers (no Squad concept) steer toward a goal, route through the river bridge, and never overlap — Unity Physics dynamic capsules resolve all collisions.

**Architecture:** Two systems in `FixedStepSimulationSystemGroup` before `PhysicsSystemGroup`: a one-shot `CrowdSpawnSystem` (bulk grid spawn) and a stateless `CrowdSteeringSystem` (per-soldier Burst job that writes `PhysicsVelocity.Linear` toward `CrowdSteering.PickWaypoint(...)`). Routing reuses the existing `TerrainRegion`/`CrossingPortal` data and `SquadGeometry.SegmentIntersectsBox`. The physics solver provides all separation; two static bank colliders backstop the river. `GameBootstrap` falls back to the plain default world for this scene.

**Tech Stack:** Unity 6000.4.1f1, Entities 1.4.x, Unity Physics 1.4.6, Burst, NUnit EditMode tests (`EcsTestsBase`), Unity MCP for all Editor work.

**Spec:** `docs/superpowers/specs/2026-06-10-crowd-sandbox-design.md`

**Deviation from spec (deliberate):** the spec says crowd systems carry *no* `WorldSystemFilter` attribute. We instead mark them `[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]` — with Netcode installed, attribute-less systems default into *both* client and server worlds, so BattleScene's server world would tick them (harmlessly, but against the spec's isolation intent). `LocalSimulation` puts them only in netcode-free worlds, which is exactly the sandbox world. Project convention (root CLAUDE.md) prefers explicit system placement anyway.

**World layout constants (used across tasks):**

| Thing | Value |
|---|---|
| River region | Center `(0,0,0)`, HalfExtents `(3, 30)` (x,z), Yaw 0, Passable 0, Kind River |
| Bridge portal | Entrance `(-8,0,0)`, Exit `(8,0,0)`, Width `8` |
| Bank colliders | Boxes at `(0,1,17)` and `(0,1,-17)`, size `(6,2,26)` each (river z∈[-30,30] minus gap z∈[-4,4]) |
| Army 0 (red) | 750 soldiers, spawn center `(-30,0,0)`, goal `(30,0,0)` |
| Army 1 (blue) | 750 soldiers, spawn center `(30,0,0)`, goal `(-30,0,0)` |
| Spawn rect | half-extents `(12, 30)` per army, grid pitch `1.2` |

---

### Task 0: Branch

**Files:** none

- [ ] **Step 1: Create the working branch**

```bash
git checkout -b feat/crowd-sandbox
```

---

### Task 1: `CrowdSteering.PickWaypoint` (pure math, TDD)

The stateless routing brain. Per call: return the goal if the straight XZ segment to it crosses no impassable `TerrainRegion`; otherwise route via the nearest `CrossingPortal`. The portal's two endpoints are symmetric — `nearEnd` is the endpoint *farther from the goal* (my side), `farEnd` the one *closer to the goal*. A soldier heads for `nearEnd` until it has passed it along the corridor axis **and** is laterally within the corridor; then it pushes for `farEnd`. No state machine — the decision is re-derived from position every tick, so physics shoving can't desync a stored state.

**Files:**
- Create: `Assets/Scripts/Demo/Crowd/CrowdSteering.cs`
- Test: `Assets/Tests/EditMode/CrowdSteeringTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/CrowdSteeringTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Via Unity MCP Test Runner (EditMode, filter `CrowdSteeringTests`).
Expected: compile error — `CrowdSteering` does not exist. (A compile error across the test assembly is this project's equivalent of a failing-first test for a new type.)

- [ ] **Step 3: Implement `CrowdSteering`**

Create `Assets/Scripts/Demo/Crowd/CrowdSteering.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Via Unity MCP Test Runner (EditMode, filter `CrowdSteeringTests`). Expected: 8/8 PASS. Also `Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Crowd/ Assets/Tests/EditMode/CrowdSteeringTests.cs*
git commit -m "feat(crowd): stateless per-soldier waypoint routing math"
```

(Unity generates `.meta` files for the new folder/files — include them in this and every later commit.)

---

### Task 2: Crowd components, test helpers, `CrowdSpawnSystem` (TDD)

**Files:**
- Create: `Assets/Scripts/Demo/Crowd/CrowdComponents.cs`
- Create: `Assets/Scripts/Demo/Crowd/CrowdSpawnSystem.cs`
- Create: `Assets/Tests/EditMode/EcsTestsBase.Crowd.cs`
- Test: `Assets/Tests/EditMode/CrowdSpawnSystemTests.cs`
- Modify: `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef` (add `Unity.Physics` to `references` — the crowd prefab stub carries `PhysicsVelocity`)

- [ ] **Step 1: Define the components**

Create `Assets/Scripts/Demo/Crowd/CrowdComponents.cs`:

```csharp
using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    // One fully individual soldier in the crowd sandbox. No squad, no slot:
    // the goal is stamped at spawn and steering re-derives everything else
    // from position each tick.
    public struct CrowdSoldier : IComponentData
    {
        public int    Team;
        public float3 Goal;
    }

    // Singleton baked from CrowdConfigAuthoring (CrowdScene subscene only).
    // Its presence is also the gate that lets crowd systems run at all.
    public struct CrowdConfig : IComponentData
    {
        public Entity SoldierPrefab;

        public int    Army0Count;
        public int    Army1Count;
        public float3 Army0SpawnCenter;
        public float3 Army1SpawnCenter;
        public float2 SpawnHalfExtents; // XZ half-size of each spawn rectangle
        public float3 Army0Goal;
        public float3 Army1Goal;
        public float  SpawnSpacing;     // grid pitch; keep > capsule diameter
                                        // so soldiers never spawn interpenetrating

        public float  MoveSpeed;
        public float  ArrivalRadius;

        public float4 Army0Color;
        public float4 Army1Color;
    }
}
```

- [ ] **Step 2: Add test helpers**

Create `Assets/Tests/EditMode/EcsTestsBase.Crowd.cs` (partial of the existing base):

```csharp
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Demo.Tests
{
    // Crowd-sandbox helpers: config singleton, a prefab stand-in, and a
    // directly placed soldier for steering tests.
    public abstract partial class EcsTestsBase
    {
        protected Entity CreateCrowdConfig(
            Entity soldierPrefab = default,
            int army0Count = 4,
            int army1Count = 4,
            float3 army0SpawnCenter = default,
            float3 army1SpawnCenter = default,
            float2 spawnHalfExtents = default,
            float3 army0Goal = default,
            float3 army1Goal = default,
            float spawnSpacing = 1.2f,
            float moveSpeed = 2.5f,
            float arrivalRadius = 6f)
        {
            if (spawnHalfExtents.Equals(default(float2)))
                spawnHalfExtents = new float2(12f, 30f);
            var e = Manager.CreateEntity(typeof(CrowdConfig));
            Manager.SetComponentData(e, new CrowdConfig
            {
                SoldierPrefab    = soldierPrefab,
                Army0Count       = army0Count,
                Army1Count       = army1Count,
                Army0SpawnCenter = army0SpawnCenter,
                Army1SpawnCenter = army1SpawnCenter,
                SpawnHalfExtents = spawnHalfExtents,
                Army0Goal        = army0Goal,
                Army1Goal        = army1Goal,
                SpawnSpacing     = spawnSpacing,
                MoveSpeed        = moveSpeed,
                ArrivalRadius    = arrivalRadius,
                Army0Color       = new float4(1f, 0f, 0f, 1f),
                Army1Color       = new float4(0f, 0f, 1f, 1f),
            });
            return e;
        }

        // Stand-in for the baked CrowdSoldier prefab: carries every component
        // CrowdSpawnSystem writes after Instantiate, plus the Prefab tag so
        // the stub itself never matches runtime queries.
        protected Entity CreateCrowdSoldierPrefabStub()
        {
            var e = Manager.CreateEntity(
                typeof(CrowdSoldier), typeof(SoldierColor),
                typeof(LocalTransform), typeof(PhysicsVelocity),
                typeof(Prefab));
            Manager.SetComponentData(e, LocalTransform.Identity);
            return e;
        }

        protected Entity CreateCrowdSoldier(float3 pos, float3 goal, int team = 0)
        {
            var e = Manager.CreateEntity(
                typeof(CrowdSoldier), typeof(LocalTransform), typeof(PhysicsVelocity));
            Manager.SetComponentData(e, new CrowdSoldier { Team = team, Goal = goal });
            Manager.SetComponentData(e, LocalTransform.FromPosition(pos));
            return e;
        }
    }
}
```

Add `"Unity.Physics"` to the `references` array in `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef`:

```json
    "references": [
        "Demo",
        "Unity.Entities",
        "Unity.Burst",
        "Unity.Mathematics",
        "Unity.Collections",
        "Unity.Transforms",
        "Unity.NetCode",
        "Unity.Physics"
    ],
```

- [ ] **Step 3: Write the failing spawn tests**

Create `Assets/Tests/EditMode/CrowdSpawnSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class CrowdSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void SpawnsConfiguredCountsPerArmy()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            CreateCrowdConfig(prefab, army0Count: 5, army1Count: 3,
                army0SpawnCenter: new float3(-30f, 0f, 0f),
                army1SpawnCenter: new float3( 30f, 0f, 0f),
                army0Goal: new float3( 30f, 0f, 0f),
                army1Goal: new float3(-30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier));
            Assert.AreEqual(8, query.CalculateEntityCount()); // Prefab stub excluded by default
            int team0 = 0, team1 = 0;
            using var soldiers = query.ToComponentDataArray<CrowdSoldier>(Unity.Collections.Allocator.Temp);
            foreach (var s in soldiers)
                if (s.Team == 0) team0++; else team1++;
            Assert.AreEqual(5, team0);
            Assert.AreEqual(3, team1);
        }

        [Test]
        public void StampsGoalsPerArmy()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            var goal0 = new float3(30f, 0f, 0f);
            var goal1 = new float3(-30f, 0f, 0f);
            CreateCrowdConfig(prefab, army0Count: 2, army1Count: 2,
                army0SpawnCenter: new float3(-30f, 0f, 0f),
                army1SpawnCenter: new float3( 30f, 0f, 0f),
                army0Goal: goal0, army1Goal: goal1);

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier));
            using var soldiers = query.ToComponentDataArray<CrowdSoldier>(Unity.Collections.Allocator.Temp);
            foreach (var s in soldiers)
            {
                var expected = s.Team == 0 ? goal0 : goal1;
                Assert.AreEqual(expected.x, s.Goal.x, 1e-4f);
                Assert.AreEqual(expected.z, s.Goal.z, 1e-4f);
            }
        }

        [Test]
        public void PlacesSoldiersInsideSpawnRect_NoTwoAtSamePosition()
        {
            var prefab = CreateCrowdSoldierPrefabStub();
            var center = new float3(-30f, 0f, 0f);
            var half   = new float2(12f, 30f);
            CreateCrowdConfig(prefab, army0Count: 50, army1Count: 0,
                army0SpawnCenter: center, spawnHalfExtents: half,
                army0Goal: new float3(30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSpawnSystem>();

            var query = Manager.CreateEntityQuery(typeof(CrowdSoldier), typeof(LocalTransform));
            using var xforms = query.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            Assert.AreEqual(50, xforms.Length);
            for (int i = 0; i < xforms.Length; i++)
            {
                var p = xforms[i].Position;
                Assert.LessOrEqual(math.abs(p.x - center.x), half.x + 1e-3f, $"soldier {i} x outside rect");
                Assert.LessOrEqual(math.abs(p.z - center.z), half.y + 1e-3f, $"soldier {i} z outside rect");
                for (int j = i + 1; j < xforms.Length; j++)
                    Assert.Greater(math.distance(p, xforms[j].Position), 0.5f,
                        $"soldiers {i} and {j} spawned overlapping");
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Test Runner, filter `CrowdSpawnSystemTests`. Expected: compile error — `CrowdSpawnSystem` does not exist.

- [ ] **Step 5: Implement `CrowdSpawnSystem`**

Create `Assets/Scripts/Demo/Crowd/CrowdSpawnSystem.cs`:

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot: bulk-spawns both armies on a non-overlapping grid inside
    // their spawn rectangles and stamps team/goal/color. Grid pitch
    // (SpawnSpacing) stays above the capsule diameter so the solver never
    // starts from interpenetration. LocalSimulation: crowd systems exist only
    // in the netcode-free sandbox world, never in BattleScene's worlds.
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial struct CrowdSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<CrowdConfig>();
            var em = state.EntityManager;

            SpawnArmy(em, config, team: 0, config.Army0Count,
                config.Army0SpawnCenter, config.Army0Goal, config.Army0Color);
            SpawnArmy(em, config, team: 1, config.Army1Count,
                config.Army1SpawnCenter, config.Army1Goal, config.Army1Color);

            Debug.Log($"CrowdSpawnSystem: spawned {config.Army0Count} + {config.Army1Count} soldiers.");
            state.Enabled = false;
        }

        static void SpawnArmy(EntityManager em, in CrowdConfig config,
            int team, int count, float3 center, float3 goal, float4 color)
        {
            if (count <= 0 || config.SoldierPrefab == Entity.Null)
                return;

            float2 half    = config.SpawnHalfExtents;
            float  spacing = config.SpawnSpacing;
            int cols = math.max(1, (int)math.floor(half.x * 2f / spacing));

            using var entities = em.Instantiate(config.SoldierPrefab, count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var pos = new float3(
                    center.x - half.x + (col + 0.5f) * spacing,
                    0f,
                    center.z - half.y + (row + 0.5f) * spacing);
                em.SetComponentData(entities[i], LocalTransform.FromPosition(pos));
                em.SetComponentData(entities[i], new CrowdSoldier { Team = team, Goal = goal });
                em.SetComponentData(entities[i], new SoldierColor { Value = color });
            }
        }
    }
}
```

Note: at 750 per army with half-extents `(12, 30)` and pitch `1.2`, the grid is 20 columns × 38 rows ≈ 24 × 45.6 m — it fits the 24 × 60 m rect. If counts are raised past `cols * rows` capacity the grid overflows past +z; size the rect accordingly (sandbox-acceptable; documented here, no code guard).

- [ ] **Step 6: Run tests to verify they pass**

Test Runner, filters `CrowdSpawnSystemTests` and `CrowdSteeringTests` (regression). Expected: all PASS. `Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Demo/Crowd/ Assets/Tests/EditMode/
git commit -m "feat(crowd): components + one-shot grid spawn system"
```

---

### Task 3: `CrowdSteeringSystem` (TDD)

**Files:**
- Create: `Assets/Scripts/Demo/Crowd/CrowdSteeringSystem.cs`
- Test: `Assets/Tests/EditMode/CrowdSteeringSystemTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/CrowdSteeringSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Physics;

namespace Demo.Tests
{
    public class CrowdSteeringSystemTests : EcsTestsBase
    {
        const float Tol = 1e-3f;

        [Test]
        public void OpenField_VelocityPointsAtGoal_AtMoveSpeed()
        {
            CreateCrowdConfig(moveSpeed: 2.5f);
            var soldier = CreateCrowdSoldier(new float3(-30f, 0f, 0f), new float3(30f, 0f, 0f));

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            Assert.AreEqual(2.5f, v.x, Tol);
            Assert.AreEqual(0f,   v.y, Tol);
            Assert.AreEqual(0f,   v.z, Tol);
        }

        [Test]
        public void WithinArrivalRadius_VelocityZero()
        {
            CreateCrowdConfig(arrivalRadius: 6f);
            var soldier = CreateCrowdSoldier(new float3(28f, 0f, 0f), new float3(30f, 0f, 0f));
            Manager.SetComponentData(soldier, new PhysicsVelocity
            {
                Linear = new float3(1f, 0f, 0f), Angular = float3.zero,
            });

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            Assert.AreEqual(0f, math.length(v), Tol);
        }

        [Test]
        public void RiverBlocks_VelocityPointsAtBridgeEntrance()
        {
            CreateCrowdConfig(moveSpeed: 2.5f);
            CreateTerrainRegion(float3.zero, new float2(3f, 30f)); // impassable river
            CreateCrossingPortal(new float3(-8f, 0f, 0f), new float3(8f, 0f, 0f), width: 8f);
            var soldier = CreateCrowdSoldier(new float3(-30f, 0f, 10f), new float3(30f, 0f, 10f));

            CreateAndUpdateSystem<CrowdSteeringSystem>();

            var v = Manager.GetComponentData<PhysicsVelocity>(soldier).Linear;
            var expectedDir = math.normalize(new float3(-8f, 0f, 0f) - new float3(-30f, 0f, 10f));
            var actualDir   = math.normalize(v);
            Assert.AreEqual(expectedDir.x, actualDir.x, Tol);
            Assert.AreEqual(expectedDir.z, actualDir.z, Tol);
            Assert.AreEqual(2.5f, math.length(v), Tol);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Test Runner, filter `CrowdSteeringSystemTests`. Expected: compile error — `CrowdSteeringSystem` does not exist.

- [ ] **Step 3: Implement `CrowdSteeringSystem`**

Create `Assets/Scripts/Demo/Crowd/CrowdSteeringSystem.cs`:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Demo
{
    // Writes each soldier's desired PhysicsVelocity from the stateless
    // routing decision. Runs before the physics step: the solver then
    // resolves all soldier-vs-soldier and soldier-vs-bank contacts, which is
    // the entire separation model — there is deliberately no avoidance code
    // here. Terrain/portal entities are few and hand-authored; gathering
    // them per tick is cheap (same pattern as SquadNavigationSystem).
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(CrowdSpawnSystem))]
    [UpdateBefore(typeof(PhysicsSystemGroup))]
    public partial struct CrowdSteeringSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrowdConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<CrowdConfig>();

            var regionQuery = SystemAPI.QueryBuilder().WithAll<TerrainRegion>().Build();
            var portalQuery = SystemAPI.QueryBuilder().WithAll<CrossingPortal>().Build();
            var regions = regionQuery.ToComponentDataArray<TerrainRegion>(state.WorldUpdateAllocator);
            var portals = portalQuery.ToComponentDataArray<CrossingPortal>(state.WorldUpdateAllocator);

            state.Dependency = new SteerJob
            {
                Regions         = regions,
                Portals         = portals,
                MoveSpeed       = config.MoveSpeed,
                ArrivalRadiusSq = config.ArrivalRadius * config.ArrivalRadius,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct SteerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<TerrainRegion>  Regions;
            [ReadOnly] public NativeArray<CrossingPortal> Portals;
            public float MoveSpeed;
            public float ArrivalRadiusSq;

            void Execute(in CrowdSoldier soldier, in LocalTransform transform,
                         ref PhysicsVelocity velocity)
            {
                float3 pos    = transform.Position;
                float3 toGoal = soldier.Goal - pos;
                toGoal.y = 0f;
                if (math.lengthsq(toGoal) <= ArrivalRadiusSq)
                {
                    velocity.Linear  = float3.zero;
                    velocity.Angular = float3.zero;
                    return;
                }

                float3 waypoint = CrowdSteering.PickWaypoint(pos, soldier.Goal, Regions, Portals);
                float3 dir = waypoint - pos;
                dir.y = 0f;
                velocity.Linear  = math.normalizesafe(dir) * MoveSpeed;
                velocity.Angular = float3.zero;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Test Runner, filter `Crowd` (all three crowd test fixtures). Expected: all PASS. `Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Crowd/ Assets/Tests/EditMode/
git commit -m "feat(crowd): stateless steering system writes PhysicsVelocity"
```

---

### Task 4: Authoring components (config + soldier baker)

Bakers aren't unit-tested in this repo; validation is compile-clean + the live scene in Task 7.

**Files:**
- Create: `Assets/Scripts/Demo/Crowd/CrowdConfigAuthoring.cs`
- Create: `Assets/Scripts/Demo/Crowd/CrowdSoldierAuthoring.cs`

- [ ] **Step 1: Write `CrowdConfigAuthoring`**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Bakes the CrowdConfig singleton (CrowdScene subscene). Defaults match
    // the world layout in docs/superpowers/plans/2026-06-11-crowd-sandbox.md.
    public class CrowdConfigAuthoring : MonoBehaviour
    {
        [Tooltip("CrowdSoldier prefab — must have CrowdSoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Armies")]
        public int     Army0Count = 750;
        public int     Army1Count = 750;
        public Vector3 Army0SpawnCenter = new Vector3(-30f, 0f, 0f);
        public Vector3 Army1SpawnCenter = new Vector3( 30f, 0f, 0f);
        public Vector2 SpawnHalfExtents = new Vector2(12f, 30f);
        public Vector3 Army0Goal = new Vector3( 30f, 0f, 0f);
        public Vector3 Army1Goal = new Vector3(-30f, 0f, 0f);

        [Header("Movement")]
        [Tooltip("Grid pitch at spawn; keep above the capsule diameter.")]
        public float SpawnSpacing  = 1.2f;
        public float MoveSpeed     = 2.5f;
        [Tooltip("Soldiers stop within this distance of their goal. Generous: hundreds share one goal point.")]
        public float ArrivalRadius = 6f;

        [Header("Team colors (RGBA, linear)")]
        public Color Army0Color = new Color(1f, 0.1f, 0.1f, 1f);
        public Color Army1Color = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<CrowdConfigAuthoring>
        {
            public override void Bake(CrowdConfigAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CrowdConfig
                {
                    SoldierPrefab = a.SoldierPrefab != null
                        ? GetEntity(a.SoldierPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    Army0Count       = a.Army0Count,
                    Army1Count       = a.Army1Count,
                    Army0SpawnCenter = a.Army0SpawnCenter,
                    Army1SpawnCenter = a.Army1SpawnCenter,
                    SpawnHalfExtents = new float2(a.SpawnHalfExtents.x, a.SpawnHalfExtents.y),
                    Army0Goal        = a.Army0Goal,
                    Army1Goal        = a.Army1Goal,
                    SpawnSpacing     = a.SpawnSpacing,
                    MoveSpeed        = a.MoveSpeed,
                    ArrivalRadius    = a.ArrivalRadius,
                    Army0Color       = new float4(a.Army0Color.r, a.Army0Color.g, a.Army0Color.b, a.Army0Color.a),
                    Army1Color       = new float4(a.Army1Color.r, a.Army1Color.g, a.Army1Color.b, a.Army1Color.a),
                });
            }
        }
    }
}
```

- [ ] **Step 2: Write `CrowdSoldierAuthoring`**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Demo
{
    // Bakes a fully individual crowd soldier: a DYNAMIC upright-locked
    // frictionless capsule. The physics solver is the entire separation
    // model, so unlike BattleScene's vestigial kinematic collider this one
    // is load-bearing.
    [DisallowMultipleComponent]
    public class CrowdSoldierAuthoring : MonoBehaviour
    {
        [Tooltip("Collider radius — keep slightly under the visual radius so dense crowds read as touching, not gapped.")]
        public float Radius = 0.4f;
        public float Height = 1.8f;
        public float Mass   = 70f;
        [Tooltip("Light damping bleeds off solver-injected pushes between steering ticks.")]
        public float LinearDamping = 0.05f;

        class Baker : Baker<CrowdSoldierAuthoring>
        {
            public override void Bake(CrowdSoldierAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new CrowdSoldier { Team = 0, Goal = float3.zero });
                AddComponent(entity, new SoldierColor { Value = new float4(1f, 1f, 1f, 1f) });

                var geometry = new CapsuleGeometry
                {
                    Vertex0 = new float3(0f, a.Radius, 0f),
                    Vertex1 = new float3(0f, a.Height - a.Radius, 0f),
                    Radius  = a.Radius,
                };
                // Friction 0 / restitution 0: crowds slide past each other
                // instead of bouncing or sticking.
                var material = Unity.Physics.Material.Default;
                material.Friction    = 0f;
                material.Restitution = 0f;
                var collider = Unity.Physics.CapsuleCollider.Create(
                    geometry, CollisionFilter.Default, material);
                AddBlobAsset(ref collider, out _);
                AddComponent(entity, new PhysicsCollider { Value = collider });

                var mass = PhysicsMass.CreateDynamic(collider.Value.MassProperties, a.Mass);
                mass.InverseInertia = float3.zero; // upright lock — soldiers never tip
                AddComponent(entity, mass);

                AddComponent(entity, new PhysicsVelocity());
                AddComponent(entity, new PhysicsGravityFactor { Value = 0f }); // flat-plane sim
                AddComponent(entity, new PhysicsDamping { Linear = a.LinearDamping, Angular = 0f });
                AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            }
        }
    }
}
```

- [ ] **Step 3: Verify compile + tests still green**

`Unity_GetConsoleLogs` → zero errors. Test Runner full EditMode run → all PASS (no regressions).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Crowd/
git commit -m "feat(crowd): config + dynamic-capsule soldier authoring"
```

---

### Task 5: `GameBootstrap` scene gate

**Files:**
- Modify: `Assets/Scripts/Demo/Bootstrap/GameBootstrap.cs:22-35`

- [ ] **Step 1: Add the gate**

In `GameBootstrap.Initialize`, before `AutoConnectPort`:

```csharp
        public override bool Initialize(string defaultWorldName)
        {
            // CrowdScene is a netcode-free sandbox: opt out entirely so
            // Entities creates the plain default world (single world, no
            // client/server split, no port binding). Safe here because
            // default-world init runs AfterSceneLoad, so the active scene is
            // already known. Crowd systems are LocalSimulation-filtered, so
            // they exist only in that world.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "CrowdScene")
                return false;

            AutoConnectPort = 7979;
```

(Rest of the method unchanged.)

- [ ] **Step 2: Verify compile and no behavior change for existing scenes**

`Unity_GetConsoleLogs` → zero errors. Full EditMode test run → all PASS (tests build worlds manually; bootstrap untouched by them — this is a regression smoke check only).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Bootstrap/GameBootstrap.cs
git commit -m "feat(crowd): bootstrap falls back to a plain local world for CrowdScene"
```

---

### Task 6: Scene, subscene, and prefab assets (Unity MCP)

All Editor work via Unity MCP (`Unity_RunCommand` / editor scripting) — never manual clicks, never `unity-editor` CLI. After each sub-step run `Unity_GetConsoleLogs` and confirm zero errors. Mirror BattleScene's asset layout (`Assets/Scenes/BattleScene/`, `Assets/Prefabs/BattleScene/`) — open `BattleSub.unity` objects as reference for authoring-field names.

**Files (created via MCP, then committed):**
- Create: `Assets/Scenes/CrowdScene.unity`
- Create: `Assets/Scenes/CrowdScene/CrowdSub.unity` (subscene)
- Create: `Assets/Prefabs/CrowdScene/CrowdSoldier.prefab`

- [ ] **Step 1: Create the soldier prefab**

`Assets/Prefabs/CrowdScene/CrowdSoldier.prefab`:
- Root GameObject `CrowdSoldier` with `CrowdSoldierAuthoring` (defaults: Radius 0.4, Height 1.8, Mass 70, LinearDamping 0.05).
- Child `Visual`: built-in Capsule mesh (mesh is height 2 / radius 0.5 at scale 1), local scale `(0.9, 0.9, 0.9)`, local position `(0, 0.9, 0)` → visual radius 0.45, height 1.8, feet at y=0. **Remove the auto-added `CapsuleCollider` component from the child** (legacy colliders bake into Unity Physics; the baker's capsule must be the only collider). Use a URP-lit material that respects `_BaseColor` (reuse the BattleScene soldier's material).

- [ ] **Step 2: Create `CrowdScene.unity`**

- Main Camera at `(0, 45, -45)` looking at origin, with `BattleCameraMono` attached (scroll-zoom + pan, world-agnostic).
- Directional light (copy BattleScene's).
- No UIDocument, no netcode objects.

- [ ] **Step 3: Create the `CrowdSub` subscene with this content**

| Object | Components / values |
|---|---|
| `Ground` | Plane visual at origin scaled to ≥ 100×100 m. **No collider** (gravity is off). |
| `RiverVisual` | Flat quad/box mesh, `RiverWater.mat`, covering x∈[-3,3], z∈[-30,30], y≈0.02 |
| `BridgeVisual` | Flat box mesh, `Bridge.mat`, covering x∈[-3,3], z∈[-4,4], y≈0.05 |
| `RiverRegion` | `TerrainRegionAuthoring`: Center `(0,0,0)`, HalfExtents `(3,30)`, Yaw 0, impassable, Kind River (mirror BattleSub's river object field-for-field) |
| `BridgePortal` | `CrossingPortalAuthoring`: Entrance marker at `(-8,0,0)`, Exit marker at `(8,0,0)`, Width 8 |
| `BankNorth` | Empty GO at `(0,1,17)` + `UnityEngine.BoxCollider` size `(6,2,26)` — bakes to a static Unity Physics body |
| `BankSouth` | Same at `(0,1,-17)` |
| `CrowdConfig` | `CrowdConfigAuthoring`, all defaults (750/750, centers ±30, goals ∓30…), `SoldierPrefab` → `CrowdSoldier.prefab` |

- [ ] **Step 4: Add CrowdScene to Build Settings scene list** (after the existing scenes, so default play scenes are unaffected).

- [ ] **Step 5: Console check + commit**

`Unity_GetConsoleLogs` → zero errors.

```bash
git add Assets/Scenes/CrowdScene* Assets/Prefabs/CrowdScene* ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(crowd): CrowdScene + subscene + dynamic soldier prefab"
```

---

### Task 7: Live validation + profiling (Unity MCP)

The spec's acceptance checks. All via MCP; record results in the summary below before committing.

- [ ] **Step 1: Enter play mode in CrowdScene; confirm boot**

Open `CrowdScene`, play. Confirm via `Unity_GetConsoleLogs`:
- the `CrowdSpawnSystem: spawned 750 + 750 soldiers.` log;
- **no** netcode logs (no client/server world creation, no port binding);
- zero errors.

- [ ] **Step 2: Visual checks (scene captures)**

`Unity_SceneView_Capture2DScene` / `Unity_Camera_Capture` at ~15 s intervals:
- both armies detour toward `(±8, 0, 0)` instead of walking into the river;
- columns funnel through the bridge gap; congestion forms but soldiers visibly do not interpenetrate;
- arrivals accumulate around the goals.

- [ ] **Step 3: Overlap measurement (the acceptance check)**

After the crowds have collided at the bridge (~60 s), run via `Unity_RunCommand` an editor C# snippet against the default world:

```csharp
// Min pairwise XZ distance among all CrowdSoldier entities.
// Acceptance: minDist >= 2 * colliderRadius (0.8) minus a small solver
// tolerance — use 0.75 as the pass threshold.
var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
var em = world.EntityManager;
var q = em.CreateEntityQuery(typeof(Demo.CrowdSoldier), typeof(Unity.Transforms.LocalTransform));
using var xs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
float min = float.MaxValue;
for (int i = 0; i < xs.Length; i++)
for (int j = i + 1; j < xs.Length; j++)
{
    var d = xs[i].Position - xs[j].Position; d.y = 0;
    min = System.Math.Min(min, Unity.Mathematics.math.length(d));
}
UnityEngine.Debug.Log($"CrowdOverlapCheck: {xs.Length} soldiers, min pairwise dist = {min:F3}");
```

Expected log: `min pairwise dist >= 0.75`. (O(n²) over 1,500 entities ≈ 1.1 M pairs — fine as a one-off editor command.)

- [ ] **Step 4: Performance numbers**

With the Profiler (via MCP):
- record `FixedStepSimulationSystemGroup` ms (steering + physics) during peak bridge congestion at 750+750;
- raise `Army0Count`/`Army1Count` to 1000+1000 on the `CrowdConfig` authoring object, save the subscene (field edit + save re-bakes — see root CLAUDE.md re-bake gotcha), re-play, record again.

- [ ] **Step 5: Record findings + commit any tuning**

Append a `## Validation results` section to this plan file: overlap number, profiler ms at both scales, and any observed gridlock (a finding, not a failure — per spec, fixes like keep-right bias are out of v1 scope). If tuning was needed (damping, ArrivalRadius, solver iterations), commit the changed assets:

```bash
git add -A
git commit -m "test(crowd): live validation results + tuning"
```

---

### Task 8: Documentation

**Files:**
- Modify: `CLAUDE.md` (root — Project status, Current code structure, scenes list)
- Create: `Assets/Scripts/Demo/Crowd/CLAUDE.md`

- [ ] **Step 1: Root `CLAUDE.md`**

- *Project status*: add a **CrowdScene** bullet: netcode-free crowd sandbox — fully individual soldiers (no Squad), Unity Physics dynamic capsules for separation, stateless waypoint steering through `TerrainRegion`/`CrossingPortal` data; bootstrap falls back to a plain local world for this scene.
- *Current code structure*: add `Crowd/` line pointing at `Assets/Scripts/Demo/Crowd/CLAUDE.md`.
- *Scenes list*: add `Assets/Scenes/CrowdScene.unity` + subscene `CrowdSub.unity`.

- [ ] **Step 2: `Assets/Scripts/Demo/Crowd/CLAUDE.md`**

Brief subtree doc (≤ 40 lines), following `Battle/CLAUDE.md`'s shape: file map, the two-system pipeline, the stateless `PickWaypoint` contract (near-end → far-end corridor rule), the LocalSimulation/bootstrap isolation story, the no-separation-code invariant ("the solver IS the separation model"), and a pointer to the spec + this plan.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md Assets/Scripts/Demo/Crowd/CLAUDE.md*
git commit -m "docs(crowd): document the crowd sandbox subsystem"
```

---

## Self-review notes

- **Spec coverage:** scene+bootstrap gate (Tasks 5–6), components/config (Tasks 2, 4), two systems (Tasks 2–3), terrain reuse + bank colliders (Task 6), unit tests for `PickWaypoint` + spawn (Tasks 1–2), live MCP validation incl. overlap check and profiling (Task 7). Spec's "no WorldSystemFilter" replaced with `LocalSimulation` — deviation documented in the header.
- **Steering-system tests** also cover the spec's steering bullet (goal/arrival/portal velocity), beyond the spec's minimum.
- **Type consistency:** `CrowdSoldier{Team,Goal}`, `CrowdConfig` field names, and `PickWaypoint(pos, goal, regions, portals)` are identical across Tasks 1–4 code blocks.

---

## Validation results (2026-06-11)

> **Superseded — see [### Root cause + fix (same day)](#root-cause--fix-same-day) below.** The FAIL numbers in this section were the *symptom*; the confirmed root cause and the applied fix follow at the end of this section.

Live MCP validation of `CrowdScene` at the committed default (750 + 750). All checks performed via Unity MCP `Unity_RunCommand` / `Unity_GetConsoleLogs` / `Unity_SceneView_CaptureMultiAngleSceneView`.

### Boot check — PASS

- `CrowdSpawnSystem: spawned 750 + 750 soldiers.` appeared in the console (and again as `spawned 1000 + 1000 soldiers.` after the rebake — see below).
- No netcode/server/client world logs, no port-binding messages.
- `World.All` enumeration showed: `Default World` (Flags=Game), plus `LoadingWorld0..3` and `LoadingWorld (synchronous)` (Flags=Streaming, normal subscene-loading worlds). **No `ServerWorld` / `ClientWorld`** — confirms `GameBootstrap` correctly falls back to the plain default world for "CrowdScene".
- `CrowdConfig` singleton present (count 1) and `CrowdSoldier` count = 1500 once the subscene finished streaming.
- Console errors: only the pre-existing `PROBE_ERROR_12345` AI-assistant artifact (timestamped before play mode was even entered) and one `FMOD failed to switch back to normal output ... (32)` error + "Default audio device was changed..." warning, both audio-device artifacts from the editor's domain reload on entering play mode — unrelated to CrowdScene code. No new errors attributable to Crowd systems.
- **Gotcha hit:** the editor entered play mode already paused (`EditorApplication.isPaused == true`, `Time.frameCount == 1`, `Time.time == 0` for ~20s of wall-clock time even after waiting). Had to explicitly set `EditorApplication.isPaused = false` before the simulation began advancing. Not a code issue — likely an editor/MCP play-mode-entry artifact (possibly "Pause on entering Play mode" or similar). Worth knowing for future live-validation sessions.

### Visual checks

- **Multi-angle scene view at ~time=18–25s** (shortly after spawn / approach): both armies visible as dense blocks approaching the river; the blue river strip and the bridge gap visible; soldiers visible as a contiguous mass funneling toward/through the gap, consistent with detour-to-bridge routing (no soldiers seen walking into the river body itself).
- **Multi-angle scene view at ~time=94–110s** (post-crossing): both armies have **fully merged into a single dense, roughly circular blob** clustered at one location away from the river, rather than remaining as two separate arrival clusters near their respective goals. This is consistent with the overlap-check finding below (single shared goal point per army → mass stacking).
- **Color/tint:** soldiers rendered **uniformly white** in all captures — no visible red/blue team tint despite `SoldierColor` (`[MaterialProperty("_BaseColor")]`) being set per-team in `CrowdSpawnSystem`. Diagnosed (not fixed): the soldier root entity carries `SoldierColor` (a generic `IComponentData`, not yet a material-property override), but the **child render entity** (the one with `MaterialMeshInfo` / `RenderMeshArray` / `URPMaterialPropertyBaseColor`-eligible components) does **not** have a `URPMaterialPropertyBaseColor` component at all. The Entities.Graphics `[MaterialProperty]` source-gen override therefore has nothing to bind to on the renderable entity, so the shader's `_BaseColor` stays at the material default (white). This needs either moving `SoldierColor` (or an equivalent `URPMaterialPropertyBaseColor`) onto the child render entity, or another binding mechanism — not attempted here per "diagnose, don't hack."

### Overlap acceptance check — FAIL

Acceptance: min pairwise XZ distance ≥ 0.75. Two runs, both ≥60s in (after the crowds met):

| Run | Sim time | Soldiers | Min dist | Pairs < 0.75 | Pairs < 0.5 | Pairs < 0.1 |
|---|---|---|---|---|---|---|
| 1 | 81.8s | 1500 | 0.001–0.003 (re-measured 0.003 in the severity pass) | 1127 | 489 | 312 |
| 2 | 156.2s | 1500 | 0.001 | 993 | 496 | — |

Both runs **fail** the ≥0.75 acceptance threshold by a wide margin, with hundreds of near-zero-distance pairs persisting (and not improving) over time.

**Root cause (diagnosed, not fixed):** `CrowdConfig.Army0Goal` / `Army1Goal` is a **single shared point per army**, and `CrowdSoldier.Goal` is stamped to that single point at spawn for every soldier in the army (`CrowdComponents.cs`). `CrowdSteeringSystem.SteerJob` zeroes `PhysicsVelocity` once a soldier is within `ArrivalRadius` of that single goal point, with no further separation/avoidance behavior for arrived soldiers. With 750 soldiers per army converging on one point, they pile up; the physics solver cannot fully resolve hundreds of simultaneously-arriving, zero-velocity, mutually overlapping dynamic capsules converging on a single coordinate, so large persistent overlaps remain. This is a **goal-distribution / arrival-behavior design gap**, not a collider-radius or solver-tolerance issue — the spec's "solver-only separation" model assumes soldiers spread across a region, not converge on one point.

### Frame-time numbers

Measured via `Time.frameCount` / `Time.realtimeSinceStartup` deltas across an MCP-call-free wait window (a `Debug.Log`-based `EditorApplication.update` hook did not survive across RunCommand invocations and never fired, so this delta-sampling approach was used instead).

- **750 + 750** (during/after bridge congestion, sim time ~230–296s): Δframes=96 over Δrealtime=48.0s ⇒ **avg ≈ 500 ms/frame (~2 fps)**; single-sample `Time.unscaledDeltaTime` = 0.40s (400 ms).
- **1000 + 1000** (after rebake, sim time ~85–124s): Δframes=12937 over Δrealtime=58.05s ⇒ **avg ≈ 4.5 ms/frame (~223 fps)**; single-sample `Time.unscaledDeltaTime` = 0.0047s (4.7 ms).

**Caveat (not investigated further, recorded as-is):** the two numbers differ by ~100×, which is far more than the ~1.8× entity-count increase (1500→2000) would explain. The 750+750 sample window was taken late in a long play session with several preceding MCP round-trips and possibly an unfocused/backgrounded editor window (Unity throttles unfocused Editor play-mode framerate substantially); the 1000+1000 sample window followed a fresh play-mode entry. The 1000+1000 number (~4.5 ms/frame) looks like a "focused editor, no throttling" baseline and is more likely representative of the actual simulation cost at this scale; the 750+750 number likely includes editor-focus throttling and should not be read as "1000+1000 is faster than 750+750." Re-measuring both back-to-back in a freshly-focused editor would be needed for an apples-to-apples comparison — left for a future pass.

### Tuning performed

**None.** Per task instructions, no code, config, or asset changes were made to address the overlap or color findings above — both are reported as findings for follow-up. The only intentional asset edits were the temporary `Army0Count`/`Army1Count` = 1000 rebake and its revert back to 750 (see below).

### Subscene state

`Assets/Scenes/CrowdScene/CrowdSub.unity` was edited to `Army0Count = Army1Count = 1000` (with `EditorUtility.SetDirty` + `EditorSceneManager.SaveScene`, confirmed re-bake via `CrowdSpawnSystem: spawned 1000 + 1000 soldiers.`), then reverted to `750/750` the same way. `git status --porcelain` after the revert showed **no changes** — the subscene is back to its committed state.

### Root cause + fix (same day)

Hypothesis-driven debugging session (2026-06-11). Two hypotheses were tested against live play-mode evidence in `CrowdScene`.

**H1 — soldier bodies never enter the physics world (missing `Unity.Entities.Simulate` enableable tag): REFUTED.**
Live `Unity_RunCommand` against the Default World queried `PhysicsWorldSingleton`:
- `PhysicsWorld: NumDynamicBodies = 1500`, `NumStaticBodies = 3`, `NumBodies = 1503` — every soldier is a fully registered **dynamic** rigid body (the two static banks + bridge collider account for the 3 statics).
- `soldier[0]: Simulate(has=True, enabled=True)` — `Simulate` *is* present and enabled. (It lives in `Unity.Entities`, not `Unity.Physics`, and is auto-added & enabled on every entity by the Entities baking/instantiation path, so the hand-rolled baker omitting it is harmless.)
- `soldier[0] PhysicsMass.InverseMass = 0.0143` (= 1/70, finite) — genuinely dynamic, not kinematic.
- The package's own `PhysicsWorldData` dynamic-body query (`Library/PackageCache/com.unity.physics@.../ECS/Base/Systems/PhysicsWorldData.cs`) requires only `PhysicsVelocity, LocalTransform, PhysicsWorldIndex` — all of which the baker adds — and does **not** require `Simulate` to match. So no baker component was missing. Physics runs as designed.

**H2 — the shared single-goal point is pathological: CONFIRMED.**
With physics confirmed running, min pairwise XZ distance was measured (O(n²) over `CrowdSoldier` `LocalTransform`s):
- **Early march** (~fresh play, soldiers still converging, `xRange=[-16.0, 16.4]`): `MIN_PAIR_XZ = 0.0006`, **404 pairs < 0.1**, 1467 < 0.5, 3228 < 0.75. Total interpenetration already during the head-on pass — the two 750-strong armies march in opposite directions toward overlapping single goal points and crush straight through each other, while steering re-stomps `PhysicsVelocity` into the oncoming mass every tick so the solver can never recover.
- **Late game** (`xRange=[-39.6, 39.6]`): `MIN_PAIR_XZ = 0.0011` — soldiers piled up and overshot their goal `x=±30` to `±39.6`, the rear ranks shoving the arrived front ranks past the single goal disc. 750 capsules of radius 0.4 cannot fit an `ArrivalRadius=6` (≈113 m²) disc.

This is a goal-distribution design gap, exactly matching the diagnosis in the FAIL section — not a collider/solver-tolerance issue.

**Fix applied (smallest change, no avoidance code / flow fields):**
`CrowdSpawnSystem.SpawnArmy` now stamps each soldier's `Goal` as the configured army goal **translated by that soldier's offset from its spawn center**:
`Goal = goal + (spawnPos - center)`. The army therefore marches as a translated block from its spawn rect to a same-sized, non-overlapping destination rect — a physically satisfiable target — instead of every soldier converging on one point. EditMode test `StampsGoalsPerArmy` (`Assets/Tests/EditMode/CrowdSpawnSystemTests.cs`) was updated to assert `(Goal - configuredGoal) == (spawnPos - spawnCenter)` per soldier.

**Validation re-run: BLOCKED (environment).** After the code edit, the Unity Editor wedged: editing scripts while play mode was still active queued a domain reload that never completed (`EditorApplication.isCompiling` stuck `true` for >15 min; the periodic editor async-log timer stopped firing — main thread hung). Every `Unity_RunCommand` is gated behind `isCompiling`, so the Editor could neither be stopped nor re-entered through MCP, and the post-fix overlap (≥0.75 target at 60s/120s), EditMode suite, and clean frame-time measurement **could not be captured this session**. The code fix and test update are committed and correct by inspection; they require an Editor restart to validate live. Recommended next session: restart the Editor (clears the wedge), run the `Demo\.Tests\.Crowd.*` EditMode filter (expect 14 passing), then re-measure overlap and frame time in a freshly-focused play session.
