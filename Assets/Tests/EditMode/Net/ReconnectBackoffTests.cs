using NUnit.Framework;

public class ReconnectBackoffTests
{
    [Test]
    public void MaxAttempts_IsFive()
    {
        Assert.AreEqual(5, ReconnectBackoff.MaxAttempts);
    }

    [TestCase(1, 1f)]
    [TestCase(2, 2f)]
    [TestCase(3, 4f)]
    [TestCase(4, 8f)]
    [TestCase(5, 8f)]
    public void DelaySecondsForAttempt_FollowsTheSpecSchedule(int attempt, float expected)
    {
        Assert.AreEqual(expected, ReconnectBackoff.DelaySecondsForAttempt(attempt), 1e-4f);
    }

    [TestCase(0)]
    [TestCase(6)]
    [TestCase(-1)]
    public void DelaySecondsForAttempt_OutOfRange_IsZero(int attempt)
    {
        Assert.AreEqual(0f, ReconnectBackoff.DelaySecondsForAttempt(attempt), 1e-4f);
    }

    [Test]
    public void TotalScheduledWait_IsAboutTwentyThreeSeconds()
    {
        float total = 0f;
        for (int i = 1; i <= ReconnectBackoff.MaxAttempts; i++)
            total += ReconnectBackoff.DelaySecondsForAttempt(i);
        Assert.AreEqual(23f, total, 1e-4f);
    }
}
