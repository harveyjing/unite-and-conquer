# Battle terrain navigation — design

**Date:** 2026-06-09
**Status:** Approved (brainstorm) — ready for implementation plan
**Scope:** BattleScene · server simulation (`Demo` assembly, `Battle/` subtree)

## Summary

Add **terrain that constrains army movement** to the squad-based BattleScene. v1
delivers two impassable terrain features that share one mechanism:

- **River + Bridge** — impassable water; squads must funnel across a bridge.
- **Valley / Mountain pass** — impassable hills leaving a narrow gap squads march through.

Both are the same navigation problem: *an impassable region with a single
crossing*. A squad whose straight path to its target crosses such a region
**routes to the crossing, re-shapes into a narrow block to fit, marches through,
then re-expands** and resumes its pursuit. Soldiers are unchanged — they keep
following their assigned formation slots; only the squad's path and its
`Rows`/`Cols` change.

Slow terrain (movement cost) and High ground (combat bonus) are **explicitly out
of scope for v1** but are designed for: terrain is modeled as a generic
`TerrainRegion` with reserved fields, so those features later attach as new
*consumers* of the same data without touching navigation.

## Design-vision alignment

Per root `CLAUDE.md`: *does this scale to tens of thousands of entities?* Yes —
the navigating unit is the **squad** (a handful per battle), not the individual
soldier. Soldiers continue to do nothing but follow a slot. The per-soldier hot
path is untouched. Terrain regions are few, hand-authored, analytic shapes;
"does my path cross a barrier?" is a cheap segment test run per squad, not per
soldier. Server-authoritative and deterministic (no physics broadphase, no
floating-point-divergent pathfinder); clients render terrain as plain geometry
and replicate nothing new beyond what squad movement already replicates via
soldier ghost transforms.

## Architecture: one schema, three consumers

Terrain is authored as generic **`TerrainRegion`** data. Three independent
systems consume different fields. **Only the Navigation consumer is built in
v1.**

```
TerrainRegion (authored, baked)
  shape           — box on the field (center, half-extents, yaw)
  passable        — can units enter?
  crossingPortal  — entrance + exit + width  (present only on impassable regions with a gap)
  moveMultiplier  — 1.0 = normal, <1 = slow        [reserved, v1 = 1.0]
  combatModifier  — attack / range / defense bonus  [reserved, v1 = none]
  kind            — river / hills / mud / highground (diagnostics + visuals)
        │
        ├─▶ Navigation  (v1)    reads passable, crossingPortal   → River+Bridge, Valley
        ├─▶ Movement speed (later) reads moveMultiplier          → Slow terrain
        └─▶ Combat (later)         reads combatModifier           → High ground
```

The non-negotiable design rule: **River/Bridge must not be a hardcoded special
case.** It is one authored `TerrainRegion` (impassable) carrying a
`CrossingPortal`. The Valley is another. Adding Slow terrain / High ground later
is "fill two reserved fields + add a small sampling step to the movement / melee
systems" — Navigation is never re-touched.

## Data model

New components (server-relevant; baked from authoring in the `BattleSub`
subscene, following the project's authoring→baker→`ISystem` pattern).

```csharp
// An impassable area. v1 only authors impassable regions; passable/modifier
// fields are reserved so Slow terrain / High ground slot in later.
public struct TerrainRegion : IComponentData
{
    public float3 Center;        // world XZ center (Y ignored for nav)
    public float2 HalfExtents;   // box half-size on XZ
    public float  Yaw;           // rotation about Y (oriented box)
    public byte   Passable;      // 0 = impassable (v1), 1 = passable
    public float  MoveMultiplier;// reserved (Slow terrain), v1 = 1
    public TerrainKind Kind;     // River / Hills / Mud / HighGround
    // CombatModifier (High ground) is intentionally NOT a field yet — it is
    // added with that feature, likely as its own component, so v1 keeps this
    // struct minimal. The schema diagram lists it to show the design intent.
}

// A crossing through an impassable region: entrance on the near approach,
// exit on the far side, plus the corridor width. River bridge and valley
// pass are both expressed this way.
public struct CrossingPortal : IComponentData
{
    public float3 Entrance;      // muster point on the approach side
    public float3 Exit;          // muster point on the far side
    public float  Width;         // usable corridor width (metres)
}
```

A terrain *feature* = one entity carrying `TerrainRegion` (+ `CrossingPortal`
when it has a gap). `Entrance`/`Exit` are symmetric — a squad approaching from
either bank uses whichever endpoint is on its side as the entrance.

**Barrier-crossing test (pure math, `SquadGeometry`-style, unit-tested):** a
squad's straight path crosses a region when the XZ segment `squadPos → targetPos`
intersects the region's oriented box. Implemented as
`SquadGeometry.SegmentIntersectsBox` — transform the segment into the box's local
frame (undo center + yaw) and run a 2D slab test against
`[-HalfExtents, +HalfExtents]`. Y is ignored (regions are vertical prisms).

**Authoring:** `TerrainRegionAuthoring` (+ optional `CrossingPortalAuthoring`)
MonoBehaviours on GameObjects in the `BattleSub` subscene, with gizmos drawing
the box and the entrance/exit points so the designer can line them up with the
visual geometry. The **visual** water plane, bridge mesh, and hill meshes are
plain scene geometry placed to match the authored region — they are not ECS and
carry no simulation meaning.

## Navigation: the squad state machine

A new server system, **`SquadNavigationSystem`**, runs
`UpdateAfter(SquadTargetingSystem)` and `UpdateBefore(SquadMovementSystem)`. It
owns a new per-squad component:

```csharp
public struct SquadNav : IComponentData
{
    public NavState State;       // Pursue | ApproachPortal | Crossing
    public Entity   Portal;      // the CrossingPortal entity being used (or Null)
    public float3   Entrance;    // cached endpoint on our side
    public float3   Exit;        // cached endpoint on far side
    public int      BaseCols;    // full-width Cols to restore after crossing
}
```

State transitions (per squad, per tick):

- **Pursue** — normal behavior. Goal = enemy target squad position. Each tick,
  test whether the segment `self → target` crosses any impassable
  `TerrainRegion`. If it does, pick the **nearest `CrossingPortal`** whose near
  endpoint is on this squad's side, cache `Entrance`/`Exit`, record `BaseCols`,
  and enter **ApproachPortal**.
- **ApproachPortal** — goal = `Entrance` (full formation, no engagement stop).
  When the squad anchor is within an arrival threshold of `Entrance`, **re-shape
  to a narrow block** (see below) and enter **Crossing**.
- **Crossing** — goal = `Exit` (narrow block, no engagement stop). When the
  anchor passes/arrives at `Exit`, **restore `BaseCols`** (re-expand), clear
  `Portal`, and return to **Pursue**.

A squad whose straight path is already clear never leaves **Pursue** — terrain
only engages when a barrier sits between a squad and its target.

### Decoupling movement from targeting

Today `SquadMovementSystem` always moves toward `SquadTarget`. We introduce an
explicit per-squad goal that the navigation system writes and the movement
system consumes:

```csharp
public struct SquadMoveGoal : IComponentData
{
    public float3 Position;   // where to head this tick
    public byte   Engage;     // 1 = apply EngagementDistance stop; 0 = walk fully to Position
}
```

- `SquadNavigationSystem` sets `SquadMoveGoal` every tick: in **Pursue**,
  `Position` = target squad position, `Engage = 1`; in **ApproachPortal** /
  **Crossing**, `Position` = waypoint, `Engage = 0`.
- `SquadMovementSystem` is refactored to rotate toward and advance to
  `SquadMoveGoal.Position`, applying the existing `EngagementDistance` stop only
  when `Engage == 1`. Its current "look up the target squad's transform / rows"
  logic moves up into `SquadNavigationSystem`'s Pursue branch.

This keeps `SquadMovementSystem` a dumb "go to a point" mover and isolates all
terrain awareness in `SquadNavigationSystem` — the seam where a smarter planner
(grid/flow-field) could later replace *how the goal is chosen* without touching
movement, soldiers, formation, or combat.

### Re-shape is just `Cols` + `Rows`

Crossing the bridge = the squad becomes a narrow column. This needs **no buffer
repack and no `SlotIndex` rewrite**, because `SquadCompactionSystem` already
keeps survivors packed into contiguous slots `0..alive-1`, and
`SoldierSlotFollowSystem` derives each soldier's world position from
`SquadGeometry.SlotLocalOffset(SlotIndex, Rows, Cols, spacing)`. Changing the
squad's `Cols` therefore re-lays the same packed soldiers into a different
formation automatically — a soldier in slot 7 moves from row 0 of a 10-wide line
to row 3 of a 2-wide column with no per-soldier write.

- Narrow `Cols` = `SquadGeometry.NarrowColsForWidth(Portal.Width, Spacing)`
  (= `floor(width / spacing)`, clamped ≥ 1) so the block fits the corridor;
  `Rows = SquadGeometry.RowsForAliveCount(alive, narrowCols)`.
- Re-shape = set `Squad.Cols` + `Squad.Rows`. Restore on exit = set them back to
  `BaseCols` (cached on entry) and the matching row count.
- `Squad.Cols` becomes squad state that navigation owns. `SquadCompactionSystem`
  already reads the current `Squad.Cols`, so it keeps compacting correctly
  whether the squad is wide or narrow — the two systems never fight because both
  only ever set `Rows`/`Cols` from the current alive count and current width.
- Transient note: if a squad re-shapes while it still holds dead-but-not-yet-
  compacted soldiers, the narrow block shows gaps until the next compaction
  interval — the same tolerated staleness the formation already exhibits between
  death and compaction.

Because soldiers always follow `SquadGeometry.SlotLocalOffset(slot, Rows, Cols,
spacing)` transformed by the squad's `LocalTransform`, a narrow block whose
width fits the portal keeps every soldier on the bridge/in the pass. The
authored `Portal.Width` and the chosen narrow `Cols` together guarantee
soldiers do not stray into water.

## System ordering (server, `SimulationSystemGroup`)

```
SquadTargetingSystem
SquadNavigationSystem      ← NEW: state machine, sets SquadMoveGoal, triggers re-shape repack
SquadMovementSystem        ← refactored: moves anchor toward SquadMoveGoal
SoldierSlotFollowSystem    (unchanged — soldiers follow new slots same tick)
MeleeDamageSystem          (unchanged)
DeathSystem                (unchanged)
SquadCompactionSystem      (unchanged logic; uses current Squad.Cols)
```

## Interaction with existing systems

- **Targeting** is unchanged — it still picks the nearest enemy *squad* by
  straight-line distance, even across a river. "How to reach it" is the
  navigation layer's job.
- **Compaction** keeps working with the current `Cols`; the only change is
  sharing its repack helper with the re-shape step.
- **Melee** is unchanged. When two squads converge on the same crossing from
  opposite banks they meet at the chokepoint as narrow blocks, so only the
  narrow front ranks are in range — **chokepoint combat emerges for free** from
  the existing front-rank proximity melee. No bridge-specific combat code.
- **Soldiers / slot-follow / health bars / ownership rings** are untouched.

## Future extensions (designed-for, not built)

- **Slow terrain (C):** author a `passable = 1`, `MoveMultiplier < 1` region; add
  a sampling step that scales `SoldierStepSpeed` / `SquadAdvanceSpeed` when a
  unit's position is inside the region. No navigation change.
- **High ground (D):** author a `combatModifier` region; sample it in
  `MeleeDamageSystem` to buff attack/range for soldiers fighting inside it. No
  navigation change.
- **Grid graduation:** analytic regions suffice while terrain is a handful of
  authored shapes. A full data-driven 500×500 tile world would replace the
  region representation with a grid and the per-squad portal routing with a
  flow-field/A\* planner — isolated behind `SquadNavigationSystem`'s goal
  selection. Out of scope; called out so the seam is intentional.

## Testing

EditMode unit tests via `EcsTestsBase` (`CreateBattleConfig` / `CreateSquad` /
`CreateSoldier` builders; add `CreateTerrainRegion` / `CreateCrossingPortal`
builders).

- **`SquadGeometry` pure math:** `SegmentIntersectsBox` (crossing / not-crossing
  / parallel-miss / endpoint-inside / rotated box); `NarrowColsForWidth`
  (conservative floor, clamp ≥ 1).
- **`SquadNavigationSystem`:** squad with a barrier between it and target
  transitions Pursue → ApproachPortal → Crossing → Pursue and writes the
  expected `SquadMoveGoal` (waypoint + `Engage` flag) at each stage; squad with
  a clear path stays in Pursue with `Engage = 1`; re-shape sets/restores `Cols`
  and repacks slots.
- **`SquadMovementSystem` refactor:** moves toward `SquadMoveGoal.Position`;
  applies the engagement stop only when `Engage == 1`.

Systems remain unit-tested in isolation (no full netcode world), per the
existing harness.

## Risks / notes

- **Re-shape vs compaction:** both only ever set `Squad.Cols`/`Rows` from the
  current alive count and current width, never the buffer or `SlotIndex`, so they
  cannot conflict. Navigation re-shape runs before slot-follow (soldiers pick up
  the new shape same tick); compaction runs after death.
- **Portal not linked to region:** v1 picks the nearest portal by entrance
  distance, not the portal that actually clears the crossed region. Fine while
  features are few and well separated; document for designers and revisit if
  maps put a river bridge and a valley pass close together.
- **Soldiers clipping water on transitions:** prevented by choosing narrow `Cols`
  so `narrowCols * spacing ≤ Portal.Width` and placing `Entrance`/`Exit` on the
  correct banks; the slot-follow path then stays within the corridor.
- **Two squads contesting one portal:** acceptable in v1 — they meet and fight at
  the chokepoint (the intended drama). No reservation/queueing system.

## Out of scope (v1)

- Slow terrain and High ground consumers (schema reserved only).
- Grid / flow-field / A\* pathfinding.
- Portal reservation, queueing, or contention resolution.
- Terrain affecting projectile/ranged combat (none exists yet).
- Procedural or data-driven terrain generation; v1 terrain is hand-authored in
  the subscene.
```
