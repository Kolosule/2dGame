using NUnit.Framework;
using Game.Audio.Core;
using Game.Match.Core;

public class MusicStateTests
{
    private const int Draw = 0;
    private const int Team1 = 1;
    private const int Team2 = 2;
    private const int TeamNone = 0;

    [Test]
    public void NoMatch_PlaysMenuLoopUnderTheMenuSnapshot()
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: false, MatchPhase.Live, Draw, Team1);
        Assert.AreEqual(MusicTrackId.MenuLoop, plan.Bed);
        Assert.AreEqual(MixerSnapshotId.Menu, plan.Snapshot);
        Assert.AreEqual(AudioCueId.None, plan.Stinger);
        Assert.IsFalse(plan.Ambient);
    }

    [TestCase(MatchPhase.Warmup, MusicTrackId.LobbyLoop, MixerSnapshotId.Menu)]
    [TestCase(MatchPhase.Intermission, MusicTrackId.LobbyLoop, MixerSnapshotId.Menu)]
    [TestCase(MatchPhase.Countdown, MusicTrackId.GameplayLoop, MixerSnapshotId.Default)]
    [TestCase(MatchPhase.Live, MusicTrackId.GameplayLoop, MixerSnapshotId.Default)]
    [TestCase(MatchPhase.SuddenDeath, MusicTrackId.SuddenDeathLoop, MixerSnapshotId.SuddenDeath)]
    public void PhaseSelectsBedAndSnapshot(MatchPhase phase, MusicTrackId bed, MixerSnapshotId snapshot)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Draw, Team1);
        Assert.AreEqual(bed, plan.Bed);
        Assert.AreEqual(snapshot, plan.Snapshot);
    }

    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, true)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void AmbientBedRunsOnlyWhileTheArenaIsInPlay(MatchPhase phase, bool ambient)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Draw, Team1);
        Assert.AreEqual(ambient, plan.Ambient);
    }

    [Test]
    public void PostMatch_StopsTheBedAndDucksUnderTheStinger()
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, Team1, Team1);
        Assert.AreEqual(MusicTrackId.None, plan.Bed);
        Assert.AreEqual(MixerSnapshotId.Stinger, plan.Snapshot);
    }

    [TestCase(Team1, Team1, AudioCueId.VictoryStinger)]
    [TestCase(Team2, Team2, AudioCueId.VictoryStinger)]
    [TestCase(Team1, Team2, AudioCueId.DefeatStinger)]
    [TestCase(Team2, Team1, AudioCueId.DefeatStinger)]
    [TestCase(Draw, Team1, AudioCueId.DrawStinger)]
    [TestCase(Draw, Team2, AudioCueId.DrawStinger)]
    public void PostMatchStingerFollowsWinnerVsLocalTeam(int winner, int localTeam, AudioCueId stinger)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, winner, localTeam);
        Assert.AreEqual(stinger, plan.Stinger);
    }

    // A spectator, or a player whose team hasn't replicated yet, must never be told they won.
    [TestCase(Team1)]
    [TestCase(Team2)]
    [TestCase(Draw)]
    public void UnassignedLocalTeam_AlwaysGetsTheDrawStinger(int winner)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, winner, TeamNone);
        Assert.AreEqual(AudioCueId.DrawStinger, plan.Stinger);
    }

    [TestCase(MatchPhase.Warmup)]
    [TestCase(MatchPhase.Countdown)]
    [TestCase(MatchPhase.Live)]
    [TestCase(MatchPhase.SuddenDeath)]
    [TestCase(MatchPhase.Intermission)]
    public void NonPostMatchPhases_HaveNoStinger(MatchPhase phase)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Team1, Team1);
        Assert.AreEqual(AudioCueId.None, plan.Stinger);
    }
}
