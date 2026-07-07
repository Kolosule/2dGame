using System;
using System.Collections.Generic;
using System.Text;

/// <summary>One player's row in the lobby roster snapshot.</summary>
public struct LobbyPlayerEntry
{
    public int Id;
    public string Name;
    public int Team; // 1 or 2

    public LobbyPlayerEntry(int id, string name, int team)
    {
        Id = id;
        Name = name;
        Team = team;
    }
}

/// <summary>Full lobby state, broadcast by the server after every lobby change.</summary>
public class LobbyStateSnapshot
{
    public bool CanStart;
    public int MaxPlayers;
    public int HostId = LobbyHostPolicy.NoHost;
    public List<LobbyPlayerEntry> Players = new List<LobbyPlayerEntry>();
}

/// <summary>
/// Byte-level encoding for lobby messages sent over Fusion reliable data. Pure C# (no UnityEngine,
/// no Fusion) so it is unit-testable. Every decoder is length-checked and returns false on
/// malformed input rather than throwing — a bad packet must never take down the lobby.
/// Snapshot wire format (little-endian ints):
///   [canStart:1][maxPlayers:1][hostId:int32][playerCount:1]
///   then per player: [id:int32][team:1][nameLen:1][name: nameLen UTF-8 bytes]
/// </summary>
public static class LobbyProtocol
{
    public const int MaxNicknameChars = 16;
    // 16 chars at up to 4 UTF-8 bytes each; nameLen is a single byte on the wire.
    public const int MaxNicknameBytes = 64;

    /// <summary>Trim, strip control chars, cap at MaxNicknameChars. Null/whitespace -> "".</summary>
    public static string SanitizeNickname(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder(MaxNicknameChars);
        foreach (char c in raw.Trim())
        {
            if (char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length == MaxNicknameChars) break;
        }
        return sb.ToString();
    }

    /// <summary>Roster name shown until the player's nickname message arrives.</summary>
    public static string PlaceholderName(int playerId) => "Player " + playerId;

    public static byte[] EncodeNickname(string sanitized) =>
        Encoding.UTF8.GetBytes(sanitized ?? "");

    public static bool TryDecodeNickname(byte[] buffer, int offset, int count, out string name)
    {
        name = "";
        if (buffer == null || count <= 0 || count > MaxNicknameBytes) return false;
        if (offset < 0 || offset + count > buffer.Length) return false;
        string decoded;
        try { decoded = Encoding.UTF8.GetString(buffer, offset, count); }
        catch (ArgumentException) { return false; }
        name = SanitizeNickname(decoded);
        return name.Length > 0;
    }

    public static byte[] EncodeLobbyState(LobbyStateSnapshot s)
    {
        var bytes = new List<byte>(8 + s.Players.Count * 24);
        bytes.Add((byte)(s.CanStart ? 1 : 0));
        bytes.Add((byte)s.MaxPlayers);
        WriteInt(bytes, s.HostId);
        bytes.Add((byte)s.Players.Count);
        foreach (var p in s.Players)
        {
            WriteInt(bytes, p.Id);
            bytes.Add((byte)p.Team);
            byte[] name = Encoding.UTF8.GetBytes(SanitizeNickname(p.Name));
            bytes.Add((byte)name.Length);
            bytes.AddRange(name);
        }
        return bytes.ToArray();
    }

    public static bool TryDecodeLobbyState(byte[] buffer, int offset, int count, out LobbyStateSnapshot s)
    {
        s = null;
        // header = canStart(1) + maxPlayers(1) + hostId(4) + playerCount(1) = 7 bytes minimum
        if (buffer == null || offset < 0 || count < 7 || offset + count > buffer.Length) return false;

        int pos = offset;
        int end = offset + count;
        var result = new LobbyStateSnapshot();
        result.CanStart = buffer[pos++] == 1;
        result.MaxPlayers = buffer[pos++];
        if (!TryReadInt(buffer, ref pos, end, out int hostId)) return false;
        result.HostId = hostId;
        int playerCount = buffer[pos++];

        for (int i = 0; i < playerCount; i++)
        {
            if (!TryReadInt(buffer, ref pos, end, out int id)) return false;
            if (pos + 2 > end) return false;
            int team = buffer[pos++];
            int nameLen = buffer[pos++];
            if (team != 1 && team != 2) return false;
            if (nameLen > MaxNicknameBytes || pos + nameLen > end) return false;
            string name;
            try { name = Encoding.UTF8.GetString(buffer, pos, nameLen); }
            catch (ArgumentException) { return false; }
            pos += nameLen;
            result.Players.Add(new LobbyPlayerEntry(id, name, team));
        }

        if (pos != end) return false; // trailing bytes = malformed
        s = result;
        return true;
    }

    private static void WriteInt(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static bool TryReadInt(byte[] buffer, ref int pos, int end, out int value)
    {
        value = 0;
        if (pos + 4 > end) return false;
        value = buffer[pos] | (buffer[pos + 1] << 8) | (buffer[pos + 2] << 16) | (buffer[pos + 3] << 24);
        pos += 4;
        return true;
    }
}
