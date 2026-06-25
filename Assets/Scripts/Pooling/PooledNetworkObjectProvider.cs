using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Object-pooling network object provider. Subclasses Fusion's default provider and overrides only
/// the instantiate/destroy extension points, so all prefab-id resolution and scene bookkeeping stay
/// in the base (NetworkObjectProviderDefault.AcquirePrefabInstance / ReleaseInstance). Only prefabs
/// carrying a Poolable component are pooled — everything else uses the base Instantiate/Destroy
/// unchanged. Pooled instances are SetActive(false) on release and reused on the next acquire of the
/// same prefab, removing per-spawn allocation/GC churn (the projectile spam at 20 players).
///
/// Assign an instance to StartGameArgs.ObjectProvider (see GameNetworkManager).
/// </summary>
public class PooledNetworkObjectProvider : NetworkObjectProviderDefault
{
    // Inactive, reusable instances keyed by the source prefab they were created from.
    private readonly Dictionary<NetworkObject, Stack<NetworkObject>> pools =
        new Dictionary<NetworkObject, Stack<NetworkObject>>();

    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        // Not poolable → default behaviour (Instantiate).
        if (prefab.GetComponent<Poolable>() == null)
            return base.InstantiatePrefab(runner, prefab);

        // Reuse an inactive instance if one is available for this prefab.
        if (pools.TryGetValue(prefab, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            reused.gameObject.SetActive(true);
            return reused;
        }

        // None pooled yet → create one and remember which prefab/pool it belongs to.
        var instance = base.InstantiatePrefab(runner, prefab);
        var poolable = instance.GetComponent<Poolable>();
        poolable.SourcePrefab = prefab;
        return instance;
    }

    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        var poolable = instance.GetComponent<Poolable>();

        // Not a pooled instance → default behaviour (Destroy).
        if (poolable == null || poolable.SourcePrefab == null)
        {
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }

        // Return to the pool instead of destroying: deactivate and keep for reuse.
        instance.gameObject.SetActive(false);
        if (!pools.TryGetValue(poolable.SourcePrefab, out var stack))
        {
            stack = new Stack<NetworkObject>();
            pools[poolable.SourcePrefab] = stack;
        }
        stack.Push(instance);
    }
}
