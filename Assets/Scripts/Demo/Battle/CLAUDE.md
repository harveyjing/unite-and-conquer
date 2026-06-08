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
