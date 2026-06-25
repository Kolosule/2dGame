using System.Collections.Generic;

/// <summary>How this process should join the session.</summary>
public enum NetworkBootKind
{
    DedicatedServer,   // headless GameMode.Server, not a player
    Client,            // normal player, GameMode.Client
    SinglePlayerHost,  // dev convenience: GameMode.Host (host is also a player)
}

/// <summary>
/// Pure decision for how GameNetworkManager should start the runner. Kept free of UnityEngine
/// so it is unit-testable. Batch mode or an explicit "-dedicatedServer" arg means this process
/// is the dedicated server; otherwise it is an interactive client (or a single-player host for
/// solo dev testing).
/// </summary>
public static class NetworkBootMode
{
    public const string DedicatedServerArg = "-dedicatedServer";

    public static NetworkBootKind Resolve(bool isBatchMode, IReadOnlyList<string> args, bool singlePlayerMode)
    {
        if (isBatchMode) return NetworkBootKind.DedicatedServer;

        if (args != null)
        {
            for (int i = 0; i < args.Count; i++)
                if (args[i] == DedicatedServerArg) return NetworkBootKind.DedicatedServer;
        }

        return singlePlayerMode ? NetworkBootKind.SinglePlayerHost : NetworkBootKind.Client;
    }
}
