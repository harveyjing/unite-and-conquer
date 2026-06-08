# Authenticate & Own Your Army — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user brings up a client, types a username on a login screen, and the server grants them ownership of one of the two pre-spawned armies — which the client then marks with a ground ring under each owned soldier.

**Architecture:** Login is layered on top of the existing (unchanged) GoInGame handshake and gates *army ownership*, not in-game status. A client RPC (`AuthenticateRequest`) makes the server claim the next free team in a `TeamClaims` singleton and stamp the netcode `GhostOwner` onto that team's soldiers. Netcode replicates `GhostOwner` and enables `GhostOwnerIsLocal` on the owning client, where a client-only spawn system instantiates a ring under each owned soldier. Server logic and the client send path are unit-tested in a bare ECS world; the login UI and ring visuals are verified in the Editor via Unity MCP.

**Tech Stack:** Unity 6000.4.1f1, Entities 1.4.x, Netcode for Entities 1.13.1, Burst, UI Toolkit. Tests: NUnit EditMode via `EcsTestsBase`.

**Spec:** `docs/superpowers/specs/2026-06-01-authenticate-and-own-army-design.md`

**Key gotcha (read before Task 4):** `HealthBarSpawnSystem` already calls `em.AddBuffer<LinkedEntityGroup>(soldier)` for *every* soldier. `AddBuffer` *replaces* any existing buffer. An owned soldier gets both a health bar and a ring, so `OwnershipRingSpawnSystem` must **append to** the existing `LinkedEntityGroup` (not `AddBuffer`), and must run **after** `HealthBarSpawnSystem` (`[UpdateAfter]`). Otherwise the two systems clobber each other's despawn links.

---

## Task 1: Add `GhostOwner` to soldiers + extend test builders

This is plumbing for every later task: the soldier ghost must carry `GhostOwner` so the server can stamp ownership, and the test soldier archetypes must include it so later unit tests can assert on it.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs` (baker)
- Modify: `Assets/Tests/EditMode/EcsTestsBase.cs` (`CreateSoldier`)
- Modify: `Assets/Tests/EditMode/EcsTestsBase.Battle.cs` (`CreateSoldierPrefabStub`)

- [ ] **Step 1: Add `GhostOwner` to the soldier baker**

In `SoldierAuthoring.cs`, inside `Baker.Bake`, after the `SquadMembership` line, add:

```csharp
                // Owner identity for replication. 0 = unowned; the server stamps
                // the claiming connection's NetworkId on auth (AuthServerSystem).
                // Netcode replicates this and enables GhostOwnerIsLocal on the owner.
                AddComponent(entity, new GhostOwner { NetworkId = 0 });
```

(`Unity.NetCode` is already imported in this file.)

- [ ] **Step 2: Add `GhostOwner` + a team parameter to the unit-test soldier builder**

In `EcsTestsBase.cs`, replace the `CreateSoldier` method with:

```csharp
        protected Entity CreateSoldier(
            Entity squad, int slot, float3 pos,
            float health = 50f, float attackRange = 0.8f, float dps = 25f,
            int team = 0)
        {
            var e = Manager.CreateEntity(
                typeof(Soldier), typeof(Team), typeof(Health), typeof(AttackStats),
                typeof(SquadMembership), typeof(LocalTransform), typeof(GhostOwner));
            Manager.SetComponentData(e, new Team { Value = team });
            Manager.SetComponentData(e, new SquadMembership { Squad = squad, SlotIndex = slot });
            Manager.SetComponentData(e, new Health { Current = health, Max = health });
            Manager.SetComponentData(e, new AttackStats { Range = attackRange, Dps = dps });
            Manager.SetComponentData(e, LocalTransform.FromPosition(pos));
            Manager.SetComponentData(e, new GhostOwner { NetworkId = 0 });
            return e;
        }
```

(`Unity.NetCode` is already imported in `EcsTestsBase.cs`.)

- [ ] **Step 3: Add `GhostOwner` to the spawn-prefab stub**

In `EcsTestsBase.Battle.cs`, in `CreateSoldierPrefabStub`, add `typeof(GhostOwner)` to the `CreateEntity` archetype list and set it. Replace the `CreateEntity(...)` call and add one `SetComponentData`:

```csharp
            var e = Manager.CreateEntity(
                typeof(Soldier), typeof(Team), typeof(SoldierColor), typeof(Health),
                typeof(AttackStats), typeof(SquadMembership),
                typeof(LocalTransform), typeof(LocalToWorld), typeof(GhostOwner));
```

Then after the existing `SetComponentData(e, LocalTransform.Identity)` line add:

```csharp
            Manager.SetComponentData(e, new GhostOwner { NetworkId = 0 });
```

- [ ] **Step 4: Run the full EditMode suite to confirm nothing regressed**

Run the `Demo.Tests.EditMode` suite via Unity MCP (Test Runner → EditMode → Run All).
Expected: all existing tests PASS (adding an unused component to the archetypes is non-breaking). Then call `Unity_GetConsoleLogs` and confirm zero compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/SoldierAuthoring.cs Assets/Tests/EditMode/EcsTestsBase.cs Assets/Tests/EditMode/EcsTestsBase.Battle.cs
git commit -m "feat(battle): bake GhostOwner on soldiers; thread it through test builders"
```

---

## Task 2: `AuthenticateRequest` RPC + `TeamClaims` + `AuthServerSystem`

Server claims the next free team and stamps `GhostOwner` on that team's soldiers.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/Auth.cs`
- Test: `Assets/Tests/EditMode/AuthServerSystemTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/AuthServerSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    public class AuthServerSystemTests : EcsTestsBase
    {
        // Creates a connection entity carrying a NetworkId.
        Entity CreateConnection(int networkId)
        {
            var e = Manager.CreateEntity(typeof(NetworkId));
            Manager.SetComponentData(e, new NetworkId { Value = networkId });
            return e;
        }

        // Creates an incoming auth RPC as the server sees it after receive.
        void SendAuth(Entity connection, string username)
        {
            var e = Manager.CreateEntity(
                typeof(ReceiveRpcCommandRequest), typeof(AuthenticateRequest));
            Manager.SetComponentData(e, new ReceiveRpcCommandRequest { SourceConnection = connection });
            Manager.SetComponentData(e, new AuthenticateRequest { Username = username });
        }

        // Two soldiers per team (teams 0 and 1), all unowned.
        void SpawnTwoTeams()
        {
            for (int t = 0; t < 2; t++)
                for (int i = 0; i < 2; i++)
                    CreateSoldier(Entity.Null, i, float3.zero, team: t);
        }

        int OwnerOfTeam(int team)
        {
            // Returns the GhostOwner.NetworkId of the first soldier on `team`.
            var q = Manager.CreateEntityQuery(typeof(Soldier), typeof(Team), typeof(GhostOwner));
            var ents = q.ToEntityArray(Allocator.Temp);
            int owner = -1;
            foreach (var e in ents)
                if (Manager.GetComponentData<Team>(e).Value == team)
                    owner = Manager.GetComponentData<GhostOwner>(e).NetworkId;
            ents.Dispose();
            return owner;
        }

        [Test]
        public void FirstValidAuth_ClaimsTeam0_AndStampsTeam0Soldiers()
        {
            SpawnTwoTeams();
            var conn = CreateConnection(1);
            SendAuth(conn, "cao_cao");

            CreateAndUpdateSystem<AuthServerSystem>();

            Assert.AreEqual(1, OwnerOfTeam(0), "team 0 soldiers should be owned by NetworkId 1");
            Assert.AreEqual(0, OwnerOfTeam(1), "team 1 soldiers should stay unowned");

            var claims = Manager.CreateEntityQuery(typeof(TeamClaims)).GetSingleton<TeamClaims>();
            Assert.AreEqual(1, claims.Team0Owner);
            Assert.AreEqual(0, claims.Team1Owner);
        }

        [Test]
        public void SecondValidAuth_ClaimsTeam1()
        {
            SpawnTwoTeams();

            var c1 = CreateConnection(1);
            SendAuth(c1, "cao_cao");
            CreateAndUpdateSystem<AuthServerSystem>();

            var c2 = CreateConnection(2);
            SendAuth(c2, "liu_bei");
            // Re-run the same system handle's OnUpdate by creating it again is not
            // possible (one handle per world), so drive a second tick via the helper.
            UpdateExistingSystem<AuthServerSystem>();

            Assert.AreEqual(1, OwnerOfTeam(0));
            Assert.AreEqual(2, OwnerOfTeam(1));
        }

        [Test]
        public void EmptyUsername_ClaimsNothing()
        {
            SpawnTwoTeams();
            var conn = CreateConnection(1);
            SendAuth(conn, "");

            CreateAndUpdateSystem<AuthServerSystem>();

            Assert.AreEqual(0, OwnerOfTeam(0));
            Assert.AreEqual(0, OwnerOfTeam(1));
        }

        [Test]
        public void ThirdAuth_IsSpectator_NoTeamReassigned()
        {
            SpawnTwoTeams();

            SendAuth(CreateConnection(1), "a");
            CreateAndUpdateSystem<AuthServerSystem>();
            SendAuth(CreateConnection(2), "b");
            UpdateExistingSystem<AuthServerSystem>();
            SendAuth(CreateConnection(3), "c");
            UpdateExistingSystem<AuthServerSystem>();

            Assert.AreEqual(1, OwnerOfTeam(0), "team 0 keeps its first owner");
            Assert.AreEqual(2, OwnerOfTeam(1), "team 1 keeps its first owner");
        }
    }
}
```

Note this test uses a helper `UpdateExistingSystem<T>` that does not yet exist — add it in Step 2.

- [ ] **Step 2: Add the `UpdateExistingSystem` helper to the test base**

`CreateAndUpdateSystem<T>` creates a new system each call, but a world allows only one handle per system type. Multi-tick auth tests need to re-tick the same handle. Add this to `EcsTestsBase.cs` right after `CreateAndUpdateSystem`:

```csharp
        // Re-runs OnUpdate on an already-created system of type T (one handle per
        // world). Use after CreateAndUpdateSystem<T>() when a test needs another tick.
        protected void UpdateExistingSystem<T>() where T : unmanaged, ISystem
        {
            var handle = World.GetExistingSystem<T>();
            ref var stateRef = ref World.Unmanaged.ResolveSystemStateRef(handle);
            World.Unmanaged.GetUnsafeSystemRef<T>(handle).OnUpdate(ref stateRef);
            stateRef.Dependency.Complete();
        }
```

- [ ] **Step 3: Run the test to verify it fails**

Run `AuthServerSystemTests` via Unity MCP (Test Runner, filter class `AuthServerSystemTests`).
Expected: FAIL to compile — `AuthenticateRequest`, `TeamClaims`, `AuthServerSystem` are undefined.

- [ ] **Step 4: Create `Auth.cs` with the RPC, singleton, and server system**

Create `Assets/Scripts/Demo/Battle/Auth.cs`:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Demo
{
    // Client → server: a user claims an army under this username.
    public struct AuthenticateRequest : IRpcCommand
    {
        public FixedString64Bytes Username;
    }

    // Server-only singleton: which NetworkId owns each team.
    // 0 = unclaimed (netcode NetworkId.Value starts at 1).
    public struct TeamClaims : IComponentData
    {
        public int Team0Owner;
        public int Team1Owner;
    }

    // Server: handles AuthenticateRequest. Claims the next free team for the
    // requesting connection and stamps GhostOwner on that team's soldiers.
    // Mirrors RespawnRequestServerSystem's RPC-handling shape.
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct AuthServerSystem : ISystem
    {
        ComponentLookup<NetworkId> _networkIdLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AuthenticateRequest>()
                .WithAll<ReceiveRpcCommandRequest>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _networkIdLookup.Update(ref state);
            var em = state.EntityManager;

            // Ensure the claims singleton exists, then read it.
            if (!SystemAPI.TryGetSingletonEntity<TeamClaims>(out var claimsEntity))
                claimsEntity = em.CreateEntity(typeof(TeamClaims));
            var claims = em.GetComponentData<TeamClaims>(claimsEntity);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var stampTeam  = new NativeList<int>(4, Allocator.Temp);
            var stampOwner = new NativeList<int>(4, Allocator.Temp);

            // Pass 1: resolve each request to a (team, owner) claim.
            foreach (var (rpc, req, reqEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<AuthenticateRequest>>()
                              .WithEntityAccess())
            {
                ecb.DestroyEntity(reqEntity);

                if (req.ValueRO.Username.Length == 0) continue;

                var src = rpc.ValueRO.SourceConnection;
                if (!_networkIdLookup.HasComponent(src)) continue;
                int id = _networkIdLookup[src].Value;

                int team;
                if      (claims.Team0Owner == 0) { claims.Team0Owner = id; team = 0; }
                else if (claims.Team1Owner == 0) { claims.Team1Owner = id; team = 1; }
                else continue; // no free team → spectator

                stampTeam.Add(team);
                stampOwner.Add(id);
            }

            em.SetComponentData(claimsEntity, claims);

            // Pass 2: stamp GhostOwner on each newly-claimed team's soldiers.
            if (stampTeam.Length > 0)
            {
                foreach (var (team, owner) in
                         SystemAPI.Query<RefRO<Team>, RefRW<GhostOwner>>().WithAll<Soldier>())
                {
                    for (int i = 0; i < stampTeam.Length; i++)
                    {
                        if (team.ValueRO.Value == stampTeam[i])
                        {
                            owner.ValueRW.NetworkId = stampOwner[i];
                            break;
                        }
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
            stampTeam.Dispose();
            stampOwner.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run `AuthServerSystemTests` via Unity MCP.
Expected: all four tests PASS. Call `Unity_GetConsoleLogs`; confirm zero errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Auth.cs Assets/Tests/EditMode/AuthServerSystemTests.cs Assets/Tests/EditMode/EcsTestsBase.cs
git commit -m "feat(battle): AuthServerSystem claims a team and stamps GhostOwner"
```

---

## Task 3: `PendingAuth` + `ClientAuthSendSystem`

Client-side: turn a `PendingAuth` request (written by the login UI) into an outgoing RPC.

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Auth.cs` (append client types)
- Test: `Assets/Tests/EditMode/ClientAuthSendSystemTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/ClientAuthSendSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Demo.Tests
{
    public class ClientAuthSendSystemTests : EcsTestsBase
    {
        [Test]
        public void PendingAuth_ProducesOneRpc_AndIsConsumed()
        {
            var pending = Manager.CreateEntity(typeof(PendingAuth));
            Manager.SetComponentData(pending, new PendingAuth { Username = "cao_cao" });

            CreateAndUpdateSystem<ClientAuthSendSystem>();

            // Exactly one outgoing RPC with the right username + send marker.
            var rpcQuery = Manager.CreateEntityQuery(
                typeof(AuthenticateRequest), typeof(SendRpcCommandRequest));
            Assert.AreEqual(1, rpcQuery.CalculateEntityCount());
            var rpc = rpcQuery.GetSingleton<AuthenticateRequest>();
            Assert.AreEqual(new FixedString64Bytes("cao_cao"), rpc.Username);

            // PendingAuth consumed.
            var pendingQuery = Manager.CreateEntityQuery(typeof(PendingAuth));
            Assert.AreEqual(0, pendingQuery.CalculateEntityCount());
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run `ClientAuthSendSystemTests` via Unity MCP.
Expected: FAIL to compile — `PendingAuth` and `ClientAuthSendSystem` are undefined.

- [ ] **Step 3: Append the client types to `Auth.cs`**

Add to `Assets/Scripts/Demo/Battle/Auth.cs`, inside the `Demo` namespace (after the existing types):

```csharp
    // Client-only request written by the login UI: send this username to the server.
    public struct PendingAuth : IComponentData
    {
        public FixedString64Bytes Username;
    }

    // Client: drains PendingAuth requests into outgoing AuthenticateRequest RPCs.
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct ClientAuthSendSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PendingAuth>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (pending, entity) in
                     SystemAPI.Query<RefRO<PendingAuth>>().WithEntityAccess())
            {
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new AuthenticateRequest { Username = pending.ValueRO.Username });
                ecb.AddComponent(rpc, new SendRpcCommandRequest()); // Null target = server
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run `ClientAuthSendSystemTests` via Unity MCP.
Expected: PASS. Call `Unity_GetConsoleLogs`; confirm zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Auth.cs Assets/Tests/EditMode/ClientAuthSendSystemTests.cs
git commit -m "feat(battle): ClientAuthSendSystem turns PendingAuth into an RPC"
```

---

## Task 4: Ring config fields + `OwnershipRingRef` + `OwnershipRingSpawnSystem`

Client-only: instantiate a ground ring under each locally-owned soldier. **Re-read the "Key gotcha" at the top before writing this — the `LinkedEntityGroup` append logic is the tricky part.**

**Files:**
- Modify: `Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs` (struct + authoring + baker)
- Modify: `Assets/Tests/EditMode/EcsTestsBase.cs` (`CreateBattleConfig` + `CreateOwnershipRingStub`)
- Create: `Assets/Scripts/Demo/Battle/System/OwnershipRingSpawnSystem.cs`
- Test: `Assets/Tests/EditMode/OwnershipRingSpawnSystemTests.cs`

- [ ] **Step 1: Add ring fields to `BattleConfig` and its authoring/baker**

In `BattleConfigAuthoring.cs`, add to the `BattleConfig` struct after the health-bar fields:

```csharp
        // Ownership ring (client-only marker for the local player's army).
        public Entity OwnershipRingPrefab;
        public float  RingHeightOffset;
```

Add to the `BattleConfigAuthoring` MonoBehaviour after the health-bar fields:

```csharp
        [Header("Ownership ring")]
        [Tooltip("OwnershipRing prefab — flat ring/disc mesh, see Assets/Prefabs/OwnershipRing.prefab (Task 7).")]
        public GameObject OwnershipRingPrefab;
        public float      RingHeightOffset = 0.05f;
```

In the baker's `AddComponent(entity, new BattleConfig { ... })`, add these two members (after `HealthBarHeightOffset = authoring.HealthBarHeightOffset,`):

```csharp
                    OwnershipRingPrefab = authoring.OwnershipRingPrefab != null
                        ? GetEntity(authoring.OwnershipRingPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    RingHeightOffset = authoring.RingHeightOffset,
```

- [ ] **Step 2: Extend the test config builder + add a ring stub**

In `EcsTestsBase.cs`, change the `CreateBattleConfig` signature to add two parameters (place them next to the health-bar params):

```csharp
            Entity healthBarPrefab = default,
            float healthBarHeightOffset = 1.2f,
            Entity ownershipRingPrefab = default,
            float ringHeightOffset = 0.05f)
```

and add the two members to the `new BattleConfig { ... }` initializer (after `HealthBarHeightOffset = healthBarHeightOffset,`):

```csharp
                OwnershipRingPrefab   = ownershipRingPrefab,
                RingHeightOffset      = ringHeightOffset,
```

Then add this helper next to `CreateHealthBarStub`:

```csharp
        // Stand-in for the baked OwnershipRing prefab: a renderable entity with a
        // transform that OwnershipRingSpawnSystem clones via EntityManager.Instantiate.
        protected Entity CreateOwnershipRingStub()
        {
            var e = Manager.CreateEntity(typeof(LocalTransform));
            Manager.SetComponentData(e, LocalTransform.Identity);
            return e;
        }
```

- [ ] **Step 3: Write the failing test**

Create `Assets/Tests/EditMode/OwnershipRingSpawnSystemTests.cs`:

```csharp
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo.Tests
{
    public class OwnershipRingSpawnSystemTests : EcsTestsBase
    {
        Entity SetupConfigWithRing()
        {
            var ring = CreateOwnershipRingStub();
            return CreateBattleConfig(ownershipRingPrefab: ring);
        }

        [Test]
        public void OwnedSoldier_GetsExactlyOneRing()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero);
            Manager.AddComponent<GhostOwnerIsLocal>(soldier); // enabled by default

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            Assert.IsTrue(Manager.HasComponent<OwnershipRingRef>(soldier),
                "owned soldier should have an OwnershipRingRef");
            var ring = Manager.GetComponentData<OwnershipRingRef>(soldier).Ring;
            Assert.AreNotEqual(Entity.Null, ring);
            Assert.AreEqual(soldier, Manager.GetComponentData<Parent>(ring).Value);

            var group = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(2, group.Length, "LinkedEntityGroup = soldier + ring");
            Assert.AreEqual(soldier, group[0].Value);
            Assert.AreEqual(ring, group[1].Value);
        }

        [Test]
        public void UnownedSoldier_GetsNoRing()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero); // no GhostOwnerIsLocal

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            Assert.IsFalse(Manager.HasComponent<OwnershipRingRef>(soldier));
        }

        [Test]
        public void Ring_AppendsToExistingLinkedGroup_FromHealthBar()
        {
            SetupConfigWithRing();
            var soldier = CreateSoldier(Entity.Null, 0, float3.zero);
            Manager.AddComponent<GhostOwnerIsLocal>(soldier);

            // Simulate HealthBarSpawnSystem having already linked a bar.
            var bar = Manager.CreateEntity(typeof(LocalTransform));
            var group = Manager.AddBuffer<LinkedEntityGroup>(soldier);
            group.Add(new LinkedEntityGroup { Value = soldier });
            group.Add(new LinkedEntityGroup { Value = bar });

            CreateAndUpdateSystem<OwnershipRingSpawnSystem>();

            var after = Manager.GetBuffer<LinkedEntityGroup>(soldier);
            Assert.AreEqual(3, after.Length, "soldier + bar + ring; existing links preserved");
            Assert.AreEqual(soldier, after[0].Value);
            Assert.AreEqual(bar, after[1].Value);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run `OwnershipRingSpawnSystemTests` via Unity MCP.
Expected: FAIL to compile — `OwnershipRingSpawnSystem` and `OwnershipRingRef` are undefined.

- [ ] **Step 5: Create the spawn system**

Create `Assets/Scripts/Demo/Battle/System/OwnershipRingSpawnSystem.cs`:

```csharp
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    // On an owned soldier (client-only): reference to its ground-ring entity.
    public struct OwnershipRingRef : IComponentData
    {
        public Entity Ring;
    }

    // Client-only presentation. For each locally-owned soldier (GhostOwnerIsLocal)
    // without a ring, instantiate the OwnershipRing prefab, parent it at the soldier's
    // feet, and link it for despawn. Runs AFTER HealthBarSpawnSystem and APPENDS to the
    // LinkedEntityGroup so it does not clobber the health-bar link (AddBuffer replaces).
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(HealthBarSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct OwnershipRingSpawnSystem : ISystem
    {
        EntityQuery _needsRing;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleConfig>();

            _needsRing = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Soldier, LocalTransform, GhostOwnerIsLocal>()
                .WithNone<OwnershipRingRef>()
                .Build(ref state);
            state.RequireForUpdate(_needsRing);
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<BattleConfig>();
            if (config.OwnershipRingPrefab == Entity.Null) return;

            var em = state.EntityManager;
            using var soldiers = _needsRing.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < soldiers.Length; i++)
            {
                var soldier = soldiers[i];

                var ring = em.Instantiate(config.OwnershipRingPrefab);
                em.AddComponentData(ring, new Parent { Value = soldier });
                em.SetComponentData(ring, LocalTransform.FromPosition(
                    new float3(0f, config.RingHeightOffset, 0f)));

                em.AddComponentData(soldier, new OwnershipRingRef { Ring = ring });

                // Append to the existing LinkedEntityGroup (HealthBarSpawnSystem may
                // have created it). AddBuffer would replace and drop the bar link.
                DynamicBuffer<LinkedEntityGroup> group;
                if (em.HasBuffer<LinkedEntityGroup>(soldier))
                {
                    group = em.GetBuffer<LinkedEntityGroup>(soldier);
                }
                else
                {
                    group = em.AddBuffer<LinkedEntityGroup>(soldier);
                    group.Add(new LinkedEntityGroup { Value = soldier });
                }
                group.Add(new LinkedEntityGroup { Value = ring });
            }
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run `OwnershipRingSpawnSystemTests` via Unity MCP.
Expected: all three tests PASS. Call `Unity_GetConsoleLogs`; confirm zero errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Demo/Battle/Authoring/BattleConfigAuthoring.cs Assets/Scripts/Demo/Battle/System/OwnershipRingSpawnSystem.cs Assets/Tests/EditMode/EcsTestsBase.cs Assets/Tests/EditMode/OwnershipRingSpawnSystemTests.cs
git commit -m "feat(battle): spawn an ownership ring under locally-owned soldiers"
```

---

## Task 5: `LoginHudController`

MonoBehaviour bridging the login UI to the client world. No EditMode test (it's a `MonoBehaviour` driving UI Toolkit) — verified in Task 7 via Unity MCP. Mirrors `DemoHudController`'s lazy client-world find.

**Files:**
- Create: `Assets/Scripts/Demo/Battle/UI/LoginHudController.cs`

- [ ] **Step 1: Create the controller**

Create `Assets/Scripts/Demo/Battle/UI/LoginHudController.cs`:

```csharp
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Demo
{
    // Bridges the login UIDocument to the ECS client world.
    //
    // OnEnable: wire the username field + Enter button.
    // Enter click: write a PendingAuth entity into the client world
    //   (ClientAuthSendSystem turns it into an AuthenticateRequest RPC).
    // Update: lazy-find the client world; once the local player owns a soldier
    //   (a Soldier with GhostOwnerIsLocal exists) hide the panel.
    [RequireComponent(typeof(UIDocument))]
    public class LoginHudController : MonoBehaviour
    {
        VisualElement _panel;
        TextField _usernameField;
        Button _enterBtn;
        EventCallback<ClickEvent> _enterHandler;

        World _clientWorld;
        EntityQuery _ownedSoldierQuery;
        bool _loggedIn;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("LoginHudController: UIDocument rootVisualElement is null.");
                enabled = false;
                return;
            }

            _panel         = root.Q<VisualElement>("login-panel");
            _usernameField = root.Q<TextField>("username-field");
            _enterBtn      = root.Q<Button>("enter-btn");

            _enterHandler = _ => SubmitLogin();
            _enterBtn?.RegisterCallback(_enterHandler);
        }

        void OnDisable()
        {
            if (_enterBtn != null && _enterHandler != null)
                _enterBtn.UnregisterCallback(_enterHandler);
            if (_ownedSoldierQuery != default) _ownedSoldierQuery.Dispose();
            _ownedSoldierQuery = default;
            _clientWorld = null;
        }

        static World FindClientWorld()
        {
            foreach (var w in World.All)
                if (w.Name.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        void SubmitLogin()
        {
            var name = _usernameField?.value;
            if (string.IsNullOrWhiteSpace(name)) return; // server also rejects empty
            if (_clientWorld == null || !_clientWorld.IsCreated) return;

            var em = _clientWorld.EntityManager;
            var e = em.CreateEntity(typeof(PendingAuth));
            em.SetComponentData(e, new PendingAuth { Username = new FixedString64Bytes(name) });
        }

        void Update()
        {
            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                if (_ownedSoldierQuery != default) _ownedSoldierQuery.Dispose();
                _ownedSoldierQuery = default;
                _clientWorld = FindClientWorld();
                if (_clientWorld == null) return;

                _ownedSoldierQuery = _clientWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<Soldier>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
            }

            if (_loggedIn) return;

            if (!_ownedSoldierQuery.IsEmpty)
            {
                _loggedIn = true;
                if (_panel != null) _panel.style.display = DisplayStyle.None;
            }
        }
    }
}
```

- [ ] **Step 2: Confirm it compiles**

In the Editor, let it recompile, then call `Unity_GetConsoleLogs`.
Expected: zero compile errors. (No unit test for this MonoBehaviour.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Demo/Battle/UI/LoginHudController.cs
git commit -m "feat(battle): LoginHudController writes PendingAuth and hides on ownership"
```

---

## Task 6: Login UI assets (`LoginHud.uxml` + `LoginHud.uss`)

**Files:**
- Create: `Assets/UI/LoginHud.uxml`
- Create: `Assets/UI/LoginHud.uss`

- [ ] **Step 1: Create the UXML**

Create `Assets/UI/LoginHud.uxml` (element names match the `root.Q<...>("...")` lookups in `LoginHudController`; USS is referenced via `<Style src>` per the project's UI Toolkit conventions):

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <Style src="LoginHud.uss" />
    <ui:VisualElement name="login-overlay" class="overlay">
        <ui:VisualElement name="login-panel" class="panel">
            <ui:Label text="Enter the Battle" class="title" />
            <ui:TextField name="username-field" label="Username" class="field" />
            <ui:Button name="enter-btn" text="Enter Battle" class="enter-btn" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Step 2: Create the USS**

Create `Assets/UI/LoginHud.uss`:

```css
.overlay {
    flex-grow: 1;
    align-items: center;
    justify-content: center;
    background-color: rgba(0, 0, 0, 0.55);
}

.panel {
    width: 360px;
    padding: 24px;
    background-color: rgba(20, 20, 28, 0.96);
    border-radius: 8px;
    align-items: stretch;
}

.title {
    font-size: 22px;
    color: rgb(235, 235, 235);
    -unity-text-align: middle-center;
    margin-bottom: 16px;
}

.field {
    margin-bottom: 16px;
}

.enter-btn {
    height: 36px;
    font-size: 15px;
}
```

- [ ] **Step 3: Confirm assets import cleanly**

Let the Editor import the assets, then call `Unity_GetConsoleLogs`.
Expected: zero import/parse errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/UI/LoginHud.uxml Assets/UI/LoginHud.uss
git commit -m "feat(battle): login HUD UXML + USS"
```

---

## Task 7: Editor wiring + end-to-end verification (Unity MCP)

No code. All steps via Unity MCP per CLAUDE.md (no manual Inspector clicks). After every change call `Unity_GetConsoleLogs` and confirm zero errors.

**Touches:** `Assets/Prefabs/OwnershipRing.prefab` (new), the soldier ghost prefab, the `BattleSub` subscene (`BattleConfigAuthoring` fields), `BattleScene` (login `UIDocument`).

- [ ] **Step 1: Create the OwnershipRing prefab**

Via Unity MCP, create `Assets/Prefabs/OwnershipRing.prefab`: a flat ground disc that reads as a friendly base marker.
- Start from a Quad (or Cylinder scaled to a flat disc). If a Quad: rotate it -90° about X so it lies flat on the ground; scale ~0.8.
- Material: a bright unlit/emissive friendly color (e.g. cyan `#22E0FF`), rendered slightly translucent.
- Remove any collider component.
The prefab needs **no** custom authoring script — `BattleConfigAuthoring`'s baker bakes it via `GetEntity(OwnershipRingPrefab, TransformUsageFlags.Dynamic)`, and Entities.Graphics auto-bakes the MeshRenderer.
(A true hollow torus ring is a nice-to-have; a disc halo satisfies the "ground ring" intent and can be swapped later.)

- [ ] **Step 2: Add `GhostOwner` to the soldier ghost prefab**

The soldier prefab already has `SoldierAuthoring`; Task 1 made its baker add `GhostOwner`. Via Unity MCP, reimport the soldier prefab and confirm the ghost's replicated component list now includes `GhostOwner` (netcode auto-detects it). Call `Unity_GetConsoleLogs`; confirm zero ghost-configuration errors.

- [ ] **Step 3: Assign the ring prefab in `BattleConfigAuthoring`**

Via Unity MCP, open the `BattleSub` subscene, select the `BattleConfigAuthoring` object, set `OwnershipRingPrefab` = `Assets/Prefabs/OwnershipRing.prefab`, leave `RingHeightOffset` = 0.05. Save the subscene.

- [ ] **Step 4: Add the login UIDocument to BattleScene**

Via Unity MCP, in `BattleScene` add a GameObject `LoginHud` with: a `UIDocument` (Source Asset = `Assets/UI/LoginHud.uxml`, a `PanelSettings` asset — reuse the existing battle HUD's PanelSettings if present, else create one) and the `LoginHudController` component. Save the scene.

- [ ] **Step 5: Single-client end-to-end verification**

Via Unity MCP, set PlayMode Tools to ClientAndServer and enter Play in `BattleScene`.
- Confirm the login panel is visible over the battle.
- Type a username and click **Enter Battle**.
Expected: the login panel disappears; ground rings appear under exactly one team's soldiers (your claimed team, Team 0), and the enemy team has no rings.
Capture the scene/game view via Unity MCP to confirm. Call `Unity_GetConsoleLogs`; confirm zero errors (watch for `GhostOwner`/ghost-collection warnings).

- [ ] **Step 6: (Optional) two-client ownership split**

If a second client is available (standalone build, or PlayMode Tools "Num Thin Clients" won't drive the login UI — use a built player as the 2nd client), connect it, authenticate, and confirm it sees rings on Team 1 while the first client still sees rings only on Team 0. This proves per-owner divergence. If no second client is convenient, note it as deferred manual verification — the unit tests already prove the server assigns distinct teams.

- [ ] **Step 7: Final full-suite run + commit the scene/prefab/wiring**

Run the full `Demo.Tests.EditMode` suite via Unity MCP; expected all PASS. Call `Unity_GetConsoleLogs`; confirm zero errors.

```bash
git add Assets/Prefabs/OwnershipRing.prefab Assets/Scenes/BattleScene.unity Assets/Scenes/BattleSub.unity
git add -A Assets/Prefabs Assets/Scenes
git commit -m "chore(battle): wire OwnershipRing prefab, GhostOwner ghost, and login UIDocument"
```

---

## Self-review notes (for the implementer)

- **Spec coverage:** §1 auth handshake → Tasks 2,3,5,6,7. §2 ownership assignment → Task 2. §3 ghost prefab → Tasks 1,7. §4 login UI → Tasks 5,6,7. §5 ownership ring → Tasks 4,7. Testing section → Tasks 2,3,4 (unit) + Task 7 (Editor). All spec sections map to tasks.
- **Type consistency:** `GhostOwner.NetworkId` (netcode field), `TeamClaims.Team0Owner/Team1Owner`, `AuthenticateRequest.Username`, `PendingAuth.Username` (all `FixedString64Bytes`), `OwnershipRingRef.Ring`, `BattleConfig.OwnershipRingPrefab`/`RingHeightOffset` — used identically across tasks.
- **Watch item:** the `LinkedEntityGroup` append (Task 4) + `[UpdateAfter(HealthBarSpawnSystem)]` is the one cross-system hazard; its dedicated test (`Ring_AppendsToExistingLinkedGroup_FromHealthBar`) guards it.
