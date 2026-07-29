using NUnit.Framework;
using Game.Match.Core;

public class MatchResolverTests
{
    [TestCase(3, 1, 1)] // team1 higher
    [TestCase(1, 3, 2)] // team2 higher
    [TestCase(2, 2, 0)] // equal -> draw
    [TestCase(0, 0, 0)] // both zero -> draw
    public void ResolveTimerWinner_HigherScoreWins_EqualIsDraw(int t1, int t2, int expected)
    {
        Assert.AreEqual(expected, MatchResolver.ResolveTimerWinner(t1, t2));
    }

    [TestCase(1, "Team 1 Wins!")]
    [TestCase(2, "Team 2 Wins!")]
    [TestCase(0, "It's a Draw!")]
    [TestCase(99, "It's a Draw!")] // any non-1/2 -> draw label
    public void WinnerLabel_MapsWinnerToText(int winner, string expected)
    {
        Assert.AreEqual(expected, MatchResolver.WinnerLabel(winner));
    }
}
