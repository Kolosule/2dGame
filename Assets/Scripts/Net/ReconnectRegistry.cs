using System.Collections.Generic;

/// <summary>
/// Server-side map of identity token -> the state of a player who dropped mid-match.
///
/// The hold lasts the REST OF THE MATCH — there is deliberately no timer here. The single expiry
/// event is the match ending, where GameNetworkManager.BeginReturnToLobby calls Clear().
///
/// Pure C#: GameNetworkManager owns the instance and does all the Fusion-facing work, so these
/// rules are unit-testable exactly like LobbyServerState.
/// </summary>
public class ReconnectRegistry
{
    private readonly Dictionary<string, ReconnectHeldSlot> held = new Dictionary<string, ReconnectHeldSlot>();

    /// <summary>Held (disconnected) slots. Counts against the player cap — see ReconnectPolicy.</summary>
    public int HeldCount => held.Count;

    /// <summary>
    /// Store this token's state, replacing any earlier hold for it (a player who dropped, rejoined,
    /// and dropped again re-holds rather than stacking). A null/empty token is ignored: a client
    /// with no identity simply cannot be held.
    /// </summary>
    public void Capture(string token, ReconnectHeldSlot slot)
    {
        if (string.IsNullOrEmpty(token) || slot == null) return;
        held[token] = slot;
    }

    public bool Has(string token) => !string.IsNullOrEmpty(token) && held.ContainsKey(token);

    /// <summary>
    /// Take this token's state AND remove it, so two rejoins racing on one token cannot both
    /// restore it — the first claim wins and the second is seated as a new player.
    /// </summary>
    public bool TryClaim(string token, out ReconnectHeldSlot slot)
    {
        slot = null;
        if (string.IsNullOrEmpty(token)) return false;
        if (!held.TryGetValue(token, out slot)) return false;
        held.Remove(token);
        return true;
    }

    /// <summary>Release every hold. Called when the match ends and on runner shutdown.</summary>
    public void Clear() => held.Clear();
}
