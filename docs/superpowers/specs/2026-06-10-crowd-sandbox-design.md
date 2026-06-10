# Individual-Soldier Crowd Sandbox (Unity Physics) — Design

**Date:** 2026-06-10
**Status:** Approved
**Predecessor:** `2026-06-09-battle-terrain-navigation-design.md` (squad-based nav)

## Problem

In BattleScene, soldiers overlap at the river bridge: same-squad soldiers stack
on each other during the narrow re-shape, and the two enemy columns
interpenetrate when they meet mid-crossing. Root cause: `SoldierSlotFollowSystem`
moves soldiers to formation-slot positions with zero soldier-vs-soldier
collision — the baked kinematic `PhysicsCollider` is vestigial (no system
queries the physics world).

Rather than patch the slot system, this project explores the opposite end of
the spectrum: **fully individual soldiers** — no `Squad` entity, no formation
slots — where each soldier steers itself and **Unity Physics** guarantees
non-penetration. Built from scratch in a **new scene**; BattleScene is left
untouched.

## Scope

**v1 is a movement sandbox.** Soldiers spawn, route around the river, funnel
across the bridge, and never overlap. Target scale: **1,000–2,000 soldiers**.

Out of scope for v1: combat, health, death, health bars, netcode replication,
login/army ownership, win banner, formation re-grouping. Movement only.

## Decisions made during brainstorming

| Decision | Choice | Rejected alternatives |
|---|---|---|
| Soldier autonomy | Fully individual (no Squad concept) | Hybrid squad-command layer; slots + separation pass |
| First milestone | Movement-only sandbox | Core sim with combat; full BattleScene parity |
| Scale target | 1,000–2,000 | 200 (too easy), 10k+ (too slow to iterate) |
| World setup | Local single default world | Netcode from day one |
| Movement tech | **Unity Physics dynamic bodies** + thin steering | Flow field + spatial-hash separation (recommended but declined); per-soldier portal state machine |

Note for the future: the design vision targets 10k+ soldiers on mobile-class
server budgets. Dense contact islands make a per-body physics solver unlikely
to reach that; this sandbox is explicitly the experiment that measures where
Unity Physics stops scaling, with flow-field + separation steering as the
known fallback architecture.

## Design

### 1. Scene & world setup

- New scene `Assets/Scenes/CrowdScene.unity` + subscene
  `Assets/Scenes/CrowdScene/CrowdSub.unity` (mirrors BattleScene layout).
- **Bootstrap gate:** `GameBootstrap.Initialize` returns `false` when the
  active scene is `CrowdScene`. Entities then creates the plain default world —
  no client/server split, no netcode systems, no port binding. (Default-world
  init runs after scene load, so the active-scene check is safe in bootstrap.)
- Crowd systems carry **no `WorldSystemFilter` attribute** → they exist only in
  the default world and never run in BattleScene's server world; conversely the
  battle systems (`ServerSimulation`-filtered) never run in the sandbox.
- Camera: reuse `BattleCameraMono` (plain MonoBehaviour, world-agnostic).

### 2. Code layout & data

New folder `Assets/Scripts/Demo/Crowd/` in the existing `Demo` assembly.

- **`CrowdConfigAuthoring` → `CrowdConfig`** (singleton, baked in subscene):
  soldiers per army, spawn rectangle per army (center + half-extents), goal
  point per army, move speed, arrival radius, soldier prefab entity reference.
- **`CrowdSoldierAuthoring`** on a capsule prefab bakes:
  - `CrowdSoldier { byte Team }` + a per-team material color (BattleScene
    soldier pattern);
  - **dynamic** capsule `PhysicsCollider`;
  - `PhysicsMass` with **infinite rotational inertia** (upright lock — soldiers
    never tip);
  - `PhysicsVelocity` (zero), `PhysicsGravityFactor = 0` (flat-plane sim, no
    ground collider needed);
  - collision material: **friction 0, restitution 0** so crowds slide past each
    other instead of bouncing or sticking.
- **Terrain:** routing data reuses the existing `TerrainRegionAuthoring` +
  `CrossingPortalAuthoring` unchanged (one impassable river region, one bridge
  portal). The *physical* barrier is two static `BoxCollider` GameObjects in
  the subscene flanking the bridge gap (legacy colliders bake to static Unity
  Physics bodies automatically) — the solver itself stops soldiers entering
  the water even if steering misbehaves.

### 3. Systems

Both in `FixedStepSimulationSystemGroup`, ordered **before
`PhysicsSystemGroup`** (steer first, solve after):

1. **`CrowdSpawnSystem`** — runs once, gated by
   `RequireForUpdate<CrowdConfig>`, then disables itself. Bulk-`Instantiate`s
   N soldiers per army inside each spawn rectangle (BattleSpawnSystem-style,
   no per-entity ECB).
2. **`CrowdSteeringSystem`** — Burst `IJobEntity`, stateless per soldier per
   tick:
   - `waypoint = PickWaypoint(pos, goal, regions, portals)`: the goal, unless
     the straight segment to it crosses an impassable `TerrainRegion`
     (`SquadGeometry.SegmentIntersectsBox`), in which case the nearest
     `CrossingPortal` endpoint on the soldier's side;
   - `PhysicsVelocity.Linear = normalize(waypoint − pos) * MoveSpeed`, Y forced
     to 0; inside `ArrivalRadius` of the goal → zero velocity.
   - `PickWaypoint` lives as a pure static function in a `CrowdSteering`
     class (no entity access) so it is directly unit-testable, mirroring
     `SquadGeometry`.

There is deliberately **no separation code and no per-soldier state machine**.
The physics solver resolves all soldier-vs-soldier and soldier-vs-bank
contacts after steering: overlap is impossible by construction and bridge
funneling/congestion is emergent.

**Default scenario:** two armies (~750 each, config-tunable to 1k each) spawn
on opposite banks with goals on the far side, meeting head-on at the bridge —
the maximal stress case for the overlap problem. Setting one army's count to 0
gives a one-way march for calmer iteration.

### 4. Known risks

- **Contact islands at the bridge:** hundreds of touching capsules form one
  solver island and solver cost spikes. Mitigation knobs: capsule collider
  radius slightly under the visual radius, linear damping, solver iteration
  count. Profiling this at 1–2k is a deliverable of the sandbox, not a
  blocker.
- **Head-on gridlock:** two opposing goal-seeking crowds in one gap can
  deadlock (real crowds do). If observed, that is a *finding*; the follow-up
  (e.g., a keep-right bias in steering) is out of v1 scope.
- **Velocity overwrite vs solver:** setting `PhysicsVelocity.Linear` every tick
  discards solver-applied corrective velocity from the previous step. Standard
  for kinematic-feel crowds; positions still cannot interpenetrate because the
  solver runs after steering each tick.

### 5. Testing & acceptance

- **EditMode unit tests** (`Demo.Tests.EditMode`):
  - `CrowdSteering.PickWaypoint`: straight shot when no region blocks; portal
    endpoint chosen when the river blocks; correct (near-side) endpoint per
    bank; goal returned once across.
  - `CrowdSpawnSystem` via `EcsTestsBase`: spawns `2 × N` soldiers, inside
    their rectangles, correct team split.
- **Live validation via Unity MCP** (no physics-in-EditMode harness in v1):
  run CrowdScene, capture scene views, and verify
  - soldiers detour to and funnel through the bridge;
  - after the crowds collide, min pairwise soldier distance ≥ ~2× capsule
    radius (the overlap acceptance check);
  - profiler numbers at 1k and 2k recorded in the implementation notes.

## Consequences

- BattleScene and all squad systems remain fully functional and untouched.
- If the sandbox proves the feel and survives profiling, a follow-up project
  adds combat and then decides between scaling this architecture or swapping
  the movement layer for flow-field steering behind the same data schema.
