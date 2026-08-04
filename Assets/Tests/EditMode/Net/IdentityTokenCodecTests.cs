using NUnit.Framework;

public class IdentityTokenCodecTests
{
    private const string Hex32 = "0123456789abcdef0123456789abcdef";

    [Test]
    public void RoundTrip_HexToBytesToHex_IsLossless()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes(Hex32);
        Assert.IsNotNull(bytes);
        Assert.AreEqual(16, bytes.Length);
        Assert.AreEqual(Hex32, IdentityTokenCodec.ToHex(bytes));
    }

    [Test]
    public void ToBytes_ParsesEachBytePairCorrectly()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes(Hex32);
        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x23, bytes[1]);
        Assert.AreEqual(0xef, bytes[7]);
    }

    [Test]
    public void ToBytes_AcceptsUppercase()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes("0123456789ABCDEF0123456789ABCDEF");
        Assert.IsNotNull(bytes);
        Assert.AreEqual(0xab, bytes[5]);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0123")]                                    // too short
    [TestCase("0123456789abcdef0123456789abcdef00")]      // too long
    [TestCase("0123456789abcdef0123456789abcdeg")]        // 'g' is not hex
    public void ToBytes_RejectsAnythingThatIsNot32HexChars(string hex)
    {
        Assert.IsNull(IdentityTokenCodec.ToBytes(hex));
    }

    [Test]
    public void ToHex_RejectsNullOrWrongLength_ReturningEmpty()
    {
        // The server path feeds this GetPlayerConnectionToken's result, which is null on a client
        // or when the token is missing. Empty string means "no identity", never a bogus key.
        Assert.AreEqual("", IdentityTokenCodec.ToHex(null));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[0]));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[15]));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[17]));
    }
}
