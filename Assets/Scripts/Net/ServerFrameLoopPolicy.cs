/// <summary>The process role considered when deciding whether to cap Unity's frame loop.</summary>
public enum ServerFrameLoopMode
{
    DedicatedServer,
    Client,
    Host
}

public enum ServerFrameLoopPlanStatus
{
    NotApplicable,
    Apply,
    InvalidRates
}

public readonly struct ServerFrameLoopRates
{
    public ServerFrameLoopRates(
        int clientSimulationRate,
        int serverSimulationRate,
        int clientSendRate,
        int serverSendRate)
    {
        ClientSimulationRate = clientSimulationRate;
        ServerSimulationRate = serverSimulationRate;
        ClientSendRate = clientSendRate;
        ServerSendRate = serverSendRate;
    }

    public int ClientSimulationRate { get; }
    public int ServerSimulationRate { get; }
    public int ClientSendRate { get; }
    public int ServerSendRate { get; }
}

public readonly struct ServerFrameLoopPlan
{
    private ServerFrameLoopPlan(
        ServerFrameLoopPlanStatus status,
        ServerFrameLoopRates rates,
        int targetFrameRate,
        string error)
    {
        Status = status;
        Rates = rates;
        TargetFrameRate = targetFrameRate;
        Error = error;
    }

    public ServerFrameLoopPlanStatus Status { get; }
    public ServerFrameLoopRates Rates { get; }
    public int TargetFrameRate { get; }
    public string Error { get; }
    public bool ShouldApply => Status == ServerFrameLoopPlanStatus.Apply;

    public static ServerFrameLoopPlan NotApplicable(ServerFrameLoopRates rates)
    {
        return new ServerFrameLoopPlan(
            ServerFrameLoopPlanStatus.NotApplicable,
            rates,
            0,
            null);
    }

    public static ServerFrameLoopPlan Apply(ServerFrameLoopRates rates)
    {
        return new ServerFrameLoopPlan(
            ServerFrameLoopPlanStatus.Apply,
            rates,
            rates.ServerSimulationRate,
            null);
    }

    public static ServerFrameLoopPlan Invalid(ServerFrameLoopRates rates, string error)
    {
        return new ServerFrameLoopPlan(
            ServerFrameLoopPlanStatus.InvalidRates,
            rates,
            0,
            error);
    }
}

/// <summary>
/// Pure selection logic for the dedicated-server frame cap. Fusion rate resolution remains in the
/// Unity-facing adapter so this decision can be covered by EditMode tests without changing globals.
/// </summary>
public static class ServerFrameLoopPolicy
{
    public static ServerFrameLoopPlan Resolve(NetworkBootKind bootKind, ServerFrameLoopRates rates)
    {
        switch (bootKind)
        {
            case NetworkBootKind.DedicatedServer:
                return Resolve(ServerFrameLoopMode.DedicatedServer, rates);
            case NetworkBootKind.Client:
                return Resolve(ServerFrameLoopMode.Client, rates);
            default:
                return ServerFrameLoopPlan.Invalid(
                    rates,
                    $"Unsupported network boot kind: {bootKind}.");
        }
    }

    public static ServerFrameLoopPlan Resolve(ServerFrameLoopMode mode, ServerFrameLoopRates rates)
    {
        switch (mode)
        {
            case ServerFrameLoopMode.Client:
            case ServerFrameLoopMode.Host:
                return ServerFrameLoopPlan.NotApplicable(rates);
            case ServerFrameLoopMode.DedicatedServer:
                break;
            default:
                return ServerFrameLoopPlan.Invalid(
                    rates,
                    $"Unsupported server frame-loop mode: {mode}.");
        }

        if (!TryValidateRate(rates.ClientSimulationRate, "client simulation", out string error)
            || !TryValidateRate(rates.ServerSimulationRate, "server simulation", out error)
            || !TryValidateRate(rates.ClientSendRate, "client send", out error)
            || !TryValidateRate(rates.ServerSendRate, "server send", out error))
        {
            return ServerFrameLoopPlan.Invalid(rates, error);
        }

        if (rates.ClientSendRate > rates.ClientSimulationRate)
        {
            return ServerFrameLoopPlan.Invalid(
                rates,
                $"Client send rate {rates.ClientSendRate} Hz exceeds client simulation rate " +
                $"{rates.ClientSimulationRate} Hz.");
        }

        if (rates.ServerSendRate > rates.ServerSimulationRate)
        {
            return ServerFrameLoopPlan.Invalid(
                rates,
                $"Server send rate {rates.ServerSendRate} Hz exceeds server simulation rate " +
                $"{rates.ServerSimulationRate} Hz.");
        }

        return ServerFrameLoopPlan.Apply(rates);
    }

    private static bool TryValidateRate(int rate, string label, out string error)
    {
        if (rate > 0)
        {
            error = null;
            return true;
        }

        error = $"The resolved Fusion {label} rate must be greater than zero (received {rate}).";
        return false;
    }
}
