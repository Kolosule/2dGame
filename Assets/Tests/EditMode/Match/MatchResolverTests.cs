using NUnit.Framework;
using Game.Match.Core;

public class MatchResolverTests
{
    [TestCase(1, "Team 1 Wins!")]
    [TestCase(2, "Team 2 Wins!")]
    [TestCase(0, "It's a Draw!")]
    [TestCase(99, "It's a Draw!")] // any non-1/2 -> draw label
    public void WinnerLabel_MapsWinnerToText(int winner, string expected)
    {
        Assert.AreEqual(expected, MatchResolver.WinnerLabel(winner));
    }
}
