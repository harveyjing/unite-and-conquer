# Battle Terrain Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Squads route around impassable terrain (river, valley) by funnelling through a crossing portal — re-shaping into a narrow block to cross, then re-expanding — while individual soldiers keep following their formation slots.

**Architecture:** A generic `TerrainRegion` (+ `CrossingPortal`) data model authored in the `BattleSub` subscene. A new server system `SquadNavigationSystem` runs a per-squad state machine (Pursue → ApproachPortal → Crossing → Pursue), writes a `SquadMoveGoal`, and re-shapes a squad by setting `Squad.Cols`/`Rows` (no buffer rewrite — survivors are already packed). `SquadMovementSystem` is refactored into a dumb "advance toward `SquadMoveGoal`" mover. Soldiers, melee, compaction, health bars, and ownership rings are untouched.

**Tech Stack:** Unity Entities 1.4 (DOTS), Burst, Netcode for Entities, Unity.Mathematics, NUnit EditMode tests on the project's `EcsTestsBase` harness.

**Spec:** `docs/superpowers/specs/2026-06-09-battle-terrain-navigation-design.md`

---

## Conventions for every task

**Compile + console check:** This is a Unity project — there is no `dotnet build`. After editing C#, let the Editor recompile and run **Unity MCP `Unity_GetConsoleLogs`**; confirm zero compile errors before running tests. Never use the `unity-editor` CLI.

**Running EditMode tests:** There is no test-runner MCP tool. Per the project memory *"Running EditMode tests via Unity MCP"*: trigger `TestRunnerApi` for the **EditMode** mode (filter by the test class name where noted), then read the generated `TestResults.xml` and confirm the named tests pass. Tests live in `Assets/Tests/EditMode/` in the `Demo.Tests.EditMode` assembly and subclass `EcsTestsBase`.

**Commits:** Work is on branch `docs/battle-terrain-navigation-design` (already created). Commit after each task's tests pass. End commit messages with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.

---

## File Structure

**Create:**
- `Assets/Scripts/Demo/Battle/Authoring/TerrainComponents.cs` — `TerrainRegion`, `CrossingPortal`, `TerrainKind` (runtime IComponentData + enum)
- `Assets/Scripts/Demo/Battle/Authoring/TerrainRegionAuthoring.cs` — MonoBehaviour + Baker + gizmo
- `Assets/Scripts/Demo/Battle/Authoring/CrossingPortalAuthoring.cs` — MonoBehaviour + Baker + gizmo
- `Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs` — the state machine
- `Assets/Tests/EditMode/SquadNavigationSystemTests.cs`
- `Assets/Tests/EditMode/SquadMovementSystemTests.cs` (if not already present — see Task 5)

**Modify:**
- `Assets/Scripts/Demo/Battle/SquadGeometry.cs` — add `SegmentIntersectsBox`, `NarrowColsForWidth`
- `Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs` — add `SquadNav`, `SquadMoveGoal`, `NavState`
- `Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs` — consume `SquadMoveGoal`
- `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs` — add nav components to squad archetype + init
- `Assets/Tests/EditMode/EcsTestsBase.cs` — extend `CreateSquad`; add `CreateTerrainRegion`, `CreateCrossingPortal`
- `Assets/Tests/EditMode/SquadGeometryTests.cs` — tests for the two new math functions
- `Assets/Scripts/Demo/Battle/CLAUDE.md` — document the new pipeline stage + terrain (Task 9)
- `Assets/Scenes/BattleScene.unity` + `Assets/Scenes/BattleSub.unity` — author terrain + visuals (Task 8, via Unity MCP)

---

## Task 1: `SquadGeometry.SegmentIntersectsBox`

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/SquadGeometry.cs`
- Test: `Assets/Tests/EditMode/SquadGeometryTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SquadGeometryTests` (class `SquadGeometryTests`, namespace `Demo.Tests`):

```csharp
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
public void SegmentIntersectsBox_PassesBeyondEnd_False()
{
    // Same box; segment at z = 8 is north of the box's z extent (half 5).
    bool hit = SquadGeometry.SegmentIntersectsBox(
        new float3(-3, 0, 8), new float3(3, 0, 8),
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run EditMode tests filtered to `SquadGeometryTests`. Expected: the five new tests FAIL to compile / "does not contain a definition for SegmentIntersectsBox".

- [ ] **Step 3: Implement `SegmentIntersectsBox`**

In `SquadGeometry.cs`, add these methods inside the `SquadGeometry` class:

```csharp
// True if the XZ segment p0->p1 intersects the oriented box (center,
// halfExtents, yaw radians about Y). Y is ignored — terrain regions are
// vertical prisms. Transforms the segment into the box's local frame and
// runs a 2D slab test. Used by SquadNavigationSystem to decide whether a
// squad's straight path to its target is blocked.
public static bool SegmentIntersectsBox(
    float3 p0, float3 p1, float3 center, float2 halfExtents, float yaw)
{
    // Undo yaw: rotate world deltas by -yaw about Y into the box-local frame
    // where the box is axis-aligned.
    float c = math.cos(-yaw);
    float s = math.sin(-yaw);
    float2 a = WorldToLocalXZ(p0, center, c, s);
    float2 b = WorldToLocalXZ(p1, center, c, s);
    float2 d = b - a;

    float tmin = 0f;
    float tmax = 1f;

    for (int axis = 0; axis < 2; axis++)
    {
        float origin = axis == 0 ? a.x : a.y;
        float dir    = axis == 0 ? d.x : d.y;
        float half   = axis == 0 ? halfExtents.x : halfExtents.y;

        if (math.abs(dir) < 1e-8f)
        {
            // Segment parallel to this slab: reject if it lies outside.
            if (origin < -half || origin > half) return false;
        }
        else
        {
            float inv = 1f / dir;
            float t1 = (-half - origin) * inv;
            float t2 = ( half - origin) * inv;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = math.max(tmin, t1);
            tmax = math.min(tmax, t2);
            if (tmin > tmax) return false;
        }
    }
    return true;
}

// Rotate a world point's XZ offset from `center` by an already-computed
// (cos,sin) into the box-local frame. Returns (localX, localZ) as a float2.
static float2 WorldToLocalXZ(float3 p, float3 center, float cosA, float sinA)
{
    float dx = p.x - center.x;
    float dz = p.z - center.z;
    return new float2(dx * cosA - dz * sinA,
                      dx * sinA + dz * cosA);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run EditMode tests filtered to `SquadGeometryTests`. Expected: all pass (existing + 5 new). Confirm `Unity_GetConsoleLogs` shows zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/SquadGeometry.cs Assets/Tests/EditMode/SquadGeometryTests.cs
git commit -m "feat(battle): SquadGeometry.SegmentIntersectsBox for terrain path tests"
```

---

## Task 2: `SquadGeometry.NarrowColsForWidth`

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/SquadGeometry.cs`
- Test: `Assets/Tests/EditMode/SquadGeometryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run EditMode tests filtered to `SquadGeometryTests`. Expected: FAIL — "does not contain a definition for NarrowColsForWidth".

- [ ] **Step 3: Implement `NarrowColsForWidth`**

Add inside the `SquadGeometry` class:

```csharp
// Widest column count whose soldiers (one `spacing` apart) fit within a
// corridor of `width`. Conservative floor leaves roughly a half-spacing
// margin on each side, keeping the narrow block clear of the water/cliff
// edges when a squad crosses a portal. Always >= 1.
public static int NarrowColsForWidth(float width, float spacing)
{
    if (spacing <= 0f) return 1;
    int cols = (int)math.floor(width / spacing);
    return math.max(1, cols);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run EditMode tests filtered to `SquadGeometryTests`. Expected: all pass. Zero console errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/SquadGeometry.cs Assets/Tests/EditMode/SquadGeometryTests.cs
git commit -m "feat(battle): SquadGeometry.NarrowColsForWidth for portal re-shape"
```

---

## Task 3: Terrain + navigation components, and test builders

No behavior yet — just the data types and the test harness support that later tasks rely on. Verified by compile + a trivial builder round-trip test.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/TerrainComponents.cs`
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs`
- Modify: `Assets/Tests/EditMode/EcsTestsBase.cs`
- Test: `Assets/Tests/EditMode/SquadNavigationSystemTests.cs` (new file, first test only)

- [ ] **Step 1: Create the terrain components**

`Assets/Scripts/Demo/Battle/Authoring/TerrainComponents.cs`:

```csharp
using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    public enum TerrainKind : byte { River = 0, Hills = 1, Mud = 2, HighGround = 3 }

    // Generic authored terrain region. v1 only authors impassable regions
    // (Passable = 0); MoveMultiplier is reserved for Slow terrain and the
    // combat modifier for High ground is intentionally not a field yet.
    public struct TerrainRegion : IComponentData
    {
        public float3      Center;        // world XZ center (Y ignored for nav)
        public float2      HalfExtents;   // box half-size on XZ (x, z)
        public float       Yaw;           // radians about Y
        public byte        Passable;      // 0 = impassable (v1), 1 = passable
        public float       MoveMultiplier;// reserved (Slow terrain); v1 = 1
        public TerrainKind Kind;
    }

    // A crossing through an impassable region. Entrance/Exit are symmetric —
    // a squad uses whichever endpoint is on its side as the entrance.
    public struct CrossingPortal : IComponentData
    {
        public float3 Entrance;
        public float3 Exit;
        public float  Width;     // usable corridor width (metres)
    }
}
```

- [ ] **Step 2: Add the navigation components**

Append to `Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs` (inside `namespace Demo`):

```csharp
    // Server-only. The squad's terrain-navigation state machine.
    public enum NavState : byte { Pursue = 0, ApproachPortal = 1, Crossing = 2 }

    public struct SquadNav : IComponentData
    {
        public NavState State;
        public float3   Entrance;     // cached portal endpoint on our side
        public float3   Exit;         // cached portal endpoint on the far side
        public float    PortalWidth;  // cached, drives the narrow Cols
        public int      BaseCols;     // full-width Cols to restore after crossing
    }

    // Server-only. Written by SquadNavigationSystem, read by SquadMovementSystem.
    public struct SquadMoveGoal : IComponentData
    {
        public float3 Position;   // where the squad anchor should head this tick
        public byte   Engage;     // 1 = stop at EngagementDistance; 0 = walk fully there
    }
```

- [ ] **Step 3: Extend `EcsTestsBase` builders**

In `EcsTestsBase.CreateSquad`, add `SquadNav` + `SquadMoveGoal` to the archetype and initialise them. Replace the existing `CreateSquad` body's entity creation + sets with:

```csharp
        protected Entity CreateSquad(
            int team, int rows, int cols, float spacing,
            float3 position, quaternion rotation)
        {
            var e = Manager.CreateEntity(
                typeof(Squad), typeof(SquadTarget), typeof(SquadMember),
                typeof(SquadNav), typeof(SquadMoveGoal),
                typeof(LocalTransform), typeof(LocalToWorld));
            Manager.SetComponentData(e, new Squad
            {
                Team = team, Rows = rows, Cols = cols, Spacing = spacing,
            });
            Manager.SetComponentData(e, new SquadTarget { Value = Entity.Null });
            Manager.SetComponentData(e, new SquadNav { State = NavState.Pursue });
            Manager.SetComponentData(e, new SquadMoveGoal { Position = position, Engage = 0 });
            Manager.SetComponentData(e, LocalTransform.FromPositionRotation(position, rotation));
            return e;
        }
```

Add two new builders (anywhere among the other `Create*` helpers):

```csharp
        protected Entity CreateTerrainRegion(
            float3 center, float2 halfExtents, float yaw = 0f,
            byte passable = 0, TerrainKind kind = TerrainKind.River)
        {
            var e = Manager.CreateEntity(typeof(TerrainRegion));
            Manager.SetComponentData(e, new TerrainRegion
            {
                Center = center, HalfExtents = halfExtents, Yaw = yaw,
                Passable = passable, MoveMultiplier = 1f, Kind = kind,
            });
            return e;
        }

        protected Entity CreateCrossingPortal(float3 entrance, float3 exit, float width)
        {
            var e = Manager.CreateEntity(typeof(CrossingPortal));
            Manager.SetComponentData(e, new CrossingPortal
            {
                Entrance = entrance, Exit = exit, Width = width,
            });
            return e;
        }
```

- [ ] **Step 4: Write a round-trip test**

Create `Assets/Tests/EditMode/SquadNavigationSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SquadNavigationSystemTests : EcsTestsBase
    {
        [Test]
        public void CreateSquad_HasNavComponents_DefaultPursue()
        {
            var squad = CreateSquad(0, 5, 10, 1.5f, float3.zero, quaternion.identity);
            Assert.IsTrue(Manager.HasComponent<SquadNav>(squad));
            Assert.IsTrue(Manager.HasComponent<SquadMoveGoal>(squad));
            Assert.AreEqual(NavState.Pursue, Manager.GetComponentData<SquadNav>(squad).State);
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run EditMode tests filtered to `SquadNavigationSystemTests`. Expected: PASS. Also confirm the existing `SquadCompactionSystemTests` / `BattleSpawnSystemTests` still pass (the `CreateSquad` archetype changed). Zero console errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/TerrainComponents.cs \
        Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs \
        Assets/Tests/EditMode/EcsTestsBase.cs \
        Assets/Tests/EditMode/SquadNavigationSystemTests.cs
git commit -m "feat(battle): terrain + squad-nav components and test builders"
```

---

## Task 4: Wire nav components into `BattleSpawnSystem`

Squads spawned in the battle must carry the new components, initialised to Pursue.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs`
- Test: `Assets/Tests/EditMode/BattleSpawnSystemTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `BattleSpawnSystemTests` (class `BattleSpawnSystemTests : EcsTestsBase`). It spawns and then asserts a squad has `SquadNav.State == Pursue`. Use the same setup the other tests in that file use (read the file's existing first test to match how it constructs `BattleConfig` with a soldier prefab and ticks `BattleSpawnSystem`). The new assertions:

```csharp
[Test]
public void Spawn_SquadsHaveNavComponents_Pursue()
{
    // Arrange identically to the existing spawn tests in this file
    // (BattleConfig with a soldier-prefab stub + NetworkTime), then:
    CreateAndUpdateSystem<BattleSpawnSystem>();

    var squadQuery = Manager.CreateEntityQuery(typeof(Squad), typeof(SquadNav), typeof(SquadMoveGoal));
    Assert.Greater(squadQuery.CalculateEntityCount(), 0, "squads should exist");

    using var squads = squadQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
    foreach (var s in squads)
        Assert.AreEqual(NavState.Pursue, Manager.GetComponentData<SquadNav>(s).State);
}
```

> If `BattleSpawnSystemTests` has a private helper that builds the config/prefab, call it; otherwise copy the arrange block from the existing first test verbatim.

- [ ] **Step 2: Run the test to verify it fails**

Run EditMode tests filtered to `BattleSpawnSystemTests`. Expected: FAIL — squads lack `SquadNav` (archetype doesn't include it yet).

- [ ] **Step 3: Add the components to the archetype and init them**

In `BattleSpawnSystem.OnUpdate`, change the archetype:

```csharp
            var squadArch = em.CreateArchetype(
                typeof(Squad),
                typeof(SquadTarget),
                typeof(SquadMember),
                typeof(SquadNav),
                typeof(SquadMoveGoal),
                typeof(LocalTransform),
                typeof(LocalToWorld));
```

Inside the `for (int i = 0; i < squadsPerTeam; i++)` loop, after the existing red `SetComponentData(... LocalTransform ...)` line add:

```csharp
                em.SetComponentData(redSquads[i], new SquadNav { State = NavState.Pursue });
                em.SetComponentData(redSquads[i], new SquadMoveGoal { Position = redPos, Engage = 0 });
```

and after the blue `SetComponentData(... LocalTransform ...)` line add:

```csharp
                em.SetComponentData(blueSquads[i], new SquadNav { State = NavState.Pursue });
                em.SetComponentData(blueSquads[i], new SquadMoveGoal { Position = bluePos, Engage = 0 });
```

- [ ] **Step 4: Run the test to verify it passes**

Run EditMode tests filtered to `BattleSpawnSystemTests`. Expected: all pass. Zero console errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs Assets/Tests/EditMode/BattleSpawnSystemTests.cs
git commit -m "feat(battle): spawn squads with nav components"
```

---

## Task 5: Refactor `SquadMovementSystem` to consume `SquadMoveGoal`

Movement becomes a dumb "advance toward goal" mover; engagement-distance stop applies only when `Engage == 1`. This is behaviour-preserving for the no-terrain case once `SquadNavigationSystem` exists (Task 6) — but tested here in isolation by setting `SquadMoveGoal` directly.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs`
- Test: `Assets/Tests/EditMode/SquadMovementSystemTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/SquadMovementSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SquadMovementSystemTests : EcsTestsBase
    {
        [Test]
        public void Engage0_WalksTowardGoal()
        {
            CreateBattleConfig(squadAdvanceSpeed: 2f, squadRotationSpeed: 100f);
            var squad = CreateSquad(0, 1, 1, 1.5f, float3.zero,
                quaternion.LookRotationSafe(new float3(1, 0, 0), math.up()));
            Manager.SetComponentData(squad, new SquadMoveGoal
            {
                Position = new float3(10, 0, 0), Engage = 0,
            });
            SetTime(1.0, 0.5f); // dt = 0.5 -> step = 1.0

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(squad).Position;
            Assert.Greater(p.x, 0.4f, "should advance toward +x goal");
        }

        [Test]
        public void Engage1_StopsInsideEngagementDistance()
        {
            CreateBattleConfig(squadAdvanceSpeed: 2f, squadRotationSpeed: 100f,
                attackRange: 0.8f, contactMargin: 0.1f);
            var self = CreateSquad(0, 1, 1, 1.5f, float3.zero,
                quaternion.LookRotationSafe(new float3(1, 0, 0), math.up()));
            // Target squad 0.5 away; engagement distance for 1x1 vs 1x1 is
            // attackRange - margin = 0.7, so 0.5 < 0.7 -> must NOT advance.
            var target = CreateSquad(1, 1, 1, 1.5f, new float3(0.5f, 0, 0), quaternion.identity);
            Manager.SetComponentData(self, new SquadTarget { Value = target });
            Manager.SetComponentData(self, new SquadMoveGoal
            {
                Position = new float3(0.5f, 0, 0), Engage = 1,
            });
            SetTime(1.0, 0.5f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var p = Manager.GetComponentData<LocalTransform>(self).Position;
            Assert.AreEqual(0f, p.x, 1e-3f, "inside engagement range -> no advance");
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run EditMode tests filtered to `SquadMovementSystemTests`. Expected: FAIL — `SquadStepJob.Execute` doesn't take `SquadMoveGoal` yet, so movement ignores the goal (Engage0 test moves toward `SquadTarget` which is Null → no move → `p.x` stays 0 → assertion fails).

- [ ] **Step 3: Refactor `SquadMovementSystem`**

Replace the whole file with:

```csharp
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadNavigationSystem))]
    public partial struct SquadMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            float dt = SystemAPI.Time.DeltaTime;
            var squadLookup = SystemAPI.GetComponentLookup<Squad>(true);

            new SquadStepJob
            {
                AdvanceSpeed  = config.SquadAdvanceSpeed,
                RotationSpeed = config.SquadRotationSpeed,
                AttackRange   = config.AttackRange,
                ContactMargin = config.ContactMargin,
                Dt            = dt,
                SquadLookup   = squadLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SquadStepJob : IJobEntity
    {
        public float AdvanceSpeed;
        public float RotationSpeed;
        public float AttackRange;
        public float ContactMargin;
        public float Dt;

        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Squad> SquadLookup;

        public void Execute(in Squad self, in SquadMoveGoal goal,
                            in SquadTarget target, ref LocalTransform xform)
        {
            float3 toGoal = goal.Position - xform.Position;
            toGoal.y = 0f;
            float dist = math.length(toGoal);
            if (dist < 1e-4f) return;

            float3 desiredFwd = toGoal / dist;
            quaternion desiredRot = quaternion.LookRotationSafe(desiredFwd, math.up());
            float slerpT = math.saturate(RotationSpeed * Dt);
            xform.Rotation = math.slerp(xform.Rotation, desiredRot, slerpT);

            // Engagement stop applies only when chasing an enemy (Engage == 1).
            float stopDist = 0f;
            if (goal.Engage != 0
                && target.Value != Entity.Null
                && SquadLookup.HasComponent(target.Value))
            {
                int targetRows = SquadLookup[target.Value].Rows;
                stopDist = SquadGeometry.EngagementDistance(
                    self.Rows, targetRows, self.Spacing, AttackRange, ContactMargin);
            }

            if (dist <= stopDist) return;

            float3 fwd = math.mul(xform.Rotation, new float3(0, 0, 1));
            float step = AdvanceSpeed * Dt;
            float maxStep = dist - stopDist;
            step = math.min(step, maxStep);
            xform.Position += fwd * step;
        }
    }
}
```

> Note: this file now references `SquadNavigationSystem` in `[UpdateAfter(...)]`, which does not exist until Task 6. **The project will not compile until Task 6's system file is created.** Create the file stub in Task 6 Step 1 before expecting compilation. If you need this task to compile standalone, temporarily change the attribute to `[UpdateAfter(typeof(SquadTargetingSystem))]` and restore it in Task 6 — but the recommended path is to do Tasks 5 and 6 back-to-back.

- [ ] **Step 4: Run the tests to verify they pass**

After Task 6's system file exists (or with the temporary attribute), run EditMode tests filtered to `SquadMovementSystemTests`. Expected: both pass. Zero console errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs Assets/Tests/EditMode/SquadMovementSystemTests.cs
git commit -m "refactor(battle): SquadMovementSystem advances toward SquadMoveGoal"
```

---

## Task 6: `SquadNavigationSystem` — Pursue detection + goal

First slice of the state machine: detect a blocked path and switch to ApproachPortal with the goal set to the entrance; otherwise stay Pursue and head at the target. No re-shape yet.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs`
- Test: `Assets/Tests/EditMode/SquadNavigationSystemTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SquadNavigationSystemTests`:

```csharp
[Test]
public void ClearPath_StaysPursue_EngageGoalAtTarget()
{
    CreateBattleConfig();
    var self   = CreateSquad(0, 2, 2, 1.5f, new float3(-5, 0, 0), quaternion.identity);
    var target = CreateSquad(1, 2, 2, 1.5f, new float3( 5, 0, 0), quaternion.identity);
    Manager.SetComponentData(self, new SquadTarget { Value = target });

    CreateAndUpdateSystem<SquadNavigationSystem>();

    var nav  = Manager.GetComponentData<SquadNav>(self);
    var goal = Manager.GetComponentData<SquadMoveGoal>(self);
    Assert.AreEqual(NavState.Pursue, nav.State);
    Assert.AreEqual((byte)1, goal.Engage);
    Assert.AreEqual(5f, goal.Position.x, 1e-3f);
}

[Test]
public void BlockedPath_EntersApproachPortal_GoalAtNearEntrance()
{
    CreateBattleConfig();
    var self   = CreateSquad(0, 2, 2, 1.5f, new float3(-5, 0, 0), quaternion.identity);
    var target = CreateSquad(1, 2, 2, 1.5f, new float3( 5, 0, 0), quaternion.identity);
    Manager.SetComponentData(self, new SquadTarget { Value = target });

    // Impassable wall straddling the straight path: thin in x, long in z.
    CreateTerrainRegion(float3.zero, new float2(1f, 5f), yaw: 0f,
        passable: 0, kind: TerrainKind.River);
    // Bridge north of the wall: endpoints at z = 8 (outside the wall's z span).
    CreateCrossingPortal(
        entrance: new float3(-1, 0, 8), exit: new float3(1, 0, 8), width: 2f);

    CreateAndUpdateSystem<SquadNavigationSystem>();

    var nav  = Manager.GetComponentData<SquadNav>(self);
    var goal = Manager.GetComponentData<SquadMoveGoal>(self);
    Assert.AreEqual(NavState.ApproachPortal, nav.State);
    Assert.AreEqual((byte)0, goal.Engage);
    // Self at x=-5 is nearer the (-1,0,8) endpoint -> that becomes the entrance.
    Assert.AreEqual(-1f, goal.Position.x, 1e-3f);
    Assert.AreEqual( 8f, goal.Position.z, 1e-3f);
    Assert.AreEqual(2f, nav.PortalWidth, 1e-3f);
    Assert.AreEqual(2, nav.BaseCols);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run EditMode tests filtered to `SquadNavigationSystemTests`. Expected: FAIL — `SquadNavigationSystem` does not exist.

- [ ] **Step 3: Implement `SquadNavigationSystem` (Pursue + ApproachPortal entry only)**

Create `Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs`:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadTargetingSystem))]
    [UpdateBefore(typeof(SquadMovementSystem))]
    public partial struct SquadNavigationSystem : ISystem
    {
        // Distance at which the squad anchor is considered "arrived" at a waypoint.
        const float ArriveThreshold = 1.0f;

        EntityQuery _squadQuery;
        EntityQuery _regionQuery;
        EntityQuery _portalQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _squadQuery = SystemAPI.QueryBuilder()
                .WithAll<Squad, SquadNav, SquadMoveGoal, SquadTarget, LocalTransform, SquadMember>()
                .Build();
            state.RequireForUpdate(_squadQuery);
            _regionQuery = SystemAPI.QueryBuilder().WithAll<TerrainRegion>().Build();
            _portalQuery = SystemAPI.QueryBuilder().WithAll<CrossingPortal>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var regions = _regionQuery.ToComponentDataArray<TerrainRegion>(Allocator.TempJob);
            var portals = _portalQuery.ToComponentDataArray<CrossingPortal>(Allocator.TempJob);

            state.Dependency = new SquadNavJob
            {
                Regions         = regions,
                Portals         = portals,
                XformLookup     = SystemAPI.GetComponentLookup<LocalTransform>(true),
                HealthLookup    = SystemAPI.GetComponentLookup<Health>(true),
                ArriveThreshold = ArriveThreshold,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = regions.Dispose(state.Dependency);
            state.Dependency = portals.Dispose(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct SquadNavJob : IJobEntity
    {
        [Unity.Collections.ReadOnly] public NativeArray<TerrainRegion>  Regions;
        [Unity.Collections.ReadOnly] public NativeArray<CrossingPortal> Portals;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> XformLookup;
        [Unity.Collections.ReadOnly] public ComponentLookup<Health> HealthLookup;
        public float ArriveThreshold;

        public void Execute(
            ref Squad squad,
            ref SquadNav nav,
            ref SquadMoveGoal goal,
            in SquadTarget target,
            in LocalTransform xform,
            in DynamicBuffer<SquadMember> members)
        {
            float3 pos = xform.Position;

            switch (nav.State)
            {
                case NavState.ApproachPortal:
                {
                    goal.Position = nav.Entrance;
                    goal.Engage   = 0;
                    if (math.distance(pos, nav.Entrance) <= ArriveThreshold)
                    {
                        int alive = CountAlive(members);
                        int narrowCols = SquadGeometry.NarrowColsForWidth(nav.PortalWidth, squad.Spacing);
                        squad.Cols = narrowCols;
                        squad.Rows = SquadGeometry.RowsForAliveCount(alive, narrowCols);
                        nav.State  = NavState.Crossing;
                        goal.Position = nav.Exit;
                    }
                    return;
                }
                case NavState.Crossing:
                {
                    goal.Position = nav.Exit;
                    goal.Engage   = 0;
                    if (math.distance(pos, nav.Exit) <= ArriveThreshold)
                    {
                        int alive = CountAlive(members);
                        squad.Cols = nav.BaseCols;
                        squad.Rows = SquadGeometry.RowsForAliveCount(alive, nav.BaseCols);
                        nav.State  = NavState.Pursue;
                    }
                    return;
                }
                default: // Pursue
                {
                    if (target.Value == Entity.Null || !XformLookup.HasComponent(target.Value))
                    {
                        goal.Position = pos;
                        goal.Engage   = 0;
                        return;
                    }
                    float3 targetPos = XformLookup[target.Value].Position;
                    if (PathBlocked(pos, targetPos)
                        && TryPickPortal(pos, out float3 entrance, out float3 exit, out float width))
                    {
                        nav.State       = NavState.ApproachPortal;
                        nav.Entrance    = entrance;
                        nav.Exit        = exit;
                        nav.PortalWidth = width;
                        nav.BaseCols    = squad.Cols;
                        goal.Position   = entrance;
                        goal.Engage     = 0;
                    }
                    else
                    {
                        goal.Position = targetPos;
                        goal.Engage   = 1;
                    }
                    return;
                }
            }
        }

        int CountAlive(in DynamicBuffer<SquadMember> members)
        {
            int n = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var e = members[i].Value;
                if (e == Entity.Null) continue;
                if (!HealthLookup.HasComponent(e)) continue;
                if (HealthLookup[e].Current <= 0f) continue;
                n++;
            }
            return n;
        }

        bool PathBlocked(float3 from, float3 to)
        {
            for (int i = 0; i < Regions.Length; i++)
            {
                var r = Regions[i];
                if (r.Passable != 0) continue;
                if (SquadGeometry.SegmentIntersectsBox(from, to, r.Center, r.HalfExtents, r.Yaw))
                    return true;
            }
            return false;
        }

        bool TryPickPortal(float3 pos, out float3 entrance, out float3 exit, out float width)
        {
            entrance = default; exit = default; width = 0f;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Portals.Length; i++)
            {
                var p = Portals[i];
                float dA = math.distance(pos, p.Entrance);
                float dB = math.distance(pos, p.Exit);
                float near = math.min(dA, dB);
                if (near < best)
                {
                    best = near;
                    found = true;
                    if (dA <= dB) { entrance = p.Entrance; exit = p.Exit; }
                    else          { entrance = p.Exit;     exit = p.Entrance; }
                    width = p.Width;
                }
            }
            return found;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run EditMode tests filtered to `SquadNavigationSystemTests` **and** `SquadMovementSystemTests` (Task 5 now compiles since this system exists). Expected: all pass. Confirm `Unity_GetConsoleLogs` shows zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs Assets/Tests/EditMode/SquadNavigationSystemTests.cs
git commit -m "feat(battle): SquadNavigationSystem detours blocked squads to a portal"
```

---

## Task 7: `SquadNavigationSystem` — re-shape + cross + re-expand

Tests for the ApproachPortal→Crossing (re-shape narrow) and Crossing→Pursue (re-expand) transitions. The implementation already handles these (added in Task 6 Step 3) — this task locks the behaviour with tests. If a test fails, fix the system, not the test.

**Files:**
- Test: `Assets/Tests/EditMode/SquadNavigationSystemTests.cs`
- (Possibly modify: `Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs`)

- [ ] **Step 1: Write the tests**

Add to `SquadNavigationSystemTests`. Helper to fill a squad's member buffer with N live soldiers:

```csharp
void FillSquad(Entity squad, int count)
{
    var buf = Manager.GetBuffer<SquadMember>(squad);
    buf.ResizeUninitialized(count);
    for (int i = 0; i < count; i++)
    {
        var s = CreateSoldier(squad, slot: i, pos: float3.zero, health: 30f);
        buf[i] = new SquadMember { Value = s };
    }
}

[Test]
public void AtEntrance_ReshapesNarrow_EntersCrossing()
{
    CreateBattleConfig();
    var self = CreateSquad(0, 5, 10, 1.5f, new float3(-1, 0, 8), quaternion.identity);
    FillSquad(self, 20); // 20 alive
    Manager.SetComponentData(self, new SquadNav
    {
        State       = NavState.ApproachPortal,
        Entrance    = new float3(-1, 0, 8),  // squad is AT the entrance
        Exit        = new float3( 1, 0, 8),
        PortalWidth = 2f,                     // narrowCols = floor(2/1.5)=1
        BaseCols    = 10,
    });

    CreateAndUpdateSystem<SquadNavigationSystem>();

    var nav   = Manager.GetComponentData<SquadNav>(self);
    var squad = Manager.GetComponentData<Squad>(self);
    var goal  = Manager.GetComponentData<SquadMoveGoal>(self);
    Assert.AreEqual(NavState.Crossing, nav.State);
    Assert.AreEqual(1, squad.Cols, "narrow cols = floor(2/1.5)");
    Assert.AreEqual(20, squad.Rows, "20 alive in 1 col -> 20 rows");
    Assert.AreEqual(1f, goal.Position.x, 1e-3f, "goal now points at the exit");
    Assert.AreEqual(8f, goal.Position.z, 1e-3f);
}

[Test]
public void AtExit_ReExpands_ReturnsPursue()
{
    CreateBattleConfig();
    // Already narrow (Cols=1) and standing AT the exit.
    var self = CreateSquad(0, 20, 1, 1.5f, new float3(1, 0, 8), quaternion.identity);
    FillSquad(self, 20);
    Manager.SetComponentData(self, new SquadNav
    {
        State       = NavState.Crossing,
        Entrance    = new float3(-1, 0, 8),
        Exit        = new float3( 1, 0, 8),
        PortalWidth = 2f,
        BaseCols    = 10,
    });

    CreateAndUpdateSystem<SquadNavigationSystem>();

    var nav   = Manager.GetComponentData<SquadNav>(self);
    var squad = Manager.GetComponentData<Squad>(self);
    Assert.AreEqual(NavState.Pursue, nav.State);
    Assert.AreEqual(10, squad.Cols, "restored to BaseCols");
    Assert.AreEqual(2, squad.Rows, "20 alive in 10 cols -> 2 rows");
}
```

- [ ] **Step 2: Run the tests**

Run EditMode tests filtered to `SquadNavigationSystemTests`. Expected: PASS (implementation from Task 6 already covers these). If any fail, correct `SquadNavigationSystem` and re-run.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/SquadNavigationSystemTests.cs Assets/Scripts/Demo/Battle/System/SquadNavigationSystem.cs
git commit -m "test(battle): squad re-shape narrow on cross, re-expand on exit"
```

---

## Task 8: Terrain authoring MonoBehaviours + bakers

Editor-facing authoring so a designer can place terrain in the subscene. No EditMode test (baking needs a conversion world); verified by compile.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/TerrainRegionAuthoring.cs`
- Create: `Assets/Scripts/Demo/Battle/Authoring/CrossingPortalAuthoring.cs`

- [ ] **Step 1: Create `TerrainRegionAuthoring`**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Authors a TerrainRegion from this GameObject's transform (position = center,
    // Y-rotation = yaw) plus inspector half-extents. Place long-and-thin for
    // rivers/passes. v1 uses impassable regions only.
    public class TerrainRegionAuthoring : MonoBehaviour
    {
        public Vector2     HalfExtents    = new Vector2(1f, 5f); // (x, z)
        public bool        Passable       = false;
        public float       MoveMultiplier = 1f;                  // reserved (Slow terrain)
        public TerrainKind Kind           = TerrainKind.River;

        class Baker : Baker<TerrainRegionAuthoring>
        {
            public override void Bake(TerrainRegionAuthoring a)
            {
                var t = a.transform;
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new TerrainRegion
                {
                    Center         = t.position,
                    HalfExtents    = new float2(a.HalfExtents.x, a.HalfExtents.y),
                    Yaw            = math.radians(t.rotation.eulerAngles.y),
                    Passable       = (byte)(a.Passable ? 1 : 0),
                    MoveMultiplier = a.MoveMultiplier,
                    Kind           = a.Kind,
                });
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Passable ? new Color(0.4f, 0.8f, 0.4f, 0.4f)
                                    : new Color(0.2f, 0.5f, 0.9f, 0.4f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(HalfExtents.x * 2f, 0.2f, HalfExtents.y * 2f));
        }
    }
}
```

- [ ] **Step 2: Create `CrossingPortalAuthoring`**

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Demo
{
    // Authors a CrossingPortal from two child marker transforms (entrance/exit).
    // If a marker is unassigned, this GameObject's own position is used.
    public class CrossingPortalAuthoring : MonoBehaviour
    {
        public Transform Entrance;
        public Transform Exit;
        public float     Width = 2f;

        class Baker : Baker<CrossingPortalAuthoring>
        {
            public override void Bake(CrossingPortalAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                float3 entrance = a.Entrance != null ? (float3)a.Entrance.position : (float3)a.transform.position;
                float3 exit     = a.Exit     != null ? (float3)a.Exit.position     : (float3)a.transform.position;
                AddComponent(e, new CrossingPortal
                {
                    Entrance = entrance,
                    Exit     = exit,
                    Width    = a.Width,
                });
            }
        }

        void OnDrawGizmosSelected()
        {
            Vector3 en = Entrance != null ? Entrance.position : transform.position;
            Vector3 ex = Exit     != null ? Exit.position     : transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(en, 0.4f);
            Gizmos.DrawSphere(ex, 0.4f);
            Gizmos.DrawLine(en, ex);
        }
    }
}
```

- [ ] **Step 3: Verify compile**

Let the Editor recompile; run `Unity_GetConsoleLogs`. Expected: zero errors (the `Demo.asmdef` already references `Unity.Entities`; `Baker<T>` and `TransformUsageFlags` resolve).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/TerrainRegionAuthoring.cs \
        Assets/Scripts/Demo/Battle/Authoring/CrossingPortalAuthoring.cs
git commit -m "feat(battle): terrain region + crossing portal authoring"
```

---

## Task 9: Author terrain in BattleScene + visual verification (Unity MCP)

Place a river+bridge and a valley+pass in the subscene, position armies so they must cross, run Play, and confirm the behaviour. All Editor operations via Unity MCP — no manual clicks, no `unity-editor` CLI.

**Files:**
- Modify: `Assets/Scenes/BattleSub.unity` (terrain authoring + visual geometry)
- Possibly modify: `BattleConfigAuthoring` spawn centers via the Inspector values in `BattleScene` so the river sits between the two armies.

- [ ] **Step 1: Open the scene and subscene**

Via Unity MCP, open `Assets/Scenes/BattleScene.unity` and ensure the `BattleSub` subscene is open for editing. Run `Unity_GetConsoleLogs`; confirm `"success": true` and zero errors before editing.

- [ ] **Step 2: Add the river region + bridge portal**

In `BattleSub`, create:
- An empty GameObject `RiverRegion` at world `(0,0,0)`, add `TerrainRegionAuthoring`, set `HalfExtents = (2, 14)`, `Passable = false`, `Kind = River`. (Thin in x, long in z — a vertical river between the −x and +x armies.)
- An empty `BridgePortal` with `CrossingPortalAuthoring`, two child markers `Entrance` at `(-3,0,0)` and `Exit` at `(3,0,0)`, `Width = 3`.
- Visual geometry (plain GameObjects, no ECS): a flattened blue cube/plane covering the river box (`scale ≈ (4, 0.1, 28)`), and a brown cube bridge across it at z≈0 (`scale ≈ (6, 0.2, 3)`).

Save the subscene via `EditorSceneManager.SaveScene` (editing an authoring field + save forces a re-bake — see root `CLAUDE.md` *Subscene re-bake staleness*).

- [ ] **Step 3: Add the valley region + pass portal**

Create a second feature offset in z so it's clearly separate from the river (e.g. centered at `(0,0,40)` if the field is large enough, or in a separate test run): two `TerrainRegionAuthoring` hill boxes flanking a gap (`Kind = Hills`, `Passable = false`) and a `CrossingPortalAuthoring` whose entrance/exit sit just outside the gap on each side. Add hill visual geometry. Save.

> For the first verification run it is acceptable to author only the river feature and add the valley in a second pass — they exercise identical code.

- [ ] **Step 4: Position the armies across the river**

Confirm `BattleConfigAuthoring` `RedCenter`/`BlueCenter` (default `(-20,0,0)` / `(20,0,0)`) put the two armies on opposite banks of the river region at x=0. Adjust if needed and save the scene.

- [ ] **Step 5: Enter Play and verify**

Set PlayMode Tools `PlayMode Type = Client & Server`, press Play via Unity MCP. Then:
- Run `Unity_GetConsoleLogs` — confirm zero errors (ignore the known MPPM `WarnAboutBatchedTicksSystem` "Server Tick Batching" noise documented in root `CLAUDE.md`).
- Capture the scene/game view via Unity MCP at a few moments and confirm visually:
  1. Each army advances toward the bridge rather than walking into the water.
  2. At the bridge the formation narrows into a column and crosses.
  3. After crossing it re-expands to full width.
  4. The two armies meet and fight at/near the bridge (narrow fronts → chokepoint).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scenes/BattleSub.unity Assets/Scenes/BattleScene.unity
git commit -m "feat(battle): author river+bridge (and valley) terrain in BattleScene"
```

---

## Task 10: Documentation

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/CLAUDE.md`
- Modify: `CLAUDE.md` (root — one-line status update)

- [ ] **Step 1: Update the Battle subsystem CLAUDE.md**

In `Assets/Scripts/Demo/Battle/CLAUDE.md`:
- Under *Battle code structure*, note the new `TerrainComponents.cs`, the two terrain authoring files, and `SquadNavigationSystem`.
- In the *Battle system pipeline* server-order list, insert a new step between `SquadTargetingSystem` and `SquadMovementSystem`:

  > **`SquadNavigationSystem`** (`UpdateAfter(SquadTargetingSystem)`, `UpdateBefore(SquadMovementSystem)`) — per-squad state machine (Pursue → ApproachPortal → Crossing → Pursue). Reads authored `TerrainRegion`/`CrossingPortal` data; when a squad's straight path to its target crosses an impassable region it routes to the nearest portal, re-shapes to a narrow block (`Squad.Cols`/`Rows`) to cross, then re-expands. Writes `SquadMoveGoal` consumed by `SquadMovementSystem`.

- Amend the `SquadMovementSystem` bullet: it now advances the anchor toward `SquadMoveGoal.Position`, applying the engagement-distance stop only when `Engage == 1`.
- Add a short *Terrain navigation* subsection summarising the one-schema / three-consumer design and that Slow terrain + High ground are reserved (point to the spec `docs/superpowers/specs/2026-06-09-battle-terrain-navigation-design.md`).

- [ ] **Step 2: Update the root CLAUDE.md status line**

In `CLAUDE.md` *Project status*, extend the BattleScene description to mention squads now navigate around impassable terrain (river/bridge, valley) via crossing portals.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/CLAUDE.md CLAUDE.md
git commit -m "docs(battle): document terrain navigation pipeline"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Generic `TerrainRegion` schema + reserved fields → Task 3. ✓
- `CrossingPortal` (entrance/exit/width) → Task 3. ✓
- Segment-vs-oriented-box crossing test → Task 1. ✓
- `NarrowColsForWidth` → Task 2. ✓
- `SquadNav` state machine (Pursue→ApproachPortal→Crossing→Pursue) → Tasks 6, 7. ✓
- `SquadMoveGoal` + movement refactor (Engage flag, engagement stop) → Task 5. ✓
- Re-shape via `Cols`/`Rows` only (no buffer repack) → Tasks 6/7 impl + tests. ✓
- Spawn wiring → Task 4. ✓
- System ordering (targeting → nav → movement → slot-follow → melee → death → compaction) → Tasks 5 & 6 `[UpdateAfter]`/`[UpdateBefore]`. ✓
- Authoring + visuals + emergent chokepoint verification → Tasks 8, 9. ✓
- Testing strategy (pure-math + system tests on `EcsTestsBase`) → Tasks 1, 2, 5, 6, 7. ✓
- Docs → Task 10. ✓
- Out-of-scope items (Slow terrain, High ground consumers; grid planner) correctly **not** implemented. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The one cross-task compile dependency (Task 5 references `SquadNavigationSystem` before Task 6 creates it) is called out explicitly with a workaround.

**Type consistency:** `SquadNav { State, Entrance, Exit, PortalWidth, BaseCols }`, `SquadMoveGoal { Position, Engage }`, `TerrainRegion { Center, HalfExtents, Yaw, Passable, MoveMultiplier, Kind }`, `CrossingPortal { Entrance, Exit, Width }`, `NavState { Pursue, ApproachPortal, Crossing }`, `SquadGeometry.SegmentIntersectsBox`, `SquadGeometry.NarrowColsForWidth` — names used identically across Tasks 1–7. ✓
