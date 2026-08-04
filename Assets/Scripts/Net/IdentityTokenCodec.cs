/// <summary>
/// Converts between the 32-char hex identity string kept in PlayerPrefs and the 16 raw bytes sent
/// as Fusion's StartGameArgs.ConnectionToken. Engine-free so the round trip is unit-testable, and
/// deliberately strict: anything that is not exactly 16 bytes / 32 hex chars is "no identity"
/// rather than a partially-parsed key that could collide with a real one.
/// </summary>
public static class IdentityTokenCodec
{
    public const int TokenBytes = 16;

    /// <summary>32 hex chars -> 16 bytes. Null for anything else (wrong length or a non-hex char).</summary>
    public static byte[] ToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != TokenBytes * 2) return null;

        var bytes = new byte[TokenBytes];
        for (int i = 0; i < TokenBytes; i++)
        {
            int hi = HexValue(hex[i * 2]);
            int lo = HexValue(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0) return null;
            bytes[i] = (byte)((hi << 4) | lo);
        }
        return bytes;
    }

    /// <summary>
    /// 16 bytes -> 32 lowercase hex chars. Empty string for anything else — including null, which
    /// is what NetworkRunner.GetPlayerConnectionToken returns on a client or when a client sent no
    /// token at all.
    /// </summary>
    public static string ToHex(byte[] token)
    {
        if (token == null || token.Length != TokenBytes) return string.Empty;

        var chars = new char[TokenBytes * 2];
        for (int i = 0; i < TokenBytes; i++)
        {
            chars[i * 2] = HexDigit(token[i] >> 4);
            chars[i * 2 + 1] = HexDigit(token[i] & 0xF);
        }
        return new string(chars);
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + (value - 10));

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }
}
