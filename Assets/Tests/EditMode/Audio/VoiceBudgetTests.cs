using NUnit.Framework;
using Game.Audio.Core;

public class VoiceBudgetTests
{
    private const int AnyCue = 1;
    private const int OtherCue = 2;

    [Test]
    public void AcquireUnderCapacity_ReturnsDistinctSlots()
    {
        var budget = new VoiceBudget(3);
        int a = budget.TryAcquire(AnyCue, priority: 10, maxConcurrent: 0, now: 0f);
        int b = budget.TryAcquire(OtherCue, priority: 10, maxConcurrent: 0, now: 1f);
        Assert.GreaterOrEqual(a, 0);
        Assert.GreaterOrEqual(b, 0);
        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void Release_FreesTheSlotForReuse()
    {
        var budget = new VoiceBudget(1);
        int a = budget.TryAcquire(AnyCue, priority: 10, maxConcurrent: 0, now: 0f);
        Assert.IsTrue(budget.IsActive(a));
        budget.Release(a);
        Assert.IsFalse(budget.IsActive(a));
        Assert.AreEqual(a, budget.TryAcquire(OtherCue, priority: 10, maxConcurrent: 0, now: 1f));
    }

    [Test]
    public void FullPool_StealsTheLowestPriorityVoice()
    {
        var budget = new VoiceBudget(2);
        int low = budget.TryAcquire(AnyCue, priority: 1, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 90, maxConcurrent: 0, now: 1f);
        int stolen = budget.TryAcquire(3, priority: 50, maxConcurrent: 0, now: 2f);
        Assert.AreEqual(low, stolen, "The priority-1 voice should be the victim, not the priority-90 one.");
    }

    [Test]
    public void FullPool_AllVoicesHigherPriority_DropsTheIncomingCue()
    {
        var budget = new VoiceBudget(2);
        budget.TryAcquire(AnyCue, priority: 90, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 90, maxConcurrent: 0, now: 1f);
        Assert.AreEqual(-1, budget.TryAcquire(3, priority: 10, maxConcurrent: 0, now: 2f));
    }

    [Test]
    public void FullPool_EqualPriority_StealsTheOldest()
    {
        var budget = new VoiceBudget(2);
        int oldest = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 0, now: 5f);
        Assert.AreEqual(oldest, budget.TryAcquire(3, priority: 50, maxConcurrent: 0, now: 9f));
    }

    // maxConcurrent = 1 is what keeps a slider drag from machine-gunning UiSliderTick.
    [Test]
    public void MaxConcurrentOne_ReusesTheSameSlotForTheSameCue()
    {
        var budget = new VoiceBudget(8);
        int first = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 0f);
        int second = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 1f);
        Assert.AreEqual(first, second);
    }

    [Test]
    public void MaxConcurrent_DoesNotConstrainADifferentCue()
    {
        var budget = new VoiceBudget(8);
        int first = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 0f);
        int other = budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 1, now: 1f);
        Assert.AreNotEqual(first, other);
    }

    [Test]
    public void MaxConcurrentZero_IsBoundedOnlyByCapacity()
    {
        var budget = new VoiceBudget(3);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 1f);
        int third = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 2f);
        Assert.GreaterOrEqual(third, 0);
        Assert.AreEqual(3, budget.Capacity);
    }

    [Test]
    public void ReleaseAll_ClearsEverySlot()
    {
        var budget = new VoiceBudget(2);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 0, now: 1f);
        budget.ReleaseAll();
        Assert.IsFalse(budget.IsActive(0));
        Assert.IsFalse(budget.IsActive(1));
    }
}
