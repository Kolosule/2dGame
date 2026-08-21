using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// The readout folds in the team's Vanguard tier: the same distance reports a smaller penalty once
/// the team has bought the vulnerability away. That fold is the whole point of showing the figure
/// on the Vanguard line — the buff is taught by the thing it changes.
/// </summary>
public class TerritoryReadoutTests
{
    [Test]
    public void ExtraDamagePercentTracksTheRealMultiplier()
    {
        Assert.AreEqual(0, TerritoryReadout.ExtraDamagePercent(0f, 0), "at the own base");
        Assert.AreEqual(150, TerritoryReadout.ExtraDamagePercent(1f, 0), "max distance, no Vanguard");
        Assert.AreEqual(75, TerritoryReadout.ExtraDamagePercent(1f, 1), "one tier halves the malus");
        Assert.AreEqual(0, TerritoryReadout.ExtraDamagePercent(1f, 2), "fully lifted");
        Assert.AreEqual(75, TerritoryReadout.ExtraDamagePercent(0.5f, 0), "scales continuously with distance");
    }

    [Test]
    public void ExtraDamagePercentIsNonZeroJustOutsideTheBase()
    {
        // There is no near-base cutoff: the penalty starts accruing immediately, and the HUD is
        // expected to say so rather than imply a flat safety zone.
        Assert.AreEqual(2, TerritoryReadout.ExtraDamagePercent(0.01f, 0));
    }

    [Test]
    public void ExtraDamagePercentClampsItsInputs()
    {
        Assert.AreEqual(0, TerritoryReadout.ExtraDamagePercent(-1f, 0), "negative distance clamps to the base");
        Assert.AreEqual(150, TerritoryReadout.ExtraDamagePercent(2f, 0), "past the enemy base is capped");
        Assert.AreEqual(0, TerritoryReadout.ExtraDamagePercent(1f, 5), "tiers above the max clamp to lifted");
        Assert.AreEqual(150, TerritoryReadout.ExtraDamagePercent(1f, -1), "negative tier clamps to locked");
    }
}
