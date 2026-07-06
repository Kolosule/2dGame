using System.Collections.Generic;

/// <summary>
/// Server-only registry of live networked coins, used to bound the total coin count so an
/// uncollected pile can't accumulate across a match and starve the server tick (each live coin
/// runs a per-tick pickup poll — see NetworkedCoinPickup). Insertion-ordered so the OLDEST coin
/// is evicted first when the cap is exceeded. Never networked; lives only on the state authority's
/// process (client processes keep their own empty instance, where every call is a harmless no-op).
///
/// Mirrors the existing static-helper style in the codebase (HitCooldownLedger, pooling). Cleared
/// on runner shutdown from GameNetworkManager.OnShutdown so it cannot leak across sessions;
/// destroyed entries are also pruned defensively on every register.
/// </summary>
public static class CoinRegistry
{
    /// <summary>Hard ceiling on simultaneously-live coins. The lifetime timer handles slow decay;
    /// this handles bursts (mass enemy deaths / player death drops during a 20-player fight).</summary>
    public const int MaxLiveCoins = 100;

    private static readonly LinkedList<NetworkedCoinPickup> live = new LinkedList<NetworkedCoinPickup>();

    /// <summary>
    /// SERVER: record a newly-spawned coin. If this pushes the live count over <see cref="MaxLiveCoins"/>,
    /// the oldest live coin is removed from the registry and RETURNED so the caller can despawn it
    /// (the registry has no NetworkRunner of its own). Destroyed entries are pruned first.
    /// </summary>
    public static NetworkedCoinPickup RegisterAndGetEvicted(NetworkedCoinPickup coin)
    {
        if (coin == null) return null;

        Prune();
        live.AddLast(coin);

        if (live.Count > MaxLiveCoins)
        {
            NetworkedCoinPickup oldest = live.First.Value;
            live.RemoveFirst();
            return oldest;
        }
        return null;
    }

    /// <summary>SERVER: drop a coin from the registry (its Despawned). No-op if not present.</summary>
    public static void Unregister(NetworkedCoinPickup coin)
    {
        if (coin == null) return;
        live.Remove(coin); // O(n), but coins are few and this is off the per-tick path
    }

    /// <summary>Forget every tracked coin. Call on runner shutdown so state can't cross sessions.</summary>
    public static void Clear() => live.Clear();

    /// <summary>Remove entries whose coin has been destroyed (Unity == null) to keep the count honest.</summary>
    private static void Prune()
    {
        LinkedListNode<NetworkedCoinPickup> node = live.First;
        while (node != null)
        {
            LinkedListNode<NetworkedCoinPickup> next = node.Next;
            if (node.Value == null) live.Remove(node);
            node = next;
        }
    }
}
