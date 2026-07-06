using System.Collections.Generic;

/// <summary>How this process should join the session.</summary>
public enum NetworkBootKind
{
    DedicatedServer,   // headless GameMode.Server, not a player
    Client,            // interactive build: show the menu (player picks Host or Client there)
}

/// <summary>
/// Pure decision for how GameNetworkManager should start the runner. Kept free of UnityEngine
/// so it is unit-testable. Batch mode or an explicit "-dedicatedServer" arg means this process
/// is the dedicated server; otherwise it is an interactive build that shows the menu.
/// </summary>
public static class NetworkBootMode
{
    public const string DedicatedServerArg = "-dedicatedServer";

    public static NetworkBootKind Resolve(bool isBatchMode, IReadOnlyList<string> args)
    {
        if (isBatchMode) return NetworkBootKind.DedicatedServer;

        if (args != null)
        {
            for (int i = 0; i < args.Count; i++)
                if (args[i] == DedicatedServerArg) return NetworkBootKind.DedicatedServer;
        }

        return NetworkBootKind.Client;
    }
}
