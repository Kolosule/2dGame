using NUnit.Framework;
using Game.Audio.Core;

public class SoundDedupeTests
{
    [Test]
    public void FirstPlay_IsAlwaysAllowed()
    {
        var dedupe = new SoundDedupe();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0.06f));
    }

    [Test]
    public void ZeroWindow_NeverSuppresses()
    {
        var dedupe = new SoundDedupe();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
    }

    [Test]
    public void SecondPlay_InsideWindow_IsSuppressed()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsFalse(dedupe.ShouldPlay(1, now: 0.05f, window: 0.06f));
    }

    [Test]
    public void SecondPlay_AtOrAfterWindow_IsAllowed()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.06f, window: 0.06f));
    }

    [Test]
    public void DifferentCueIds_HaveIndependentWindows()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsTrue(dedupe.ShouldPlay(2, now: 0f, window: 0.06f));
    }

    // A 20-player scrum hammers the same cue continuously. If every SUPPRESSED attempt pushed the
    // window forward, the cue would go permanently silent instead of firing once per window.
    [Test]
    public void SuppressedAttempt_DoesNotExtendTheWindow()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsFalse(dedupe.ShouldPlay(1, now: 0.05f, window: 0.06f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.06f, window: 0.06f),
            "The denied attempt at 0.05 must not have moved the window to 0.11.");
    }

    [Test]
    public void Clear_ResetsAllWindows()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        dedupe.Clear();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.01f, window: 0.06f));
    }
}
