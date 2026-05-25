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
- After every Unity-touching task, the engineer calls Unity MCP `Unity_GetConsoleLogs` and expects `"success": true` with zero compiler errors before moving on.
- "Run the tests" means: in Unity Editor, **Window → General → Test Runner → EditMode → Run All** (or the targeted class). Tests pass = green checkmarks, no red.
- Commits use Conventional Commits (the repo's existing style: `feat(battle):`, `fix(battle):`, etc.).

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
- `Assets/Scenes/BattleSub.unity` — assign the `HealthBar` prefab to `BattleConfigAuthoring.HealthBarPrefab` (done in Editor).

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

- [ ] **Step 3: Sanity-tick BattleScene**

In Unity Editor, open `Assets/Scenes/BattleScene.unity` and enter Play mode for ~5 seconds. Confirm:
1. `Unity_GetConsoleLogs` after exiting Play: zero errors.
2. Soldiers still spawn, target each other, and take damage (server-side behavior unchanged).

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

In Unity: **Window → General → Test Runner → EditMode → Run All** (or just this class).
Expected: 3 RED failures — `HealthBarUpdateSystem` does not yet exist (compile error in the test file or missing type reference). This is the expected failing-test state.

If instead all tests are skipped or no errors surface, Unity didn't recompile — re-focus the Editor to trigger a refresh, then run again.

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

Test Runner → Run the `HealthBarUpdateSystemTests` class.
Expected: 3 GREEN.

If a test fails: read the assertion message. Most likely cause is the `IJobEntity` source-generator not running (Unity may need an Editor refocus). Trigger a recompile by saving any C# file.

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

Test Runner → Run `HealthBarSpawnSystemTests`.
Expected: 3 RED (system type does not exist yet → compile or resolution error).

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

Test Runner → Run All.
Expected: all 6 health-bar tests GREEN, plus all pre-existing tests (`SquadGeometryTests`, `SquadCompactionSystemTests`, etc.) still GREEN.

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

## Task 10: Create the `HealthBar` material

**Files:**
- Create: `Assets/Materials/HealthBar.mat`

This task is done in the Unity Editor — there is no automated way to author a `.mat`. The engineer performs these manual steps.

- [ ] **Step 1: Create the directory if needed**

```bash
mkdir -p Assets/Materials
```

- [ ] **Step 2: Create the material in the Editor**

In Unity Project window:
1. Right-click on `Assets/Materials/` → **Create → Material**.
2. Name it `HealthBar`.
3. In the Inspector, click the **Shader** dropdown and select `Demo/HealthBar`.
4. Verify the `Health (0..1)` slider appears and defaults to `1`.
5. Tick **Enable GPU Instancing** if visible (URP unlit usually exposes it).

- [ ] **Step 3: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Materials/HealthBar.mat Assets/Materials/HealthBar.mat.meta Assets/Materials/.meta 2>/dev/null
git commit -m "feat(battle): add HealthBar material"
```

---

## Task 11: Create the `HealthBar` prefab

**Files:**
- Create: `Assets/Prefabs/HealthBar.prefab`

The `HealthBarAuthoring` MonoBehaviour was created in Task 3; this task only builds the prefab asset in the Editor.

- [ ] **Step 1: Create the prefab in the Editor**

In the Unity Editor scene hierarchy (any open scene works for authoring):
1. Right-click in Hierarchy → **3D Object → Quad**. Rename it `HealthBar`.
2. In the Inspector, click **Add Component** → search **HealthBarAuthoring** → add.
3. On the **Mesh Renderer**, drag `Assets/Materials/HealthBar` into the **Materials → Element 0** slot.
4. On the **Mesh Renderer**, disable **Cast Shadows** (set to Off), **Receive Shadows**, **Contribute Global Illumination**. Leave Light Probes/Reflection Probes at Off.
5. Drag the `HealthBar` GameObject from the Hierarchy into `Assets/Prefabs/` to create `Assets/Prefabs/HealthBar.prefab`.
6. Delete the `HealthBar` from the Hierarchy (the prefab is what we want, not a scene instance).

- [ ] **Step 2: Compile check**

Unity MCP `Unity_GetConsoleLogs`: zero errors. (A baking warning about the prefab is fine until Task 12.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/HealthBar.prefab Assets/Prefabs/HealthBar.prefab.meta
git commit -m "feat(battle): add HealthBar prefab"
```

---

## Task 12: Wire the prefab into `BattleSub.unity`

**Files:**
- Modify: `Assets/Scenes/BattleSub.unity` (via Editor)

- [ ] **Step 1: Open the BattleSub subscene**

In Unity: open `Assets/Scenes/BattleScene.unity`, then in Hierarchy open the **BattleSub** subscene (double-click).

- [ ] **Step 2: Assign the prefab**

1. In Hierarchy, select the GameObject carrying `BattleConfigAuthoring` (the one with the existing `Soldier Prefab` reference).
2. In the Inspector, find the new **Health Bar** section.
3. Drag `Assets/Prefabs/HealthBar.prefab` into the **Health Bar Prefab** slot.
4. Leave **Health Bar Height Offset** at `1.2` (or tune).
5. Save the scene (Cmd+S / Ctrl+S).

- [ ] **Step 3: Compile + console check**

Unity MCP `Unity_GetConsoleLogs`: zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/BattleSub.unity
git commit -m "feat(battle): wire HealthBar prefab into BattleSub"
```

---

## Task 13: In-Editor validation via Unity MCP

**Files:** none — validation only.

- [ ] **Step 1: Enter Play mode in BattleScene**

In Unity: open `Assets/Scenes/BattleScene.unity`, press Play. Let it run for ~10 seconds while soldiers spawn and engage.

- [ ] **Step 2: Capture the scene**

Unity MCP `Unity_SceneView_Capture2DScene` (or `Unity_Camera_Capture` on the main game camera).
Expected: green bars visible above every soldier, billboarded to the camera, oriented horizontally regardless of camera pan/zoom.

- [ ] **Step 3: Confirm color shift under combat**

After ~5 seconds of combat, capture again. Expected: bars on damaged soldiers have shortened and shifted yellow/orange/red.

- [ ] **Step 4: Confirm bars despawn with their soldier**

Let combat finish on one team. Confirm dead soldiers' bars are gone (no orphaned bars hanging in space).

- [ ] **Step 5: Console hygiene**

Unity MCP `Unity_GetConsoleLogs`: zero errors at end of session. Warnings related to `MaterialPropertyOverride` are acceptable only if the bars still render correctly; otherwise investigate.

- [ ] **Step 6: Profiler sanity (optional but recommended)**

Open **Window → Analysis → Profiler**, attach to the Editor's client world, look at CPU usage during a Play session. `HealthBarUpdateSystem` should sit well under 0.1 ms at 100/side. Record the number for future regression baselines.

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
