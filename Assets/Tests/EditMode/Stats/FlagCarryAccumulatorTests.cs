using NUnit.Framework;
using Game.Stats.Core;

public class FlagCarryAccumulatorTests
{
    [Test]
    public void SubSecondDeltaFlushesNothingAndKeepsRemainder()
    {
        float remainder = 0f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 0.3f);
        Assert.AreEqual(0, whole);
        Assert.AreEqual(0.3f, remainder, 0.0001f);
    }

    [Test]
    public void CrossingOneSecondFlushesExactlyOne()
    {
        float remainder = 0.8f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 0.3f);
        Assert.AreEqual(1, whole);
        Assert.AreEqual(0.1f, remainder, 0.0001f);
    }

    [Test]
    public void RepeatedTicksAccumulateAcrossCalls()
    {
        float remainder = 0f;
        int totalFlushed = 0;
        for (int i = 0; i < 10; i++) // 10 ticks of 0.11s = 1.1s
            totalFlushed += FlagCarryAccumulator.Tick(ref remainder, 0.11f);
        Assert.AreEqual(1, totalFlushed);
        Assert.AreEqual(0.1f, remainder, 0.001f);
    }

    [Test]
    public void ALargeDeltaCanFlushMoreThanOneSecond()
    {
        float remainder = 0f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 2.3f);
        Assert.AreEqual(2, whole);
        Assert.AreEqual(0.3f, remainder, 0.0001f);
    }
}
