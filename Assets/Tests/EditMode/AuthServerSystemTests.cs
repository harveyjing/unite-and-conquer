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

        [Test]
        public void SameConnectionAuthingTwice_DoesNotClaimBothTeams()
        {
            SpawnTwoTeams();
            var conn = CreateConnection(1);

            SendAuth(conn, "cao_cao");
            CreateAndUpdateSystem<AuthServerSystem>();

            // Same connection authenticates again (e.g. a duplicate/late RPC).
            SendAuth(conn, "cao_cao");
            UpdateExistingSystem<AuthServerSystem>();

            Assert.AreEqual(1, OwnerOfTeam(0), "team 0 owned by NetworkId 1");
            Assert.AreEqual(0, OwnerOfTeam(1), "team 1 stays unclaimed; one connection never owns both");
        }
    }
}
