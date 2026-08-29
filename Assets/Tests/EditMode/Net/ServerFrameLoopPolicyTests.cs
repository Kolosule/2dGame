using NUnit.Framework;

public class ServerFrameLoopPolicyTests
{
    private static readonly ServerFrameLoopRates ProjectRates =
        new ServerFrameLoopRates(64, 64, 64, 64);

    [Test]
    public void Resolve_DedicatedServer_AppliesResolvedServerRate()
    {
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            NetworkBootKind.DedicatedServer,
            ProjectRates);

        Assert.That(plan.Status, Is.EqualTo(ServerFrameLoopPlanStatus.Apply));
        Assert.That(plan.ShouldApply, Is.True);
        Assert.That(plan.TargetFrameRate, Is.EqualTo(64));
    }

    [Test]
    public void Resolve_Client_DoesNotApplyServerOverride()
    {
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            ServerFrameLoopMode.Client,
            ProjectRates);

        Assert.That(plan.Status, Is.EqualTo(ServerFrameLoopPlanStatus.NotApplicable));
        Assert.That(plan.ShouldApply, Is.False);
    }

    [Test]
    public void Resolve_Host_DoesNotApplyServerOverride()
    {
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            ServerFrameLoopMode.Host,
            ProjectRates);

        Assert.That(plan.Status, Is.EqualTo(ServerFrameLoopPlanStatus.NotApplicable));
        Assert.That(plan.ShouldApply, Is.False);
    }

    [Test]
    public void Resolve_BatchMode_AppliesDedicatedServerOverride()
    {
        NetworkBootKind bootKind = NetworkBootMode.Resolve(true, new string[0]);
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(bootKind, ProjectRates);

        Assert.That(bootKind, Is.EqualTo(NetworkBootKind.DedicatedServer));
        Assert.That(plan.ShouldApply, Is.True);
    }

    [Test]
    public void Resolve_DedicatedServerFlag_AppliesDedicatedServerOverride()
    {
        NetworkBootKind bootKind = NetworkBootMode.Resolve(
            false,
            new[] { NetworkBootMode.DedicatedServerArg });
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(bootKind, ProjectRates);

        Assert.That(bootKind, Is.EqualTo(NetworkBootKind.DedicatedServer));
        Assert.That(plan.ShouldApply, Is.True);
    }

    [Test]
    public void Resolve_NonDefaultRates_FollowsResolvedServerSimulationRate()
    {
        var rates = new ServerFrameLoopRates(120, 60, 30, 30);

        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            NetworkBootKind.DedicatedServer,
            rates);

        Assert.That(plan.ShouldApply, Is.True);
        Assert.That(plan.TargetFrameRate, Is.EqualTo(60));
        Assert.That(plan.Rates.ServerSendRate, Is.EqualTo(30));
    }

    [Test]
    public void Resolve_UnavailableRates_ReturnsExplicitError()
    {
        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            NetworkBootKind.DedicatedServer,
            default);

        Assert.That(plan.Status, Is.EqualTo(ServerFrameLoopPlanStatus.InvalidRates));
        Assert.That(plan.ShouldApply, Is.False);
        Assert.That(plan.Error, Does.Contain("client simulation"));
        Assert.That(plan.Error, Does.Contain("greater than zero"));
    }

    [Test]
    public void Resolve_ServerSendRateAboveSimulation_ReturnsExplicitError()
    {
        var rates = new ServerFrameLoopRates(64, 32, 64, 64);

        ServerFrameLoopPlan plan = ServerFrameLoopPolicy.Resolve(
            NetworkBootKind.DedicatedServer,
            rates);

        Assert.That(plan.Status, Is.EqualTo(ServerFrameLoopPlanStatus.InvalidRates));
        Assert.That(plan.ShouldApply, Is.False);
        Assert.That(plan.Error, Does.Contain("exceeds server simulation rate"));
    }
}
