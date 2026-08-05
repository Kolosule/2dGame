using UnityEngine;
using Fusion;

/// <summary>
/// Spawner that automatically assigns team to spawned enemies.
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
    [SerializeField] private string teamID = "Team3";

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

        // Assign team component value
        EnemyTeamComponent teamComponent = enemyObj.GetComponent<EnemyTeamComponent>();
        if (teamComponent != null)
        {
            teamComponent.teamID = teamID;
        }

        // Networked team so clients colorize correctly (the teamComponent field above is
        // server-local; it remains as the authored fallback for scene-placed enemies).
        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.ServerSetTeam(TeamUtil.Normalize(teamID));
        }

        // Track enemy count; the Enemy reports back via NotifyEnemyDespawned when it dies
        // (event-driven — replaces one polling coroutine per live enemy).
        CurrentEnemyCount++;
        if (enemy != null)
        {
            enemy.ServerSetOwnerSpawner(this);
        }
    }

    /// <summary>SERVER: called by a spawned Enemy from its Despawned() callback.</summary>
    public void NotifyEnemyDespawned()
    {
        if (!HasStateAuthority) return;
        CurrentEnemyCount = Mathf.Max(0, CurrentEnemyCount - 1);
    }

    // Visual debugging
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
