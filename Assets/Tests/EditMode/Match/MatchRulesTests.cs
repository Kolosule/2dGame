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

    // The timer-expiry advance table. Live -> SuddenDeath (not a winner resolution) because
    // capture is the only win condition. Intermission maps to itself, meaning "no transition":
    // an untimed/terminal phase's timer expiry (TickTimer.None never expires anyway) must be a
    // no-op rather than a self re-entry.
    [TestCase(MatchPhase.Warmup, MatchPhase.Countdown)]
    [TestCase(MatchPhase.Countdown, MatchPhase.Live)]
    [TestCase(MatchPhase.Live, MatchPhase.SuddenDeath)]
    [TestCase(MatchPhase.SuddenDeath, MatchPhase.PostMatch)]
    [TestCase(MatchPhase.PostMatch, MatchPhase.Intermission)]
    [TestCase(MatchPhase.Intermission, MatchPhase.Intermission)]
    public void NextOnTimerExpiry_AdvancesToExpectedPhase(MatchPhase phase, MatchPhase expected)
    {
        Assert.AreEqual(expected, MatchRules.NextOnTimerExpiry(phase));
    }

    // Operator hard-cap's ops safety valve only: true in SuddenDeath, false everywhere else.
    // Unreachable in default play (suddenDeathHardCap defaults to 0 = off) — not a game rule.
    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, false)]
    [TestCase(MatchPhase.Live, false)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void ResolvesAsDrawOnTimerExpiry_TrueInSuddenDeathOnly(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.ResolvesAsDrawOnTimerExpiry(phase));
    }

    // A drop preserves state only while the match is actually being played. Once it is decided
    // (PostMatch/Intermission) the scene reload is about to reset everything anyway, so holding
    // state that is seconds from deletion buys nothing.
    [TestCase(MatchPhase.Warmup, true)]
    [TestCase(MatchPhase.Countdown, true)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void PreservesDisconnectState_FalseOnceTheMatchIsDecided(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.PreservesDisconnectState(phase));
    }
}
