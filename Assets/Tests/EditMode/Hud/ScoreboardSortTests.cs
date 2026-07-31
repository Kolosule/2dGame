using System.Collections.Generic;
using NUnit.Framework;
using Game.Hud.Core;

public class ScoreboardSortTests
{
    private static ScoreboardRow Row(int playerId, float score) =>
        new ScoreboardRow { PlayerId = playerId, OverallScore = score };

    [Test]
    public void EmptyListSortsToEmptyList()
    {
        var result = ScoreboardSort.SortByScoreDescending(new List<ScoreboardRow>());
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void SingleRowSortsToItself()
    {
        var result = ScoreboardSort.SortByScoreDescending(new List<ScoreboardRow> { Row(1, 42f) });
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].PlayerId);
    }

    [Test]
    public void HighestScoreSortsFirst()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f), Row(3, 50f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 2, 3, 1 }, new[] { result[0].PlayerId, result[1].PlayerId, result[2].PlayerId });
    }

    [Test]
    public void NegativeScoresSortBelowPositiveScores()
    {
        var input = new List<ScoreboardRow> { Row(1, -30f), Row(2, 5f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(2, result[0].PlayerId);
        Assert.AreEqual(1, result[1].PlayerId);
    }

    [Test]
    public void TiedScoresPreserveInputOrder()
    {
        // Stable sort: ties keep the order they arrived in, so a fresh repaint doesn't jitter rows.
        var input = new List<ScoreboardRow> { Row(1, 20f), Row(2, 20f), Row(3, 20f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 1, 2, 3 }, new[] { result[0].PlayerId, result[1].PlayerId, result[2].PlayerId });
    }

    [Test]
    public void InputListIsNotMutated()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f) };
        ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(1, input[0].PlayerId, "original list order must be untouched");
    }
}
