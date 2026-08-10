using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Audio.Core;

public class SoundBankTests
{
    private static SoundBank BankWith(params SoundCue[] cues)
    {
        SoundBank bank = ScriptableObject.CreateInstance<SoundBank>();
        bank.SetCuesForTests(cues);
        return bank;
    }

    private static SoundCue Cue(AudioCueId id, AudioBus bus = AudioBus.World)
        => new SoundCue { id = id, bus = bus };

    [Test]
    public void TryGet_ReturnsTheAuthoredCue()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.Jump), Cue(AudioCueId.Land));

        Assert.IsTrue(bank.TryGet(AudioCueId.Land, out SoundCue cue));
        Assert.AreEqual(AudioCueId.Land, cue.id);
    }

    [Test]
    public void TryGet_UnknownCue_ReturnsFalse()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.Jump));

        Assert.IsFalse(bank.TryGet(AudioCueId.EnemyDeath, out SoundCue cue));
        Assert.IsNull(cue);
    }

    [Test]
    public void NoneEntries_AreNeverIndexed()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.None), Cue(AudioCueId.Jump));

        Assert.IsFalse(bank.TryGet(AudioCueId.None, out _));
        Assert.IsTrue(bank.TryGet(AudioCueId.Jump, out _));
    }

    [Test]
    public void DuplicateIds_KeepTheFirstEntry()
    {
        SoundCue first = Cue(AudioCueId.Jump, AudioBus.World);
        SoundCue second = Cue(AudioCueId.Jump, AudioBus.Combat);
        SoundBank bank = BankWith(first, second);

        LogAssert.ignoreFailingMessages = true;
        Assert.IsTrue(bank.TryGet(AudioCueId.Jump, out SoundCue cue));
        Assert.AreEqual(AudioBus.World, cue.bus);
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void HasClip_IsFalseForAnEmptyOrNullVariantList()
    {
        var empty = new SoundCue { id = AudioCueId.Jump };
        var nulled = new SoundCue { id = AudioCueId.Jump, variants = new AudioClip[] { null } };

        Assert.IsFalse(empty.HasClip);
        Assert.IsFalse(nulled.HasClip);
    }
}
