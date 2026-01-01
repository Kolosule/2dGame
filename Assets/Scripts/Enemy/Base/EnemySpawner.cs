using UnityEngine;
using Fusion;

/// <summary>
/// Spawner that automatically assigns team, territorial advantage, AND patrol points to spawned enemies.
/// Uses standard EnemyAI (non-networked AI).
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

    [Header("Patrol Points")]
    [Tooltip("Assign patrol points for enemies spawned from this spawner")]
    [SerializeField] private Transform patrolPointA;
    [SerializeField] private Transform patrolPointB;

    [Tooltip("Create patrol points relative to spawn position (if manual points not assigned)")]
    [SerializeField] private bool useRelativePatrolPoints = true;
    [SerializeField] private Vector2 relativePointA = new Vector2(-5f, 0f);
    [SerializeField] private Vector2 relativePointB = new Vector2(5f, 0f);

    [Header("Debug")]
    [SerializeField] private bool showGizmo = true;

    // Network state
    [Networked] private int CurrentEnemyCount { get; set; }
    [Networked] private TickTimer NextSpawnTimer { get; set; }

    private Transform autoPatrolPointA;
    private Transform autoPatrolPointB;

    public override void Spawned()
    {
        // Only server handles spawning
        if (!HasStateAuthority) return;

        // Initialize spawn timer
        NextSpawnTimer = TickTimer.CreateFromSeconds(Runner, spawnInterval);
        CurrentEnemyCount = 0;

        // Create automatic patrol points if using relative positioning
        if (useRelativePatrolPoints && (patrolPointA == null || patrolPointB == null))
        {
            CreateRelativePatrolPoints();
        }

        Debug.Log($"[SERVER] NetworkedEnemySpawner initialized: {gameObject.name}");
    }

    private void CreateRelativePatrolPoints()
    {
        // Create patrol point A
        GameObject pointAObj = new GameObject($"{gameObject.name}_PatrolPointA");
        pointAObj.transform.position = transform.position + (Vector3)relativePointA;
        pointAObj.transform.parent = transform; // Make it a child so it moves with spawner
        autoPatrolPointA = pointAObj.transform;

        // Create patrol point B
        GameObject pointBObj = new GameObject($"{gameObject.name}_PatrolPointB");
        pointBObj.transform.position = transform.position + (Vector3)relativePointB;
        pointBObj.transform.parent = transform;
        autoPatrolPointB = pointBObj.transform;

        Debug.Log($"[SERVER] Created automatic patrol points for {gameObject.name}");
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

        Debug.Log($"[SERVER] Spawned enemy at {transform.position}");
    }

    /// <summary>
    /// Initialize the spawned enemy with team, territory, and patrol points
    /// Called by the spawn callback on the server
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
            //Debug.Log($"[SERVER] Assigned team {teamID} to {enemyObj.name}");
        }
        else
        {
           // Debug.LogWarning($"[SERVER] Spawned enemy doesn't have EnemyTeamComponent!");
        }

        // Assign patrol points to AI (using standard EnemyAI, not NetworkedEnemyAI)
        EnemyAI enemyAI = enemyObj.GetComponent<EnemyAI>();
        if (enemyAI != null)
        {
            // Use manually assigned points if available, otherwise use auto-created ones
            Transform pointA = patrolPointA != null ? patrolPointA : autoPatrolPointA;
            Transform pointB = patrolPointB != null ? patrolPointB : autoPatrolPointB;

            //if (pointA != null && pointB != null)
            //{
               // enemyAI.SetPatrolPoints(pointA, pointB);
              //  Debug.Log($"[SERVER] Assigned patrol points to {enemyObj.name}");
          //  }
           // else
           // {
           //     Debug.LogWarning($"[SERVER] No patrol points available for {enemyObj.name}");
           // }
        }
        else
        {
            Debug.LogWarning($"[SERVER] Spawned enemy doesn't have EnemyAI component!");
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
            Debug.Log($"[SERVER] Enemy despawned. Count: {CurrentEnemyCount}/{maxEnemies}");
        }
    }

    // Visual debugging
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        // Draw spawn point
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        // Draw patrol points
        Gizmos.color = Color.yellow;

        if (patrolPointA != null)
        {
            Gizmos.DrawLine(transform.position, patrolPointA.position);
            Gizmos.DrawWireSphere(patrolPointA.position, 0.3f);
        }
        else if (useRelativePatrolPoints)
        {
            Vector3 pointA = transform.position + (Vector3)relativePointA;
            Gizmos.DrawLine(transform.position, pointA);
            Gizmos.DrawWireSphere(pointA, 0.3f);
        }

        if (patrolPointB != null)
        {
            Gizmos.DrawLine(transform.position, patrolPointB.position);
            Gizmos.DrawWireSphere(patrolPointB.position, 0.3f);
        }
        else if (useRelativePatrolPoints)
        {
            Vector3 pointB = transform.position + (Vector3)relativePointB;
            Gizmos.DrawLine(transform.position, pointB);
            Gizmos.DrawWireSphere(pointB, 0.3f);
        }
    }
}