using UnityEngine;

/// <summary>
/// Spawner that automatically assigns team, territorial advantage, AND patrol points to spawned enemies
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private GameObject enemyPrefab;
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

    private float nextSpawnTime;
    private int currentEnemyCount;
    private Transform autoPatrolPointA;
    private Transform autoPatrolPointB;

    private void Start()
    {
        nextSpawnTime = Time.time + spawnInterval;

        // Create automatic patrol points if using relative positioning
        if (useRelativePatrolPoints && (patrolPointA == null || patrolPointB == null))
        {
            CreateRelativePatrolPoints();
        }
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

        Debug.Log($"Created automatic patrol points for {gameObject.name}");
    }

    private void Update()
    {
        if (Time.time >= nextSpawnTime && currentEnemyCount < maxEnemies)
        {
            SpawnEnemy();
            nextSpawnTime = Time.time + spawnInterval;
        }
    }

    private void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("Enemy prefab not assigned to spawner!");
            return;
        }

        GameObject enemyObj = Instantiate(enemyPrefab, transform.position, Quaternion.identity);

        // Assign team component values
        EnemyTeamComponent teamComponent = enemyObj.GetComponent<EnemyTeamComponent>();
        if (teamComponent != null)
        {
            teamComponent.teamID = teamID;
            teamComponent.territorialAdvantage = territorialAdvantage;
        }
        else
        {
            Debug.LogWarning($"Spawned enemy doesn't have EnemyTeamComponent! Adding one...");
            teamComponent = enemyObj.AddComponent<EnemyTeamComponent>();
            teamComponent.teamID = teamID;
            teamComponent.territorialAdvantage = territorialAdvantage;
        }

        // Assign patrol points to AI
        EnemyAI enemyAI = enemyObj.GetComponent<EnemyAI>();
        if (enemyAI != null)
        {
            // Use manually assigned points if available, otherwise use auto-created ones
            Transform pointA = patrolPointA != null ? patrolPointA : autoPatrolPointA;
            Transform pointB = patrolPointB != null ? patrolPointB : autoPatrolPointB;

            if (pointA != null && pointB != null)
            {
                // Directly assign the public fields
                enemyAI.SetPatrolPoints(pointA, pointB);
                Debug.Log($"Assigned patrol points to {enemyObj.name}: {pointA.position} to {pointB.position}");
            }
            else
            {
                Debug.LogWarning($"No patrol points available for {enemyObj.name}");
            }
        }
        else
        {
            Debug.LogWarning($"Spawned enemy doesn't have EnemyAI component!");
        }

        // Track enemy count
        currentEnemyCount++;

        // Subscribe to enemy death to update count
        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy != null)
        {
            StartCoroutine(WaitForEnemyDestruction(enemyObj));
        }
    }

    private System.Collections.IEnumerator WaitForEnemyDestruction(GameObject enemy)
    {
        yield return new WaitUntil(() => enemy == null);
        currentEnemyCount--;
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
    }
}