# Per-soldier Health Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render a floating, camera-facing health bar above every soldier in BattleScene with green→yellow→red gradient fill driven by exact per-tick HP.

**Architecture:** Replicate `Health.Current` as a `[GhostField]` (Max stays server-only and clients read `BattleConfig.MaxHealth`). On the client, a presentation-group spawn system instantiates a non-ghost `HealthBar` entity per soldier, parents it via `Parent` + `LinkedEntityGroup` for auto-cleanup on ghost despawn. A second presentation-group system writes `Health.Current / BattleConfig.MaxHealth` into a `_Health01` material-property override each frame. A custom URP unlit shader billboards the quad in the vertex stage and computes the gradient + fill in the fragment stage.

**Tech Stack:** Unity 6000.4.1f1 · DOTS Entities 1.x · Netcode for Entities 1.11.0 · Entities.Graphics 6.4.0 · URP 17.4.0 · NUnit EditMode tests · Unity MCP for in-Editor validation.

**Spec:** [docs/superpowers/specs/2026-05-24-per-soldier-health-bar-design.md](docs/superpowers/specs/2026-05-24-per-soldier-health-bar-design.md)

---

## Conventions used in this plan

- All new C# code lives under `Assets/Scripts/Demo/Battle/` in the `Demo` namespace.
- Tests live under `Assets/Tests/EditMode/` in the `Demo.Tests` namespace; they extend `EcsTestsBase` and use `CreateAndUpdateSystem<T>` to tick one frame.
- **All Editor interactions are performed via Unity MCP** — no manual clicks in the Inspector, no menu navigation. The two MCP tools used:
  - `Unity_GetConsoleLogs` to read the Editor console.
  - `Unity_RunCommand` to execute a C# script inside the Editor process (compiles + runs synchronously, returns logs in the response).
- After every Unity-touching task, call `Unity_GetConsoleLogs` and expect `"success": true` with zero compile errors before moving on.
- Commits use Conventional Commits (the repo's existing style: `feat(battle):`, `fix(battle):`, etc.).

---

## Reusable MCP snippets

Several tasks reuse these. When a task says "run the test snippet with filter X" or "run the material-create snippet", paste the snippet below into `Unity_RunCommand`'s `Code` parameter, replacing the placeholder.

### Snippet A — Run EditMode tests (optionally filtered)

Replace `FILTER_CATEGORY` with either an empty `Filter { testMode = TestMode.EditMode }` (run all) or one scoped to a class name via `groupNames = new[] { "^Demo\\.Tests\\.XxxTests$" }` (run one class).

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var cb  = new TestCallbacks();
        api.RegisterCallbacks(cb);
        var filter = new Filter
        {
            testMode   = TestMode.EditMode,
            // groupNames = new[] { "^Demo\\.Tests\\.HealthBarUpdateSystemTests$" },
        };
        var settings = new ExecutionSettings(filter) { runSynchronously = true };
        api.Execute(settings);
        api.UnregisterCallbacks(cb);
        result.Log($"DONE passed={cb.Passed} failed={cb.Failed} skipped={cb.Skipped}{cb.Failures}");
    }
}

internal class TestCallbacks : ICallbacks
{
    public int Passed, Failed, Skipped;
    public string Failures = "";
    public void RunStarted(ITestAdaptor t) {}
    public void RunFinished(ITestResultAdaptor r)
    {
        Passed = r.PassCount; Failed = r.FailCount; Skipped = r.SkipCount;
        Collect(r);
    }
    void Collect(ITestResultAdaptor r)
    {
        if (r.HasChildren) { foreach (var c in r.Children) Collect(c); }
        else if (r.TestStatus != TestStatus.Passed)
            Failures += $"\n  {r.Test.FullName} :: {r.TestStatus} :: {r.Message}";
    }
    public void TestStarted(ITestAdaptor t) {}
    public void TestFinished(ITestResultAdaptor r) {}
}
```

A successful run returns something like `[Log] DONE passed=18 failed=0 skipped=0`. Failures append a per-test line listing the failure message.

### Snippet B — Force domain reload

Use after writing/changing C# source if Unity hasn't auto-recompiled yet:

```csharp
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh();
        CompilationPipeline.RequestScriptCompilation();
        result.Log("Refresh + RequestScriptCompilation triggered");
    }
}
```

---

## File map

**Create**
- `Assets/Scripts/Demo/Battle/Authoring/HealthBarAuthoring.cs` — `HealthBarRef`, `HealthBarLink`, `HealthBarFill` components + `HealthBarAuthoring` MonoBehaviour and its `Baker` (follows the same one-file-per-prefab pattern as `SoldierAuthoring.cs`)
- `Assets/Scripts/Demo/Battle/System/HealthBarSpawnSystem.cs`
- `Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs`
- `Assets/Shaders/HealthBar.shader`
- `Assets/Materials/HealthBar.mat`
- `Assets/Prefabs/HealthBar.prefab`
- `Assets/Tests/EditMode/HealthBarUpdateSystemTests.cs`
- `Assets/Tests/EditMode/HealthBarSpawnSystemTests.cs`

**Modify**
- `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` — `Health` becomes `GhostPrefabType.All` with `[GhostField]` on `Current`.
- `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` — add `HealthBarPrefab` (GameObject + baked Entity) and `HealthBarHeightOffset`.
- `Assets/Tests/EditMode/EcsTestsBase.cs` — extend `CreateBattleConfig` to accept `healthBarPrefab` + `healthBarHeightOffset`; add `CreateHealthBarStub` helper.
- `Assets/Scenes/BattleSub.unity` — assign the `HealthBar` prefab to `BattleConfigAuthoring.HealthBarPrefab` (done via MCP script in Task 12).

---

## Task 1: Add `HealthBarPrefab` + `HealthBarHeightOffset` fields to `BattleConfig`

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs`

This task does not produce runtime behavior on its own — it just adds two fields so that later tasks can wire the prefab through baking and read the offset in systems. No test in this task; compilation and a sanity check are sufficient.

- [ ] **Step 1: Add fields to the `BattleConfig` struct**

In `BattleConfigAuthoring.cs`, inside the `BattleConfig` struct, append two fields after `RedColor`/`BlueColor` and before the closing brace:

```csharp
// Health bar.
public Entity HealthBarPrefab;
public float  HealthBarHeightOffset;
```

- [ ] **Step 2: Add the authoring inputs**

In the `BattleConfigAuthoring` class, add a new `[Header]` block above `class Baker`:

```csharp
[Header("Health bar")]
[Tooltip("HealthBar prefab — see Assets/Prefabs/HealthBar.prefab (created in Task 8).")]
public GameObject HealthBarPrefab;
public float      HealthBarHeightOffset = 1.2f;
```

- [ ] **Step 3: Bake the new fields**

In the `Baker.Bake` method, extend the `AddComponent` payload. Locate the existing `BlueColor = ...` line and append:

```csharp
                    BlueColor = new float4(authoring.BlueColor.r, authoring.BlueColor.g, authoring.BlueColor.b, authoring.BlueColor.a),

                    HealthBarPrefab = authoring.HealthBarPrefab != null
                        ? GetEntity(authoring.HealthBarPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    HealthBarHeightOffset = authoring.HealthBarHeightOffset,
```

(The null-guard lets the project compile and run before the HealthBar prefab is created in Task 8.)

- [ ] **Step 4: Compile check**

Unity MCP: call `Unity_GetConsoleLogs`. Expected: `"success": true` and no compile errors. If a "field not assigned" warning appears for `HealthBarPrefab`, that's expected until Task 12.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs
git commit -m "feat(battle): add HealthBarPrefab + HealthBarHeightOffset to BattleConfig"
```

---

## Task 2: Replicate `Health.Current` to clients

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs`

- [ ] **Step 1: Change `Health`'s ghost-prefab scope and mark `Current` replicated**

In `SoldierAuthoring.cs`, find the existing `Health` struct:

```csharp
    // Server-only.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;
    }
```

Replace with:

```csharp
    // Replicated to clients: Current is per-tick, Max stays server-side
    // (clients read BattleConfig.MaxHealth instead — see design doc).
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct Health : IComponentData
    {
        [GhostField] public float Current;
        public float Max;
    }
```

- [ ] **Step 2: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors. Netcode's source generator runs on Health's serializer; expect a brief recompile.

- [ ] **Step 3: Verify pre-existing tests still pass**

Run Snippet A (no filter — all EditMode tests). Expected log line: `DONE passed=18 failed=0 skipped=0`. If the pass count changes, investigate before continuing.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs
git commit -m "feat(battle): replicate Health.Current to clients via GhostField"
```

---

## Task 3: Create `HealthBarAuthoring.cs` (components + Baker)

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Authoring/HealthBarAuthoring.cs`

Follows the same one-file-per-prefab pattern as `SoldierAuthoring.cs`: component definitions live in the same file as the MonoBehaviour + Baker that uses them. The Baker stays in this task so the file is complete and the Soldier-side tests in Tasks 5 and 7 can compile.

- [ ] **Step 1: Write the file**

```csharp
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace Demo
{
    // On a soldier (client-only): reference to that soldier's bar entity.
    public struct HealthBarRef : IComponentData
    {
        public Entity Bar;
    }

    // On a bar entity (client-only): back-pointer to its owning soldier.
    // Not used in v1's hot path (HealthBarUpdateSystem indexes from the soldier
    // side), but kept for debug introspection and future systems.
    public struct HealthBarLink : IComponentData
    {
        public Entity Owner;
    }

    // Entities.Graphics material-property override. Drives the _Health01
    // shader uniform; value should be in [0, 1].
    [MaterialProperty("_Health01")]
    public struct HealthBarFill : IComponentData
    {
        public float Value;
    }

    // Authoring MonoBehaviour for the HealthBar prefab. Its baker attaches
    // a HealthBarFill component initialized to full so the shader's
    // _Health01 starts at 1.0 before HealthBarUpdateSystem first ticks.
    [DisallowMultipleComponent]
    public class HealthBarAuthoring : MonoBehaviour
    {
        class Baker : Baker<HealthBarAuthoring>
        {
            public override void Bake(HealthBarAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new HealthBarFill { Value = 1f });
            }
        }
    }
}
```

- [ ] **Step 2: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/HealthBarAuthoring.cs Assets/Scripts/Demo/Battle/Authoring/HealthBarAuthoring.cs.meta
git commit -m "feat(battle): add HealthBar components + authoring/baker"
```

---

## Task 4: Extend `EcsTestsBase` with health-bar helpers

**Files:**
- Modify: `Assets/Tests/EditMode/EcsTestsBase.cs`

Subsequent test tasks need (a) a `BattleConfig` carrying a fake bar prefab entity and a height offset, and (b) a helper that constructs the stand-in prefab entity.

- [ ] **Step 1: Add new parameters to `CreateBattleConfig`**

Find the signature of `CreateBattleConfig` and add two parameters at the end (keeping defaults so existing callers compile unchanged):

```csharp
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
            int targetRefreshIntervalTicks = 1,
            Entity healthBarPrefab = default,
            float healthBarHeightOffset = 1.2f)
```

- [ ] **Step 2: Wire the fields into the `SetComponentData` block**

Find the `Manager.SetComponentData(e, new BattleConfig { ... })` block and append the two fields before the closing brace:

```csharp
                CountPerSide               = squadsPerTeam * rows * cols,
                HealthBarPrefab            = healthBarPrefab,
                HealthBarHeightOffset      = healthBarHeightOffset,
            });
```

- [ ] **Step 3: Add the `CreateHealthBarStub` helper**

Add this method to `EcsTestsBase` after `CreateSoldier`:

```csharp
        // Constructs a stand-in entity that masquerades as a baked HealthBar
        // prefab for tests: it carries the components that HealthBarSpawnSystem
        // copies/relies on when instantiating. EntityManager.Instantiate clones
        // these components onto the spawned bar.
        protected Entity CreateHealthBarStub()
        {
            var e = Manager.CreateEntity(typeof(HealthBarFill), typeof(LocalTransform));
            Manager.SetComponentData(e, new HealthBarFill { Value = 1f });
            Manager.SetComponentData(e, LocalTransform.Identity);
            return e;
        }
```

- [ ] **Step 4: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Tests/EditMode/EcsTestsBase.cs
git commit -m "test(battle): extend EcsTestsBase with health-bar helpers"
```

---

## Task 5: Write failing tests for `HealthBarUpdateSystem`

**Files:**
- Create: `Assets/Tests/EditMode/HealthBarUpdateSystemTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class HealthBarUpdateSystemTests : EcsTestsBase
    {
        [Test]
        public void WritesFillRatio_FromCurrentOverConfigMaxHealth()
        {
            CreateBattleConfig(maxHealth: 50f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 1f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 25f, Max = 50f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(0.5f, fill.Value, 0.0001f);
        }

        [Test]
        public void ClampsToZero_WhenConfigMaxHealthIsZero()
        {
            CreateBattleConfig(maxHealth: 0f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 1f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 25f, Max = 0f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(0f, fill.Value, 0.0001f, "must not be NaN or > 0 when Max=0");
        }

        [Test]
        public void ClampsToOne_WhenCurrentExceedsMax()
        {
            CreateBattleConfig(maxHealth: 50f);

            var bar = Manager.CreateEntity(typeof(HealthBarFill));
            Manager.SetComponentData(bar, new HealthBarFill { Value = 0f });

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(Health), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new Health { Current = 999f, Max = 50f });
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = bar });

            CreateAndUpdateSystem<HealthBarUpdateSystem>();

            var fill = Manager.GetComponentData<HealthBarFill>(bar);
            Assert.AreEqual(1f, fill.Value, 0.0001f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Call `Unity_GetConsoleLogs` first. If the console shows compile errors mentioning `HealthBarUpdateSystem` (type or namespace not found), that's the expected failing state — the test file references a system that doesn't exist yet. Proceed to commit.

If Unity hasn't recompiled yet, run Snippet B (force domain reload), then call `Unity_GetConsoleLogs` again. Confirm the compile errors are present.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/HealthBarUpdateSystemTests.cs Assets/Tests/EditMode/HealthBarUpdateSystemTests.cs.meta
git commit -m "test(battle): add failing HealthBarUpdateSystem tests"
```

---

## Task 6: Implement `HealthBarUpdateSystem` to make tests pass

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs`

- [ ] **Step 1: Write the system**

`[UpdateAfter(typeof(HealthBarSpawnSystem))]` is commented out for now — that type doesn't exist yet and would prevent this file from compiling. Task 8 re-enables it once the spawn system is in place.

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Demo
{
    // Client-only presentation. Each frame, writes
    // saturate(Health.Current / BattleConfig.MaxHealth) into the linked
    // HealthBarFill so the shader's _Health01 stays in sync with HP.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    // [UpdateAfter(typeof(HealthBarSpawnSystem))]  // re-enable in Task 8
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct HealthBarUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float maxHealth = SystemAPI.GetSingleton<BattleConfig>().MaxHealth;
            var fillLookup  = SystemAPI.GetComponentLookup<HealthBarFill>(false);

            state.Dependency = new WriteFillJob
            {
                MaxHealth  = maxHealth,
                FillLookup = fillLookup,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct WriteFillJob : IJobEntity
        {
            public float MaxHealth;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HealthBarFill> FillLookup;

            void Execute(in Health health, in HealthBarRef barRef)
            {
                if (barRef.Bar == Entity.Null) return;
                if (!FillLookup.HasComponent(barRef.Bar)) return;

                float ratio = MaxHealth > 0f
                    ? math.saturate(health.Current / MaxHealth)
                    : 0f;
                FillLookup[barRef.Bar] = new HealthBarFill { Value = ratio };
            }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run Snippet A with the filter scoped to this class:
```
groupNames = new[] { "^Demo\\.Tests\\.HealthBarUpdateSystemTests$" },
```
Expected log: `DONE passed=3 failed=0 skipped=0`.

If the call fails or `failed > 0`: read the appended failure lines. The most common cause is the `IJobEntity` source-generator not having run yet — execute Snippet B (force compilation) and retry Snippet A.

- [ ] **Step 3: Compile check + console**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs.meta
git commit -m "feat(battle): HealthBarUpdateSystem writes per-soldier fill"
```

---

## Task 7: Write failing tests for `HealthBarSpawnSystem`

**Files:**
- Create: `Assets/Tests/EditMode/HealthBarSpawnSystemTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo.Tests
{
    public class HealthBarSpawnSystemTests : EcsTestsBase
    {
        [Test]
        public void SpawnsBar_LinksFromSoldier_AndParentsToSoldier()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab, healthBarHeightOffset: 1.2f);

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform));
            Manager.SetComponentData(soldier, LocalTransform.FromPosition(new float3(3f, 0f, 7f)));

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            Assert.IsTrue(Manager.HasComponent<HealthBarRef>(soldier),
                "soldier should gain HealthBarRef");
            var barRef = Manager.GetComponentData<HealthBarRef>(soldier);
            Assert.AreNotEqual(Entity.Null, barRef.Bar);
            Assert.IsTrue(Manager.Exists(barRef.Bar));

            Assert.IsTrue(Manager.HasComponent<Parent>(barRef.Bar),
                "bar should have a Parent component");
            Assert.AreEqual(soldier, Manager.GetComponentData<Parent>(barRef.Bar).Value);

            Assert.IsTrue(Manager.HasComponent<HealthBarLink>(barRef.Bar));
            Assert.AreEqual(soldier, Manager.GetComponentData<HealthBarLink>(barRef.Bar).Owner);

            var localT = Manager.GetComponentData<LocalTransform>(barRef.Bar);
            Assert.AreEqual(new float3(0f, 1.2f, 0f), localT.Position);
        }

        [Test]
        public void AddsLinkedEntityGroup_SoDestroyingSoldierDestroysBar()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab);

            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform));

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            Assert.IsTrue(Manager.HasBuffer<LinkedEntityGroup>(soldier));
            var group = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(2, group.Length);
            Assert.AreEqual(soldier, group[0].Value, "element 0 must be the root entity");

            var bar = Manager.GetComponentData<HealthBarRef>(soldier).Bar;
            Assert.AreEqual(bar, group[1].Value);

            Manager.DestroyEntity(soldier);
            Assert.IsFalse(Manager.Exists(bar), "bar should be cascaded-destroyed");
        }

        [Test]
        public void DoesNotRespawn_WhenSoldierAlreadyHasHealthBarRef()
        {
            var prefab = CreateHealthBarStub();
            CreateBattleConfig(healthBarPrefab: prefab);

            var existingBar = Manager.CreateEntity(typeof(HealthBarFill));
            var soldier = Manager.CreateEntity(
                typeof(Soldier), typeof(LocalTransform), typeof(HealthBarRef));
            Manager.SetComponentData(soldier, new HealthBarRef { Bar = existingBar });

            // Count entities before
            var beforeQuery = Manager.CreateEntityQuery(typeof(HealthBarFill));
            int beforeCount = beforeQuery.CalculateEntityCount();
            beforeQuery.Dispose();

            CreateAndUpdateSystem<HealthBarSpawnSystem>();

            // The same single HealthBarFill entity (existingBar) should still be the only one.
            var afterQuery = Manager.CreateEntityQuery(typeof(HealthBarFill));
            int afterCount = afterQuery.CalculateEntityCount();
            afterQuery.Dispose();

            Assert.AreEqual(beforeCount, afterCount, "no new bar should be spawned");
            Assert.AreEqual(existingBar, Manager.GetComponentData<HealthBarRef>(soldier).Bar);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Call `Unity_GetConsoleLogs`. Expected: compile errors mentioning `HealthBarSpawnSystem` (system type does not exist yet). If no errors yet, run Snippet B then re-check.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/HealthBarSpawnSystemTests.cs Assets/Tests/EditMode/HealthBarSpawnSystemTests.cs.meta
git commit -m "test(battle): add failing HealthBarSpawnSystem tests"
```

---

## Task 8: Implement `HealthBarSpawnSystem`

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/HealthBarSpawnSystem.cs`
- Modify: `Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs` (re-enable the `[UpdateAfter]`)

This system uses `EntityManager` directly (not a deferred ECB) because (a) per-soldier instantiation is rare — only fires when a ghost first appears on the client — and (b) it avoids the test-only friction of standing up `BeginPresentationEntityCommandBufferSystem`.

- [ ] **Step 1: Write the system**

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Demo
{
    // Client-only presentation. For each Soldier without a HealthBarRef,
    // instantiates the HealthBar prefab, parents it to the soldier, and
    // registers the link via LinkedEntityGroup so ghost despawn cascades.
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct HealthBarSpawnSystem : ISystem
    {
        EntityQuery _needsBar;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();

            _needsBar = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Soldier, LocalTransform>()
                .WithNone<HealthBarRef>()
                .Build(ref state);
            state.RequireForUpdate(_needsBar);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            if (config.HealthBarPrefab == Entity.Null) return;

            var em = state.EntityManager;
            using var soldiers = _needsBar.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < soldiers.Length; i++)
            {
                var soldier = soldiers[i];

                var bar = em.Instantiate(config.HealthBarPrefab);

                em.AddComponentData(bar, new Parent { Value = soldier });
                em.AddComponentData(bar, new HealthBarLink { Owner = soldier });
                em.SetComponentData(bar, LocalTransform.FromPosition(
                    new float3(0f, config.HealthBarHeightOffset, 0f)));

                em.AddComponentData(soldier, new HealthBarRef { Bar = bar });

                // LinkedEntityGroup: element 0 must be the root entity for
                // DestroyEntity cascades.
                var group = em.AddBuffer<LinkedEntityGroup>(soldier);
                group.Add(new LinkedEntityGroup { Value = soldier });
                group.Add(new LinkedEntityGroup { Value = bar });
            }
        }
    }
}
```

- [ ] **Step 2: Re-enable the `[UpdateAfter]` on `HealthBarUpdateSystem`**

Open `HealthBarUpdateSystem.cs`. Replace:

```csharp
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    // [UpdateAfter(typeof(HealthBarSpawnSystem))]  // re-enable in Task 8
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
```

with:

```csharp
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(HealthBarSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
```

- [ ] **Step 3: Run all EditMode tests**

Run Snippet A with no filter (all EditMode tests).
Expected log: `DONE passed=24 failed=0 skipped=0` (18 pre-existing + 6 new health-bar tests).

If a pre-existing test fails because `BattleConfig` no longer has expected defaults: re-check Task 4 Step 2 — the new fields should default to `Entity.Null` / `1.2f` and not overwrite anything.

- [ ] **Step 4: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/HealthBarSpawnSystem.cs Assets/Scripts/Demo/Battle/System/HealthBarSpawnSystem.cs.meta Assets/Scripts/Demo/Battle/System/HealthBarUpdateSystem.cs
git commit -m "feat(battle): HealthBarSpawnSystem instantiates + parents bars per soldier"
```

---

## Task 9: Write the URP HealthBar shader

**Files:**
- Create: `Assets/Shaders/HealthBar.shader`

The shader is unlit URP, billboards via the view matrix in the vertex stage, and computes a green→yellow→red gradient + fill mask in the fragment stage. `_Health01` is exposed both as a regular material property (so the Inspector works) and as a DOTS-instanced property (so `[MaterialProperty]` overrides apply per soldier).

- [ ] **Step 1: Create the directory and write the shader**

```bash
mkdir -p Assets/Shaders
```

File `Assets/Shaders/HealthBar.shader`:

```hlsl
Shader "Demo/HealthBar"
{
    Properties
    {
        _Health01 ("Health (0..1)", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HealthBar"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float _Health01;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _Health01)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Health01 UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Health01)
            #endif

            static const float BarWidth  = 0.8;
            static const float BarHeight = 0.1;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Camera-facing billboard. The quad's object-space vertices are
                // assumed in [-0.5, 0.5]^2 (Unity built-in Quad). We expand
                // around the bar's world-space origin using camera-right/up
                // extracted from the view matrix.
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;

                float3 offset = camRight * (IN.positionOS.x * BarWidth)
                              + camUp    * (IN.positionOS.y * BarHeight);
                float3 worldPos = originWS + offset;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float h      = saturate(_Health01);
                float filled = step(IN.uv.x, h);

                // Gradient: red -> yellow at h=0.5 -> green at h=1.0
                float3 redToYellow   = lerp(float3(1, 0, 0), float3(1, 1, 0), saturate(h * 2.0));
                float3 fillCol       = lerp(redToYellow,   float3(0, 1, 0), saturate(h * 2.0 - 1.0));
                float3 bgCol         = float3(0.08, 0.08, 0.08);

                float3 col = lerp(bgCol, fillCol, filled);
                float  a   = lerp(0.55,  1.0,     filled);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```

- [ ] **Step 2: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero shader errors. Unity will auto-import. If the console shows "Shader is not supported": the URP package is missing — verify with `ls Packages/manifest.json | grep urp`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Shaders/HealthBar.shader Assets/Shaders/HealthBar.shader.meta Assets/Shaders/.meta 2>/dev/null
git commit -m "feat(battle): add Demo/HealthBar URP shader (billboard + gradient)"
```

---

## Task 10: Create the `HealthBar` material via MCP

**Files:**
- Create: `Assets/Materials/HealthBar.mat`

- [ ] **Step 1: Run the material-create script**

Call `Unity_RunCommand` with this code:

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var shader = Shader.Find("Demo/HealthBar");
        if (shader == null) { result.LogError("Shader 'Demo/HealthBar' not found"); return; }

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var mat = new Material(shader) { name = "HealthBar", enableInstancing = true };
        const string path = "Assets/Materials/HealthBar.mat";
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        result.RegisterObjectCreation(mat);
        result.Log("Created " + path + " with shader Demo/HealthBar");
    }
}
```

Expected log: `Created Assets/Materials/HealthBar.mat with shader Demo/HealthBar`. If the shader can't be found, Task 9 hasn't compiled — call Snippet B and retry.

- [ ] **Step 2: Compile check**

`Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Materials
git commit -m "feat(battle): add HealthBar material"
```

---

## Task 11: Build the `HealthBar` prefab via MCP

**Files:**
- Create: `Assets/Prefabs/HealthBar.prefab`

The `HealthBarAuthoring` MonoBehaviour was created in Task 3; this task assembles the prefab asset.

- [ ] **Step 1: Run the prefab-create script**

Call `Unity_RunCommand` with:

```csharp
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HealthBar.mat");
        if (mat == null) { result.LogError("Assets/Materials/HealthBar.mat not found"); return; }

        var authoringType = Type.GetType("Demo.HealthBarAuthoring, Assembly-CSharp");
        if (authoringType == null) { result.LogError("Demo.HealthBarAuthoring not found"); return; }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "HealthBar";
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.DestroyImmediate(col);

        go.AddComponent(authoringType);

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial         = mat;
        renderer.shadowCastingMode      = ShadowCastingMode.Off;
        renderer.receiveShadows         = false;
        renderer.lightProbeUsage        = LightProbeUsage.Off;
        renderer.reflectionProbeUsage   = ReflectionProbeUsage.Off;

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        const string path = "Assets/Prefabs/HealthBar.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        result.RegisterObjectCreation(prefab);
        result.Log("Created prefab at " + path);
    }
}
```

Expected log: `Created prefab at Assets/Prefabs/HealthBar.prefab`. If `Demo.HealthBarAuthoring` is reported missing, the Demo assembly hasn't compiled yet — run Snippet B and retry.

- [ ] **Step 2: Compile check**

`Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/HealthBar.prefab Assets/Prefabs/HealthBar.prefab.meta
git commit -m "feat(battle): add HealthBar prefab"
```

---

## Task 12: Wire the prefab into `BattleSub.unity` via MCP

**Files:**
- Modify: `Assets/Scenes/BattleSub.unity`

- [ ] **Step 1: Run the scene-wiring script**

Call `Unity_RunCommand` with:

```csharp
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        const string scenePath  = "Assets/Scenes/BattleSub.unity";
        const string prefabPath = "Assets/Prefabs/HealthBar.prefab";

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { result.LogError(prefabPath + " not found"); return; }

        var authoringType = Type.GetType("Demo.BattleConfigAuthoring, Assembly-CSharp");
        if (authoringType == null) { result.LogError("Demo.BattleConfigAuthoring not found"); return; }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            Component target = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                target = root.GetComponentInChildren(authoringType, includeInactive: true);
                if (target != null) break;
            }
            if (target == null) { result.LogError("BattleConfigAuthoring not in BattleSub"); return; }

            var so = new SerializedObject(target);
            var prop = so.FindProperty("HealthBarPrefab");
            if (prop == null) { result.LogError("HealthBarPrefab field not found on BattleConfigAuthoring"); return; }
            prop.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            { result.LogError("Failed to save " + scenePath); return; }

            result.Log("Wired HealthBar.prefab into BattleConfigAuthoring on " + target.gameObject.name);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }
}
```

Expected log: `Wired HealthBar.prefab into BattleConfigAuthoring on <go-name>`.

- [ ] **Step 2: Compile + console check**

`Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scenes/BattleSub.unity
git commit -m "feat(battle): wire HealthBar prefab into BattleSub"
```

---

## Task 13: In-Editor validation via Unity MCP

**Files:** none — validation only.

Play-mode validation happens across three MCP calls because entering Play mode is asynchronous: the first call schedules entry + the engagement window + capture + exit. Snapshots land in `Assets/Captures/`.

- [ ] **Step 1: Run the play+capture+exit script**

Call `Unity_RunCommand` with:

```csharp
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

internal class CommandScript : IRunCommand
{
    static int _frame;
    static int _capturesTaken;

    public void Execute(ExecutionResult result)
    {
        const string scenePath = "Assets/Scenes/BattleScene.unity";

        // Open the scene if it's not already the active one.
        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        if (!Directory.Exists("Assets/Captures"))
            Directory.CreateDirectory("Assets/Captures");

        _frame = 0;
        _capturesTaken = 0;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
        result.Log("Scheduled Play+capture+exit. Watch console for capture confirmations.");
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying && _frame < 30) { _frame++; return; }
        _frame++;

        // ~2s after entering play: first capture (bars should be full green).
        if (_capturesTaken == 0 && _frame > 120)
        {
            Capture("healthbar_t0_full.png");
            _capturesTaken = 1;
        }
        // ~6s after entering play: second capture (mid-combat).
        else if (_capturesTaken == 1 && _frame > 360)
        {
            Capture("healthbar_t1_combat.png");
            _capturesTaken = 2;
        }
        // ~10s: exit play mode.
        else if (_capturesTaken == 2 && _frame > 600)
        {
            EditorApplication.ExitPlaymode();
            EditorApplication.update -= Tick;
            Debug.Log("[HEALTHBAR-VALIDATE] Done");
        }
    }

    static void Capture(string name)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[HEALTHBAR-VALIDATE] no main camera"); return; }
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        cam.targetTexture = null;
        RenderTexture.active = null;
        var bytes = tex.EncodeToPNG();
        var path = "Assets/Captures/" + name;
        File.WriteAllBytes(path, bytes);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Debug.Log("[HEALTHBAR-VALIDATE] captured " + path);
    }
}
```

This kicks off Play mode and schedules two captures (~2s and ~6s after entering Play) followed by an automatic exit. The script returns immediately; Unity continues running it in the background.

- [ ] **Step 2: Wait for the capture run to finish**

Wait ~15 seconds, then call `Unity_GetConsoleLogs` and look for:
- `[HEALTHBAR-VALIDATE] captured Assets/Captures/healthbar_t0_full.png`
- `[HEALTHBAR-VALIDATE] captured Assets/Captures/healthbar_t1_combat.png`
- `[HEALTHBAR-VALIDATE] Done`

If `Done` is not present yet, wait longer and re-check. If errors appear (e.g., null main camera, missing scene), investigate.

- [ ] **Step 3: Inspect the captures**

Use the `Read` tool on `Assets/Captures/healthbar_t0_full.png` and `Assets/Captures/healthbar_t1_combat.png`. Confirm visually:
- `t0_full`: green bars are visible above every soldier, billboarded to the camera (oriented horizontally).
- `t1_combat`: bars on damaged soldiers have shortened and shifted yellow/orange/red.

- [ ] **Step 4: Confirm bars despawn with their soldier**

Run another short MCP capture window from a later moment (e.g., 12s into Play) by editing the `_frame > 600` threshold up to `> 720` and re-running Step 1. After most of one team has fallen, the capture should show no orphaned bars hanging where dead soldiers were.

(If both teams are still alive at the long timestamp, increase further; if you're already confident the `LinkedEntityGroup` cleanup works from unit tests, you may skip this step.)

- [ ] **Step 5: Console hygiene**

`Unity_GetConsoleLogs`: zero errors. Warnings about `MaterialPropertyOverride` are acceptable only if the bars rendered correctly in Step 3.

- [ ] **Step 6: Commit captures (optional)**

If you want the screenshots in the repo for review:

```bash
git add Assets/Captures
git commit -m "chore(battle): add HealthBar validation captures"
```

Otherwise, add `Assets/Captures` to `.gitignore` before committing.

- [ ] **Step 7: Final commit (optional notes)**

Only if any tuning changes were made (e.g., `HealthBarHeightOffset`):

```bash
git add Assets/Scenes/BattleSub.unity
git commit -m "chore(battle): tune HealthBarHeightOffset after visual review"
```

---

## Done criteria

- All EditMode tests pass, including the 6 new health-bar tests.
- BattleScene shows a green→yellow→red gradient bar above every soldier; bars track soldier transform, billboard to the camera, and despawn with the soldier.
- Zero compiler errors in `Unity_GetConsoleLogs` at the end of the session.
- No regressions in pre-existing tests or the existing BattleScene HUD count.
