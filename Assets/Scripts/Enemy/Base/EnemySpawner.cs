using UnityEngine;

/// <summary>
/// Optional spawner script that automatically assigns team and territorial advantage to spawned enemies
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

    [Header("Debug")]
    [SerializeField] private bool showGizmo = true;

    private float nextSpawnTime;
    private int currentEnemyCount;

    private void Start()
    {
        nextSpawnTime = Time.time + spawnInterval;
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

        // Track enemy count
        currentEnemyCount++;

        // Subscribe to enemy death to update count
        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy != null)
        {
            // You might want to add an event system for this
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

        // Color based on territorial advantage
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
    }
}