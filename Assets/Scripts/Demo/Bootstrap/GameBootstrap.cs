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
    //
    // Multiplayer Play Mode (MPPM): only the MAIN editor instance hosts. Each
    // additional "virtual player" runs this same bootstrap, so without this guard
    // every instance would create a server world and fight over port 7979
    // ("address already in use"). We force additional instances to be client-only;
    // they auto-connect to the main editor's server on 127.0.0.1:7979.
    public class GameBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            AutoConnectPort = 7979;

#if UNITY_EDITOR
            if (IsAdditionalEditorInstance())
            {
                // Client-only: do not create a server world (no port conflict).
                CreateClientWorld("ClientWorld");
                return true;
            }
#endif
            return base.Initialize(defaultWorldName);
        }

#if UNITY_EDITOR
        // True only inside a Multiplayer Play Mode *additional* editor instance
        // (a "virtual player"); false for the main editor or when MPPM is absent.
        //
        // MPPM launches each additional instance as a separate Editor process with a
        // "-scenarioClone" command-line argument (the main editor is launched with
        // "-projectpath" instead). We key off that directly: it's available the instant
        // the process starts — unlike the MPPM CurrentPlayer API, whose assembly isn't
        // reliably loaded this early in bootstrap (so reflecting it silently no-ops and
        // every instance tried to host → port 7979 conflict).
        static bool IsAdditionalEditorInstance()
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg.IndexOf("scenarioClone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
#endif
    }
}
