using NUnit.Framework;
using UnityEngine;

public class DifficultyRingConfigTests
{
    // rings ordered INNER -> OUTER (ascending maxDistanceFromCenter)
    private static DifficultyRingConfig MakeConfig()
    {
        var config = ScriptableObject.CreateInstance<DifficultyRingConfig>();
        config.rings = new[]
        {
            new RingTier { maxDistanceFromCenter = 10f, healthMult = 3f, damageMult = 3f, speedMult = 1.5f },
            new RingTier { maxDistanceFromCenter = 25f, healthMult = 2f, damageMult = 2f, speedMult = 1.25f },
            new RingTier { maxDistanceFromCenter = 50f, healthMult = 1f, damageMult = 1f, speedMult = 1f },
        };
        return config;
    }

    [Test]
    public void GetRing_AtCenter_ReturnsInnermostToughestRing()
    {
        var ring = MakeConfig().GetRing(0f);
        Assert.AreEqual(3f, ring.healthMult);
    }

    [Test]
    public void GetRing_OnBandBoundary_ReturnsThatBand()
    {
        // distance exactly == a band's max belongs to that band (>= comparison)
        var ring = MakeConfig().GetRing(10f);
        Assert.AreEqual(3f, ring.healthMult);
    }

    [Test]
    public void GetRing_MidBand_ReturnsContainingBand()
    {
        var ring = MakeConfig().GetRing(20f);
        Assert.AreEqual(2f, ring.healthMult);
    }

    [Test]
    public void GetRing_BeyondOutermost_ClampsToOutermostBaseline()
    {
        var ring = MakeConfig().GetRing(9999f);
        Assert.AreEqual(1f, ring.healthMult);
        Assert.AreEqual(1f, ring.speedMult);
    }

    [Test]
    public void GetRing_EmptyConfig_ReturnsIdentity()
    {
        var config = ScriptableObject.CreateInstance<DifficultyRingConfig>();
        config.rings = new RingTier[0];
        var ring = config.GetRing(5f);
        Assert.AreEqual(1f, ring.healthMult);
        Assert.AreEqual(1f, ring.damageMult);
        Assert.AreEqual(1f, ring.speedMult);
    }
}
