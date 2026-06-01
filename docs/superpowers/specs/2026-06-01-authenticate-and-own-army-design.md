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

### 1. Auth handshake (replaces auto-GoInGame)

Today `GoInGameClientSystem` sends `GoInGameRequest` automatically as soon as it has a `NetworkId`. We gate this behind login.

- **New RPC:** `AuthenticateRequest { FixedString64Bytes Username }` — carries the go-in-game intent plus the claimed identity.
- **Client send path:** the login UI writes a `PendingAuth { Username }` singleton into the client world on submit. `ClientAuthSendSystem` (ClientSimulation) observes `PendingAuth`, sends the `AuthenticateRequest` RPC to the server connection, then clears `PendingAuth`. The old auto-send in `GoInGameClientSystem` is removed.
- **Server receive path:** `GoInGameServerSystem` is repurposed/renamed to `AuthServerSystem`. On receiving `AuthenticateRequest`:
  - If `Username` is empty → do nothing (connection stays out-of-game; no `NetworkStreamInGame`). The login screen keeps waiting. No explicit reject RPC.
  - If `Username` is non-empty → add `NetworkStreamInGame` to the connection, record the identity, and perform team assignment (§2).

### 2. Server-side ownership assignment

- **State:** a server-side record of which `NetworkId` owns Team 0 and Team 1, plus a "next free team" cursor. Implemented as a `TeamClaims` singleton (two slots + cursor) or equivalent.
- **On a valid auth:**
  1. If a free team exists, claim the next one; otherwise the connection is a **spectator** (skip the remaining steps).
  2. Store `ConnectionOwner { NetworkId, Team }` on the connection entity.
  3. **Stamp** `GhostOwner.Value = <connection NetworkId>` onto every already-spawned soldier whose `Team.Value == claimedTeam`, via an `IJobEntity` over soldiers. Because `GhostOwner` is replicated, clients observe the change on the next snapshot.
- **Team-claim invariant:** the cursor never assigns the same team twice.

### 3. Ghost prefab change

- Add the standard netcode `GhostOwner` component to the soldier ghost via `SoldierAuthoring`'s baker, default `Value = 0` (unowned). One int; coalesces into the existing soldier ghost snapshot.
- Squads remain server-only and untouched.

### 4. Client login UI (UI Toolkit, DemoHud pattern)

- **Assets:** `Assets/UI/LoginHud.{uxml,uss}` — a centered panel with a username text field and an "Enter Battle" button. USS loaded via `<Style src="LoginHud.uss" />` inside the UXML (per the project's UI Toolkit conventions).
- **Controller:** `LoginHudController` mirrors `DemoHudController` — lazy client-world find, request-entity pattern (like `RespawnRequest`). On submit it writes `PendingAuth { Username }`.
- **Visibility:** the login panel is shown until the local connection has `NetworkStreamInGame`, then hidden; the battle view / `BattleHud` takes over.

### 5. Client ownership ring (mirrors health-bar systems)

- **Asset:** an `OwnershipRingPrefab` (flat disc/ring quad with a ring material), referenced from `BattleConfig` alongside `HealthBarPrefab`.
- **`OwnershipRingSpawnSystem`** (ClientSimulation, PresentationSystemGroup): for each soldier ghost `WithAll<GhostOwnerIsLocal>` that lacks a ring, instantiate the ring. Same managed-companion lifecycle/cleanup shape as `HealthBarSpawnSystem`.
- **`OwnershipRingUpdateSystem`** (`UpdateAfter(OwnershipRingSpawnSystem)`): position each ring at its soldier's feet. Mirrors `HealthBarUpdateSystem`.
- Enemy and neutral soldiers get no ring. Spectators see no rings at all.

## Data flow (end to end)

1. Client process starts → netcode connects (auto-connect to 127.0.0.1:7979 unchanged) → client world gets a `NetworkId`. Login panel visible; **no** auto-GoInGame.
2. User types a username, clicks Enter Battle → `LoginHudController` writes `PendingAuth { Username }`.
3. `ClientAuthSendSystem` sends `AuthenticateRequest` → clears `PendingAuth`.
4. `AuthServerSystem` validates, adds `NetworkStreamInGame`, claims next free team, writes `ConnectionOwner`, stamps `GhostOwner` on that team's soldiers.
5. Snapshot replicates `GhostOwner`; netcode enables `GhostOwnerIsLocal` on the client for owned soldiers.
6. `OwnershipRingSpawnSystem`/`UpdateSystem` render rings under owned soldiers. Login panel hides (connection now `NetworkStreamInGame`).

## Testing (EditMode, `EcsTestsBase`)

- `AuthServerSystem`:
  - valid username → claims the next free team and stamps `GhostOwner` on that team's soldiers;
  - a second valid auth → claims the other team;
  - empty username → claims nothing, no `NetworkStreamInGame`;
  - a third valid auth → claims nothing (spectator), no team stamped.
- Team-claim cursor never double-assigns a team.
- Extend the test builders with a soldier carrying `GhostOwner`.
- Pure client-side ring rendering and the login UI are verified manually in the Editor (consistent with how the existing health-bar visuals are validated).

## Out of scope (deferred)

- Real authentication (tokens/sessions/passwords), accounts, and persistence of owned resources.
- Army roster UI panel, camera framing on the owned army, friend/foe recoloring.
- Issuing commands to the owned army (the next natural step — and the reason `GhostOwner` was chosen now).
- Spawning a user's army on login (we claim pre-spawned teams instead).

## Files touched (anticipated)

- `Assets/Scripts/Demo/Bootstrap/GoInGame.cs` — remove client auto-send; repurpose server system as `AuthServerSystem`; add `AuthenticateRequest` RPC and `PendingAuth` + `ConnectionOwner` components (or split into new files).
- `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` — bake `GhostOwner`.
- `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` (+ `BattleConfig`) — add `OwnershipRingPrefab`.
- `Assets/Scripts/Demo/Battle/System/` — new `OwnershipRingSpawnSystem`, `OwnershipRingUpdateSystem`; new server `TeamClaims`/assignment logic.
- `Assets/Scripts/Demo/UI/` or `Assets/Scripts/Demo/Battle/UI/` — `LoginHudController`, `PendingAuth` request type.
- `Assets/UI/LoginHud.{uxml,uss}` — new login panel.
- `Assets/Tests/EditMode/` — `AuthServerSystem` tests; builder extension for `GhostOwner`.
- Prefabs: `OwnershipRingPrefab`.
