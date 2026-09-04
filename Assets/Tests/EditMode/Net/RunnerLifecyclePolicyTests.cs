using NUnit.Framework;

public class RunnerLifecyclePolicyTests
{
    [Test]
    public void FreshRunner_IsReusedWithoutARebuild()
    {
        // First connect after launch: the runner built at Start has never run StartGame.
        Assert.IsFalse(RunnerLifecyclePolicy.NeedsRebuild(hasRunner: true, runnerConsumed: false));
    }

    [Test]
    public void ConsumedRunner_MustBeRebuilt_FusionRefusesToReuseOne()
    {
        // The failed-Join-then-click-Host case. StartGame on this instance would return
        // ShutdownReason.OperationCanceled without ever touching the network.
        Assert.IsTrue(RunnerLifecyclePolicy.NeedsRebuild(hasRunner: true, runnerConsumed: true));
    }

    [Test]
    public void MissingRunner_MustBeRebuilt()
    {
        Assert.IsTrue(RunnerLifecyclePolicy.NeedsRebuild(hasRunner: false, runnerConsumed: false));
        Assert.IsTrue(RunnerLifecyclePolicy.NeedsRebuild(hasRunner: false, runnerConsumed: true));
    }
}
