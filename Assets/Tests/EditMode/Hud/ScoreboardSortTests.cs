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
        //
        // This uses 20 tied rows, not a handful, on purpose: .NET's List<T>.Sort/Array.Sort is an
        // introsort that falls back to insertion sort for partitions under ~16 elements, and
        // insertion sort happens to be stable for equal keys. A regression from the current
        // OrderByDescending(...).ToList() to an unstable `rows.Sort((a, b) => ...)` would still
        // pass a small tied-row test on this exact runtime. 20 rows clears that threshold (and
        // matches the project's 20-player roster cap), so this test can actually catch that
        // regression. Do not shrink this back down.
        var input = new List<ScoreboardRow>();
        for (int i = 1; i <= 20; i++)
        {
            input.Add(Row(i, 20f));
        }

        var result = ScoreboardSort.SortByScoreDescending(input);

        var expectedIds = new int[20];
        var actualIds = new int[20];
        for (int i = 0; i < 20; i++)
        {
            expectedIds[i] = i + 1;
            actualIds[i] = result[i].PlayerId;
        }

        Assert.AreEqual(expectedIds, actualIds);
    }

    [Test]
    public void InputListIsNotMutated()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f) };
        ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(1, input[0].PlayerId, "original list order must be untouched");
    }
}
