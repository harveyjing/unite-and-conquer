using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Demo
{
    public struct RespawnRequest : IRpcCommand { }

    // Server-side handler for RespawnRequest. Matches the
    // GoInGameServerSystem shape: query incoming RPCs, look up the
    // requesting connection's NetworkId, then walk the player ghosts
    // to find the one owned by that NetworkId and reset its position.
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct RespawnRequestServerSystem : ISystem
    {
        ComponentLookup<NetworkId> _networkIdLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<RespawnRequest>()
                .WithAll<ReceiveRpcCommandRequest>();
            state.RequireForUpdate(state.GetEntityQuery(builder));
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _networkIdLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var requesterIds = new NativeList<int>(8, Allocator.Temp);

            // Pass 1: collect requesting NetworkIds and destroy request entities.
            foreach (var (rpc, reqEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                              .WithAll<RespawnRequest>()
                              .WithEntityAccess())
            {
                var src = rpc.ValueRO.SourceConnection;
                if (_networkIdLookup.HasComponent(src))
                    requesterIds.Add(_networkIdLookup[src].Value);
                ecb.DestroyEntity(reqEntity);
            }

            // Pass 2: reset position on each player ghost whose owner matches.
            if (requesterIds.Length > 0)
            {
                foreach (var (owner, transform) in
                         SystemAPI.Query<RefRO<GhostOwner>, RefRW<LocalTransform>>()
                                  .WithAll<PlayerTag>())
                {
                    int ownerId = owner.ValueRO.NetworkId;
                    for (int i = 0; i < requesterIds.Length; i++)
                    {
                        if (ownerId == requesterIds[i])
                        {
                            transform.ValueRW.Position = float3.zero;
                            break;
                        }
                    }
                }
            }

            requesterIds.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
