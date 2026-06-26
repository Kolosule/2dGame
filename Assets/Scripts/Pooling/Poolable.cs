using Fusion;
using UnityEngine;

/// <summary>
/// Marks a NetworkObject prefab as poolable by PooledNetworkObjectProvider. Add this to high-churn
/// prefabs (e.g. the projectile) so they are reused instead of Instantiate/Destroy'd every shot.
/// SourcePrefab is stamped by the provider at runtime so a released instance returns to the pool
/// for the prefab it came from. It is non-serialized (runtime-only); do not set it in the Inspector.
/// </summary>
public class Poolable : MonoBehaviour
{
    [System.NonSerialized] public NetworkObject SourcePrefab;
}
