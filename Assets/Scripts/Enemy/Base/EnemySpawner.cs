using UnityEngine;
using Fusion;

/// <summary>
/// Networked spawner that automatically assigns team, territorial advantage, AND patrol points to spawned enemies.
/// PHOTON FUSION VERSION - Only server spawns enemies, all clients see them.
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
            Debug.Log($"[SERVER] Assigned team {teamID} to {enemyObj.name}");
        }
        else
        {
            Debug.LogWarning($"[SERVER] Spawned enemy doesn't have EnemyTeamComponent!");
        }

        // Assign patrol points to AI
        NetworkedEnemyAI enemyAI = enemyObj.GetComponent<NetworkedEnemyAI>();
        if (enemyAI != null)
        {
            // Use manually assigned points if available, otherwise use auto-created ones
            Transform pointA = patrolPointA != null ? patrolPointA : autoPatrolPointA;
            Transform pointB = patrolPointB != null ? patrolPointB : autoPatrolPointB;

            if (pointA != null && pointB != null)
            {
                enemyAI.SetPatrolPoints(pointA, pointB);
                Debug.Log($"[SERVER] Assigned patrol points to {enemyObj.name}");
            }
            else
            {
                Debug.LogWarning($"[SERVER] No patrol points available for {enemyObj.name}");
            }
        }
        else
        {
            Debug.LogWarning($"[SERVER] Spawned enemy doesn't have NetworkedEnemyAI component!");
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

    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        // Draw spawner position
        Color gizmoColor;
        if (territorialAdvantage > 0.3f)
            gizmoColor = Color.green; // Own territory
        else if (territorialAdvantage < -0.3f)
            gizmoColor = Color.red; // Enemy territory
        else
            gizmoColor = Color.yellow; // Neutral

        gizmoColor.a = 0.5f;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1f);

        // Draw patrol area
        Transform pointA = patrolPointA;
        Transform pointB = patrolPointB;

        // If manual points not set, show where auto points would be
        if (useRelativePatrolPoints && (pointA == null || pointB == null))
        {
            Vector3 autoPointAPos = transform.position + (Vector3)relativePointA;
            Vector3 autoPointBPos = transform.position + (Vector3)relativePointB;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(autoPointAPos, 0.3f);
            Gizmos.DrawWireSphere(autoPointBPos, 0.3f);
            Gizmos.DrawLine(autoPointAPos, autoPointBPos);
        }
        else if (pointA != null && pointB != null)
        {
            // Draw manual patrol points
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(pointA.position, 0.3f);
            Gizmos.DrawWireSphere(pointB.position, 0.3f);
            Gizmos.DrawLine(pointA.position, pointB.position);
        }

        // Draw enemy count info - ONLY if the object has been spawned on the network
#if UNITY_EDITOR
        // Check if Object is valid and spawned before accessing networked properties
        if (Object != null && Object.IsValid)
        {
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"{teamID}\nEnemies: {CurrentEnemyCount}/{maxEnemies}",
                new GUIStyle()
                {
                    normal = new GUIStyleState() { textColor = Color.white },
                    alignment = TextAnchor.MiddleCenter
                }
            );
        }
        else
        {
            // Show static info when not spawned (in editor)
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.5f,
                $"{teamID}\nMax: {maxEnemies}",
                new GUIStyle()
                {
                    normal = new GUIStyleState() { textColor = Color.gray },
                    alignment = TextAnchor.MiddleCenter
                }
            );
        }
#endif
    }
}