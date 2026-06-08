# Authenticate & Own Your Army — Design (thin vertical slice)

**Date:** 2026-06-01
**Status:** Approved, ready for planning
**Scope:** Thin vertical slice. One end-to-end path: client shows a login screen → authenticates (stub auth) → server maps the connection to a user identity → client visually identifies *its* army in the existing `BattleScene`. No account database, no persistence. Proves the ownership wiring.

## Goal

Move from the current anonymous demo (client auto-connects, server spawns two ownerless armies) to a model where a connected user **authenticates** and the server associates that connection with **ownership of one army**, which the client can then **see marked as "mine."**

This slice is deliberately read-only: ownership grants *visual identification only*. No army roster panel, no camera focus, no commands. Those are explicitly out of scope and deferred.

## Decisions (from brainstorming)

- **User→Army mapping:** *Claim an existing team.* `BattleSpawnSystem` keeps spawning the two teams on frame 1. The first authenticated user claims Team 0, the second claims Team 1.
- **Ownership UX:** *Identify as mine only.* No roster panel, no camera move, no commands.
- **Auth:** *Username only, accept any.* Login screen takes a non-empty username; server accepts any name. Empty username is the only failure and is handled silently (connection stays out-of-game; login screen keeps waiting).
- **Highlight:** *Ground ring / outline.* Each owned soldier gets a ring/outline decal at its feet. Existing per-team `SoldierColor` body coloring is unchanged for both friend and foe; the ring sits on top.
- **Ownership mechanism:** *Netcode-native `GhostOwner`* (Approach A). The engine computes `GhostOwnerIsLocal` on the client for free.
- **Overflow connections:** The 3rd+ connection becomes a **spectator** — goes in-game, owns no team, sees both armies as neutral (no ring).

## Architecture

Five pieces, each with a clear boundary. The auth handshake and ownership assignment are **server-authoritative**; the login UI and ring rendering are **client-only/presentation**.

### 1. Auth handshake (layered on top of GoInGame)

The existing `GoInGameClientSystem` auto-send and `GoInGameServerSystem` are **left untouched** — the client still goes in-game automatically and begins receiving ghost snapshots, so the battle renders behind the login overlay and SampleScene is unaffected. Login is layered on top and gates **army ownership**, not in-game status. (This avoids a subscene-load race: gating in-game on login would let the client auto-go-in-game before `BattleConfig` loads, bypassing the gate.)

- **New RPC:** `AuthenticateRequest { FixedString64Bytes Username }` — carries the claimed identity.
- **Client send path:** the login UI writes a `PendingAuth { Username }` singleton into the client world on submit. `ClientAuthSendSystem` (ClientSimulation) observes `PendingAuth`, sends the `AuthenticateRequest` RPC to the server (`SendRpcCommandRequest` with `TargetConnection = Entity.Null` = the single server connection, mirroring `DemoHudController.SendRpc`), then destroys the `PendingAuth` entity.
- **Server receive path:** a new `AuthServerSystem` (server) handles `AuthenticateRequest`, mirroring `RespawnRequestServerSystem`'s shape (query incoming RPCs, look up the requester's `NetworkId`, destroy the request entity). On a request:
  - If `Username` is empty → claim nothing (destroy the request; no ownership granted).
  - If `Username` is non-empty → perform team assignment (§2).
- **Login complete signal (client):** the login panel hides when the local client owns at least one soldier, i.e. a `Soldier` ghost with `GhostOwnerIsLocal` enabled exists. No `AuthResult` RPC is needed. (A 3rd+ spectator owns nothing, so its panel stays up — an accepted edge for this 2-player slice.)

### 2. Server-side ownership assignment

- **State:** a `TeamClaims` singleton component `{ int Team0Owner; int Team1Owner; }` recording which `NetworkId` owns each team. `0` = unclaimed (netcode `NetworkId` values start at 1). `AuthServerSystem` creates the singleton on first update if absent.
- **On a valid auth:**
  1. Pick the team to claim: if `Team0Owner == 0` → team 0; else if `Team1Owner == 0` → team 1; else **spectator** (claim nothing, return).
  2. Record the requester's `NetworkId` in the chosen `TeamClaims` slot.
  3. **Stamp** `GhostOwner.NetworkId = <requester NetworkId>` onto every soldier whose `Team.Value == claimedTeam` (`foreach`/`IJobEntity` over `Soldier`+`Team`+`GhostOwner`). Because `GhostOwner` is replicated, clients observe the change next snapshot.
- **Team-claim invariant:** a team slot, once non-zero, is never reassigned.
- **Assumption:** soldiers are all spawned once by `BattleSpawnSystem` on frame 1 (long before a human types a username), so a one-shot stamp at auth time covers the whole team. No re-stamp system is needed (no soldier respawns in this slice).

### 3. Ghost prefab change

- Add the standard netcode `GhostOwner` component to the soldier ghost via `SoldierAuthoring`'s baker, default `Value = 0` (unowned). One int; coalesces into the existing soldier ghost snapshot.
- Squads remain server-only and untouched.

### 4. Client login UI (UI Toolkit, DemoHud pattern)

- **Assets:** `Assets/UI/LoginHud.{uxml,uss}` — a centered panel with a username text field and an "Enter Battle" button. USS loaded via `<Style src="LoginHud.uss" />` inside the UXML (per the project's UI Toolkit conventions).
- **Controller:** `LoginHudController` mirrors `DemoHudController` — lazy client-world find, request-entity pattern (like `RespawnRequest`). On submit it writes `PendingAuth { Username }`.
- **Visibility:** the login panel is shown until the local connection has `NetworkStreamInGame`, then hidden; the battle view / `BattleHud` takes over.

### 5. Client ownership ring (mirrors health-bar spawn system)

- **Asset:** an `OwnershipRingPrefab` (flat disc/ring mesh authored lying flat on the ground), referenced from `BattleConfig` alongside `HealthBarPrefab`, plus a `RingHeightOffset` (small, e.g. `0.05`).
- **`OwnershipRingSpawnSystem`** (ClientSimulation, PresentationSystemGroup): for each soldier ghost `WithAll<Soldier, LocalTransform, GhostOwnerIsLocal>` that lacks an `OwnershipRingRef`, instantiate the ring, parent it to the soldier at the height offset, set `OwnershipRingRef { Ring }` on the soldier, and register a `LinkedEntityGroup` so the ring despawns with the soldier — identical lifecycle to `HealthBarSpawnSystem`.
- **No update system.** The ring is parented to the soldier with a static local offset, so the transform hierarchy moves it for free. (The health bar needs an update system only to drive its fill from `Health`; the ring has no per-frame data.)
- Enemy and neutral soldiers get no ring. Spectators see no rings at all.

## Data flow (end to end)

1. Client process starts → netcode connects (auto-connect to 127.0.0.1:7979) → `GoInGameClientSystem` auto-sends `GoInGameRequest` (unchanged) → client goes in-game and starts rendering both armies (no rings yet). Login panel is shown as an overlay.
2. User types a username, clicks Enter Battle → `LoginHudController` writes a `PendingAuth { Username }` entity into the client world.
3. `ClientAuthSendSystem` sends `AuthenticateRequest` to the server → destroys `PendingAuth`.
4. `AuthServerSystem` looks up the requester's `NetworkId`, claims the next free team in `TeamClaims`, and stamps `GhostOwner.NetworkId` on that team's soldiers (empty username or no free team → claims nothing).
5. The snapshot replicates `GhostOwner`; netcode enables `GhostOwnerIsLocal` on the client for the owned soldiers.
6. `OwnershipRingSpawnSystem` renders ground rings under the owned soldiers. `LoginHudController` sees a local `GhostOwnerIsLocal` soldier and hides the login panel.

## Testing (EditMode, `EcsTestsBase`)

- `AuthServerSystem` (driven by hand-built `ReceiveRpcCommandRequest` + `AuthenticateRequest` entities and connection entities carrying `NetworkId`):
  - valid username → claims team 0 and stamps `GhostOwner.NetworkId` on team-0 soldiers; team-1 soldiers untouched;
  - a second valid auth from a different `NetworkId` → claims team 1 and stamps team-1 soldiers;
  - empty username → claims nothing (`TeamClaims` unchanged, no soldier stamped);
  - a third valid auth → claims nothing (spectator); both team slots keep their first owners.
- `ClientAuthSendSystem`: given a `PendingAuth` singleton, produces one entity carrying `AuthenticateRequest` (matching username) + `SendRpcCommandRequest`, and the `PendingAuth` entity is consumed.
- `OwnershipRingSpawnSystem`: a soldier with `GhostOwnerIsLocal` enabled and a ring-prefab stub gets exactly one ring (`OwnershipRingRef` set, `LinkedEntityGroup` registered); a soldier without `GhostOwnerIsLocal` gets none.
- Extend the test builders so soldier archetypes include `GhostOwner`, plus a `CreateOwnershipRingStub` helper.
- The login UXML/USS, `LoginHudController`, scene/prefab wiring, and the visual ring appearance are verified in the Editor via Unity MCP (consistent with how health-bar visuals are validated).

## Out of scope (deferred)

- Real authentication (tokens/sessions/passwords), accounts, and persistence of owned resources.
- Army roster UI panel, camera framing on the owned army, friend/foe recoloring.
- Issuing commands to the owned army (the next natural step — and the reason `GhostOwner` was chosen now).
- Spawning a user's army on login (we claim pre-spawned teams instead).

## Files touched (anticipated)

- `Assets/Scripts/Demo/Bootstrap/GoInGame.cs` — **unchanged** (auto-send and `GoInGameServerSystem` retained).
- `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` — bake `GhostOwner { NetworkId = 0 }`.
- `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` (+ `BattleConfig`) — add `OwnershipRingPrefab` + `RingHeightOffset`.
- `Assets/Scripts/Demo/Battle/Auth.cs` (new) — `AuthenticateRequest` RPC, `TeamClaims`, `PendingAuth`, `AuthServerSystem`, `ClientAuthSendSystem`.
- `Assets/Scripts/Demo/Battle/Authoring/OwnershipRingAuthoring.cs` (new) — `OwnershipRingRef` component (+ optional authoring tag for the prefab).
- `Assets/Scripts/Demo/Battle/System/OwnershipRingSpawnSystem.cs` (new).
- `Assets/Scripts/Demo/Battle/UI/LoginHudController.cs` (new).
- `Assets/UI/LoginHud.{uxml,uss}` (new) — login panel.
- `Assets/Tests/EditMode/` — `AuthServerSystemTests`, `ClientAuthSendSystemTests`, `OwnershipRingSpawnSystemTests`; builder extensions (`GhostOwner` on soldiers, `CreateOwnershipRingStub`).
- Prefab: `Assets/Prefabs/OwnershipRing.prefab`; BattleScene `BattleSub` subscene wiring + a `UIDocument` for the login panel; `GhostOwner` added to the soldier ghost prefab's component list.
