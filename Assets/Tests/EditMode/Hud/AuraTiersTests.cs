using NUnit.Framework;
using Game.Hud.Core;

public class AuraTiersTests
{
    private static readonly int[] Thresholds = { 5, 15, 30 };

    [Test]
    public void Below_First_Threshold_Is_Tier_Zero()
    {
        Assert.AreEqual(0, AuraTiers.Resolve(0, Thresholds));
        Assert.AreEqual(0, AuraTiers.Resolve(4, Thresholds));
    }

    [Test]
    public void Thresholds_Are_Inclusive()
    {
        Assert.AreEqual(1, AuraTiers.Resolve(5, Thresholds));
        Assert.AreEqual(2, AuraTiers.Resolve(15, Thresholds));
        Assert.AreEqual(3, AuraTiers.Resolve(30, Thresholds));
    }

    [Test]
    public void Between_Thresholds_Uses_Lower_Tier()
    {
        Assert.AreEqual(1, AuraTiers.Resolve(14, Thresholds));
        Assert.AreEqual(2, AuraTiers.Resolve(29, Thresholds));
    }

    [Test]
    public void Above_Top_Threshold_Caps_At_Max_Tier()
    {
        Assert.AreEqual(3, AuraTiers.Resolve(9999, Thresholds));
    }

    [Test]
    public void Null_Or_Empty_Thresholds_Is_Tier_Zero()
    {
        Assert.AreEqual(0, AuraTiers.Resolve(50, null));
        Assert.AreEqual(0, AuraTiers.Resolve(50, new int[0]));
    }
}
