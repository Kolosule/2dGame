using System.Collections.Generic;
using NUnit.Framework;
using Game.Settings.Core;

public class ResolutionListTests
{
    private static List<ResolutionOption> Raw()
    {
        // Mirrors a real Screen.resolutions array: the same size repeated per refresh rate.
        return new List<ResolutionOption>
        {
            new ResolutionOption(1280, 720),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(2560, 1440),
        };
    }

    [Test]
    public void DeduplicateCollapsesRefreshRateVariants()
    {
        List<ResolutionOption> result = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(3, result.Count);
    }

    [Test]
    public void DeduplicatePreservesFirstSeenOrder()
    {
        List<ResolutionOption> result = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(1280, result[0].Width);
        Assert.AreEqual(1920, result[1].Width);
        Assert.AreEqual(2560, result[2].Width);
    }

    [Test]
    public void DeduplicateDropsNonPositiveDimensions()
    {
        var raw = new List<ResolutionOption>
        {
            new ResolutionOption(0, 1080),
            new ResolutionOption(1920, 0),
            new ResolutionOption(-1, -1),
            new ResolutionOption(1920, 1080),
        };
        List<ResolutionOption> result = ResolutionList.Deduplicate(raw);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1920, result[0].Width);
    }

    [Test]
    public void DeduplicateHandlesNull()
    {
        Assert.AreEqual(0, ResolutionList.Deduplicate(null).Count);
    }

    [Test]
    public void IndexOfFindsAMatchAndReportsAbsence()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(1, ResolutionList.IndexOf(options, 1920, 1080));
        Assert.AreEqual(-1, ResolutionList.IndexOf(options, 800, 600));
    }

    [Test]
    public void StoredResolutionWinsWhenAvailable()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(0, ResolutionList.ResolveStoredIndex(options, 1280, 720, 2560, 1440));
    }

    [Test]
    public void UnavailableStoredResolutionFallsBackToNative()
    {
        // The player unplugged the monitor their stored resolution belonged to.
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(2, ResolutionList.ResolveStoredIndex(options, 3840, 2160, 2560, 1440));
    }

    [Test]
    public void NeitherStoredNorNativeAvailableFallsBackToLargestArea()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(2, ResolutionList.ResolveStoredIndex(options, 3840, 2160, 5120, 2880));
    }

    [Test]
    public void EmptyOptionListYieldsNoIndex()
    {
        Assert.AreEqual(-1, ResolutionList.ResolveStoredIndex(new List<ResolutionOption>(), 1920, 1080, 1920, 1080));
        Assert.AreEqual(-1, ResolutionList.ResolveStoredIndex(null, 1920, 1080, 1920, 1080));
    }
}
