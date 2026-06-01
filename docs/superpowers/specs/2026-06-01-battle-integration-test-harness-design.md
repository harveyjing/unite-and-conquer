# Battle Integration Test Harness — Design

**Date:** 2026-06-01
**Status:** Approved (design); implementation pending
**Author:** brainstormed with Claude

## Problem

The two bugs that most recently bit the squad battle simulation — the **attrition
freeze** (`4699639`) and the **`ServerTick`-parity compaction starvation** (documented
in CLAUDE.md, fixed by switching `SquadCompactionSystem` to a system-local `_phase`
counter) — are **emergent, multi-system, multi-tick** behaviors.

The existing EditMode suite tests each system **in isolation, ticked once**, via
`EcsTestsBase.CreateAndUpdateSystem<T>()`. That structure *cannot* observe a freeze
that only manifests when targeting → movement → slot-follow → melee → death →
compaction interact across many ticks. The suite was green while the game was frozen.

**Goal:** add tests that drive the whole server pipeline across many ticks and assert
the battle *resolves* and *never freezes* — catching this bug class as a red test on the
exact tick it breaks.

This is **Approach A** from brainstorming: an in-process integration harness in the
existing bare-`World` style. The full-fidelity Netcode path (`NetCodeTestWorld`, real
client+server, replication assertions) is **Approach B**, deferred — see Non-Goals.

## Motivation (chosen during brainstorming)

> Emergent bugs slip through despite green unit tests. Want tests that catch
> multi-system, multi-tick regressions — the battle actually resolving.

## Approach

Extend `EcsTestsBase` with a harness that:

1. Creates the **real `SimulationSystemGroup`** in the test world, adds the six
   continuous server systems, and calls `SortSystems()`. (`BattleSpawnSystem` is one-shot
   and ticked separately, not added to the looping group.) The systems already carry
   `[UpdateAfter]`, so the
   group resolves the true execution order — the **ordering itself becomes under test**.
   Breaking an `[UpdateAfter]` will surface as an integration failure rather than silent
   reordering.
2. Runs a **tick loop** that advances `Time` and `NetworkTime.ServerTick`, calls
   `simGroup.Update()`, then drains scheduled jobs before assertions read data.
3. Supports two board-setup entry points: drive the real `BattleSpawnSystem` (realistic
   symmetric board + first coverage of that system), or hand-build pathological boards
   with the existing `CreateSquad` / `CreateSoldier` builders.

### Server pipeline under test (execution order)

`SquadTargetingSystem` → `SquadMovementSystem` → `SoldierSlotFollowSystem` →
`MeleeDamageSystem` → `DeathSystem` → `SquadCompactionSystem`, plus the one-shot
`BattleSpawnSystem` for spawn-based setup.

### Key technical facts (verified against the source)

- `BattleSpawnSystem` instantiates `config.SoldierPrefab` → the harness must provide a
  **soldier-prefab stub entity** carrying the full soldier archetype for `Instantiate`
  to clone.
- `SquadTargetingSystem` throttles on
  `NetworkTime.ServerTick.SerializedData % TargetRefreshIntervalTicks` → the harness must
  **advance `ServerTick` each tick**. Advancing by **2** reproduces the parity-constrained
  server tick that originally starved even-index squads of compaction.
- Systems schedule jobs whose dependency flows through `state.Dependency` → after each
  group tick the harness calls `Manager.CompleteAllTrackedJobs()` before asserting.
- `SquadCompactionSystem` uses a system-local monotonic `_phase` (not `ServerTick`); the
  parity test guards that this remains correct.

## Architecture

### File layout

| File | Purpose |
|------|---------|
| `Assets/Tests/EditMode/EcsTestsBase.Battle.cs` | Harness helpers (partial of `EcsTestsBase`); keeps core builders file focused |
| `Assets/Tests/EditMode/BattleIntegrationTests.cs` | Multi-tick integration tests |
| `Assets/Tests/EditMode/DeathSystemTests.cs` | Coverage-hole unit test |
| `Assets/Tests/EditMode/BattleSpawnSystemTests.cs` | Coverage-hole unit test |

Existing single-system unit tests and the core `EcsTestsBase` builders are unchanged.

### Tick loop

```
RunBattle(int ticks, float dt = 0.1f, uint tickStride = 1):
    for each tick:
        ServerTick += tickStride          // 1 normally; 2 for the parity regression
        SetTime(elapsed += dt, dt)         // existing TimeData helper
        simGroup.Update()
        Manager.CompleteAllTrackedJobs()   // drain scheduled jobs before asserting
        (optional) per-tick invariant checks
```

## Helper API

Added to `EcsTestsBase` (partial), reusing existing `CreateBattleConfig`, `CreateSquad`,
`CreateSoldier`, `SetTime`, `CreateNetworkTime`:

**Driving**
- `void RunBattle(int ticks, float dt = 0.1f, uint tickStride = 1)` — the loop above.
- `int RunUntilResolved(int maxTicks, float dt = 0.1f, uint tickStride = 1)` — ticks
  until one team reaches zero live soldiers or budget exhausted; returns ticks elapsed.
  Throws (reporting the offending tick) if a per-tick invariant trips.

**Observing**
- `int CountLive(int team)` — live `Soldier` entities on a team (`Health.Current > 0`,
  not destroyed).
- `int CountLiveSquads(int team)` — non-destroyed `Squad` entities with ≥1 live member.

**Invariants (checked each tick inside the loop)**
- `AssertSafety()` — no NaN/negative `Health` on live soldiers; every live soldier's
  `SquadMembership.SlotIndex` is within its squad's `SquadMember` buffer length; no live
  soldier points at a destroyed squad.
- `AssertLiveness(int window)` — the **freeze detector**: tracks total live count; once
  both teams are engaged, the total must strictly drop at least once per `window` ticks.
  A stall fails on the offending tick.

**Setup**
- `Entity CreateSoldierPrefabStub()` — entity with the full soldier archetype
  (`Soldier, Team, Health, AttackStats, SquadMembership, SoldierColor, LocalTransform,
  LocalToWorld`) for `BattleSpawnSystem.Instantiate` to clone.
- `SimulationSystemGroup CreateServerPipeline()` — creates + adds the six continuous
  server systems (not `BattleSpawnSystem`), sorts, returns the group.
- `void SpawnViaBattleSpawnSystem(...)` — sets the prefab stub on `BattleConfig`, ticks
  `BattleSpawnSystem` once.
- `void BuildBoard(...)` — hand-places squads/soldiers for asymmetric/pathological cases.

## Test cases

### Integration tests (`BattleIntegrationTests.cs`)

1. **`LopsidedBattle_ResolvesWithStrongerSideWinning`** — asymmetric board (e.g. 2 red
   squads vs 1 blue). `RunUntilResolved(budget)` finishes under budget with blue at 0
   live and red > 0. *Proves battles end.*
2. **`SymmetricBattle_NeverFreezes_AndTerminates`** — mirror board via
   `SpawnViaBattleSpawnSystem`; `AssertLiveness` each tick; must reach a terminal state
   (one side 0) within budget. *Direct regression for the attrition freeze* — a stall
   fails on the exact tick.
3. **`Battle_Resolves_WhenServerTickAdvancesByTwo`** — symmetric board, `tickStride = 2`
   simulating the parity-constrained server tick. Must still resolve. *Direct regression
   for the `ServerTick`-parity → `_phase` fix.*
4. **`Compaction_ShrinksRows_AsCasualtiesMount`** — mid-battle, each squad's
   `Rows == SquadGeometry.RowsForAliveCount(aliveCount, Cols)` and survivors are
   left-packed into low slots. *Guards the compaction contract across ticks.*
5. **`SafetyInvariants_HoldEveryTick`** — medium battle with `AssertSafety` each tick; an
   explicit test documenting the intent (also exercised implicitly by 1–3).

### Coverage-hole unit tests (single-tick, existing `CreateAndUpdateSystem<T>` style)

6. **`DeathSystemTests`** — soldier at `Health.Current <= 0` destroyed; soldier above 0
   survives; soldier at exactly 0 dies.
7. **`BattleSpawnSystemTests`** — after one update: `2 * SquadsPerTeam` squads exist;
   each `SquadMember` buffer fully wired (no `Entity.Null`); every soldier's
   `SquadMembership.SlotIndex` matches its buffer position; per-team counts correct;
   `state.Enabled == false` afterward.

## Non-goals

- **No client worlds, ghost replication, or prediction.** The freeze lived in server
  simulation; replication-level testing is **Approach B** (`NetCodeTestWorld`), deferred
  until there is a replication bug class to guard.
- **No performance/scale assertions** (thousands of soldiers, allocation counts, frame
  budgets). Valuable but a separate effort; this harness targets correctness, not perf.
- **No changes to production systems.** This is purely additive test infrastructure. If
  a test reveals a real bug, that fix is tracked separately.

## Risks & mitigations

- **Burst-compiled systems in EditMode tests** — already exercised by the existing
  single-system tests (e.g. `MeleeDamageSystem`), so the harness inherits a known-good
  path; no new risk.
- **Determinism of multi-tick outcomes** — battles use fixed `dt` and no RNG in the
  server pipeline, so outcomes are reproducible. Tests assert *bounded* properties
  (resolves within budget, strictly-decreasing liveness) rather than exact tick counts,
  to stay robust to tuning changes in `BattleConfig`.
- **`SimulationSystemGroup` in a non-netcode bare world** — plain `SimulationSystemGroup`
  is core Entities and instantiable without netcode bootstrap; the systems'
  `[UpdateInGroup]` attribute only affects automatic bootstrap, not manual
  `AddSystemToUpdateList`.
- **Tick budgets too tight** — choose budgets with generous headroom over the observed
  resolution time so normal `BattleConfig` tuning changes don't flake the suite.

## Future work

- **Approach B** — full Netcode integration via `NetCodeTestWorld`: assert `Health`
  ghost replication to a client, prediction correctness, and HUD soldier counts.
- **Scale/perf harness** — N-thousand-soldier battles with allocation and timing budgets.
- **CI wiring** — once the harness is in place, run the EditMode suite headless on
  GitHub Actions (GameCI `unity-test-runner`) on every push/PR (separate spec).
