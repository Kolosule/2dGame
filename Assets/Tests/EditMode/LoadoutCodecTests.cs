using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class LoadoutCodecTests
{
    [Test]
    public void RoundTrip_PreservesFourEntryDefaultOrder()
    {
        var order = new List<BuffId>
        {
            BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash, BuffId.FlagRunner
        };

        BuffId[] back = LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(order));

        CollectionAssert.AreEqual(order, back);
    }

    [Test]
    public void RoundTrip_PreservesAReorderedFourEntryLoadout()
    {
        var order = new List<BuffId>
        {
            BuffId.FlagRunner, BuffId.QuickerDash, BuffId.ExtraJump, BuffId.Stealth
        };

        CollectionAssert.AreEqual(order, LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(order)));
    }

    [Test]
    public void ToBytes_EncodesBuffIdAsItsByteValue()
    {
        byte[] bytes = LoadoutCodec.ToBytes(new List<BuffId> { BuffId.FlagRunner, BuffId.ExtraJump });

        Assert.AreEqual(new byte[] { 3, 0 }, bytes);
    }

    [Test]
    public void ToBytes_NullOrderYieldsEmpty()
    {
        Assert.AreEqual(0, LoadoutCodec.ToBytes(null).Length);
    }

    [Test]
    public void FromBytes_NullYieldsEmpty()
    {
        Assert.AreEqual(0, LoadoutCodec.FromBytes(null).Length);
    }

    [Test]
    public void ToBytes_TruncatesAtMaxEntries()
    {
        var tooMany = new List<BuffId>();
        for (int i = 0; i < LoadoutCodec.MaxEntries + 3; i++) tooMany.Add(BuffId.ExtraJump);

        Assert.AreEqual(LoadoutCodec.MaxEntries, LoadoutCodec.ToBytes(tooMany).Length);
    }
}
