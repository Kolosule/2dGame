/// <summary>
/// The server's admission rule, applied in GameNetworkManager.OnConnectRequest.
///
/// A held (disconnected) slot RESERVES its seat, which Fusion's own PlayerCount cannot express —
/// Fusion frees its slot the moment a player disconnects. So the real cap is enforced one level up,
/// here, while StartGameArgs.PlayerCount stays at maxPlayers as a backstop.
/// </summary>
public static class ReconnectPolicy
{
    /// <summary>
    /// A known token is always admitted: it is reclaiming a seat already reserved for it. An unknown
    /// token is admitted only while active + held is below the cap, which keeps the invariant
    /// active + held &lt;= maxPlayers — every held slot was previously an active one, so no headroom
    /// above Fusion's PlayerCount is ever needed.
    /// </summary>
    public static bool CanAdmit(bool knownToken, int activeCount, int heldCount, int maxPlayers)
    {
        if (knownToken) return true;
        return activeCount + heldCount < maxPlayers;
    }
}
