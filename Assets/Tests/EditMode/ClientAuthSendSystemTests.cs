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
