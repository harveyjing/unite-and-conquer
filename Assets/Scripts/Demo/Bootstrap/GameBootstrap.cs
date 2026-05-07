using Unity.Entities;
using Unity.NetCode;

namespace Demo
{

    // Custom bootstrap. Respects the PlayMode Tools window setting:
    //   ClientAndServer → both worlds (default for hit-Play dev)
    //   Server          → server only
    //   Client          → client only
    //
    // AutoConnectPort is set so the client auto-connects to 127.0.0.1:7979
    // and the server auto-listens on the same port — no manual RPC needed.
    public class GameBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            AutoConnectPort = 7979;
            return base.Initialize(defaultWorldName);
        }
    }
}
