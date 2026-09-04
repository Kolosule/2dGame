/// <summary>
/// When a menu-initiated connection attempt must rebuild the NetworkRunner stack first.
///
/// Fusion's NetworkRunner is strictly SINGLE USE. NetworkRunner.StartGame latches an internal
/// "already initialized" flag on its very first call and every later call on the SAME instance
/// returns immediately with ShutdownReason.OperationCanceled, logging
/// "Failed: NetworkRunner should not be reused." That latch is set whether the first attempt
/// succeeded, failed, or was shut down afterwards — so a failed Join followed by a click on Host
/// reports "Failed to start host: OperationCanceled" and never touches the network at all.
///
/// The menu deliberately leaves Join/Host clickable after a failure, so every attempt from it has
/// to run on a runner that has never been started.
/// </summary>
public static class RunnerLifecyclePolicy
{
    /// <summary>
    /// True when the caller must TeardownRunner/BuildRunner before calling StartGame. A runner that
    /// exists and has never been started is reused as-is, which keeps a first connection attempt on
    /// a freshly launched client free of any teardown.
    /// </summary>
    public static bool NeedsRebuild(bool hasRunner, bool runnerConsumed)
    {
        return !hasRunner || runnerConsumed;
    }
}
