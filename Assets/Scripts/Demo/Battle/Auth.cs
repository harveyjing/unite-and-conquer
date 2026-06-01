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
        Entity _claimsEntity;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AuthenticateRequest>()
                .WithAll<ReceiveRpcCommandRequest>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);

            // Create the claims singleton eagerly so OnUpdate can read/write it
            // without a structural change. Use parameterless CreateEntity +
            // generic AddComponent: CreateEntity(typeof(...)) builds a managed
            // ComponentType[] which Burst rejects (BC1028) in this [BurstCompile]
            // OnCreate.
            _claimsEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<TeamClaims>(_claimsEntity);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _networkIdLookup.Update(ref state);
            var em = state.EntityManager;

            var claims = em.GetComponentData<TeamClaims>(_claimsEntity);

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

            em.SetComponentData(_claimsEntity, claims);

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
}
