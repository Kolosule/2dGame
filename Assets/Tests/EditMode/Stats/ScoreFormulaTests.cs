using NUnit.Framework;
using Game.Stats.Core;

public class ScoreFormulaTests
{
    private static readonly ScoreWeights Weights = ScoreWeights.Default;

    [Test]
    public void AllZeroInputsScoreZero()
    {
        Assert.AreEqual(0f, ScoreFormula.Compute(0, 0, 0, 0, 0, Weights));
    }

    [Test]
    public void KillsAndDeathsAreWeightedAtParity()
    {
        // 10 kills = +100, 10 deaths = -100 -> net zero, per the design spec's weight table.
        float score = ScoreFormula.Compute(kills: 10, deaths: 10, coinsDeposited: 0,
            flagCarrySeconds: 0, flagReturns: 0, Weights);
        Assert.AreEqual(0f, score);
    }

    [Test]
    public void DeathsSubtractFromScore()
    {
        float score = ScoreFormula.Compute(kills: 0, deaths: 5, coinsDeposited: 0,
            flagCarrySeconds: 0, flagReturns: 0, Weights);
        Assert.AreEqual(-50f, score);
    }

    [Test]
    public void CoinsContributeAtThreeQuarterWeight()
    {
        float score = ScoreFormula.Compute(0, 0, coinsDeposited: 100, 0, 0, Weights);
        Assert.AreEqual(75f, score);
    }

    [Test]
    public void FlagCarrySecondsContributeOneToOne()
    {
        float score = ScoreFormula.Compute(0, 0, 0, flagCarrySeconds: 120, 0, Weights);
        Assert.AreEqual(120f, score);
    }

    [Test]
    public void FlagReturnsAreWorthTwentyEach()
    {
        float score = ScoreFormula.Compute(0, 0, 0, 0, flagReturns: 3, Weights);
        Assert.AreEqual(60f, score);
    }

    [Test]
    public void AllFiveInputsCombineAdditively()
    {
        // 2 kills(+20) - 1 death(-10) + 40 coins(+30) + 30 carry-seconds(+30) + 2 returns(+40) = 110
        float score = ScoreFormula.Compute(kills: 2, deaths: 1, coinsDeposited: 40,
            flagCarrySeconds: 30, flagReturns: 2, Weights);
        Assert.AreEqual(110f, score, 0.001f);
    }
}
