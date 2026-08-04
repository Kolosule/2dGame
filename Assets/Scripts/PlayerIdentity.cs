using UnityEngine;

/// <summary>
/// The local player's stable identity: a GUID minted once, kept in PlayerPrefs, and sent as Fusion's
/// StartGameArgs.ConnectionToken on every connect so the server can recognise a rejoining player
/// whose PlayerRef has changed.
///
/// It is an identity HINT, not a credential: it only ever unlocks state the holder already earned in
/// the current match. See docs/superpowers/specs/2026-07-29-reconnection-design.md.
///
/// NOTE: this lives in the default assembly rather than Assets/Scripts/Net/ because Game.Net is
/// declared noEngineReferences and PlayerPrefs is a UnityEngine type.
/// </summary>
public static class PlayerIdentity
{
    private const string PrefKeyBase = "reconnect.identity.v1";

    /// <summary>Command-line override: `-identitySuffix bravo` -> key "reconnect.identity.v1.bravo".</summary>
    private const string SuffixArg = "-identitySuffix";

    private static string cachedHex;
    private static byte[] cachedBytes;

    /// <summary>The 32-char lowercase hex identity, minted and persisted on first access.</summary>
    public static string Hex
    {
        get
        {
            if (!string.IsNullOrEmpty(cachedHex)) return cachedHex;

            string key = PrefKey();
            string stored = PlayerPrefs.GetString(key, "");

            // Re-mint anything that is not a well-formed token (first run, cleared prefs, a value
            // written by an older build). ToBytes is the single definition of "well-formed".
            if (IdentityTokenCodec.ToBytes(stored) == null)
            {
                stored = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, stored);
                PlayerPrefs.Save();
            }

            cachedHex = stored;
            return cachedHex;
        }
    }

    /// <summary>The same identity as the 16 raw bytes Fusion sends as the connection token.</summary>
    public static byte[] TokenBytes
    {
        get
        {
            if (cachedBytes == null) cachedBytes = IdentityTokenCodec.ToBytes(Hex);
            return cachedBytes;
        }
    }

    /// <summary>
    /// PlayerPrefs is per-PRODUCT, not per-process: on Windows it is one registry key derived from
    /// company + product name. Two clients on one machine — two standalone builds, or Multiplayer
    /// Play Mode virtual players — therefore share an identity by default, which makes every local
    /// peer look like a duplicate token and makes reconnection untestable locally.
    ///
    /// Two salts fix that: the editor always gets its own key (so editor + build are distinct), and
    /// `-identitySuffix &lt;value&gt;` gives each standalone build its own. The suffix is stable across
    /// relaunches of the same peer, which matters — a per-process salt would mint a new identity on
    /// every restart and break exactly the reconnect-after-relaunch case worth testing.
    /// </summary>
    private static string PrefKey()
    {
        string key = PrefKeyBase;

        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == SuffixArg && !string.IsNullOrEmpty(args[i + 1]))
            {
                key = key + "." + args[i + 1];
                break;
            }
        }

#if UNITY_EDITOR
        key = key + ".editor";
#endif
        return key;
    }
}
