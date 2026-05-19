# Squad-Formation Battle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `BattleScene` from per-soldier nearest-enemy chase to rigid squad-based formations: squads pick targets and advance as blocks; soldiers hold their slot; front rank fights; squad compacts every ~0.33 s.

**Architecture:** A new server-only `Squad` entity owns each regiment's anchor transform, shape, and roster (via `DynamicBuffer<SquadMember>`). Soldiers reference their squad via `SquadMembership`. Five server systems replace the existing two: `SquadTargetingSystem` → `SquadMovementSystem` → `SoldierSlotFollowSystem` → modified `MeleeDamageSystem` → `DeathSystem` (unchanged) → `SquadCompactionSystem`. The physics broadphase is no longer used.

**Tech Stack:** Unity 6000.4.1f1, Entities 1.x, Burst + Jobs, Netcode for Entities (server-only entities, no replication of squad-level data).

**Reference spec:** [docs/superpowers/specs/2026-05-19-squad-formation-battle-design.md](../specs/2026-05-19-squad-formation-battle-design.md)

**Testing approach:** No automated tests in this iteration (project has no `.asmdef` test setup). After each task, save and confirm zero Unity console errors via `mcp__unity-mcp__Unity_GetConsoleLogs`. Final task is a manual smoke test in BattleScene.

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

Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `success: true`, zero compilation errors. The four new types are defined but no system writes to them yet — no behavior change.

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

The `.meta` file is created automatically by Unity when the new `.cs` file is recognized; include it in the commit.

---

## Task 2: Extend BattleConfig with squad fields (atomic rename)

Add new squad-shape and squad-behavior fields to `BattleConfig` and `BattleConfigAuthoring`. Rename `Spacing` → `SquadSpacing` and `MoveSpeed` → `SoldierStepSpeed`. Update all current references in the same task so the project compiles end-to-end.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs`
- Modify: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs:63` (rename Spacing reference)
- Modify: `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs:31` (rename MoveSpeed reference)

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

- [ ] **Step 2: Fix BattleSpawnSystem's Spacing reference**

In `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs`, line 63 currently reads:

```csharp
                Spacing        = config.Spacing,
```

Change to:

```csharp
                Spacing        = config.SquadSpacing,
```

There is a second occurrence at line 86 (the `initBlue` job) — change that one too.

- [ ] **Step 3: Fix SoldierMovementSystem's MoveSpeed reference**

In `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs`, line 31 currently reads:

```csharp
                MoveSpeed   = config.MoveSpeed,
```

Change to:

```csharp
                MoveSpeed   = config.SoldierStepSpeed,
```

- [ ] **Step 4: Confirm authoring asset still bakes correctly**

The `BattleConfigAuthoring` component lives on a GameObject inside `Assets/Scenes/BattleSub.unity`. `[FormerlySerializedAs]` preserves the old `Spacing` and `MoveSpeed` values into the new fields, so no manual re-entry is needed. Verify in the Editor:

1. Open `Assets/Scenes/BattleScene.unity`; open the embedded subscene `BattleSub`.
2. Select the `BattleConfig` GameObject.
3. Confirm the new "Squad shape" / "Squad behavior" / "Combat tuning" headers appear, that `SquadSpacing` is 1.5 (preserved from `Spacing`), and `SoldierStepSpeed` is 2 (preserved from `MoveSpeed`).
4. Save the subscene if any value was changed.

- [ ] **Step 5: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → `success: true`, zero errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs \
        Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs \
        Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs \
        Assets/Scenes/BattleSub.unity
git commit -m "$(cat <<'EOF'
feat(battle): extend BattleConfig with squad shape and behavior

Renames Spacing -> SquadSpacing and MoveSpeed -> SoldierStepSpeed.
Adds SquadsPerTeam/SquadRows/SquadCols, SquadAdvanceSpeed,
SquadRotationSpeed, ContactMargin, CompactionIntervalTicks. Drops the
unused SearchRadius field. CountPerSide is now derived from squad shape.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Add SquadMembership to SoldierAuthoring (keep Target for now)

Add `SquadMembership` to the soldier baker so newly-baked soldiers carry the component (zero-initialized; `BattleSpawnSystem` fills it in at runtime). Keep `Target` baking in place so existing systems still compile — `Target` will be removed in Task 9 after its last consumer is rewritten.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs:72` (insert after Target line)

- [ ] **Step 1: Add the SquadMembership component to the baker**

Find this block in `SoldierAuthoring.cs` (around line 67-72):

```csharp
                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new SoldierColor { Value = new float4(1, 1, 1, 1) });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new Target { Value = Entity.Null });
```

Add this line immediately after `AddComponent(entity, new Target { ... });`:

```csharp
                AddComponent(entity, new SquadMembership { Squad = Entity.Null, SlotIndex = -1 });
```

The resulting block reads:

```csharp
                AddComponent<Soldier>(entity);
                AddComponent(entity, new Team { Value = 0 });
                AddComponent(entity, new SoldierColor { Value = new float4(1, 1, 1, 1) });
                AddComponent(entity, new Health { Current = 0f, Max = 0f });
                AddComponent(entity, new AttackStats { Range = 0f, Dps = 0f });
                AddComponent(entity, new Target { Value = Entity.Null });
                AddComponent(entity, new SquadMembership { Squad = Entity.Null, SlotIndex = -1 });
```

- [ ] **Step 2: Re-bake the soldier prefab**

In Unity Editor: the soldier prefab lives in `Assets/Prefabs/`. When the authoring class changes, Unity should re-bake automatically when entering Play mode. No manual rebake needed.

- [ ] **Step 3: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs
git commit -m "$(cat <<'EOF'
feat(battle): bake SquadMembership onto soldiers

New component starts zero-initialized; BattleSpawnSystem will set Squad
and SlotIndex once squad entities exist. Target is kept for now — its
last consumer (MeleeDamageSystem) is replaced in a later task.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Rewrite BattleSpawnSystem to spawn squads

Spawn `2 * SquadsPerTeam` Squad entities, lay them out in a line per team, bulk-instantiate soldiers, populate each squad's `SquadMember` buffer, and set each soldier's `SquadMembership`. Soldier initial positions are computed from `(squadAnchor, slotIndex)` — soldiers spawn directly into their slot world position so the formation looks correct on tick 0.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs` (full rewrite)

- [ ] **Step 1: Replace BattleSpawnSystem.cs with the squad-aware version**

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

            // 1. Create Squad entity archetype + entities (one batch per team).
            var squadArch = em.CreateArchetype(
                typeof(Squad),
                typeof(SquadTarget),
                typeof(SquadMember),
                typeof(LocalTransform),
                typeof(LocalToWorld));

            var redSquads  = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);
            var blueSquads = em.CreateEntity(squadArch, squadsPerTeam, Allocator.TempJob);

            // Red squads face +X (toward blue). Blue squads face -X.
            quaternion redFacing  = quaternion.LookRotationSafe(new float3( 1, 0, 0), math.up());
            quaternion blueFacing = quaternion.LookRotationSafe(new float3(-1, 0, 0), math.up());

            for (int i = 0; i < squadsPerTeam; i++)
            {
                float offsetZ = (i - (squadsPerTeam - 1) * 0.5f) * squadStrideZ;

                var redPos = (float3)config.RedCenter + new float3(0, 0, offsetZ);
                em.SetComponentData(redSquads[i], new Squad
                {
                    Team    = 0,
                    Rows    = rows,
                    Cols    = cols,
                    Spacing = spacing,
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
                    Team    = 1,
                    Rows    = rows,
                    Cols    = cols,
                    Spacing = spacing,
                });
                em.SetComponentData(blueSquads[i], new SquadTarget { Value = Entity.Null });
                em.SetComponentData(blueSquads[i], LocalTransform.FromPositionRotation(bluePos, blueFacing));
                var blueBuf = em.GetBuffer<SquadMember>(blueSquads[i]);
                blueBuf.ResizeUninitialized(soldiersPerSquad);
                for (int s = 0; s < soldiersPerSquad; s++)
                    blueBuf[s] = new SquadMember { Value = Entity.Null };
            }

            // 2. Bulk-instantiate soldiers.
            var reds  = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);
            var blues = em.Instantiate(config.SoldierPrefab, countPerSide, Allocator.TempJob);

            // 3. Capture squad anchor transforms for the jobs (TempJob arrays).
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

            // 4. Initialize per-soldier data in parallel.
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

            // 5. Populate SquadMember buffers serially (one cross-cutting write per soldier).
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

            // 6. Dispose temporary arrays.
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
                int col        = slot % Cols;
                int row        = slot / Cols;

                float localX = (col - (Cols - 1) * 0.5f) * Spacing;
                float localZ = ((Rows - 1) * 0.5f - row) * Spacing;
                var   local  = new float3(localX, 0f, localZ);
                var   world  = SquadAnchorPos[squadIndex] + math.mul(SquadAnchorRot[squadIndex], local);

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
- `OnCreate` no longer caches `ComponentLookup` fields — they were unused beyond `OnUpdate`. Pulling them inside `OnUpdate` keeps the system one-shot-style simple.
- Step 5 (buffer population) runs single-threaded on the main thread after the parallel job completes. With `2 * countPerSide` (≤ 200 at default config, ~20k at production scale) buffer writes, the cost is negligible and avoids cross-entity write races.
- Squad entities have `LocalTransform` so `SquadMovementSystem` can read/write them; no `Parent` linkage to soldiers — soldiers read their squad's transform via `ComponentLookup<LocalTransform>` directly.

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Run BattleScene briefly to confirm spawn count**

Enter Play mode in BattleScene. The Debug.Log should report e.g. `spawned 2 red + 2 blue squads, 100 soldiers per side.` Soldiers will spawn into their slot positions but then immediately run toward the nearest enemy (old `TargetingSystem` + `SoldierMovementSystem` still active). That's expected — they get replaced in later tasks.

Exit Play mode.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/BattleSpawnSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): spawn Squad entities and wire SquadMembership

BattleSpawnSystem now creates 2 * SquadsPerTeam Squad entities laid out
in a line per team, bulk-instantiates soldiers, and populates each
squad's SquadMember buffer + each soldier's SquadMembership. Soldier
initial positions are computed from (squad anchor, slot index) so the
formation is correct on tick 0.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Create SquadTargetingSystem

For each squad, scan all enemy squads via a Burst job and set `SquadTarget.Value` to the nearest enemy squad anchor. O(squads²); at ~100 squads/team the inner loop runs ~20k comparisons per refresh — trivial.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs`

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

            // Snapshot every squad (entity, team, position) into TempJob arrays
            // so each entity in the parallel job can scan over all peers.
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
        // Each squad writes its own slot — but we need a deterministic
        // index per squad. Use a chunk-stable enumeration: iterate via
        // EntityIndexInQuery (Entities.ForEach equivalent in IJobEntity).
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
        [ReadOnly] public NativeArray<SquadSnapshot> Snapshot;

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

`[Unity.Entities.EntityIndexInQuery]` gives the parallel job a stable per-entity index suitable for indexing the snapshot array.

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs Assets/Scripts/Demo/Battle/System/SquadTargetingSystem.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadTargetingSystem (nearest-enemy-squad)

Throttled by TargetRefreshIntervalTicks. Snapshots all squads, then
each squad picks the nearest enemy-team squad by anchor distance.
O(squads^2); trivial at ~100 squads/team.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Create SquadMovementSystem

Each squad with a valid target lerps rotation toward `LookRotation(target - self)` and advances along the facing direction at `SquadAdvanceSpeed`, stopping when anchor distance falls below the engagement threshold.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs`

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

        [ReadOnly] public ComponentLookup<Squad> SquadLookup;
        // We only read target squads' positions via XformLookup; the entity
        // we write to is the squad we're iterating, never the same as the
        // target. NativeDisableContainerSafetyRestriction tells the safety
        // system this is safe (we never read+write the same entity).
        [ReadOnly, NativeDisableContainerSafetyRestriction]
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

            // Rotate toward target (lerp).
            float3 desiredFwd = toTarget / dist;
            quaternion desiredRot = quaternion.LookRotationSafe(desiredFwd, math.up());
            float slerpT = math.saturate(RotationSpeed * Dt);
            xform.Rotation = math.slerp(xform.Rotation, desiredRot, slerpT);

            // Engagement distance threshold: stop advancing.
            int targetRows = SquadLookup[target.Value].Rows;
            float engageDist = (self.Rows  - 1) * 0.5f * self.Spacing
                             + (targetRows - 1) * 0.5f * self.Spacing
                             + AttackRange
                             + ContactMargin;

            if (dist <= engageDist) return;

            // Advance along current facing's forward (+Z in squad-local frame).
            float3 fwd = math.mul(xform.Rotation, new float3(0, 0, 1));
            float step = AdvanceSpeed * Dt;
            // Don't overshoot the engagement boundary.
            float maxStep = dist - engageDist;
            step = math.min(step, maxStep);
            xform.Position += fwd * step;
        }
    }
}
```

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs Assets/Scripts/Demo/Battle/System/SquadMovementSystem.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadMovementSystem

Each squad with a valid SquadTarget lerps rotation toward the line
between anchors at SquadRotationSpeed, then advances along its facing
forward at SquadAdvanceSpeed. Stops advancing at the engagement
distance: (selfRows + targetRows - 2) * spacing/2 + AttackRange +
ContactMargin.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Replace SoldierMovementSystem with SoldierSlotFollowSystem

This task atomically: deletes `SoldierMovementSystem.cs`, creates `SoldierSlotFollowSystem.cs`, and updates `MeleeDamageSystem`'s `[UpdateAfter]` attribute to point at the new system. Without all three changes together, the project fails to compile.

**Files:**
- Delete: `Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs` and its `.meta`
- Create: `Assets/Scripts/Demo/Battle/System/SoldierSlotFollowSystem.cs`
- Modify: `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs:22` (UpdateAfter target)

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
            // Per-soldier LocalTransform is written via the IJobEntity ref
            // parameter; we read squad LocalTransform via lookup. The two
            // archetypes (Soldier vs Squad) don't overlap, so a single
            // ComponentLookup<LocalTransform> read inside the job is safe
            // with NativeDisableContainerSafetyRestriction.
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

        [ReadOnly] public ComponentLookup<Squad> SquadLookup;
        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform>   XformLookup;

        public void Execute(ref LocalTransform xform, in SquadMembership membership)
        {
            if (membership.Squad == Entity.Null) return;
            if (!SquadLookup.HasComponent(membership.Squad)) return;

            var squad     = SquadLookup[membership.Squad];
            var anchor    = XformLookup[membership.Squad];
            int slot      = membership.SlotIndex;
            int col       = slot % squad.Cols;
            int row       = slot / squad.Cols;
            float localX  = (col - (squad.Cols - 1) * 0.5f) * squad.Spacing;
            float localZ  = ((squad.Rows - 1) * 0.5f - row) * squad.Spacing;
            float3 local  = new float3(localX, 0f, localZ);
            float3 target = anchor.Position + math.mul(anchor.Rotation, local);

            float3 toSlot = target - xform.Position;
            float  dist   = math.length(toSlot);
            float  step   = StepSpeed * Dt;

            if (dist <= step || dist < 1e-4f)
            {
                xform.Position = target;
            }
            else
            {
                xform.Position += (toSlot / dist) * step;
            }
            // Face the squad's facing so soldiers visually line up.
            xform.Rotation = anchor.Rotation;
        }
    }
}
```

- [ ] **Step 2: Update MeleeDamageSystem's `[UpdateAfter]` attribute**

In `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs`, line 22 currently reads:

```csharp
    [UpdateAfter(typeof(SoldierMovementSystem))]
```

Change to:

```csharp
    [UpdateAfter(typeof(SoldierSlotFollowSystem))]
```

The rest of `MeleeDamageSystem` still references the old `Target` component; that gets rewritten in Task 8.

- [ ] **Step 3: Delete SoldierMovementSystem files**

```bash
rm Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs
rm Assets/Scripts/Demo/Battle/System/SoldierMovementSystem.cs.meta
```

- [ ] **Step 4: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. `MeleeDamageSystem` still compiles because `Target` is still defined in `SoldierAuthoring.cs`. `SoldierSlotFollowSystem` now drives all soldier motion.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/Scripts/Demo/Battle/System/
git commit -m "$(cat <<'EOF'
feat(battle): replace SoldierMovementSystem with SoldierSlotFollowSystem

Soldiers now step toward their assigned (squad anchor + slot offset)
world position each tick at SoldierStepSpeed instead of running at an
individual target. MeleeDamageSystem's UpdateAfter is repointed at the
new system. Per-soldier Target is still consumed by MeleeDamageSystem
until Task 8.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Rewrite MeleeDamageSystem for front-rank slot pairing

Front-rank soldiers (`SlotIndex < Squad.Cols`) read their squad's `SquadTarget`, look up the paired enemy by slot index in the target squad's `SquadMember` buffer, and damage that enemy if alive and within `AttackRange`. Back-rank soldiers do not damage. The scatter/gather `NativeStream` shape is preserved.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs` (rewrite jobs; `DamageEvent` and `ReduceDamageJob` unchanged)

- [ ] **Step 1: Replace MeleeDamageSystem.cs with the slot-pairing version**

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
    // Scatter-write per-attacker; gather-apply on victim's Health.
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

                // Front rank only: slot index in [0, Cols).
                if (m.SlotIndex < 0 || m.SlotIndex >= selfSquad.Cols) continue;

                // Pair with target squad's slot index = our column (wrapped).
                var targetSquadEntity = TargetLookup[m.Squad].Value;
                if (targetSquadEntity == Entity.Null) continue;
                if (!BufferLookup.HasBuffer(targetSquadEntity)) continue;
                if (!SquadLookup.HasComponent(targetSquadEntity)) continue;

                var enemyBuf  = BufferLookup[targetSquadEntity];
                var enemySquad = SquadLookup[targetSquadEntity];
                int pairCol   = m.SlotIndex % enemySquad.Cols;
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

    // Single-threaded reduce: read every event, decrement victim Health.
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

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. `TargetingSystem.cs` still exists and still writes to `Target`, but nothing reads `Target` anymore.

- [ ] **Step 3: Run BattleScene briefly to confirm front-rank combat**

Enter Play mode. Expected behavior:
- Two squads per team advance toward each other.
- Squads stop at engagement distance.
- Front-rank soldiers (5 per squad with default 5×10) trade blows; back ranks idle.
- Some deaths occur, but compaction isn't wired yet — dead soldiers leave stale buffer entries.

Exit Play mode.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/MeleeDamageSystem.cs
git commit -m "$(cat <<'EOF'
feat(battle): MeleeDamageSystem uses front-rank slot pairing

Only soldiers with SlotIndex < Squad.Cols (the front rank) deal damage.
Each front-rank soldier in slot i attacks targetSquad.Buffer[i % Cols].
Stale references and dead enemies are skipped silently; compaction
(next task) keeps the buffer fresh.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Delete TargetingSystem and the Target component

Nothing reads `Target` anymore. Remove the system and the component cleanly.

**Files:**
- Delete: `Assets/Scripts/Demo/Battle/System/TargetingSystem.cs` and its `.meta`
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` (remove `Target` struct + baker line)

- [ ] **Step 1: Remove the Target component definition**

In `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs`, delete this block (around lines 51-56):

```csharp
    // Server-only. Refreshed by TargetingSystem every TargetRefreshIntervalTicks.
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct Target : IComponentData
    {
        public Entity Value;
    }
```

- [ ] **Step 2: Remove the Target baker line**

In the same file, in the `Baker.Bake` method, delete this line:

```csharp
                AddComponent(entity, new Target { Value = Entity.Null });
```

The resulting baker block should read:

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

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors. The `Target` symbol is gone; nothing references it.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/Scripts/Demo/Battle/
git commit -m "$(cat <<'EOF'
refactor(battle): remove Target component and TargetingSystem

Both are replaced by squad-level targeting. The physics broadphase has
no consumer now; soldier colliders remain on the prefab for future
ranged/picking use.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Add SquadCompactionSystem

Every `CompactionIntervalTicks` ticks, each squad re-packs its `SquadMember` buffer: drops dead/null entries, reassigns `SlotIndex` on surviving soldiers, updates `Squad.Rows`. Destroys empty squads.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs`

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
            }.Run();  // single-threaded: cross-entity writes via lookups.

            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    public partial struct CompactJob : IJobEntity
    {
        public uint Tick;
        public uint Interval;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<SquadMembership> MembershipLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        public EntityCommandBuffer Ecb;

        public void Execute(Entity squadEntity,
                            ref Squad squad,
                            ref DynamicBuffer<SquadMember> buf)
        {
            // Per-squad stagger so all squads don't compact on the same tick.
            uint squadHash = (uint)squadEntity.Index;
            if (((Tick + squadHash) % Interval) != 0u) return;

            // Gather alive entities into a temp array.
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
                // Squad is wiped — destroy it.
                buf.Clear();
                Ecb.DestroyEntity(squadEntity);
                alive.Dispose();
                return;
            }

            // Re-pack: Cols stays fixed, Rows shrinks.
            int cols    = squad.Cols;
            int newRows = (aliveCount + cols - 1) / cols;

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

Notes:
- Job is single-threaded (`.Run()`) because it writes the buffer of one entity while also writing `SquadMembership` on possibly-distant soldiers via lookup. Compaction happens only every ~10 ticks per squad — running on the main thread is fine.
- `Ecb.DestroyEntity` queues squad destruction; soldier entities are already destroyed by `DeathSystem` before compaction observes them as dead.

- [ ] **Step 2: Verify console clean**

`mcp__unity-mcp__Unity_GetConsoleLogs` → zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs Assets/Scripts/Demo/Battle/System/SquadCompactionSystem.cs.meta
git commit -m "$(cat <<'EOF'
feat(battle): add SquadCompactionSystem

Every CompactionIntervalTicks (default 10), each squad re-packs its
SquadMember buffer: drops dead/null entries, reassigns surviving
soldiers' SlotIndex, recomputes Rows. Destroys squads that hit zero
alive. Staggered per squad via Entity.Index to avoid frame-time
spikes. Single-threaded; budget is negligible at expected squad count.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Smoke test in Unity Editor

Manual end-to-end verification. No code changes — confirm the pipeline behaves as designed.

**Files:** none

- [ ] **Step 1: Open BattleScene and verify console clean at startup**

In Unity Editor, open `Assets/Scenes/BattleScene.unity`. Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `success: true`, zero errors.

- [ ] **Step 2: Enter Play mode**

Click Play. Watch the scene view.

Expected:
- Two red squads on the left, two blue squads on the right (default `SquadsPerTeam = 2`, `5 × 10 = 50` soldiers per squad).
- All four squads slide toward their nearest enemy squad, rotating to face it as they go.
- Squads stop at engagement distance — front ranks are within `AttackRange`, back ranks are visibly behind.
- Front-rank soldiers (10 per squad) trade blows; back ranks hold their slot.
- Soldiers die one by one (HP 50, DPS 25 → ~2 s per kill at full DPS contact).
- After each death, within ~0.33 s the surviving members of that squad shift to a tighter rectangle (Rows shrinks; Cols stays at 10 until alive < 10).
- When one team's squads are wiped, the winning team is the only one with surviving soldiers; the existing `BattleHudController` should display the winner banner.

- [ ] **Step 3: Capture scene view for visual record**

Call `mcp__unity-mcp__Unity_SceneView_Capture2DScene` (or `CaptureMultiAngleSceneView`) mid-battle for a visual record. Save the path of the screenshot.

- [ ] **Step 4: Verify console clean at end of play**

Exit Play mode. Call `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: zero errors, zero exceptions. Warnings about deprecated APIs are acceptable; new errors are not.

- [ ] **Step 5: Final commit (if anything tweaked during smoke test)**

If the smoke test surfaced no issues, no commit is needed. If small fixes were made (e.g., default config tuning), commit them with:

```bash
git add -A Assets/
git commit -m "$(cat <<'EOF'
fix(battle): smoke-test tuning for squad-formation default config

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

If the smoke test reveals a bug (e.g., squads never stop advancing, soldiers spin in place, compaction destroys the wrong entity), stop here and report the symptom — the design will need revisiting before further iteration.

---

## Summary

End state:
- New files: `SquadComponents.cs`, `SquadTargetingSystem.cs`, `SquadMovementSystem.cs`, `SoldierSlotFollowSystem.cs`, `SquadCompactionSystem.cs`.
- Deleted files: `TargetingSystem.cs`, `SoldierMovementSystem.cs`.
- Modified files: `BattleConfigAuthoring.cs`, `SoldierAuthoring.cs`, `BattleSpawnSystem.cs`, `MeleeDamageSystem.cs`.
- Soldiers belong to squads; squads pick squad targets; front rank fights; squads compact periodically.
- Physics broadphase no longer used (kinematic colliders remain on soldier prefab for future use).
- Per-soldier `Target` component is gone.
