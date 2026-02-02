using UnityEngine;
using Fusion;

public class EnemySpawnerDebugger : NetworkBehaviour
{
    [SerializeField] private bool enableVerboseLogging = true;

    private void Update()
    {
        if (!enableVerboseLogging) return;

        if (Time.frameCount % 120 == 0) // Every 2 seconds
        {
            LogSpawnerStatus();
        }
    }

    private void LogSpawnerStatus()
    {
        Debug.Log($"🐛 ENEMY SPAWNER: {gameObject.name}");
        Debug.Log($"  HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"  IsServer: {Runner.IsServer}");

        NetworkObject[] enemies = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        int enemyCount = 0;

        foreach (NetworkObject obj in enemies)
        {
            if (obj.GetComponent<EnemyAI>() != null)
            {
                enemyCount++;
                Debug.Log($"  Enemy found: {obj.name} at {obj.transform.position}");
            }
        }

        Debug.Log($"  Total enemies: {enemyCount}");
    }
}