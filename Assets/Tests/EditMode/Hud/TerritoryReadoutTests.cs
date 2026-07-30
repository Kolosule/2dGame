using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// The zone indicator's displayed state folds in the team's Vanguard tier: the same position
/// stops reading as penalised once the team has bought the debuff away. That fold is the whole
/// point of the merged Team Power strip — the buff is taught by the thing it changes.
/// </summary>
public class TerritoryReadoutTests
{
    [Test]
    public void OwnHalfAndMidfieldAreClearAtEveryTier()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(1f, 0), "own base");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 0), "midpoint");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(-0.32f, 0), "just outside the enemy third");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 2), "clear stays clear when maxed");
    }

    [Test]
    public void TheBoundaryItselfIsClear_AndJustPastItIsPenalised()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(-0.33f, 0), "boundary is not the enemy third");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-0.34f, 0));
    }

    [Test]
    public void EnemyThirdReadsPenalisedUntilVanguardIsMaxed()
    {
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, 0), "locked");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, 1), "half lifted is still a penalty");
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(-1f, 2), "fully lifted");
    }

    [Test]
    public void TiersBeyondTheMaximumStillReadAsLifted()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(-1f, 5));
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, -1), "negative clamps to locked");
    }
}
