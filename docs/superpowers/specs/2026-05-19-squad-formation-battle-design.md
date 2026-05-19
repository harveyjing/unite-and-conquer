# Squad-formation battle — design

## Problem

Soldiers in `BattleScene` pile onto the same point. Each soldier independently
targets the nearest enemy via `TargetingSystem`, then `SoldierMovementSystem`
writes `LocalTransform.Position` directly toward that target with no
separation. When many soldiers chase the same enemy — or different enemies
that converge — they overlap.

## Goal

Replace per-soldier free movement with **rigid squad-based formations**:

- Soldiers belong to squads. Each soldier has a fixed slot in its squad.
- Squads — not individual soldiers — pick targets and advance.
- On contact, the front rank fights; back ranks hold their slot.
- Squads periodically compact to fill gaps from casualties.

This aligns with the project's long-term direction (regiments, class counters,
formation cyclic-advantage) per [CLAUDE.md](../../../CLAUDE.md) and
[docs/basic-idea.md](../../basic-idea.md).

## Non-goals

- Player-issued squad orders, formation switching, retreat/rout logic.
- Squad-vs-squad rock-paper-scissors counters or unit classes.
- Ranged units. (The kinematic physics collider stays on soldiers in case a
  future ranged-targeting system wants the broadphase, but it goes unused
  in this iteration.)
- Backwards compatibility with the existing per-soldier `Target` flow. The
  old `TargetingSystem` and `Target` component are removed.

## Data model

All squad-level data is **server-only**. Clients continue to see per-soldier
`Team`, `SoldierColor`, and `LocalTransform` ghosts as today — `BattleHud`
keeps working with no changes.

```csharp
// Server-only. One Squad entity per regiment per team.
public struct Squad : IComponentData
{
    public int Team;        // 0 = red, 1 = blue
    public int Rows;        // mutable — shrinks during compaction
    public int Cols;        // fixed (line width stays constant)
    public float Spacing;
}

// Server-only. Squad's current target.
public struct SquadTarget : IComponentData
{
    public Entity Value;    // enemy Squad entity, or Entity.Null
}

// Server-only buffer on the Squad entity, indexed by slot.
// Stale references are tolerated until the next compaction.
[InternalBufferCapacity(0)]
public struct SquadMember : IBufferElementData
{
    public Entity Value;    // soldier entity, or Entity.Null for empty
}

// Server-only on each soldier. Replaces the removed `Target` component.
public struct SquadMembership : IComponentData
{
    public Entity Squad;
    public int    SlotIndex;
}
```

**Squad's transform.** The Squad entity has its own `LocalTransform` —
`Position` is the squad anchor (center of the formation rectangle),
`Rotation` is the facing toward the current target. Soldiers compute their
world slot from these plus their `SlotIndex` and the shape parameters.

**Component removals.** `Target` (per-soldier) is deleted. `TargetingSystem`
is deleted.

## System pipeline

All systems run in `WorldSystemFilterFlags.ServerSimulation`,
`SimulationSystemGroup`, in this order:

1. **`BattleSpawnSystem`** (one-shot, then `state.Enabled = false`).
   Instantiates `2 * SquadsPerTeam` Squad entities, then bulk-instantiates
   soldiers, assigns `SquadMembership`, populates each squad's
   `SquadMember` buffer, sets initial squad anchor transforms (squads laid
   in a line perpendicular to the red↔blue axis).
2. **`SquadTargetingSystem`** — throttled every `TargetRefreshIntervalTicks`
   (5). For each squad, scan all enemy squads in a Burst job (O(squads²) —
   ~20k comparisons at 100 squads per team, trivial). Set `SquadTarget.Value`
   to the nearest enemy squad anchor. No physics broadphase needed at this
   level.
3. **`SquadMovementSystem`** (`UpdateAfter` SquadTargetingSystem). For each
   squad with a valid target: lerp `LocalTransform.Rotation` toward
   `LookRotation(targetAnchor - selfAnchor)` at `SquadRotationSpeed`;
   advance `Position` toward `targetAnchor` at `SquadAdvanceSpeed`. Stop
   advancing when anchor distance falls below
   `EngagementDistance(selfRows, targetRows, Spacing, AttackRange)`
   (see Geometry below). Rotation may still adjust after stopping.
4. **`SoldierSlotFollowSystem`** (`UpdateAfter` SquadMovementSystem) —
   **replaces** `SoldierMovementSystem`. For each soldier: read the parent
   `Squad.LocalTransform` + shape + own `SlotIndex`, compute world slot
   position, step `LocalTransform.Position` toward it at
   `SoldierStepSpeed * dt`, clamp when within one step (no jitter).
5. **`MeleeDamageSystem`** (modified) — same scatter/gather (`NativeStream`)
   pattern as today. Filter to **front-rank** soldiers only (`SlotIndex <
   Squad.Cols`). Each front-rank soldier reads
   `targetSquad.SquadMember[SlotIndex % targetSquad.Cols]`; if that entity
   exists, has `Health > 0`, and is within `AttackRange`, write a
   `DamageEvent`. The serial reducer drains the stream as today.
6. **`DeathSystem`** (unchanged) — destroys entities with
   `Health.Current ≤ 0` via ECB. No squad bookkeeping happens here; stale
   references in `SquadMember` buffers are cleaned up by compaction.
7. **`SquadCompactionSystem`** — throttled every `CompactionIntervalTicks`
   (default **10**, ≈ 0.33 s at 30 Hz). For each squad: gather alive
   members, re-pack into a tight rectangle (`Cols` fixed,
   `Rows = ceil(alive / Cols)`), rewrite the buffer, update each survivor's
   `SquadMembership.SlotIndex`, write back `Squad.Rows`. If `alive == 0`,
   destroy the Squad entity.

**Stagger.** `SquadCompactionSystem` runs each squad on
`(tick + squadHash) % CompactionIntervalTicks == 0` (where `squadHash =
squadEntity.Index`) so all squads don't compact on the same tick.

## Geometry

**Slot index → local offset** (squad-local frame; `+Z` faces target):

```
col = slot % Cols
row = slot / Cols                              // row 0 = front
localX = (col - (Cols - 1) * 0.5f) * Spacing
localZ = ((Rows - 1) * 0.5f - row) * Spacing
worldPos = anchor.Position + math.mul(anchor.Rotation, new float3(localX, 0, localZ))
```

**Engagement distance** (anchor-to-anchor threshold at which a squad stops
advancing — front ranks are then within `AttackRange` of each other):

```
EngagementDistance(selfRows, targetRows, spacing, attackRange) =
      (selfRows   - 1) * 0.5f * spacing
    + (targetRows - 1) * 0.5f * spacing
    + attackRange
    + ContactMargin
```

For symmetric `Rows = 5`, `Spacing = 1.5`, `AttackRange = 0.8`,
`ContactMargin = 0.1` → engagement distance ≈ 6.9.

**Front-rank pairing.** Front rank = slot indices `[0, Cols)`. Soldier in
slot `i` (own front rank) attacks `targetSquad.SquadMember[i %
targetSquad.Cols]`. If our `Cols` exceeds the target's, slots wrap. If the
target's slot holds `Entity.Null` or a dead entity, no damage is dealt
this tick — compaction repairs within `CompactionIntervalTicks` (≤ 0.33 s).

## Configuration

`BattleConfigAuthoring` changes:

```csharp
[Header("Squad shape")]
public int SquadsPerTeam   = 2;
public int SquadRows       = 5;
public int SquadCols       = 10;        // SoldiersPerSquad = Rows * Cols = 50
public float SquadSpacing  = 1.5f;      // replaces existing Spacing

[Header("Squad behavior")]
public float SquadAdvanceSpeed       = 2f;
public float SquadRotationSpeed      = 2f;   // rad/s
public float ContactMargin           = 0.1f;
public int   CompactionIntervalTicks = 10;
```

- `MoveSpeed` is **renamed** to `SoldierStepSpeed` (the per-soldier speed
  of stepping into the assigned slot). Default equal to
  `SquadAdvanceSpeed`.
- `CountPerSide` is removed from the inspector; derived as
  `SquadsPerTeam * SquadRows * SquadCols` and baked into `BattleConfig`
  for diagnostics.
- `TargetRefreshIntervalTicks`, `AttackRange`, `Dps`, `MaxHealth`,
  `SearchRadius`, team colors — unchanged.

## Authoring

- **`SoldierAuthoring.cs`**: remove `AddComponent(entity, new Target ...)`;
  add `AddComponent<SquadMembership>(entity)` (zero-initialized; populated
  by `BattleSpawnSystem`). Physics collider stays in place.
- **`BattleConfigAuthoring.cs`**: field renames + new fields per the
  Configuration section.
- **No `SquadAuthoring`.** Squad entities are server-spawned by
  `BattleSpawnSystem`, never baked from MonoBehaviours.

## Spawn layout

For each team:
- Squad anchor `i` of `N` (per team) sits at
  `teamCenter + sideAxis * (i - (N - 1) * 0.5f) * SquadStrideZ`
  where `sideAxis = (0, 0, 1)` and
  `SquadStrideZ = SquadCols * SquadSpacing + InterSquadGap`
  (`InterSquadGap` = 2.0 hardcoded for now; promote to config if needed).
- Red squads spawn facing `+X` (toward `BlueCenter`); blue squads face `-X`.
- Each squad's `SquadMember` buffer is pre-sized to `Rows * Cols` and
  populated with soldier entities in slot-index order.

## Replication

Squad entities are **not** ghosted. Per-soldier ghost components
(`Team`, `SoldierColor`, `LocalTransform`) are unchanged. The HUD
(`BattleHudController`) still counts soldiers by `Team` and works as-is.

## Edge cases

- **Paired enemy just died.** Front-rank soldier swings at empty air for
  up to `CompactionIntervalTicks` ticks (≈ 0.33 s). Acceptable visual cost.
- **Squad shrinks below `Cols`.** `Rows` becomes 1; the last row may be
  partial. Pairing skips empty slots cleanly.
- **Squad's target dies mid-interval.** Next `SquadTargetingSystem` tick
  (≤ 5 ticks later) reassigns. `SquadMovementSystem` idles in the meantime
  (no advance, no rotation change).
- **All squads compact simultaneously.** Prevented by per-squad
  `(tick + squadHash) % interval` stagger.
- **Two squads converge from oblique angles.** Both lerp their rotation
  toward the line between anchors; once locked at engagement distance the
  front ranks face each other within rotation-lerp tolerance.

## Testing

This iteration also introduces an EditMode test infrastructure that the
project currently lacks (no `.asmdef` yet, all code in `Assembly-CSharp`).
Pure math is extracted into a static helper for cheap unit testing; each
new system gets a focused EditMode test.

**Pure-math helper.** `Demo.SquadGeometry` (static class) owns:
- `SlotLocalOffset(slot, rows, cols, spacing) → float3`
- `EngagementDistance(selfRows, targetRows, spacing, attackRange, contactMargin) → float`
- `RowsForAliveCount(aliveCount, cols) → int`

Burst-compatible pure functions; called from `BattleSpawnSystem`,
`SoldierSlotFollowSystem`, `SquadMovementSystem`, and
`SquadCompactionSystem`. Inlined math in those systems is removed.

**Test assemblies.**
- `Assets/Scripts/Demo/Demo.asmdef` — wraps all production code (first
  asmdef in the project; expected to require iteration on references).
- `Assets/Tests/EditMode/Demo.Tests.EditMode.asmdef` — EditMode test
  assembly, references `Demo` + NUnit + Entities/Mathematics/Transforms/
  NetCode/Collections.
- `Assets/Tests/EditMode/EcsTestsBase.cs` — minimal fixture: per-test
  `World`, helpers for creating Squad/Soldier/BattleConfig/NetworkTime
  entities. (Avoids the `Unity.Entities.Tests` package dependency.)

**Automated test coverage.**
- `SquadGeometryTests` — slot offset for known shapes, engagement
  distance for symmetric and asymmetric pairs, row count for typical
  and edge alive counts.
- `SquadTargetingSystemTests` — two enemy squads at different distances
  → each squad's `SquadTarget` resolves to the nearest enemy.
- `SquadMovementSystemTests` — squad far from target advances along
  facing; squad at engagement distance holds position; squad with
  `Entity.Null` target does not move.
- `SoldierSlotFollowSystemTests` — soldier moves toward computed slot
  world position; soldier within one step distance snaps without
  overshoot.
- `SquadCompactionSystemTests` — squad with mixed-alive members
  re-packs its buffer, surviving soldiers get new contiguous
  `SlotIndex` values, `Rows` updates, fully-wiped squad is destroyed.

**Not automated** (cost > value at this scope):
- `BattleSpawnSystem` — requires a baked soldier prefab fixture.
- `MeleeDamageSystem` — scatter/gather correctness is most visible
  in a live battle; tested manually via smoke.

**Manual smoke test.** Final verification step in `BattleScene`:
default config (2 squads per team, 5×10 each = 100 vs 100). Confirm
squads advance, collide front-to-front, rank-0 soldiers swing, ranks
behind hold position, casualties compact every ≈ 0.33 s, winner banner
fires. Console clean before, during, after Play. Scale test: bump
`SquadsPerTeam` to 4 (400 vs 400) and confirm tick rate holds.

## Risks

- **`SoldierSlotFollowSystem` write contention.** Every soldier writes
  `LocalTransform` every tick; at 10k+10k that is 20k writes/tick. Same
  load profile as today's `SoldierMovementSystem`, so it should hold.
- **Squad rotation jitter under retargeting churn.** If a squad flips
  targets between adjacent enemy squads each `TargetRefreshIntervalTicks`,
  it could oscillate. Mitigation if it bites: target hysteresis (require
  a new target to be ≥ 10 % closer to switch). Defer until observed.
- **Compaction reassigning `SlotIndex` while
  `SoldierSlotFollowSystem` reads it.** Compaction runs in the same
  `SimulationSystemGroup` after slot-follow — no intra-tick race. Across
  ticks the soldier simply walks toward its new slot at
  `SoldierStepSpeed`.
- **Phantom-slot damage gap.** Up to ≈ 0.33 s of "empty swings" between
  pair death and compaction. Reduced from a multi-second gap by the
  `CompactionIntervalTicks = 10` choice.

## Open questions (deferred)

- Should squads have a `SquadStats` profile (HP modifier, dps modifier,
  unit class) for future class counters? Out of scope here.
- Should `InterSquadGap` be configurable rather than hardcoded? Promote if
  the editor needs it; otherwise leave the constant in code.
- Should the kinematic collider be stripped from soldiers now that
  `TargetingSystem` is removed? Keep for now — ranged targeting and
  client picking will want it.
