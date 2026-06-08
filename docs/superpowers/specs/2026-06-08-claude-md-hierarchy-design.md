# CLAUDE.md hierarchy split — design

**Date:** 2026-06-08
**Status:** Approved

## Goal

Split the monolithic root `CLAUDE.md` (174 lines) into a two-level hierarchy. Two motivations, weighted equally:

1. **Leaner root context** — every session loads less. Battle-specific deep content loads only when working in the `Battle/` subtree (Claude Code auto-loads nested `CLAUDE.md` files for the directory you're working in).
2. **Domain co-location** — battle guidance lives next to the battle code it describes.

This is a documentation reorganization only. No code is touched and no runtime behavior changes.

## Structure

Two files:

- `CLAUDE.md` (root) — cross-cutting guidance that applies to the whole repo or to both SampleScene and BattleScene.
- `Assets/Scripts/Demo/Battle/CLAUDE.md` (new) — battle-subsystem deep content, auto-loaded when working in `Battle/`.

### Root `CLAUDE.md` — keeps

- Project status
- Unity project (Editor/package versions, assemblies, tests)
- Design vision
- Tech stack
- Mobile constraints to honor
- **Current code structure** — keeps the top-level folder list. The `Battle/*` sub-bullets (Authoring, Auth.cs, SquadGeometry, System/, UI/) collapse to a single pointer line:
  > Battle subsystem internals (squad pipeline, auth/ownership): see `Assets/Scripts/Demo/Battle/CLAUDE.md`.
- **Bootstrap & multi-client testing** (stays in root by decision — Bootstrap does not get its own nested file)
- UI Toolkit conventions
- DOTS conventions
- Known risks, **including the full netcode/baking gotchas list** (kept in root by decision as cross-cutting institutional knowledge — visible for SampleScene work too)
- Unity Editor operations via MCP
- Required tools
- References

### New `Assets/Scripts/Demo/Battle/CLAUDE.md` — receives (moved out of root)

- **Battle system pipeline** — the full section: squad-driven overview, server execution order steps 1–6 (`SquadTargetingSystem` → `SquadMovementSystem` → `SoldierSlotFollowSystem` → `MeleeDamageSystem` → `DeathSystem` → `SquadCompactionSystem`), `BattleSpawnSystem`, the client-only health-bar / ownership-ring systems, and the physics note.
- **Authentication & army ownership** — the full section.
- The `Battle/` portions of the *Current code structure* listing (`Battle/Authoring/`, `Battle/Auth.cs`, `Battle/SquadGeometry.cs`, `Battle/System/`, `Battle/UI/`).

## Cross-references

Links must stay coherent after the move, in both directions:

- **Root → battle content:** existing root references like `(see *Authentication & army ownership*)` and `(see Battle system pipeline below)` are updated to point at `Assets/Scripts/Demo/Battle/CLAUDE.md` instead of an in-file section.
- **Within moved battle content:** internal references (e.g. the pipeline section pointing at the auth section, or `(see gotchas)`) — references to sections that *also* moved stay coherent; references to sections that stayed in root (the gotchas list) are rewritten to point back at the root file.
- The new `Battle/CLAUDE.md` opens with a one-line note that root `CLAUDE.md` holds project-wide conventions (DOTS conventions, netcode gotchas, MCP tooling) so a reader landing there knows where the cross-cutting rules live.

## Expected outcome

- Root `CLAUDE.md`: ~174 → ~130 lines.
- New `Battle/CLAUDE.md`: ~45 lines of battle-specific content.
- No duplicated content between the two files; every section lives in exactly one place with pointers bridging them.

## Non-goals

- No Bootstrap/, UI/, or per-folder nested files (single nested file by decision).
- No content rewrite or condensation beyond what the move requires — text moves verbatim except for adjusted cross-reference wording.
- No code changes.
