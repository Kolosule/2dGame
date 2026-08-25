using System.Collections.Generic;
using NUnit.Framework;
using Game.Hud.Core;

public class ScoreboardSortTests
{
    private static ScoreboardRow Row(int playerId, float score, int kills = 0) =>
        new ScoreboardRow { PlayerId = playerId, OverallScore = score, Kills = kills };

    private static int[] IdsOf(IReadOnlyList<ScoreboardRow> rows)
    {
        var ids = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++) ids[i] = rows[i].PlayerId;
        return ids;
    }

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
        Assert.AreEqual(new[] { 2, 3, 1 }, IdsOf(result));
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
    public void EqualScoresBreakTieByKillsDescending()
    {
        // Second key in the chain. Player 1 arrives first but has fewer kills, so it must lose the
        // tie -- input order is explicitly NOT the tie-break any more.
        var input = new List<ScoreboardRow> { Row(1, 20f, kills: 2), Row(2, 20f, kills: 7) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 2, 1 }, IdsOf(result));
    }

    [Test]
    public void EqualScoreAndKillsBreakTieByPlayerIdAscending()
    {
        // Final total tie-break. PlayerId is unique per player, so this leaves no pair unordered --
        // which is what makes two peers render an identical board.
        var input = new List<ScoreboardRow> { Row(9, 20f, kills: 3), Row(4, 20f, kills: 3), Row(6, 20f, kills: 3) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 4, 6, 9 }, IdsOf(result));
    }

    [Test]
    public void ScoreOutranksKillsAndKillsOutranksPlayerId()
    {
        // The three keys applied together, each one only breaking what the one before it left tied:
        // 3 wins on score despite 0 kills; 1 beats 2 on kills; 4 and 5 tie on both and fall to id.
        var input = new List<ScoreboardRow>
        {
            Row(1, 50f, kills: 5),
            Row(2, 50f, kills: 1),
            Row(3, 80f, kills: 0),
            Row(5, 10f, kills: 2),
            Row(4, 10f, kills: 2)
        };

        var result = ScoreboardSort.SortByScoreDescending(input);

        Assert.AreEqual(new[] { 3, 1, 2, 4, 5 }, IdsOf(result));
    }

    [Test]
    public void DifferentInputOrderingsOfSameRosterProduceIdenticalOutput()
    {
        // The cross-peer determinism contract, stated directly. Every client builds its list from
        // Runner.ActivePlayers, whose iteration order is per-peer, so the sort's output must depend
        // on the rows' content alone and never on the order they arrived in.
        //
        // 20 rows, not a handful, on purpose: it matches the project's roster cap, and it clears
        // the ~16-element threshold under which .NET's introsort falls back to a (stable) insertion
        // sort -- so a regression to an order-dependent comparison can actually be caught here.
        var forward = new List<ScoreboardRow>();
        var reversed = new List<ScoreboardRow>();
        for (int i = 1; i <= 20; i++)
        {
            // Deliberately degenerate keys: 10 distinct scores and 2 distinct kill counts across
            // 20 players, so most pairs tie all the way down to PlayerId.
            var row = Row(i, (i % 10) * 5f, kills: i % 2);
            forward.Add(row);
            reversed.Insert(0, row);
        }

        var fromForward = ScoreboardSort.SortByScoreDescending(forward);
        var fromReversed = ScoreboardSort.SortByScoreDescending(reversed);

        Assert.AreEqual(IdsOf(fromForward), IdsOf(fromReversed));
    }

    [Test]
    public void InputListIsNotMutated()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f) };
        ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(1, input[0].PlayerId, "original list order must be untouched");
    }
}
