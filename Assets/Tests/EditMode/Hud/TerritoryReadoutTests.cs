using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// The zone indicator's displayed state folds in the team's Vanguard tier: the same distance
/// stops reading as penalised once the team has bought the vulnerability away. That fold is the
/// whole point of the merged Team Power strip — the buff is taught by the thing it changes.
/// </summary>
public class TerritoryReadoutTests
{
    [Test]
    public void AtOwnBase_AlwaysReadsClear()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 0));
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 2));
    }

    [Test]
    public void NearBaseThreshold_IsInclusive()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0.05f, 0), "at the threshold");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(0.051f, 0), "just past it");
    }

    [Test]
    public void FarFromBase_ReadsPenalisedUntilVanguardIsMaxed()
    {
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, 0), "locked");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, 1), "half lifted is still a penalty");
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 2), "fully lifted");
    }

    [Test]
    public void MaxVanguardTier_ReadsLiftedAtAnyDistance()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(0.5f, 2));
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 2));
    }

    [Test]
    public void TiersBeyondTheMaximumStillReadAsLifted()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 5));
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, -1), "negative clamps to locked");
    }
}
