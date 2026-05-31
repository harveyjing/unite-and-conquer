# Squad-Formation Battle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `BattleScene` from per-soldier nearest-enemy chase to rigid squad-based formations: squads pick targets and advance as blocks; soldiers hold their slot; front rank fights; squad compacts every ~0.33 s.

**Architecture:** A new server-only `Squad` entity owns each regiment's anchor transform, shape, and roster (via `DynamicBuffer<SquadMember>`). Soldiers reference their squad via `SquadMembership`. Five server systems replace the existing two: `SquadTargetingSystem` → `SquadMovementSystem` → `SoldierSlotFollowSystem` → modified `MeleeDamageSystem` → `DeathSystem` (unchanged) → `SquadCompactionSystem`. The physics broadphase is no longer used. Pure math (slot offset, engagement distance, compaction sizing) lives in a static `SquadGeometry` helper, kept Burst-compatible and unit-testable.

**Tech Stack:** Unity 6000.4.1f1, Entities 1.x, Burst + Jobs, Netcode for Entities (server-only entities, no replication of squad-level data). New: NUnit EditMode tests in a fresh `Demo.Tests.EditMode` asmdef.

**Reference spec:** [docs/superpowers/specs/2026-05-19-squad-formation-battle-design.md](../specs/2026-05-19-squad-formation-battle-design.md)

**Testing approach:** EditMode unit tests for pure math; EditMode system tests for four of the new systems (Targeting, Movement, SlotFollow, Compaction) using a hand-rolled `EcsTestsBase` fixture. `BattleSpawnSystem` and `MeleeDamageSystem` are covered only by the final smoke test (their fixture cost is not worth the value at this scope). After each task, confirm zero Unity console errors via `mcp__unity-mcp__Unity_GetConsoleLogs` and run the relevant EditMode tests via MCP using the helper below.

**Editor operations:** Per CLAUDE.md, every Unity Editor action goes through Unity MCP — never through manual UI clicks or the `unity-editor` CLI. Use `mcp__unity-mcp__Unity_RunCommand` with the helper scripts in the next section. Surface failures via `Unity_GetConsoleLogs` after each command.

---

## MCP Helpers

These scripts are referenced from individual tasks. Run them via `mcp__unity-mcp__Unity_RunCommand` (pass the script as the `Code` argument). Each follows the Golden Template (`internal class CommandScript : IRunCommand`).

### Helper A: Run EditMode tests filtered by class name

Use this after each task that adds a test file. Pass the test class name (e.g. `"SquadGeometryTests"`) in the `targetClass` constant. The script kicks off the EditMode runner; results land in the console with the `[TestRunner]` prefix. After invoking, wait a few seconds and call `Unity_GetConsoleLogs` to read results.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

internal class CommandScript : IRunCommand
{
    // Edit this to the class you want to run, or leave empty to run all
    // tests in Demo.Tests.EditMode.
    const string targetClass = "SquadGeometryTests";

    public void Execute(ExecutionResult result)
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Logger());
        var filter = new Filter { testMode = TestMode.EditMode };
        filter.assemblyNames = new[] { "Demo.Tests.EditMode" };
        if (!string.IsNullOrEmpty(targetClass))
            filter.groupNames = new[] { "Demo\\.Tests\\." + targetClass };
        api.Execute(new ExecutionSettings(filter));
        result.Log("[TestRunner] kicked off filter='{0}'", targetClass);
    }

    internal class Logger : ICallbacks
    {
        public void RunStarted(ITestAdaptor t) { }
        public void RunFinished(ITestResultAdaptor r)
        {
            if (r.FailCount > 0)
                Debug.LogError($"[TestRunner] DONE: {r.PassCount} passed, {r.FailCount} FAILED, {r.SkipCount} skipped");
            else
                Debug.Log($"[TestRunner] DONE: {r.PassCount} passed, {r.FailCount} failed, {r.SkipCount} skipped");
        }
        public void TestStarted(ITestAdaptor t) { }
        public void TestFinished(ITestResultAdaptor r)
        {
            if (r.TestStatus == TestStatus.Failed)
                Debug.LogError($"[TestRunner] FAILED {r.Test.FullName}: {r.Message}");
        }
    }
}
```

**Verification protocol:** call `Unity_RunCommand` with this script, wait ~3 seconds, then call `Unity_GetConsoleLogs`. Look for the `[TestRunner] DONE: N passed, 0 failed` line and zero `[TestRunner] FAILED` lines. If failures appear, fix the code and re-run.

### Helper B: Confirm BattleConfig serialized values after rename

Used in Task 4 to verify `[FormerlySerializedAs]` preserved the inspector values inside `BattleSub.unity`.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Demo;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/BattleSub.unity", OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            var auth = root.GetComponentInChildren<BattleConfigAuthoring>(true);
            if (auth == null) continue;
            result.Log(
                "SquadSpacing={0}  SoldierStepSpeed={1}  SquadsPerTeam={2}  SquadRows={3}  SquadCols={4}",
                auth.SquadSpacing, auth.SoldierStepSpeed,
                auth.SquadsPerTeam, auth.SquadRows, auth.SquadCols);
            return;
        }
        result.LogError("BattleConfigAuthoring not found in BattleSub.unity");
    }
}
```

After running, fetch console logs and verify `SquadSpacing=1.5` and `SoldierStepSpeed=2` (preserved from the prior `Spacing` and `MoveSpeed` values).

### Helper C: Open BattleScene and enter Play mode for N seconds

Used in Task 13 (smoke test). Opens the scene, plays for `playSeconds`, then stops. The console will accumulate runtime logs during play.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

internal class CommandScript : IRunCommand
{
    const float playSeconds = 12f;

    public void Execute(ExecutionResult result)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        EditorApplication.update += new StopAfter(playSeconds).Tick;
        result.Log("Entered Play mode; will exit after {0}s", playSeconds);
    }

    class StopAfter
    {
        readonly double _deadline;
        public StopAfter(float seconds)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds;
        }
        public void Tick()
        {
            if (EditorApplication.timeSinceStartup < _deadline) return;
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
            EditorApplication.update -= Tick;
            Debug.Log("[Smoke] Exited Play mode");
        }
    }
}
```

After running, wait `playSeconds + ~2 s`, then call `Unity_GetConsoleLogs` and look for `BattleSpawnSystem: spawned ...`, no exceptions, and the `[Smoke] Exited Play mode` line.

### Helper D: Tweak `SquadsPerTeam` for the scale test

Used in Task 13 step 5. Opens `BattleSub.unity`, sets a value on the authoring component, saves the scene.

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Demo;

internal class CommandScript : IRunCommand
{
    const int targetSquadsPerTeam = 4;

    public void Execute(ExecutionResult result)
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/BattleSub.unity", OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            var auth = root.GetComponentInChildren<BattleConfigAuthoring>(true);
            if (auth == null) continue;
            result.RegisterObjectModification(auth);
            auth.SquadsPerTeam = targetSquadsPerTeam;
            EditorSceneManager.SaveScene(scene);
            result.Log("Set SquadsPerTeam={0} and saved {1}", targetSquadsPerTeam, scene.path);
            return;
        }
        result.LogError("BattleConfigAuthoring not found");
    }
}
```

After the scale test, run the same helper with `targetSquadsPerTeam = 2` to revert.

---

## Task 1: Add new component types

Create the four squad-related components in one new file. Additive — no existing code is touched.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs`

- [ ] **Step 1: Create the component file**

Create `Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs` with this content:

```csharp
using Unity.Entities;

namespace Demo
{
    // Server-only. One Squad entity per regiment per team.
    public struct Squad : IComponentData
    {
        public int   Team;     // 0 = red, 1 = blue
        public int   Rows;     // mutable — shrinks during compaction
        public int   Cols;     // fixed (line width stays constant)
        public float Spacing;
    }

    // Server-only. Set by SquadTargetingSystem.
    public struct SquadTarget : IComponentData
    {
        public Entity Value;   // enemy Squad entity, or Entity.Null
    }

    // Server-only buffer on each Squad entity, indexed by slot.
    // Stale references are tolerated until the next compaction.
    // Capacity 0: this buffer is always at least Rows*Cols (50+) elements,
    // so inline storage just wastes chunk space — keep it all on the heap.
    [InternalBufferCapacity(0)]
    public struct SquadMember : IBufferElementData
    {
        public Entity Value;   // soldier entity, or Entity.Null = empty slot
    }

    // Server-only on each soldier. Replaces the removed `Target` component.
    public struct SquadMembership : IComponentData
    {
        public Entity Squad;
        public int    SlotIndex;
    }
}
```

- [ ] **Step 2: Verify Unity recompiles cleanly**

Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `success: true`, zero compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs Assets/Scripts/Demo/Battle/Authoring/SquadComponents.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add Squad components (data only)

Adds Squad, SquadTarget, SquadMember, SquadMembership — server-only
data types for the upcoming squad-formation refactor. No system reads or
writes them yet.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Set up test infrastructure

The project currently has no `.asmdef` files; all code compiles into `Assembly-CSharp`. This task introduces a `Demo` asmdef wrapping the production code and a `Demo.Tests.EditMode` asmdef wrapping the new tests, plus a minimal `EcsTestsBase` fixture for ECS system tests.

Expect this task to surface "missing reference" errors after the production asmdef is added — anything `Assembly-CSharp` implicitly resolved must now be listed explicitly. Iterate the references list until the console is clean.

**Files:**
- Create: `Assets/Scripts/Demo/Demo.asmdef`
- Create: `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/EcsTestsBase.cs`

- [ ] **Step 1: Create the production code asmdef**

Create `Assets/Scripts/Demo/Demo.asmdef` with this content:

```json
{
    "name": "Demo",
    "rootNamespace": "Demo",
    "references": [
        "Unity.Entities",
        "Unity.Entities.Hybrid",
        "Unity.Entities.Graphics",
        "Unity.Burst",
        "Unity.Mathematics",
        "Unity.Collections",
        "Unity.Transforms",
        "Unity.Physics",
        "Unity.NetCode",
        "Unity.Properties",
        "Unity.InputSystem"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Verify the production asmdef compiles**

Call `mcp__unity-mcp__Unity_GetConsoleLogs`. If errors appear, they will be missing-reference errors of the form `error CS0234: The type or namespace name 'X' does not exist in the namespace 'Y'`. For each missing namespace, add the corresponding assembly to the `references` array and re-check. Likely additions if missing: `Unity.RenderPipelines.Universal.Runtime`, `Unity.Mathematics.Extensions`, anything else the existing scripts pull in.

Iterate until `Unity_GetConsoleLogs` returns zero errors.

- [ ] **Step 3: Create the test asmdef**

Create `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef` with:

```json
{
    "name": "Demo.Tests.EditMode",
    "rootNamespace": "Demo.Tests",
    "references": [
        "Demo",
        "Unity.Entities",
        "Unity.Burst",
        "Unity.Mathematics",
        "Unity.Collections",
        "Unity.Transforms",
        "Unity.NetCode"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Create the test fixture**

Create `Assets/Tests/EditMode/EcsTestsBase.cs` with:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    // Minimal per-test ECS world. Avoids the Unity.Entities.Tests package
    // dependency. Subclasses get a fresh World + EntityManager and helpers
    // for the four entity shapes our systems read.
    public abstract class EcsTestsBase
    {
        protected World         World;
        protected EntityManager Manager;

        [SetUp]
        public virtual void SetUp()
        {
            World   = new World("Test " + GetType().Name);
            Manager = World.EntityManager;
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (World != null && World.IsCreated)
                World.Dispose();
            World   = null;
            Manager = default;
        }

        protected Entity CreateBattleConfig(
            int squadsPerTeam = 1,
            int rows = 2,
            int cols = 2,
            float spacing = 1.5f,
            float attackRange = 0.8f,
            float dps = 25f,
            float maxHealth = 50f,
            float soldierStepSpeed = 2f,
            float squadAdvanceSpeed = 2f,
            float squadRotationSpeed = 2f,
            float contactMargin = 0.1f,
            int compactionIntervalTicks = 10,
            int targetRefreshIntervalTicks = 1)
        {
            var e = Manager.CreateEntity(typeof(BattleConfig));
            Manager.SetComponentData(e, new BattleConfig
            {
                SquadsPerTeam              = squadsPerTeam,
                SquadRows                  = rows,
                SquadCols                  = cols,
                SquadSpacing               = spacing,
                SquadAdvanceSpeed          = squadAdvanceSpeed,
                SquadRotationSpeed         = squadRotationSpeed,
                ContactMargin              = contactMargin,
                CompactionIntervalTicks    = compactionIntervalTicks,
                AttackRange                = attackRange,
                Dps                        = dps,
                MaxHealth                  = maxHealth,
                SoldierStepSpeed           = soldierStepSpeed,
                TargetRefreshIntervalTicks = targetRefreshIntervalTicks,
                RedCenter                  = new float3(-5f, 0f, 0f),
                BlueCenter                 = new float3( 5f, 0f, 0f),
                RedColor                   = new float4(1f, 0f, 0f, 1f),
                BlueColor                  = new float4(0f, 0f, 1f, 1f),
                CountPerSide               = squadsPerTeam * rows * cols,
            });
            return e;
        }

        protected Entity CreateNetworkTime(uint tick = 1)
        {
            var e = Manager.CreateEntity(typeof(NetworkTime));
            Manager.SetComponentData(e, new NetworkTime
            {
                ServerTick = new NetworkTick(tick),
            });
            return e;
        }

        protected Entity CreateSquad(
            int team, int rows, int cols, float spacing,
            float3 position, quaternion rotation)
        {
            var e = Manager.CreateEntity(
                typeof(Squad), typeof(SquadTarget), typeof(SquadMember),
                typeof(LocalTransform), typeof(LocalToWorld));
            Manager.SetComponentData(e, new Squad
            {
                Team = team, Rows = rows, Cols = cols, Spacing = spacing,
            });
            Manager.SetComponentData(e, new SquadTarget { Value = Entity.Null });
            Manager.SetComponentData(e, LocalTransform.FromPositionRotation(position, rotation));
            return e;
        }

        protected Entity CreateSoldier(
            Entity squad, int slot, float3 pos,
            float health = 50f, float attackRange = 0.8f, float dps = 25f)
        {
            var e = Manager.CreateEntity(
                typeof(Soldier), typeof(Team), typeof(Health), typeof(AttackStats),
                typeof(SquadMembership), typeof(LocalTransform));
            Manager.SetComponentData(e, new Team { Value = 0 });
            Manager.SetComponentData(e, new SquadMembership { Squad = squad, SlotIndex = slot });
            Manager.SetComponentData(e, new Health { Current = health, Max = health });
            Manager.SetComponentData(e, new AttackStats { Range = attackRange, Dps = dps });
            Manager.SetComponentData(e, LocalTransform.FromPosition(pos));
            return e;
        }

        // Advance the world's time so SystemAPI.Time.DeltaTime returns `dt`.
        protected void SetTime(double elapsed, float dt)
        {
            World.SetTime(new TimeData(elapsed, dt));
        }

        // Create system, tick once, return the SystemHandle.
        protected SystemHandle CreateAndUpdateSystem<T>() where T : unmanaged, ISystem
        {
            var handle = World.CreateSystem<T>();
            World.Unmanaged.GetUnsafeSystemRef<T>(handle).OnUpdate(
                ref World.Unmanaged.ResolveSystemStateRef(handle));
            return handle;
        }
    }
}
```

If `CreateAndUpdateSystem` does not compile (Entities 1.x API drift), fall back to:
```csharp
var handle = World.CreateSystem<T>();
World.Unmanaged.ResolveSystemStateRef(handle).Update();
```
or scheduling the system in a group. The intent is "install one system, tick it once."

- [ ] **Step 5: Verify console clean and the test assembly registers**

Call `mcp__unity-mcp__Unity_GetConsoleLogs` — zero errors. Then run Helper A from the MCP Helpers section with `targetClass = ""` (run-all) via `mcp__unity-mcp__Unity_RunCommand`. Expected log: `[TestRunner] DONE: 0 passed, 0 failed, 0 skipped` — zero counts are fine here since no tests exist yet; the line proves the runner found the assembly. If you see `[TestRunner] kicked off` but no `DONE` line after ~3 s, the assembly is not registered — re-check `defineConstraints` (`UNITY_INCLUDE_TESTS` is required for tests to be visible).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/Demo.asmdef \
        Assets/Scripts/Demo/Demo.asmdef.meta \
        Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef \
        Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef.meta \
        Assets/Tests/EditMode/EcsTestsBase.cs \
        Assets/Tests/EditMode/EcsTestsBase.cs.meta
git commit -m "$(cat <<'EOF'
chore(test): set up Demo.asmdef + Demo.Tests.EditMode.asmdef

Adds the project's first .asmdef pair so EditMode tests can reference
Demo types without going through Assembly-CSharp. EcsTestsBase provides
per-test World + helpers for BattleConfig/NetworkTime/Squad/Soldier
fixtures. Avoids the Unity.Entities.Tests package dependency.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Create SquadGeometry + unit tests

Extract pure math (slot offset, engagement distance, compaction sizing) into a static class so it can be unit-tested without spinning up an ECS world and reused from all four systems that need it.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/SquadGeometry.cs`
- Create: `Assets/Tests/EditMode/SquadGeometryTests.cs`

- [ ] **Step 1: Create the helper**

Create `Assets/Scripts/Demo/Battle/SquadGeometry.cs` with:

```csharp
using Unity.Burst;
using Unity.Mathematics;

namespace Demo
{
    // Pure math used by BattleSpawnSystem, SoldierSlotFollowSystem,
    // SquadMovementSystem, and SquadCompactionSystem. All static, all
    // Burst-compatible, no allocations, no entity access.
    [BurstCompile]
    public static class SquadGeometry
    {
        // Offset of a slot in the squad's local frame.
        // Row 0 is the front rank, facing +Z. Cols centered on X=0.
        public static float3 SlotLocalOffset(int slot, int rows, int cols, float spacing)
        {
            int col = slot % cols;
            int row = slot / cols;
            float localX = (col - (cols - 1) * 0.5f) * spacing;
            float localZ = ((rows - 1) * 0.5f - row) * spacing;
            return new float3(localX, 0f, localZ);
        }

        // Anchor-to-anchor distance at which two facing squads' front ranks
        // are within `attackRange` of each other.
        public static float EngagementDistance(
            int selfRows, int targetRows, float spacing,
            float attackRange, float contactMargin)
        {
            return (selfRows   - 1) * 0.5f * spacing
                 + (targetRows - 1) * 0.5f * spacing
                 + attackRange
                 + contactMargin;
        }

        // Row count required to hold `aliveCount` soldiers in `cols`-wide rows.
        public static int RowsForAliveCount(int aliveCount, int cols)
        {
            if (aliveCount <= 0) return 0;
            if (cols <= 0) return 0;
            return (aliveCount + cols - 1) / cols;
        }
    }
}
```

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Create the unit tests**

Create `Assets/Tests/EditMode/SquadGeometryTests.cs` with:

```csharp
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
```

- [ ] **Step 4: Run the tests via MCP**

Run Helper A with `targetClass = "SquadGeometryTests"` through `mcp__unity-mcp__Unity_RunCommand`. Wait ~3 s, then call `Unity_GetConsoleLogs`. Expected: `[TestRunner] DONE: 8 passed, 0 failed`. If a test fails, the bug is in `SquadGeometry` — fix and re-run.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/SquadGeometry.cs \
        Assets/Scripts/Demo/Battle/SquadGeometry.cs.meta \
        Assets/Tests/EditMode/SquadGeometryTests.cs \
        Assets/Tests/EditMode/SquadGeometryTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): SquadGeometry helper + unit tests

Pulls slot offset, engagement distance, and post-compaction row count
into a static Burst-compatible class. EditMode tests cover symmetric
and asymmetric cases plus edge counts.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Extend BattleConfig with squad fields (atomic rename)

Add new squad-shape and squad-behavior fields to `BattleConfig` and `BattleConfigAuthoring`. Rename `Spacing` → `SquadSpacing` and `MoveSpeed` → `SoldierStepSpeed`. `[FormerlySerializedAs]` preserves existing serialized values.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs`
- Modify: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs` (rename `config.Spacing`)
- Modify: `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs` (rename `config.MoveSpeed`)

- [ ] **Step 1: Rewrite BattleConfigAuthoring.cs**

Replace the entire contents of `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` with:

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Demo
{
    // Singleton baked from BattleConfigAuthoring. Drives every battle system.
    public struct BattleConfig : IComponentData
    {
        public Entity SoldierPrefab;

        // Derived from squad shape: SquadsPerTeam * SquadRows * SquadCols.
        // Kept on the component for HUD / diagnostics.
        public int CountPerSide;

        // Squad shape.
        public int   SquadsPerTeam;
        public int   SquadRows;
        public int   SquadCols;
        public float SquadSpacing;

        // Squad behavior.
        public float SquadAdvanceSpeed;
        public float SquadRotationSpeed;
        public float ContactMargin;
        public int   CompactionIntervalTicks;

        // Spawn centers.
        public float3 RedCenter;
        public float3 BlueCenter;

        // Combat / soldier tuning.
        public float SoldierStepSpeed;
        public float AttackRange;
        public float Dps;
        public float MaxHealth;
        public int   TargetRefreshIntervalTicks;

        // Visuals.
        public float4 RedColor;
        public float4 BlueColor;
    }

    public class BattleConfigAuthoring : MonoBehaviour
    {
        [Tooltip("Soldier prefab — must have a GhostAuthoringComponent + SoldierAuthoring.")]
        public GameObject SoldierPrefab;

        [Header("Squad shape")]
        public int   SquadsPerTeam = 2;
        public int   SquadRows     = 5;
        public int   SquadCols     = 10;
        [FormerlySerializedAs("Spacing")]
        public float SquadSpacing  = 1.5f;

        [Header("Squad behavior")]
        public float SquadAdvanceSpeed       = 2f;
        public float SquadRotationSpeed      = 2f;   // rad/s
        public float ContactMargin           = 0.1f;
        public int   CompactionIntervalTicks = 10;

        [Header("Spawn centers")]
        public Vector3 RedCenter  = new Vector3(-20f, 0f, 0f);
        public Vector3 BlueCenter = new Vector3( 20f, 0f, 0f);

        [Header("Combat tuning")]
        [FormerlySerializedAs("MoveSpeed")]
        public float SoldierStepSpeed = 2f;
        public float AttackRange      = 0.8f;
        public float Dps              = 25f;
        public float MaxHealth        = 50f;
        public int   TargetRefreshIntervalTicks = 5;

        [Header("Team colors (RGBA, linear)")]
        public Color RedColor  = new Color(1f, 0.1f, 0.1f, 1f);
        public Color BlueColor = new Color(0.1f, 0.4f, 1f, 1f);

        class Baker : Baker<BattleConfigAuthoring>
        {
            public override void Bake(BattleConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                int countPerSide = authoring.SquadsPerTeam
                                   * authoring.SquadRows
                                   * authoring.SquadCols;

                AddComponent(entity, new BattleConfig
                {
                    SoldierPrefab = GetEntity(authoring.SoldierPrefab, TransformUsageFlags.Dynamic),
                    CountPerSide  = countPerSide,

                    SquadsPerTeam = authoring.SquadsPerTeam,
                    SquadRows     = authoring.SquadRows,
                    SquadCols     = authoring.SquadCols,
                    SquadSpacing  = authoring.SquadSpacing,

                    SquadAdvanceSpeed       = authoring.SquadAdvanceSpeed,
                    SquadRotationSpeed      = authoring.SquadRotationSpeed,
                    ContactMargin           = authoring.ContactMargin,
                    CompactionIntervalTicks = authoring.CompactionIntervalTicks,

                    RedCenter  = authoring.RedCenter,
                    BlueCenter = authoring.BlueCenter,

                    SoldierStepSpeed           = authoring.SoldierStepSpeed,
                    AttackRange                = authoring.AttackRange,
                    Dps                        = authoring.Dps,
                    MaxHealth                  = authoring.MaxHealth,
                    TargetRefreshIntervalTicks = authoring.TargetRefreshIntervalTicks,

                    RedColor  = new float4(authoring.RedColor.r,  authoring.RedColor.g,  authoring.RedColor.b,  authoring.RedColor.a),
                    BlueColor = new float4(authoring.BlueColor.r, authoring.BlueColor.g, authoring.BlueColor.b, authoring.BlueColor.a),
                });
            }
        }
    }
}
```

`SearchRadius` is dropped — `SquadTargetingSystem` does not use a physics broadphase.

- [ ] **Step 2: Fix BattleSpawnSystem's Spacing references**

In `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs`, the field references `config.Spacing` appear in two places (the `initRed` and `initBlue` job setups, around lines 63 and 86). Change both to `config.SquadSpacing`.

- [ ] **Step 3: Fix SoldierMovementSystem's MoveSpeed reference**

In `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs`, line 31 currently reads:

```csharp
                MoveSpeed   = config.MoveSpeed,
```

Change to:

```csharp
                MoveSpeed   = config.SoldierStepSpeed,
```

- [ ] **Step 4: Confirm authoring asset still bakes correctly via MCP**

Run Helper B (BattleConfig inspector dump) via `mcp__unity-mcp__Unity_RunCommand`. Wait, then `Unity_GetConsoleLogs`. Expected log line: `SquadSpacing=1.5  SoldierStepSpeed=2  SquadsPerTeam=2  SquadRows=5  SquadCols=10`. If `SquadSpacing` or `SoldierStepSpeed` is `0`, `[FormerlySerializedAs]` did not pick up the previous value — write a short fix-up script that sets them explicitly via `Unity_RunCommand` (mirroring Helper D's pattern) and re-run Helper B.

- [ ] **Step 5: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs \
        Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs \
        Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs \
        Assets/Scenes/BattleSub.unity
git commit -m "$(cat <<'EOF'
feat(battle): extend BattleConfig with squad shape and behavior

Renames Spacing -> SquadSpacing and MoveSpeed -> SoldierStepSpeed via
FormerlySerializedAs. Adds SquadsPerTeam/SquadRows/SquadCols,
SquadAdvanceSpeed, SquadRotationSpeed, ContactMargin,
CompactionIntervalTicks. Drops unused SearchRadius. CountPerSide is now
derived from squad shape.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Add SquadMembership to SoldierAuthoring (keep Target for now)

Add `SquadMembership` to the soldier baker so newly-baked soldiers carry the component (zero-initialized; `BattleSpawnSystem` fills it in at runtime). Keep `Target` baking in place — its last consumer is rewritten in Task 10.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` (insert after Target line)

- [ ] **Step 1: Add the SquadMembership component to the baker**

In `SoldierAuthoring.cs`, find this block (around lines 67-72):

```csharp
                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new SoldierColor { Value = new float4(1, 1, 1, 1) });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new Target { Value = Entity.Null });
```

Add this line immediately after the `Target` add:

```csharp
                AddComponent(entity, new SquadMembership { Squad = Entity.Null, SlotIndex = -1 });
```

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs
git commit -m "$(cat <<'EOF'
feat(battle): bake SquadMembership onto soldiers

New component starts zero-initialized; BattleSpawnSystem will set Squad
and SlotIndex once squad entities exist. Target stays for now — its
last consumer (MeleeDamageSystem) is replaced in a later task.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Rewrite BattleSpawnSystem to spawn squads

Spawn `2 * SquadsPerTeam` Squad entities, lay them out in a line per team, bulk-instantiate soldiers, populate each squad's `SquadMember` buffer, and set each soldier's `SquadMembership`. Uses `SquadGeometry.SlotLocalOffset` for slot math.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs` (full rewrite)

- [ ] **Step 1: Replace BattleSpawnSystem.cs**

Replace the entire contents of `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Demo
{
    // One-shot. Creates 2 * SquadsPerTeam Squad entities, lays squads in
    // a line per team perpendicular to the red<->blue axis, bulk-spawns
    // soldiers, and wires SquadMembership + SquadMember buffers.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct BattleSpawnSystem : ISystem
    {
        const float InterSquadGap = 2f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var em     = state.EntityManager;

            int squadsPerTeam   = config.SquadsPerTeam;
            int rows            = config.SquadRows;
            int cols            = config.SquadCols;
            int soldiersPerSquad = rows * cols;
            int countPerSide    = squadsPerTeam * soldiersPerSquad;
            float spacing       = config.SquadSpacing;
            float squadStrideZ  = cols * spacing + InterSquadGap;

            var squadArch = em.CreateArchetype(
                typeof(Squad),
                typeof(SquadTarget),
                typeof(SquadMember),
                typeof(LocalTransform),
                typeof(LocalToWorld));

            var redSquads  = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);
            var blueSquads = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);

            quaternion redFacing  = quaternion.LookRotationSafe(new float3( 1, 0, 0), math.up());
            quaternion blueFacing = quaternion.LookRotationSafe(new float3(-1, 0, 0), math.up());

            for (int i = 0; i < squadsPerTeam; i++)
            {
                float offsetZ = (i - (squadsPerTeam - 1) * 0.5f) * squadStrideZ;

                var redPos = (float3)config.RedCenter + new float3(0, 0, offsetZ);
                em.SetComponentData(redSquads[i], new Squad
                {
                    Team = 0, Rows = rows, Cols = cols, Spacing = spacing,
                });
                em.SetComponentData(redSquads[i], new SquadTarget { Value = Entity.Null });
                em.SetComponentData(redSquads[i], LocalTransform.FromPositionRotation(redPos, redFacing));
                var redBuf = em.GetBuffer<SquadMember>(redSquads[i]);
                redBuf.ResizeUninitialized(soldiersPerSquad);
                for (int s = 0; s < soldiersPerSquad; s++)
                    redBuf[s] = new SquadMember { Value = Entity.Null };

                var bluePos = (float3)config.BlueCenter + new float3(0, 0, offsetZ);
                em.SetComponentData(blueSquads[i], new Squad
                {
                    Team = 1, Rows = rows, Cols = cols, Spacing = spacing,
                });
                em.SetComponentData(blueSquads[i], new SquadTarget { Value = Entity.Null });
                em.SetComponentData(blueSquads[i], LocalTransform.FromPositionRotation(bluePos, blueFacing));
                var blueBuf = em.GetBuffer<SquadMember>(blueSquads[i]);
                blueBuf.ResizeUninitialized(soldiersPerSquad);
                for (int s = 0; s < soldiersPerSquad; s++)
                    blueBuf[s] = new SquadMember { Value = Entity.Null };
            }

            var reds  = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);
            var blues = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);

            var redAnchorPos  = new NativeArray<float3>(squadsPerTeam, Allocator.TempJob);
            var redAnchorRot  = new NativeArray<quaternion>(squadsPerTeam, Allocator.TempJob);
            var blueAnchorPos = new NativeArray<float3>(squadsPerTeam, Allocator.TempJob);
            var blueAnchorRot = new NativeArray<quaternion>(squadsPerTeam, Allocator.TempJob);
            for (int i = 0; i < squadsPerTeam; i++)
            {
                var rt = em.GetComponentData<LocalTransform>(redSquads[i]);
                redAnchorPos[i] = rt.Position;
                redAnchorRot[i] = rt.Rotation;
                var bt = em.GetComponentData<LocalTransform>(blueSquads[i]);
                blueAnchorPos[i] = bt.Position;
                blueAnchorRot[i] = bt.Rotation;
            }

            var xformLookup      = SystemAPI.GetComponentLookup<LocalTransform>(false);
            var teamLookup       = SystemAPI.GetComponentLookup<Team>(false);
            var healthLookup     = SystemAPI.GetComponentLookup<Health>(false);
            var attackLookup     = SystemAPI.GetComponentLookup<AttackStats>(false);
            var colorLookup      = SystemAPI.GetComponentLookup<SoldierColor>(false);
            var membershipLookup = SystemAPI.GetComponentLookup<SquadMembership>(false);

            state.Dependency = new InitSoldierJob
            {
                Entities         = reds,
                SquadEntities    = redSquads,
                SquadAnchorPos   = redAnchorPos,
                SquadAnchorRot   = redAnchorRot,
                Rows             = rows,
                Cols             = cols,
                Spacing          = spacing,
                SoldiersPerSquad = soldiersPerSquad,
                TeamValue        = 0,
                TeamColor        = config.RedColor,
                MaxHealth        = config.MaxHealth,
                AttackRange      = config.AttackRange,
                Dps              = config.Dps,
                XformLookup      = xformLookup,
                TeamLookup       = teamLookup,
                HealthLookup     = healthLookup,
                AttackLookup     = attackLookup,
                ColorLookup      = colorLookup,
                MembershipLookup = membershipLookup,
            }.Schedule(reds.Length, 64, state.Dependency);

            state.Dependency = new InitSoldierJob
            {
                Entities         = blues,
                SquadEntities    = blueSquads,
                SquadAnchorPos   = blueAnchorPos,
                SquadAnchorRot   = blueAnchorRot,
                Rows             = rows,
                Cols             = cols,
                Spacing          = spacing,
                SoldiersPerSquad = soldiersPerSquad,
                TeamValue        = 1,
                TeamColor        = config.BlueColor,
                MaxHealth        = config.MaxHealth,
                AttackRange      = config.AttackRange,
                Dps              = config.Dps,
                XformLookup      = xformLookup,
                TeamLookup       = teamLookup,
                HealthLookup     = healthLookup,
                AttackLookup     = attackLookup,
                ColorLookup      = colorLookup,
                MembershipLookup = membershipLookup,
            }.Schedule(blues.Length, 64, state.Dependency);

            state.Dependency.Complete();

            for (int i = 0; i < reds.Length; i++)
            {
                int squadIndex = i / soldiersPerSquad;
                int slot       = i % soldiersPerSquad;
                var buf        = em.GetBuffer<SquadMember>(redSquads[squadIndex]);
                buf[slot]      = new SquadMember { Value = reds[i] };
            }
            for (int i = 0; i < blues.Length; i++)
            {
                int squadIndex = i / soldiersPerSquad;
                int slot       = i % soldiersPerSquad;
                var buf        = em.GetBuffer<SquadMember>(blueSquads[squadIndex]);
                buf[slot]      = new SquadMember { Value = blues[i] };
            }

            reds.Dispose();
            blues.Dispose();
            redSquads.Dispose();
            blueSquads.Dispose();
            redAnchorPos.Dispose();
            redAnchorRot.Dispose();
            blueAnchorPos.Dispose();
            blueAnchorRot.Dispose();

            Debug.Log($"BattleSpawnSystem: spawned {squadsPerTeam} red + {squadsPerTeam} blue squads, {countPerSide} soldiers per side.");
            state.Enabled = false;
        }

        [BurstCompile]
        struct InitSoldierJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity>     Entities;
            [ReadOnly] public NativeArray<Entity>     SquadEntities;
            [ReadOnly] public NativeArray<float3>     SquadAnchorPos;
            [ReadOnly] public NativeArray<quaternion> SquadAnchorRot;

            public int    Rows;
            public int    Cols;
            public float  Spacing;
            public int    SoldiersPerSquad;
            public int    TeamValue;
            public float4 TeamColor;
            public float  MaxHealth;
            public float  AttackRange;
            public float  Dps;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform>  XformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Team>            TeamLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Health>          HealthLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<AttackStats>     AttackLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SoldierColor>    ColorLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SquadMembership> MembershipLookup;

            public void Execute(int i)
            {
                int squadIndex = i / SoldiersPerSquad;
                int slot       = i % SoldiersPerSquad;

                var local = SquadGeometry.SlotLocalOffset(slot, Rows, Cols, Spacing);
                var world = SquadAnchorPos[squadIndex] + math.mul(SquadAnchorRot[squadIndex], local);

                var e = Entities[i];
                XformLookup[e]      = LocalTransform.FromPositionRotation(world, SquadAnchorRot[squadIndex]);
                TeamLookup[e]       = new Team { Value = TeamValue };
                HealthLookup[e]     = new Health { Current = MaxHealth, Max = MaxHealth };
                AttackLookup[e]     = new AttackStats { Range = AttackRange, Dps = Dps };
                ColorLookup[e]      = new SoldierColor { Value = TeamColor };
                MembershipLookup[e] = new SquadMembership
                {
                    Squad     = SquadEntities[squadIndex],
                    SlotIndex = slot,
                };
            }
        }
    }
}
```

Notes:
- `InitSoldierJob` now calls `SquadGeometry.SlotLocalOffset` instead of inlining the math.
- Step 5 (buffer population) runs single-threaded on the main thread after the parallel job completes. Cost is negligible and avoids cross-entity write races.

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Re-run SquadGeometryTests via MCP**

Helper A with `targetClass = "SquadGeometryTests"` → expect `8 passed, 0 failed` (no production change to the helper).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): spawn Squad entities and wire SquadMembership

BattleSpawnSystem now creates 2 * SquadsPerTeam Squad entities laid out
in a line per team, bulk-instantiates soldiers, and populates each
squad's SquadMember buffer + each soldier's SquadMembership. Soldier
initial positions are computed from (squad anchor, slot index) via
SquadGeometry.SlotLocalOffset.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Create SquadTargetingSystem (with tests)

For each squad, scan all enemy squads via a Burst job and set `SquadTarget.Value` to the nearest enemy squad anchor.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs`
- Create: `Assets/Tests/EditMode/SquadTargetingSystemTests.cs`

- [ ] **Step 1: Create the system**

Create `Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SquadTargetingSystem : ISystem
    {
        EntityQuery _squadQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<NetworkTime>();
            _squadQuery = SystemAPI.QueryBuilder()
                .WithAll<Squad, SquadTarget, LocalTransform>()
                .Build();
            state.RequireForUpdate(_squadQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            var tick   = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            if ((tick % (uint)config.TargetRefreshIntervalTicks) != 0u) return;

            int squadCount = _squadQuery.CalculateEntityCount();
            var snapshot = new NativeArray<SquadSnapshot>(squadCount, Allocator.TempJob);

            state.Dependency = new SnapshotJob
            {
                Snapshot = snapshot,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = new AssignTargetJob
            {
                Snapshot = snapshot,
            }.ScheduleParallel(_squadQuery, state.Dependency);

            state.Dependency = snapshot.Dispose(state.Dependency);
        }
    }

    public struct SquadSnapshot
    {
        public Entity Entity;
        public int    Team;
        public float3 Position;
    }

    [BurstCompile]
    public partial struct SnapshotJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<SquadSnapshot> Snapshot;

        public void Execute([Unity.Entities.EntityIndexInQuery] int index,
                            Entity entity,
                            in Squad squad,
                            in LocalTransform xform)
        {
            Snapshot[index] = new SquadSnapshot
            {
                Entity   = entity,
                Team     = squad.Team,
                Position = xform.Position,
            };
        }
    }

    [BurstCompile]
    public partial struct AssignTargetJob : IJobEntity
    {
        [Unity.Collections.ReadOnly] public NativeArray<SquadSnapshot> Snapshot;

        public void Execute(in Squad squad,
                            in LocalTransform xform,
                            ref SquadTarget target)
        {
            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;
            float3 self = xform.Position;

            for (int i = 0; i < Snapshot.Length; i++)
            {
                var s = Snapshot[i];
                if (s.Team == squad.Team) continue;
                float d = math.distancesq(self, s.Position);
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestEntity = s.Entity;
                }
            }

            target.Value = bestEntity;
        }
    }
}
```

- [ ] **Step 2: Create the system test**

Create `Assets/Tests/EditMode/SquadTargetingSystemTests.cs` with:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class SquadTargetingSystemTests : EcsTestsBase
    {
        [Test]
        public void NearestEnemySquad_IsChosen()
        {
            CreateBattleConfig(targetRefreshIntervalTicks: 1);
            CreateNetworkTime(tick: 1);

            // Red squad at origin. Two blue squads: one at +5, one at +20.
            var red   = CreateSquad(0, 2, 2, 1f, new float3( 0, 0, 0), quaternion.identity);
            var nearBlue = CreateSquad(1, 2, 2, 1f, new float3(+5, 0, 0), quaternion.identity);
            var farBlue  = CreateSquad(1, 2, 2, 1f, new float3(+20, 0, 0), quaternion.identity);

            CreateAndUpdateSystem<SquadTargetingSystem>();

            var t = Manager.GetComponentData<SquadTarget>(red);
            Assert.AreEqual(nearBlue, t.Value);

            // And from blue's POV: nearBlue's nearest is also the red squad (only enemy).
            var t2 = Manager.GetComponentData<SquadTarget>(nearBlue);
            Assert.AreEqual(red, t2.Value);

            // farBlue's nearest enemy is still red (only red is enemy).
            var t3 = Manager.GetComponentData<SquadTarget>(farBlue);
            Assert.AreEqual(red, t3.Value);
        }

        [Test]
        public void NoEnemySquad_LeavesTargetNull()
        {
            CreateBattleConfig(targetRefreshIntervalTicks: 1);
            CreateNetworkTime(tick: 1);

            // Only red squads exist.
            var redA = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            CreateSquad(0, 2, 2, 1f, new float3(5, 0, 0), quaternion.identity);

            CreateAndUpdateSystem<SquadTargetingSystem>();

            var t = Manager.GetComponentData<SquadTarget>(redA);
            Assert.AreEqual(Entity.Null, t.Value);
        }
    }
}
```

- [ ] **Step 3: Verify console + run tests via MCP**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. Helper A with `targetClass = "SquadTargetingSystemTests"` → expect `2 passed, 0 failed`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs \
        Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs.meta \
        Assets/Tests/EditMode/SquadTargetingSystemTests.cs \
        Assets/Tests/EditMode/SquadTargetingSystemTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadTargetingSystem + tests

Throttled by TargetRefreshIntervalTicks. Snapshots all squads, then
each squad picks the nearest enemy-team squad by anchor distance.
EditMode tests cover the nearest-enemy choice and the no-enemy case.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Create SquadMovementSystem (with tests)

Each squad with a valid target lerps rotation toward `LookRotation(target - self)` and advances along its facing forward at `SquadAdvanceSpeed`, stopping at `SquadGeometry.EngagementDistance(...)`.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs`
- Create: `Assets/Tests/EditMode/SquadMovementSystemTests.cs`

- [ ] **Step 1: Create the system**

Create `Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs` with:

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
    [UpdateAfter(typeof(SquadTargetingSystem))]
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
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SquadStepJob
            {
                AdvanceSpeed  = config.SquadAdvanceSpeed,
                RotationSpeed = config.SquadRotationSpeed,
                AttackRange   = config.AttackRange,
                ContactMargin = config.ContactMargin,
                Dt            = dt,
                SquadLookup   = squadLookup,
                XformLookup   = xformLookup,
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

        [Unity.Collections.ReadOnly] public ComponentLookup<Squad> SquadLookup;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> XformLookup;

        public void Execute(in Squad self, in SquadTarget target, ref LocalTransform xform)
        {
            if (target.Value == Entity.Null) return;
            if (!XformLookup.HasComponent(target.Value)) return;
            if (!SquadLookup.HasComponent(target.Value)) return;

            float3 targetPos = XformLookup[target.Value].Position;
            float3 toTarget  = targetPos - xform.Position;
            toTarget.y = 0f;
            float dist = math.length(toTarget);
            if (dist < 1e-4f) return;

            float3 desiredFwd = toTarget / dist;
            quaternion desiredRot = quaternion.LookRotationSafe(desiredFwd, math.up());
            float slerpT = math.saturate(RotationSpeed * Dt);
            xform.Rotation = math.slerp(xform.Rotation, desiredRot, slerpT);

            int targetRows = SquadLookup[target.Value].Rows;
            float engageDist = SquadGeometry.EngagementDistance(
                self.Rows, targetRows, self.Spacing, AttackRange, ContactMargin);

            if (dist <= engageDist) return;

            float3 fwd = math.mul(xform.Rotation, new float3(0, 0, 1));
            float step = AdvanceSpeed * Dt;
            float maxStep = dist - engageDist;
            step = math.min(step, maxStep);
            xform.Position += fwd * step;
        }
    }
}
```

- [ ] **Step 2: Create the system test**

Create `Assets/Tests/EditMode/SquadMovementSystemTests.cs` with:

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
        public void FarSquad_AdvancesTowardTarget()
        {
            CreateBattleConfig();
            // Red faces +X at origin, target at +10 on X. Engagement distance with
            // rows=2 spacing=1.5 attack=0.8 margin=0.1 is:
            //   (1*0.5*1.5)*2 + 0.8 + 0.1 = 1.5 + 0.8 + 0.1 = 2.4
            // dist = 10 > 2.4, so red must advance.
            var faceRedAtPlusX = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            var red  = CreateSquad(0, 2, 2, 1.5f, new float3( 0, 0, 0), faceRedAtPlusX);
            var blue = CreateSquad(1, 2, 2, 1.5f, new float3(10, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = blue });

            SetTime(elapsed: 0.0, dt: 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.Greater(pos.x, 0f);
            // SquadAdvanceSpeed=2, dt=0.1 → step ≈ 0.2 along forward (+X).
            Assert.LessOrEqual(pos.x, 0.21f);
        }

        [Test]
        public void SquadAtEngagementDistance_DoesNotAdvance()
        {
            CreateBattleConfig();
            var face = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            // Engagement distance is 2.4 with rows=2, spacing=1.5, range=0.8, margin=0.1.
            var red  = CreateSquad(0, 2, 2, 1.5f, new float3(0, 0, 0), face);
            var blue = CreateSquad(1, 2, 2, 1.5f, new float3(2.4f, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = blue });

            SetTime(0.0, 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.AreEqual(0f, pos.x, 1e-4f);
        }

        [Test]
        public void NullTarget_DoesNotMove()
        {
            CreateBattleConfig();
            var red = CreateSquad(0, 2, 2, 1.5f, new float3(0, 0, 0), quaternion.identity);
            Manager.SetComponentData(red, new SquadTarget { Value = Entity.Null });

            SetTime(0.0, 0.1f);

            CreateAndUpdateSystem<SquadMovementSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(red).Position;
            Assert.AreEqual(0f, pos.x, 1e-4f);
        }
    }
}
```

- [ ] **Step 3: Verify console + run tests via MCP**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. Helper A with `targetClass = "SquadMovementSystemTests"` → expect `3 passed, 0 failed`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs \
        Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs.meta \
        Assets/Tests/EditMode/SquadMovementSystemTests.cs \
        Assets/Tests/EditMode/SquadMovementSystemTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadMovementSystem + tests

Each squad with a valid SquadTarget lerps rotation toward the line
between anchors at SquadRotationSpeed, then advances along its facing
forward at SquadAdvanceSpeed. Stops at SquadGeometry.EngagementDistance.
EditMode tests cover advance, hold-at-engagement, and null-target cases.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Replace SoldierMovementSystem with SoldierSlotFollowSystem (with tests)

Atomic swap: delete `SoldierMovementSystem.cs`, create `SoldierSlotFollowSystem.cs`, repoint `MeleeDamageSystem`'s `[UpdateAfter]`.

**Files:**
- Delete: `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs` (+ .meta)
- Create: `Assets/Scripts/Demo/Battle/System/SoldierSlotFollowSystem.cs`
- Create: `Assets/Tests/EditMode/SoldierSlotFollowSystemTests.cs`
- Modify: `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs` (UpdateAfter target)

- [ ] **Step 1: Create SoldierSlotFollowSystem.cs**

Create `Assets/Scripts/Demo/Battle/System/SoldierSlotFollowSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SquadMovementSystem))]
    public partial struct SoldierSlotFollowSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config      = SystemAPI.GetSingleton<BattleConfig>();
            float dt        = SystemAPI.Time.DeltaTime;
            var squadLookup = SystemAPI.GetComponentLookup<Squad>(true);
            var xformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new SlotFollowJob
            {
                StepSpeed   = config.SoldierStepSpeed,
                Dt          = dt,
                SquadLookup = squadLookup,
                XformLookup = xformLookup,
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct SlotFollowJob : IJobEntity
    {
        public float StepSpeed;
        public float Dt;

        [Unity.Collections.ReadOnly] public ComponentLookup<Squad> SquadLookup;
        [Unity.Collections.ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform>                     XformLookup;

        public void Execute(ref LocalTransform xform, in SquadMembership membership)
        {
            if (membership.Squad == Entity.Null) return;
            if (!SquadLookup.HasComponent(membership.Squad)) return;

            var squad   = SquadLookup[membership.Squad];
            var anchor  = XformLookup[membership.Squad];
            var local   = SquadGeometry.SlotLocalOffset(membership.SlotIndex, squad.Rows, squad.Cols, squad.Spacing);
            float3 target = anchor.Position + math.mul(anchor.Rotation, local);

            float3 toSlot = target - xform.Position;
            float  dist   = math.length(toSlot);
            float  step   = StepSpeed * Dt;

            if (dist <= step || dist < 1e-4f)
                xform.Position = target;
            else
                xform.Position += (toSlot / dist) * step;

            xform.Rotation = anchor.Rotation;
        }
    }
}
```

- [ ] **Step 2: Create the system test**

Create `Assets/Tests/EditMode/SoldierSlotFollowSystemTests.cs` with:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class SoldierSlotFollowSystemTests : EcsTestsBase
    {
        [Test]
        public void SoldierFarFromSlot_AdvancesAtStepSpeed()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Slot 0 in 2×2 grid: local (-0.5, 0, +0.5), world is the same since identity rotation.
            // Soldier starts far behind at z = -10.
            var soldier = CreateSoldier(squad, slot: 0, pos: new float3(-0.5f, 0, -10f));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            // Moved 0.2 toward (-0.5, 0, 0.5) from (-0.5, 0, -10); only z changes.
            Assert.AreEqual(-0.5f, pos.x, 1e-4f);
            Assert.AreEqual(-9.8f, pos.z, 1e-3f);
        }

        [Test]
        public void SoldierWithinOneStep_SnapsToSlot()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Slot 0 world pos: (-0.5, 0, +0.5). Soldier within step distance (step = 0.2).
            var soldier = CreateSoldier(squad, slot: 0, pos: new float3(-0.5f, 0, 0.4f));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            Assert.AreEqual(-0.5f, pos.x, 1e-4f);
            Assert.AreEqual( 0.5f, pos.z, 1e-4f);
        }

        [Test]
        public void NullSquadMembership_DoesNotMove()
        {
            CreateBattleConfig(soldierStepSpeed: 2f);
            var soldier = CreateSoldier(Entity.Null, slot: -1, pos: new float3(7, 0, 7));

            SetTime(0.0, 0.1f);
            CreateAndUpdateSystem<SoldierSlotFollowSystem>();

            var pos = Manager.GetComponentData<LocalTransform>(soldier).Position;
            Assert.AreEqual(7f, pos.x, 1e-4f);
            Assert.AreEqual(7f, pos.z, 1e-4f);
        }
    }
}
```

- [ ] **Step 3: Update MeleeDamageSystem's `[UpdateAfter]`**

In `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs`, line 22 currently reads:

```csharp
    [UpdateAfter(typeof(SoldierMovementSystem))]
```

Change to:

```csharp
    [UpdateAfter(typeof(SoldierSlotFollowSystem))]
```

- [ ] **Step 4: Delete SoldierMovementSystem files**

```bash
rm Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs
rm Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs.meta
```

- [ ] **Step 5: Verify console + run tests via MCP**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. Helper A with `targetClass = "SoldierSlotFollowSystemTests"` → expect `3 passed, 0 failed`.

- [ ] **Step 6: Commit**

```bash
git add -A Assets/Scripts/Demo/Battle/System/ Assets/Tests/EditMode/
git commit -m "$(cat <<'EOF'
feat(battle): replace SoldierMovementSystem with SoldierSlotFollowSystem

Soldiers step toward their assigned (squad anchor + slot offset) world
position each tick via SquadGeometry.SlotLocalOffset, clamping when
within one step. MeleeDamageSystem's UpdateAfter is repointed. EditMode
tests cover far-from-slot, within-one-step, and null-membership cases.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Rewrite MeleeDamageSystem for front-rank slot pairing

Front-rank soldiers (`SlotIndex < Squad.Cols`) attack `targetSquad.SquadMember[SlotIndex % targetCols]`. No automated test — coverage is via the manual smoke test.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs` (rewrite jobs)

- [ ] **Step 1: Replace MeleeDamageSystem.cs**

Replace the entire contents of `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    public struct DamageEvent
    {
        public Entity Victim;
        public float  Amount;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SoldierSlotFollowSystem))]
    public partial struct MeleeDamageSystem : ISystem
    {
        EntityQuery _attackerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            _attackerQuery = SystemAPI.QueryBuilder()
                .WithAll<Soldier, AttackStats, SquadMembership, LocalTransform>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            int chunkCount = _attackerQuery.CalculateChunkCount();
            if (chunkCount == 0) return;

            var stream = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            state.Dependency = new WriteDamageJob
            {
                MembershipHandle = SystemAPI.GetComponentTypeHandle<SquadMembership>(true),
                AttackHandle     = SystemAPI.GetComponentTypeHandle<AttackStats>(true),
                XformHandle      = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                SquadLookup      = SystemAPI.GetComponentLookup<Squad>(true),
                TargetLookup     = SystemAPI.GetComponentLookup<SquadTarget>(true),
                BufferLookup     = SystemAPI.GetBufferLookup<SquadMember>(true),
                XformLookup      = SystemAPI.GetComponentLookup<LocalTransform>(true),
                HealthLookup     = SystemAPI.GetComponentLookup<Health>(true),
                DamageWriter     = stream.AsWriter(),
                Dt               = dt,
            }.ScheduleParallel(_attackerQuery, state.Dependency);

            state.Dependency = new ReduceDamageJob
            {
                Reader       = stream.AsReader(),
                HealthLookup = SystemAPI.GetComponentLookup<Health>(false),
            }.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    struct WriteDamageJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<SquadMembership> MembershipHandle;
        [ReadOnly] public ComponentTypeHandle<AttackStats>     AttackHandle;
        [ReadOnly] public ComponentTypeHandle<LocalTransform>  XformHandle;
        [ReadOnly] public ComponentLookup<Squad>               SquadLookup;
        [ReadOnly] public ComponentLookup<SquadTarget>         TargetLookup;
        [ReadOnly] public BufferLookup<SquadMember>            BufferLookup;
        [ReadOnly] public ComponentLookup<LocalTransform>      XformLookup;
        [ReadOnly] public ComponentLookup<Health>              HealthLookup;
        public NativeStream.Writer DamageWriter;
        public float Dt;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DamageWriter.BeginForEachIndex(unfilteredChunkIndex);

            var memberships = chunk.GetNativeArray(ref MembershipHandle);
            var attacks     = chunk.GetNativeArray(ref AttackHandle);
            var xforms      = chunk.GetNativeArray(ref XformHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var m = memberships[i];
                if (m.Squad == Entity.Null) continue;
                if (!SquadLookup.HasComponent(m.Squad)) continue;

                var selfSquad = SquadLookup[m.Squad];
                if (m.SlotIndex < 0 || m.SlotIndex >= selfSquad.Cols) continue;

                var targetSquadEntity = TargetLookup[m.Squad].Value;
                if (targetSquadEntity == Entity.Null) continue;
                if (!BufferLookup.HasBuffer(targetSquadEntity)) continue;
                if (!SquadLookup.HasComponent(targetSquadEntity)) continue;

                var enemyBuf   = BufferLookup[targetSquadEntity];
                var enemySquad = SquadLookup[targetSquadEntity];
                int pairCol    = m.SlotIndex % enemySquad.Cols;
                if (pairCol >= enemyBuf.Length) continue;

                Entity enemy = enemyBuf[pairCol].Value;
                if (enemy == Entity.Null) continue;
                if (!HealthLookup.HasComponent(enemy)) continue;
                if (HealthLookup[enemy].Current <= 0f) continue;
                if (!XformLookup.HasComponent(enemy)) continue;

                float distSq = math.distancesq(xforms[i].Position, XformLookup[enemy].Position);
                float range  = attacks[i].Range;
                if (distSq <= range * range)
                {
                    DamageWriter.Write(new DamageEvent
                    {
                        Victim = enemy,
                        Amount = attacks[i].Dps * Dt,
                    });
                }
            }

            DamageWriter.EndForEachIndex();
        }
    }

    [BurstCompile]
    struct ReduceDamageJob : IJob
    {
        public NativeStream.Reader Reader;
        [NativeDisableParallelForRestriction] public ComponentLookup<Health> HealthLookup;

        public void Execute()
        {
            int foreachCount = Reader.ForEachCount;
            for (int i = 0; i < foreachCount; i++)
            {
                int eventCount = Reader.BeginForEachIndex(i);
                for (int j = 0; j < eventCount; j++)
                {
                    var ev = Reader.Read<DamageEvent>();
                    if (HealthLookup.HasComponent(ev.Victim))
                    {
                        var h = HealthLookup[ev.Victim];
                        h.Current -= ev.Amount;
                        HealthLookup[ev.Victim] = h;
                    }
                }
                Reader.EndForEachIndex();
            }
        }
    }
}
```

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. `TargetingSystem.cs` still exists and writes `Target`, but nothing reads it now.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): MeleeDamageSystem uses front-rank slot pairing

Only soldiers with SlotIndex < Squad.Cols (the front rank) deal damage.
Each front-rank soldier in slot i attacks targetSquad.Buffer[i % Cols].
Stale references and dead enemies are skipped silently; compaction
keeps the buffer fresh.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Delete TargetingSystem and the Target component

Nothing reads `Target` anymore. Remove the system and the component cleanly.

**Files:**
- Delete: `Assets/Scripts/Demo/Battle/System/TargetingSystem.cs` (+ .meta)
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` (remove `Target` struct + baker line)

- [ ] **Step 1: Remove the Target component definition**

In `SoldierAuthoring.cs`, delete this block (around lines 51-56):

```csharp
    // Server-only. Refreshed by TargetingSystem every TargetRefreshIntervalTicks.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Target : IComponentData
    {
        public Entity Value;
    }
```

- [ ] **Step 2: Remove the Target baker line**

In the same file, delete this line from the baker:

```csharp
                AddComponent(entity, new Target { Value = Entity.Null });
```

The remaining baker block reads:

```csharp
                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new SoldierColor { Value = new float4(1, 1, 1, 1) });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new SquadMembership { Squad = Entity.Null, SlotIndex = -1 });
```

- [ ] **Step 3: Delete TargetingSystem files**

```bash
rm Assets/Scripts/Demo/Battle/System/TargetingSystem.cs
rm Assets/Scripts/Demo/Battle/System/TargetingSystem.cs.meta
```

- [ ] **Step 4: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/Scripts/Demo/Battle/
git commit -m "$(cat <<'EOF'
refactor(battle): remove Target component and TargetingSystem

Both replaced by squad-level targeting. The physics broadphase has no
consumer now; soldier colliders remain on the prefab for future
ranged/picking use.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: Add SquadCompactionSystem (with tests)

Every `CompactionIntervalTicks` ticks (staggered per squad), each squad re-packs its `SquadMember` buffer: drops dead/null entries, reassigns `SlotIndex` on survivors, updates `Squad.Rows` via `SquadGeometry.RowsForAliveCount`, destroys empty squads.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs`
- Create: `Assets/Tests/EditMode/SquadCompactionSystemTests.cs`

- [ ] **Step 1: Create the system**

Create `Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs` with:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace Demo
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathSystem))]
    public partial struct SquadCompactionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
            state.RequireForUpdate<NetworkTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            uint tick  = SystemAPI.GetSingleton<NetworkTime>().ServerTick.SerializedData;
            int interval = config.CompactionIntervalTicks;
            if (interval <= 0) return;

            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            new CompactJob
            {
                Tick             = tick,
                Interval         = (uint)interval,
                MembershipLookup = SystemAPI.GetComponentLookup<SquadMembership>(false),
                HealthLookup     = SystemAPI.GetComponentLookup<Health>(true),
                Ecb              = ecb,
            }.Run();

            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    public partial struct CompactJob : IJobEntity
    {
        public uint Tick;
        public uint Interval;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SquadMembership>      MembershipLookup;
        [Unity.Collections.ReadOnly] public ComponentLookup<Health> HealthLookup;
        public EntityCommandBuffer                   Ecb;

        public void Execute(Entity squadEntity,
                            ref Squad squad,
                            ref DynamicBuffer<SquadMember> buf)
        {
            uint squadHash = (uint)squadEntity.Index;
            if (((Tick + squadHash) % Interval) != 0u) return;

            int original = buf.Length;
            var alive = new NativeList<Entity>(original, Allocator.Temp);
            for (int i = 0; i < original; i++)
            {
                var e = buf[i].Value;
                if (e == Entity.Null) continue;
                if (!HealthLookup.HasComponent(e)) continue;
                if (HealthLookup[e].Current <= 0f) continue;
                alive.Add(e);
            }

            int aliveCount = alive.Length;
            if (aliveCount == 0)
            {
                buf.Clear();
                Ecb.DestroyEntity(squadEntity);
                alive.Dispose();
                return;
            }

            int newRows = SquadGeometry.RowsForAliveCount(aliveCount, squad.Cols);

            buf.ResizeUninitialized(aliveCount);
            for (int i = 0; i < aliveCount; i++)
            {
                var e = alive[i];
                buf[i] = new SquadMember { Value = e };
                if (MembershipLookup.HasComponent(e))
                {
                    var m = MembershipLookup[e];
                    m.SlotIndex = i;
                    MembershipLookup[e] = m;
                }
            }
            squad.Rows = newRows;

            alive.Dispose();
        }
    }
}
```

- [ ] **Step 2: Create the system test**

Create `Assets/Tests/EditMode/SquadCompactionSystemTests.cs` with:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo.Tests
{
    public class SquadCompactionSystemTests : EcsTestsBase
    {
        // Pick a tick that satisfies (tick + squad.Index) % interval == 0
        // for our default interval. We set interval = 1 in tests so any tick fires.
        const uint FireEveryTick = 1;

        [Test]
        public void RepacksBuffer_DropsDeadAndNulls_ReassignsSlotIndex()
        {
            CreateBattleConfig(compactionIntervalTicks: (int)FireEveryTick);
            CreateNetworkTime(tick: 1);

            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            // Build a 4-slot buffer: [alive, null, dead, alive]
            var buf = Manager.GetBuffer<SquadMember>(squad);
            buf.ResizeUninitialized(4);
            var a = CreateSoldier(squad, slot: 0, pos: float3.zero, health: 30f);
            var c = CreateSoldier(squad, slot: 2, pos: float3.zero, health: 0f); // dead
            var d = CreateSoldier(squad, slot: 3, pos: float3.zero, health: 30f);
            buf[0] = new SquadMember { Value = a };
            buf[1] = new SquadMember { Value = Entity.Null };
            buf[2] = new SquadMember { Value = c };
            buf[3] = new SquadMember { Value = d };

            CreateAndUpdateSystem<SquadCompactionSystem>();

            var freshBuf = Manager.GetBuffer<SquadMember>(squad);
            Assert.AreEqual(2, freshBuf.Length, "buffer should be packed to alive count");
            Assert.AreEqual(a, freshBuf[0].Value);
            Assert.AreEqual(d, freshBuf[1].Value);

            var freshSquad = Manager.GetComponentData<Squad>(squad);
            Assert.AreEqual(1, freshSquad.Rows, "2 alive in cols=2 → 1 row");

            var membershipA = Manager.GetComponentData<SquadMembership>(a);
            var membershipD = Manager.GetComponentData<SquadMembership>(d);
            Assert.AreEqual(0, membershipA.SlotIndex);
            Assert.AreEqual(1, membershipD.SlotIndex);
        }

        [Test]
        public void AllDead_DestroysSquadEntity()
        {
            CreateBattleConfig(compactionIntervalTicks: (int)FireEveryTick);
            CreateNetworkTime(tick: 1);

            var squad = CreateSquad(0, 2, 2, 1f, new float3(0, 0, 0), quaternion.identity);
            var buf = Manager.GetBuffer<SquadMember>(squad);
            buf.ResizeUninitialized(2);
            var dead1 = CreateSoldier(squad, 0, float3.zero, health: 0f);
            var dead2 = CreateSoldier(squad, 1, float3.zero, health: 0f);
            buf[0] = new SquadMember { Value = dead1 };
            buf[1] = new SquadMember { Value = dead2 };

            CreateAndUpdateSystem<SquadCompactionSystem>();

            Assert.IsFalse(Manager.Exists(squad), "Squad entity should be destroyed when alive count hits zero");
        }
    }
}
```

Note: tests set `CompactionIntervalTicks = 1` so the stagger formula `(tick + Index) % 1 == 0` is always true regardless of the squad's `Entity.Index`.

- [ ] **Step 3: Verify console + run tests via MCP**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. Helper A with `targetClass = "SquadCompactionSystemTests"` → expect `2 passed, 0 failed`.

- [ ] **Step 4: Run the full EditMode suite via MCP**

Helper A with `targetClass = ""` (run-all) → expect `18 passed, 0 failed` (8 geometry + 2 targeting + 3 movement + 3 slot-follow + 2 compaction). Investigate any failure before proceeding.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs \
        Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs.meta \
        Assets/Tests/EditMode/SquadCompactionSystemTests.cs \
        Assets/Tests/EditMode/SquadCompactionSystemTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadCompactionSystem + tests

Every CompactionIntervalTicks ticks (staggered per squad via
Entity.Index), each squad re-packs its SquadMember buffer: drops dead
and null entries, reassigns surviving soldiers' SlotIndex via
SquadGeometry.RowsForAliveCount, destroys squads that hit zero alive.
EditMode tests cover the repack-with-survivors case and the all-dead
squad-destruction case.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: Smoke test via MCP

Final end-to-end verification. All steps go through MCP — no manual Editor clicks.

**Files:** none

- [ ] **Step 1: Confirm console clean before play**

Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: zero errors.

- [ ] **Step 2: Open BattleScene and play for 12 seconds via MCP**

Run Helper C (`playSeconds = 12f`) via `mcp__unity-mcp__Unity_RunCommand`. The script enters Play mode and exits after 12 s. Wait at least 15 s before fetching logs.

While Play is in progress, capture the scene view with `mcp__unity-mcp__Unity_SceneView_Capture2DScene` (timing: ~5 s into Play, while combat is in full swing). Save the returned path for the PR description.

- [ ] **Step 3: Inspect logs from the play session**

Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected log lines:
- `BattleSpawnSystem: spawned 2 red + 2 blue squads, 100 soldiers per side.`
- `[Smoke] Exited Play mode` at the end.
- No exceptions, no NullReferenceException, no Burst compile failures.

The visual record (scene capture from step 2) should show:
- Two red squads on the left, two blue squads on the right.
- All four squads sliding toward their nearest enemy squad, rotating to face it.
- Squads at engagement distance — front ranks within `AttackRange`, back ranks behind.
- Front-rank soldiers (10 per squad) trading blows; back ranks holding their slot.
- After deaths (HP 50, DPS 25 → ~2 s per kill), surviving members of a squad shift into a tighter rectangle within ~0.33 s.
- Winning team's `BattleHudController` banner if 12 s was enough for one side to wipe; otherwise re-run with a longer `playSeconds` if you want to confirm the win path.

- [ ] **Step 4: Scale test via MCP**

Run Helper D with `targetSquadsPerTeam = 4` → 400 total soldiers. Then re-run Helper C with `playSeconds = 8f`. Inspect logs via `Unity_GetConsoleLogs` — confirm spawn log shows 200 per side, no exceptions, no Burst safety errors. Then run Helper D again with `targetSquadsPerTeam = 2` to restore the default.

- [ ] **Step 5: Final commit if anything was tweaked**

If the smoke test surfaced minor tuning (e.g., adjusted `ContactMargin`):

```bash
git add -A Assets/
git commit -m "$(cat <<'EOF'
fix(battle): smoke-test tuning for squad-formation default config

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

If the smoke test reveals a bug (squads never stop advancing, soldiers spin in place, compaction destroys the wrong entity), stop and report the symptom — the design needs revisiting.

---

## Summary

End state:
- New code files: `SquadComponents.cs`, `SquadGeometry.cs`, `SquadTargetingSystem.cs`, `SquadMovementSystem.cs`, `SoldierSlotFollowSystem.cs`, `SquadCompactionSystem.cs`.
- New test files: `EcsTestsBase.cs`, `SquadGeometryTests.cs`, `SquadTargetingSystemTests.cs`, `SquadMovementSystemTests.cs`, `SoldierSlotFollowSystemTests.cs`, `SquadCompactionSystemTests.cs` (18 passing tests total).
- New asmdefs: `Demo.asmdef`, `Demo.Tests.EditMode.asmdef` (first asmdefs in the project).
- Deleted: `TargetingSystem.cs`, `SoldierMovementSystem.cs`.
- Modified: `BattleConfigAuthoring.cs`, `SoldierAuthoring.cs`, `BattleSpawnSystem.cs`, `MeleeDamageSystem.cs`.
- Soldiers belong to squads; squads pick squad targets; front rank fights; squads compact periodically.
- Physics broadphase no longer used (kinematic colliders remain on soldier prefab for future use).
- Per-soldier `Target` component is gone.
