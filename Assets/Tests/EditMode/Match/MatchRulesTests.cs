using NUnit.Framework;
using Game.Match.Core;

public class MatchRulesTests
{
    // Play runs in Live and SuddenDeath only: input live, enemies thinking, captures counted.
    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, false)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void IsPlayActive_TrueInLiveAndSuddenDeathOnly(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.IsPlayActive(phase));
    }

    // The same predicate is the capture guard: a capture counts in Live and SuddenDeath,
    // and is rejected in Countdown and PostMatch.
    [Test]
    public void IsPlayActive_IsTheCaptureGuard()
    {
        Assert.IsTrue(MatchRules.IsPlayActive(MatchPhase.Live));
        Assert.IsTrue(MatchRules.IsPlayActive(MatchPhase.SuddenDeath));
        Assert.IsFalse(MatchRules.IsPlayActive(MatchPhase.Countdown));
        Assert.IsFalse(MatchRules.IsPlayActive(MatchPhase.PostMatch));
    }

    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, false)]
    [TestCase(MatchPhase.Live, false)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void AllBuffsMaxed_TrueInSuddenDeathOnly(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.AllBuffsMaxed(phase));
    }

    // SuddenDeath is appended LAST because Phase is a [Networked] byte enum: inserting it
    // between Live and PostMatch would renumber the existing wire values.
    [Test]
    public void SuddenDeath_IsAppendedLast_SoWireValuesAreStable()
    {
        Assert.AreEqual(0, (int)MatchPhase.Warmup);
        Assert.AreEqual(1, (int)MatchPhase.Countdown);
        Assert.AreEqual(2, (int)MatchPhase.Live);
        Assert.AreEqual(3, (int)MatchPhase.PostMatch);
        Assert.AreEqual(4, (int)MatchPhase.Intermission);
        Assert.AreEqual(5, (int)MatchPhase.SuddenDeath);
    }
}
