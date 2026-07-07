using NUnit.Framework;

public class LobbyProtocolTests
{
    [Test]
    public void SanitizeNickname_TrimsCapsAndStripsControls()
    {
        Assert.AreEqual("Bob", LobbyProtocol.SanitizeNickname("  Bob  "));
        Assert.AreEqual(new string('a', 16), LobbyProtocol.SanitizeNickname(new string('a', 40)));
        Assert.AreEqual("", LobbyProtocol.SanitizeNickname("   "));
        Assert.AreEqual("", LobbyProtocol.SanitizeNickname(null));
        Assert.AreEqual("ab", LobbyProtocol.SanitizeNickname("a\tb"));
    }

    [Test]
    public void Nickname_RoundTrip()
    {
        byte[] buf = LobbyProtocol.EncodeNickname("Bob");
        Assert.IsTrue(LobbyProtocol.TryDecodeNickname(buf, 0, buf.Length, out string name));
        Assert.AreEqual("Bob", name);
    }

    [Test]
    public void Nickname_MultibyteRoundTrip()
    {
        byte[] buf = LobbyProtocol.EncodeNickname("Ünïcøde");
        Assert.IsTrue(LobbyProtocol.TryDecodeNickname(buf, 0, buf.Length, out string name));
        Assert.AreEqual("Ünïcøde", name);
    }

    [Test]
    public void TryDecodeNickname_Malformed_False()
    {
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(null, 0, 3, out _));
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(new byte[0], 0, 0, out _));
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(new byte[100], 0, 100, out _)); // > MaxNicknameBytes
        byte[] spaces = LobbyProtocol.EncodeNickname("   ");
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(spaces, 0, spaces.Length, out _));
    }

    [Test]
    public void LobbyState_RoundTrip_EmptyRoster()
    {
        var s = new LobbyStateSnapshot { CanStart = false, MaxPlayers = 20, HostId = LobbyHostPolicy.NoHost };
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        Assert.IsTrue(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out var d));
        Assert.IsFalse(d.CanStart);
        Assert.AreEqual(20, d.MaxPlayers);
        Assert.AreEqual(LobbyHostPolicy.NoHost, d.HostId);
        Assert.AreEqual(0, d.Players.Count);
    }

    [Test]
    public void LobbyState_RoundTrip_FullRoster()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = 1 };
        for (int i = 1; i <= 20; i++)
            s.Players.Add(new LobbyPlayerEntry(i, "Player" + i, (i % 2) + 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        Assert.IsTrue(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out var d));
        Assert.IsTrue(d.CanStart);
        Assert.AreEqual(1, d.HostId);
        Assert.AreEqual(20, d.Players.Count);
        Assert.AreEqual("Player7", d.Players[6].Name);
        Assert.AreEqual(2, d.Players[6].Team);
        Assert.AreEqual(7, d.Players[6].Id);
    }

    [Test]
    public void LobbyState_TruncatedBuffers_AllRejected()
    {
        var s = new LobbyStateSnapshot { CanStart = false, MaxPlayers = 20, HostId = 3 };
        s.Players.Add(new LobbyPlayerEntry(3, "Ann", 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        for (int len = 0; len < buf.Length; len++)
            Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(buf, 0, len, out _), $"len={len} should fail");
    }

    [Test]
    public void LobbyState_BadTeamByte_Rejected()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = 5 };
        s.Players.Add(new LobbyPlayerEntry(5, "Ann", 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        buf[11] = 9; // header is 7 bytes, player id is 4 -> team byte sits at index 11
        Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out _));
    }

    [Test]
    public void LobbyState_TrailingBytes_Rejected()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = LobbyHostPolicy.NoHost };
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        byte[] longer = new byte[buf.Length + 1];
        System.Array.Copy(buf, longer, buf.Length);
        Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(longer, 0, longer.Length, out _));
    }
}
