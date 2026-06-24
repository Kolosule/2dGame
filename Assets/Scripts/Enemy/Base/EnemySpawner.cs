using UnityEngine;
using Fusion;

/// <summary>
/// Spawner that automatically assigns team and territorial advantage to spawned enemies.
/// The Enemy captures its own home anchor at spawn.
/// </summary>
public class NetworkedEnemySpawner : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("Enemy prefab - MUST have NetworkObject component!")]
    [SerializeField] private NetworkObject enemyPrefab;

    [SerializeField] private float spawnInterval = 5f;
    [SerializeField] private int maxEnemies = 10;

    [Header("Team Configuration")]
    [Tooltip("Which team spawns from this spawner (Team1, Team2, or Team3 for AI)")]
    [SerializeField] private string teamID = "Team1";

    [Tooltip("Territorial advantage for enemies spawned here: -1 (enemy base) to +1 (own base)")]
    [Range(-1f, 1f)]
    [SerializeField] private float territorialAdvantage = 0f;

    [Header("Debug")]
    [SerializeField] private bool showGizmo = true;

    // Network state
    [Networked] private int CurrentEnemyCount { get; set; }
    [Networked] private TickTimer NextSpawnTimer { get; set; }

    public override void Spawned()
    {
        // Only server handles spawning
        if (!HasStateAuthority) return;

        // Initialize spawn timer
        NextSpawnTimer = TickTimer.CreateFromSeconds(Runner, spawnInterval);
        CurrentEnemyCount = 0;
    }

    public override void FixedUpdateNetwork()
    {
        // Only server spawns enemies
        if (!HasStateAuthority) return;

        // Check if it's time to spawn and we're under the limit
        if (NextSpawnTimer.Expired(Runner) && CurrentEnemyCount < maxEnemies)
        {
            SpawnEnemy();
            NextSpawnTimer = TickTimer.CreateFromSeconds(Runner, spawnInterval);
        }
    }

    private void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("[SERVER] Enemy prefab not assigned to spawner!");
            return;
        }

        // Spawn the networked enemy (only server can do this)
        NetworkObject enemyNetObj = Runner.Spawn(
            enemyPrefab,
            transform.position,
            Quaternion.identity,
            null, // No specific player authority
            (runner, obj) => {
                // This callback is called after the object is spawned
                InitializeEnemy(obj);
            }
        );

    }

    /// <summary>
    /// Initialize the spawned enemy with team and territory.
    /// Called by the spawn callback on the server.
    /// </summary>
    private void InitializeEnemy(NetworkObject enemyNetObj)
    {
        GameObject enemyObj = enemyNetObj.gameObject;

        // Assign team component values
        EnemyTeamComponent teamComponent = enemyObj.GetComponent<EnemyTeamComponent>();
        if (teamComponent != null)
        {
            teamComponent.teamID = teamID;
            teamComponent.territorialAdvantage = territorialAdvantage;
        }

        // Track enemy count
        CurrentEnemyCount++;

        // Subscribe to enemy despawn to update count
        StartCoroutine(WaitForEnemyDespawn(enemyNetObj));
    }

    /// <summary>
    /// Wait for enemy to be despawned and decrement counter
    /// </summary>
    private System.Collections.IEnumerator WaitForEnemyDespawn(NetworkObject enemy)
    {
        // Wait until enemy is despawned or destroyed
        yield return new WaitUntil(() => enemy == null || !enemy.IsValid);

        if (HasStateAuthority)
        {
            CurrentEnemyCount--;
        }
    }

    // Visual debugging
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
