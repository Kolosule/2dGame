using Fusion;
using UnityEngine;

/// <summary>Applies Fusion's documented dedicated-server frame cap once during process startup.</summary>
public static class DedicatedServerFrameLoop
{
    private static bool attempted;
    private static bool configured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        attempted = false;
        configured = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureBeforeSceneLoad()
    {
        NetworkBootKind bootKind = NetworkBootMode.Resolve(
            Application.isBatchMode,
            System.Environment.GetCommandLineArgs());

        if (!EnsureConfigured(bootKind))
            Application.Quit(1);
    }

    public static bool EnsureConfigured(NetworkBootKind bootKind)
    {
        if (bootKind != NetworkBootKind.DedicatedServer)
            return true;

        if (attempted)
            return configured;

        attempted = true;

        if (!TryResolveRates(out ServerFrameLoopRates rates, out string error))
            return Fail(error);

        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(bootKind, rates);
        if (!plan.ShouldApply)
            return Fail(plan.Error);

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = plan.TargetFrameRate;
        configured = true;

        Debug.Log(
            $"[Server] Frame loop configured for Fusion: target frame rate {plan.TargetFrameRate} FPS, " +
            $"client simulation {rates.ClientSimulationRate} Hz, server simulation " +
            $"{rates.ServerSimulationRate} Hz, client send rate {rates.ClientSendRate} Hz, server send " +
            $"rate {rates.ServerSendRate} Hz, VSync disabled.");

        return true;
    }

    private static bool TryResolveRates(out ServerFrameLoopRates rates, out string error)
    {
        rates = default;

        if (!NetworkProjectConfigAsset.TryGetGlobal(out NetworkProjectConfigAsset configAsset)
            || configAsset == null
            || configAsset.Config == null)
        {
            error = "Fusion's global NetworkProjectConfig is unavailable.";
            return false;
        }

        if (configAsset.Config.Simulation == null)
        {
            error = "Fusion's global NetworkProjectConfig has no Simulation configuration.";
            return false;
        }

        TickRate.Selection selection = configAsset.Config.Simulation.TickRateSelection;
        if (!TickRate.IsValid(selection.Client))
        {
            error = $"Fusion client tick rate {selection.Client} Hz is not supported by this installed SDK.";
            return false;
        }

        TickRate tickRates = TickRate.Get(selection.Client);
        TickRate.ValidateResult validation = tickRates.ValidateSelection(selection);
        if (validation != TickRate.ValidateResult.Ok)
        {
            error =
                $"Fusion tick-rate selection is invalid ({validation}): client {selection.Client} Hz, " +
                $"server index {selection.ServerIndex}, client send index {selection.ClientSendIndex}, " +
                $"server send index {selection.ServerSendIndex}.";
            return false;
        }

        TickRate.Resolved resolved = TickRate.Resolve(selection);
        rates = new ServerFrameLoopRates(
            resolved.Client,
            resolved.Server,
            resolved.ClientSend,
            resolved.ServerSend);
        error = null;
        return true;
    }

    private static bool Fail(string error)
    {
        Debug.LogError(
            $"[Server] Frame loop configuration failed: {error} No fallback frame rate was applied; " +
            "dedicated-server startup will stop.");
        return false;
    }
}
