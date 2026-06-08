# CLAUDE.md Hierarchy Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the monolithic root `CLAUDE.md` into a two-level hierarchy — a leaner root plus a new `Assets/Scripts/Demo/Battle/CLAUDE.md` that auto-loads when working in the battle subtree.

**Architecture:** Move the two battle-specific sections (Battle system pipeline, Authentication & army ownership) plus the `Battle/` code-structure bullets out of root and into a new nested `CLAUDE.md`. Keep all cross-cutting content (including the full netcode/baking gotchas) in root. Replace moved content with pointer lines, and fix cross-references in both directions. No code touched; documentation reorganization only.

**Tech Stack:** Markdown only. Claude Code auto-loads nested `CLAUDE.md` files for the directory being worked in.

**Spec:** `docs/superpowers/specs/2026-06-08-claude-md-hierarchy-design.md`

---

## File Structure

- **Create:** `Assets/Scripts/Demo/Battle/CLAUDE.md` — battle-subsystem deep content (code structure for `Battle/`, battle system pipeline, authentication & army ownership). Auto-loaded when working in `Battle/`.
- **Modify:** `CLAUDE.md` (root) — remove the two moved sections, collapse the 5 `Battle/*` code-structure bullets to one pointer bullet, fix one cross-reference. Keeps all other sections including the full gotchas list.

There is no test framework for docs. "Verification" steps are `grep`/`wc` checks that confirm content moved exactly once (no loss, no duplication) and that cross-references resolve.

**Note on Unity `.meta`:** `Battle/CLAUDE.md` lives under `Assets/`, so Unity will generate `Battle/CLAUDE.md.meta` next time the Editor has focus. That is expected and harmless; commit it alongside if it appears (matching how prior commits handled generated `.meta` files). Do not block on it — the Editor need not be running to complete this plan.

---

## Task 1: Create the nested Battle/CLAUDE.md

**Files:**
- Create: `Assets/Scripts/Demo/Battle/CLAUDE.md`

This file receives, verbatim, the content currently at root `CLAUDE.md` lines 55–59 (the `Battle/*` code-structure bullets), 68–89 (Battle system pipeline), and 91–97 (Authentication & army ownership). The only text changes are the three `(see gotchas)` references, which must now point at the root file because the gotchas list stays in root.

- [ ] **Step 1: Write the new nested file**

Create `Assets/Scripts/Demo/Battle/CLAUDE.md` with exactly this content:

````markdown
# Battle subsystem — CLAUDE.md

Guidance for the squad-based **BattleScene** code under `Assets/Scripts/Demo/Battle/`. This file loads automatically when working in this subtree. Project-wide conventions — DOTS conventions, netcode/baking gotchas, Unity MCP tooling, tech stack, mobile constraints — live in the **root `CLAUDE.md`**; consult it for anything not battle-specific.

## Battle code structure

- **`Battle/Authoring/`** — `SoldierAuthoring` (bakes `Soldier`, `Team`, `SoldierColor`, `Health`, `AttackStats`, `SquadMembership`, `GhostOwner`, kinematic `PhysicsCollider`), `BattleConfigAuthoring` (singleton `BattleConfig` that drives every battle system — squad shape, behavior, combat tuning, colors, health-bar + ownership-ring prefabs), `HealthBarAuthoring`, `SquadComponents.cs` (the `Squad`, `SquadTarget`, `SquadMember` buffer, `SquadMembership` components — all server-only)
- **`Battle/Auth.cs`** — login/army-ownership RPC pipeline (see *Authentication & army ownership* below)
- **`Battle/SquadGeometry.cs`** — static Burst math (slot offsets, engagement distance, rows-for-alive-count) shared by spawn/movement/follow/compaction systems; no entity access, unit-tested directly
- **`Battle/System/`** — server battle pipeline + client-only health-bar & ownership-ring systems (see *Battle system pipeline* below)
- **`Battle/UI/`** — `BattleHudController`, `BattleHudViewModel`, `LoginHudController` (same pattern as `DemoHudController`)

## Battle system pipeline

The simulation is **squad-driven**: a `Squad` entity carries the formation shape (`Rows × Cols`, `Spacing`, `Team`) and a `SquadMember` buffer (one slot per formation position, `Entity.Null` = empty). Each soldier holds a `SquadMembership` (`Squad` + `SlotIndex`). Squads pick targets and move; individual soldiers just chase their assigned slot's world position. There is no per-soldier targeting.

Server execution order (all `ServerSimulation`, `SimulationSystemGroup`):

1. **`SquadTargetingSystem`** — throttled to every `TargetRefreshIntervalTicks` server ticks. Snapshots all squads into a `NativeArray<SquadSnapshot>` (`SnapshotJob`), then `AssignTargetJob` sets each squad's `SquadTarget` to the nearest enemy **squad** by squared distance. O(squads²), cheap because squad count is small. Requires `NetworkTime`.
2. **`SquadMovementSystem`** (`UpdateAfter(SquadTargetingSystem)`) — rotates each squad toward its target (`SquadRotationSpeed`) and advances it (`SquadAdvanceSpeed`) until front ranks are within `SquadGeometry.EngagementDistance`.
3. **`SoldierSlotFollowSystem`** (`UpdateAfter(SquadMovementSystem)`) — moves each soldier toward its slot's world position (`SquadGeometry.SlotLocalOffset` transformed by the squad's `LocalTransform`) at `SoldierStepSpeed`.
4. **`MeleeDamageSystem`** (`UpdateAfter(SoldierSlotFollowSystem)`) — scatter/gather: `WriteDamageJob` (`IJobChunk`) has each front-rank soldier (`SlotIndex < Cols`) scan the target squad's front row via `BufferLookup<SquadMember>` and damage the **single nearest live enemy within `AttackStats.Range`** (proximity, not column-index pairing — robust to compaction's left-packing and partial rows), writing into a `NativeStream`; `ReduceDamageJob` (serial `IJob`) drains the stream and decrements `Health`. Stream avoids concurrent writes to the same victim.
5. **`DeathSystem`** (`UpdateAfter(MeleeDamageSystem)`) — destroys entities with `Health.Current <= 0` via ECB. It does **not** clear the dead soldier's `SquadMember` slot; that buffer cleanup happens later in `SquadCompactionSystem`. Systems reading the buffer must guard against destroyed/dead entities in the interim.
6. **`SquadCompactionSystem`** (`UpdateAfter(DeathSystem)`) — staggered/throttled by `CompactionIntervalTicks` using a **system-local monotonic update counter** (`_phase`), *not* `NetworkTime.ServerTick`. (Keying off `ServerTick` froze battles: the server-observed tick is parity-constrained, so `(tick + squadIndex) % interval` permanently starved even-index squads of compaction — their dead front rows blocked survivors out of melee range.) Reclaims dead slots: shrinks `Squad.Rows` to `SquadGeometry.RowsForAliveCount`, repacks survivors into low slot indices, and rewrites each survivor's `SquadMembership.SlotIndex`. Does **not** require `NetworkTime`.

**`BattleSpawnSystem`** runs once on the first frame (no ordering attribute needed — gated by `RequireForUpdate<BattleConfig>`): creates `2 * SquadsPerTeam` `Squad` entities laid in a line per team, bulk-spawns soldiers via `EntityManager.Instantiate`, then wires `SquadMembership` + `SquadMember` buffers and initializes per-soldier data with parallel jobs. Sets `state.Enabled = false` afterward. ECB-per-entity would cost hundreds of ms at scale.

Client-only (both `ClientSimulation`, `PresentationSystemGroup`):

- **`HealthBarSpawnSystem`** — instantiates the `HealthBarPrefab` for each ghost soldier that lacks a bar.
- **`HealthBarUpdateSystem`** (`UpdateAfter(HealthBarSpawnSystem)`) — positions each bar above its soldier (`HealthBarHeightOffset`) and drives the `HealthBarFill` material property from replicated `Health.Current` / `BattleConfig.MaxHealth`.
- **`OwnershipRingSpawnSystem`** (`UpdateAfter(HealthBarSpawnSystem)`) — for each soldier the local client owns (`GhostOwner.NetworkId == local NetworkId`; **not** `GhostOwnerIsLocal` — see *Netcode / baking gotchas* in the root `CLAUDE.md`), instantiates `OwnershipRingPrefab`, parents it at the soldier's feet, and appends to the soldier's `LinkedEntityGroup` (does not clobber the bar link). The disc is sized in code via `PostTransformMatrix`, not the prefab scale — the ring inherits the soldier's ~0.3× world scale and prefab-scale changes don't reliably re-bake (see *Netcode / baking gotchas* in the root `CLAUDE.md`).

**Physics note:** `SoldierAuthoring` still bakes a kinematic `PhysicsCollider` (`Soldier.Layer = 1u << 1`, zero `PhysicsVelocity`, `PhysicsMass.CreateKinematic`, `PhysicsWorldIndex(0)`). **No battle system currently queries the physics world** — squad-level distance targeting replaced the old `PhysicsWorldSingleton`/`NearestEnemyCollector` broadphase approach, so the collider is presently vestigial (the `Soldier.Layer` doc comment is stale). Don't reintroduce a physics-broadphase dependency without re-checking this.

## Authentication & army ownership

Lives in **`Battle/Auth.cs`** (server + client RPC systems) plus `Battle/UI/LoginHudController.cs` (the login UIDocument bridge, `Assets/UI/LoginHud.{uxml,uss}`). `SoldierAuthoring` bakes a `GhostOwner` (NetworkId 0 = unowned) on every soldier; `GhostOwner.NetworkId` is the replicated owner field.

Flow: login HUD writes a client-only **`PendingAuth`** → **`ClientAuthSendSystem`** (client) turns it into an **`AuthenticateRequest`** RPC → **`AuthServerSystem`** (server) claims the next free team for the requesting `NetworkId`, stamps `GhostOwner.NetworkId` on that team's soldiers, and records it in the **`TeamClaims`** singleton (`Team0Owner`/`Team1Owner`, 0 = unclaimed; netcode `NetworkId.Value` starts at 1). Idempotent per connection (a NetworkId that already owns a team is ignored, so one user can't grab both). Only **two teams** exist → a 3rd client gets no team (spectator).

The stamped `GhostOwner.NetworkId` replicates to clients; `LoginHudController` hides the overlay and `OwnershipRingSpawnSystem` spawns rings once the local client owns a soldier. Both detect ownership by **comparing `GhostOwner.NetworkId` to the local connection's `NetworkId`**, never `GhostOwnerIsLocal` (see *Netcode / baking gotchas* in the root `CLAUDE.md`).
````

- [ ] **Step 2: Verify the file was created with all moved content**

Run: `grep -c "## Battle system pipeline" Assets/Scripts/Demo/Battle/CLAUDE.md && grep -c "## Authentication & army ownership" Assets/Scripts/Demo/Battle/CLAUDE.md && grep -c "## Battle code structure" Assets/Scripts/Demo/Battle/CLAUDE.md`
Expected: `1` printed three times (each header present exactly once).

- [ ] **Step 3: Verify the gotchas cross-references now point at root**

Run: `grep -c "Netcode / baking gotchas\* in the root" Assets/Scripts/Demo/Battle/CLAUDE.md`
Expected: `3` (the two in the OwnershipRing bullet + the one in the auth closing paragraph). Confirm no bare `(see gotchas)` remains: `grep -c "see gotchas)" Assets/Scripts/Demo/Battle/CLAUDE.md` → `0`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Demo/Battle/CLAUDE.md
git commit -m "docs(battle): add nested CLAUDE.md for battle subsystem

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Trim the root CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (root)

Three edits: (A) collapse the five `Battle/*` code-structure bullets to one pointer bullet, (B) remove the two moved sections, (C) repoint the one cross-reference that stays in root.

- [ ] **Step 1: Collapse the `Battle/*` code-structure bullets (Edit A)**

Replace these five consecutive lines (currently lines 55–59):

```
- **`Battle/Authoring/`** — `SoldierAuthoring` (bakes `Soldier`, `Team`, `SoldierColor`, `Health`, `AttackStats`, `SquadMembership`, `GhostOwner`, kinematic `PhysicsCollider`), `BattleConfigAuthoring` (singleton `BattleConfig` that drives every battle system — squad shape, behavior, combat tuning, colors, health-bar + ownership-ring prefabs), `HealthBarAuthoring`, `SquadComponents.cs` (the `Squad`, `SquadTarget`, `SquadMember` buffer, `SquadMembership` components — all server-only)
- **`Battle/Auth.cs`** — login/army-ownership RPC pipeline (see *Authentication & army ownership*)
- **`Battle/SquadGeometry.cs`** — static Burst math (slot offsets, engagement distance, rows-for-alive-count) shared by spawn/movement/follow/compaction systems; no entity access, unit-tested directly
- **`Battle/System/`** — server battle pipeline + client-only health-bar & ownership-ring systems (see Battle system pipeline below)
- **`Battle/UI/`** — `BattleHudController`, `BattleHudViewModel`, `LoginHudController` (same pattern as `DemoHudController`)
```

with this single line:

```
- **`Battle/`** — squad-based BattleScene subsystem (`Authoring/`, `Auth.cs`, `SquadGeometry.cs`, `System/`, `UI/`). Internals — squad pipeline, authentication & army ownership — documented in **`Assets/Scripts/Demo/Battle/CLAUDE.md`** (auto-loaded when working in that subtree).
```

- [ ] **Step 2: Remove the two moved sections (Edit B)**

Delete the entire block from the `## Battle system pipeline` heading through the end of the `## Authentication & army ownership` section — i.e. everything between the `Pattern: ...` line (which ends `NetCode settings in \`ProjectSettings/\`.`) and the `## Bootstrap & multi-client testing` heading. After this edit those two headings must sit with one blank line between them:

```
Pattern: **tag/component → authoring+baker → Burst `ISystem`**. Ghost prefab in `Assets/Prefabs/`. NetCode settings in `ProjectSettings/`.

## Bootstrap & multi-client testing
```

Concretely, the deleted text starts at `## Battle system pipeline` and ends at `...never \`GhostOwnerIsLocal\` (see gotchas).` (the last line of the Authentication section). Use an Edit whose `old_string` is `## Battle system pipeline` … through … `never \`GhostOwnerIsLocal\` (see gotchas).\n\n` and whose `new_string` is empty, OR anchor on the surrounding lines as shown in the block above (replace `...ProjectSettings/\`.\n\n## Battle system pipeline ... (see gotchas).\n\n## Bootstrap` with `...ProjectSettings/\`.\n\n## Bootstrap`).

- [ ] **Step 3: Repoint the surviving cross-reference (Edit C)**

In the **Project status** section (line 10, the `BattleScene` bullet), the phrase `(see *Authentication & army ownership*)` now points at a section that no longer exists in this file. Replace:

```
the claiming client sees a ground "ownership ring" under its own soldiers (see *Authentication & army ownership*).
```

with:

```
the claiming client sees a ground "ownership ring" under its own soldiers (see *Authentication & army ownership* in `Assets/Scripts/Demo/Battle/CLAUDE.md`).
```

- [ ] **Step 4: Verify root no longer contains the moved sections**

Run: `grep -c "## Battle system pipeline" CLAUDE.md && grep -c "## Authentication & army ownership" CLAUDE.md`
Expected: `0` printed twice.

- [ ] **Step 5: Verify root has no dangling references to moved content**

Run: `grep -n "Authentication & army ownership\|Battle system pipeline below\|see gotchas" CLAUDE.md`
Expected: exactly one match — the Project-status line now reading `... in \`Assets/Scripts/Demo/Battle/CLAUDE.md\`)`. No `Battle system pipeline below`, no bare `see gotchas` (those lived only in the moved sections), no other unqualified `Authentication & army ownership`.

- [ ] **Step 6: Verify the root shrank as expected**

Run: `wc -l CLAUDE.md`
Expected: roughly 139 lines (down from 174 — about 35 lines removed: ~31 from the two sections plus 4 from collapsing five bullets to one).

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: trim root CLAUDE.md after splitting out battle subsystem

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Cross-file integrity check

**Files:** none modified — verification only.

Confirm every moved unique phrase now lives in exactly one file (no loss, no duplication), and the root↔nested pointers are mutually consistent.

- [ ] **Step 1: Each moved section header appears exactly once across both files**

Run: `grep -rc "## Battle system pipeline" CLAUDE.md Assets/Scripts/Demo/Battle/CLAUDE.md`
Expected:
```
CLAUDE.md:0
Assets/Scripts/Demo/Battle/CLAUDE.md:1
```

- [ ] **Step 2: A representative unique phrase from each moved section is present exactly once total**

Run: `grep -rc "ReduceDamageJob" CLAUDE.md Assets/Scripts/Demo/Battle/CLAUDE.md && echo "---" && grep -rc "TeamClaims" CLAUDE.md Assets/Scripts/Demo/Battle/CLAUDE.md`
Expected: `CLAUDE.md:0` and `Assets/Scripts/Demo/Battle/CLAUDE.md:1` for both phrases (each section's content lives only in the nested file).

- [ ] **Step 3: Root points to the nested file, and the nested file points back to root**

Run: `grep -c "Assets/Scripts/Demo/Battle/CLAUDE.md" CLAUDE.md && grep -c "root \`CLAUDE.md\`" Assets/Scripts/Demo/Battle/CLAUDE.md`
Expected: first command ≥ `2` (the collapsed `Battle/` bullet + the Project-status cross-reference), second command ≥ `1` (the nested file's opening note plus the gotchas back-references).

- [ ] **Step 4: No commit needed**

This task only verifies. If any check fails, return to Task 1 or Task 2, fix the file, and re-run the failing check before proceeding.

---

## Self-Review Notes

- **Spec coverage:** root-keeps list (Task 2 leaves all non-battle sections untouched) ✓; nested-receives list — Battle system pipeline + Authentication & army ownership + `Battle/` code-structure bullets (Task 1) ✓; gotchas stay in root (Task 2 does not touch the gotchas section) ✓; cross-references fixed both directions (Task 1 Step 3 nested→root; Task 2 Steps 1 & 3 root→nested) ✓; nested file opens with a pointer to root conventions (Task 1 Step 1 first paragraph) ✓; expected line counts (Task 2 Step 6, Task 1 file ≈45 lines of battle content) ✓.
- **No placeholders:** all file content is spelled out verbatim; all commands have expected output.
- **Type/string consistency:** the pointer path `Assets/Scripts/Demo/Battle/CLAUDE.md` and the back-reference phrase `*Netcode / baking gotchas* in the root \`CLAUDE.md\`` are used identically across tasks.
