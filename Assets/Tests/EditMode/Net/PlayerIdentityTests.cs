using System;
using System.Reflection;
using NUnit.Framework;

public class PlayerIdentityTests
{
    private const string Hex32 = "0123456789abcdef0123456789abcdef";

    private FieldInfo cachedHexField;
    private FieldInfo cachedBytesField;
    private PropertyInfo hexProperty;
    private PropertyInfo tokenBytesProperty;
    private string originalHex;
    private byte[] originalBytes;

    [OneTimeSetUp]
    public void FindIdentityMembers()
    {
        // This test asmdef cannot reference Assembly-CSharp, where PlayerIdentity lives.
        Type identityType = Type.GetType("PlayerIdentity, Assembly-CSharp", true);
        cachedHexField = identityType.GetField("cachedHex", BindingFlags.Static | BindingFlags.NonPublic);
        cachedBytesField = identityType.GetField("cachedBytes", BindingFlags.Static | BindingFlags.NonPublic);
        hexProperty = identityType.GetProperty("Hex", BindingFlags.Static | BindingFlags.Public);
        tokenBytesProperty = identityType.GetProperty("TokenBytes", BindingFlags.Static | BindingFlags.Public);

        Assert.IsNotNull(cachedHexField);
        Assert.IsNotNull(cachedBytesField);
        Assert.IsNotNull(hexProperty);
        Assert.IsNotNull(tokenBytesProperty);
    }

    [SetUp]
    public void IsolateCache()
    {
        originalHex = (string)cachedHexField.GetValue(null);
        originalBytes = (byte[])cachedBytesField.GetValue(null);

        // Seed only memory so the getters never read, mint, or write a real PlayerPrefs identity.
        cachedHexField.SetValue(null, Hex32);
        cachedBytesField.SetValue(null, null);
    }

    [TearDown]
    public void RestoreCache()
    {
        cachedHexField.SetValue(null, originalHex);
        cachedBytesField.SetValue(null, originalBytes);
    }

    [Test]
    public void TokenBytes_RepeatedCallsReturnEqualContentsInIndependentArrays()
    {
        byte[] first = GetTokenBytes();
        byte[] cache = (byte[])cachedBytesField.GetValue(null);
        byte[] second = GetTokenBytes();

        CollectionAssert.AreEqual(IdentityTokenCodec.ToBytes(Hex32), first);
        CollectionAssert.AreEqual(first, second);
        Assert.AreNotSame(first, second);
        Assert.AreNotSame(cache, first);
        Assert.AreNotSame(cache, second);
        Assert.AreSame(cache, cachedBytesField.GetValue(null), "The internal token stays cached.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TokenBytes_CallerMutationDoesNotChangeLaterTokensOrIdentity(bool warmCache)
    {
        if (warmCache)
            GetTokenBytes();

        byte[] returned = GetTokenBytes();
        for (int i = 0; i < returned.Length; i++)
            returned[i] ^= 0xff;

        byte[] later = GetTokenBytes();
        byte[] expected = IdentityTokenCodec.ToBytes(Hex32);

        Assert.AreNotSame(returned, later);
        CollectionAssert.AreEqual(expected, later);
        CollectionAssert.AreEqual(expected, (byte[])cachedBytesField.GetValue(null));
        Assert.AreEqual(Hex32, hexProperty.GetValue(null));
    }

    private byte[] GetTokenBytes() => (byte[])tokenBytesProperty.GetValue(null);
}
