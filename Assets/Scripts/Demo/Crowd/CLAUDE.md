# Crowd subsystem — CLAUDE.md

Guidance for the netcode-free **CrowdScene** sandbox under `Assets/Scripts/Demo/Crowd/`. This file loads automatically when working in this subtree. Project-wide conventions — DOTS conventions, Unity MCP tooling, tech stack — live in the **root `CLAUDE.md`**; consult it for anything not crowd-specific.

## Crowd code structure

- **`CrowdSteering.cs`** — pure stateless Burst math (mirrors `SquadGeometry`): `PickWaypoint(pos, goal, regions, portals)`, no entity access, unit-tested directly.
- **`CrowdComponents.cs`** — `CrowdSoldier{Team,Goal}` (per-soldier, no slot/squad) + `CrowdConfig` singleton (spawn rects, goals, speed, colors).
- **`CrowdSpawnSystem.cs`** — one-shot bulk grid spawn of both armies; stamps each soldier's `Goal` and pushes team tint via `LinkedEntityGroup`.
- **`CrowdSteeringSystem.cs`** — per-tick `IJobEntity` writing `PhysicsVelocity.Linear` toward the waypoint.
- **`CrowdConfigAuthoring.cs` / `CrowdSoldierAuthoring.cs` / `CrowdSoldierVisualAuthoring.cs`** — bakers.

## Pipeline

Both systems are `[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]` in `FixedStepSimulationSystemGroup`, ordered `CrowdSpawnSystem` → `CrowdSteeringSystem` → `PhysicsSystemGroup`. Spawn runs once and disables itself; steering writes desired velocity every tick; the physics solver then resolves all contacts.

## `PickWaypoint` contract

Returns `goal` unless an impassable `TerrainRegion` blocks the straight XZ segment to it; otherwise routes via the nearest `CrossingPortal` using a **near-end → far-end corridor rule** (walk to the portal endpoint on your own bank first, then to the far endpoint once past the near one and laterally inside the corridor). Re-derived from current position **every tick** — there is no state machine, by design: physics shoving can never desync a stored navigation state.

## Isolation

`LocalSimulation` filter + `GameBootstrap.Initialize` returning false for scene `"CrowdScene"` (plain default world, no netcode) keep these systems from ever running in BattleScene's client/server worlds.

## THE invariant: no separation code

The Unity Physics solver — dynamic frictionless upright-locked capsules, banks as static colliders — **is** the separation model. Do not add avoidance/repulsion code; if soldiers interpenetrate, the fix is solver/collider tuning, not steering.

Two hard-won findings:
- **Single shared goal point = physically impossible density = total interpenetration.** Per-soldier goals (`armyGoal + spawnOffset`) preserve the spawn footprint so the army marches as a translated block.
- **Material-property components (`SoldierColor`) bind only on the entity carrying `MaterialMeshInfo`** (the Visual child) — the root's copy is inert.

Spec: `docs/superpowers/specs/2026-06-10-crowd-sandbox-design.md`. Plan + validation results: `docs/superpowers/plans/2026-06-11-crowd-sandbox.md`.
