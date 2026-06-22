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

        NetworkObject[] enemies = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
        int enemyCount = 0;

        foreach (NetworkObject obj in enemies)
        {
            if (obj.GetComponent<EnemyAI>() != null)
            {
                enemyCount++;
            }
        }

    }
}