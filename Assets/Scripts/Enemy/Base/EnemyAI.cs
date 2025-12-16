using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyStats stats;

    [Header("Patrol Points")]
    [Tooltip("Assign these in the Inspector OR they will be set by EnemySpawner")]
    public Transform pointA;
    public Transform pointB;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 5f;
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Coin Dropping")]
    [Tooltip("Which team/color this enemy belongs to (Red, Blue, etc.)")]
    [SerializeField] private string enemyTeam;

    [Tooltip("Prefab of the coin to drop when enemy dies")]
    [SerializeField] private GameObject coinPrefab;

    [Tooltip("Should this enemy drop a coin when defeated?")]
    [SerializeField] private bool dropsCoin = true;

    [Tooltip("Offset from enemy position where coin spawns")]
    [SerializeField] private Vector2 coinDropOffset = Vector2.zero;

    private Transform currentTargetPoint;
    private Rigidbody2D rb;
    private Enemy enemy;
    private PlayerStatsHandler lockedTarget;

    // Track if we've logged the patrol point warning
    private bool hasLoggedPatrolWarning = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();
    }

    void Start()
    {
        // Set initial target point only if both points exist
        if (pointA != null && pointB != null)
        {
            currentTargetPoint = pointA;
            Debug.Log($"{gameObject.name}: Patrol points set! Will patrol between {pointA.position} and {pointB.position}");
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: Patrol points NOT set! Enemy will only stand still until player is detected.");
        }

        // Validate coin dropping setup
        if (dropsCoin && coinPrefab == null)
        {
            Debug.LogError($"Enemy {gameObject.name} is set to drop coins but has no coin prefab assigned!");
        }
    }

    void Update()
    {
        // CRITICAL: Don't override velocity during knockback!
        if (enemy != null && enemy.IsKnockedBack())
        {
            return;
        }

        if (lockedTarget != null)
        {
            ChaseTarget();
        }
        else
        {
            // Only patrol if we have valid points
            if (pointA != null && pointB != null)
            {
                Patrol();
            }
            else
            {
                // Stand still and just detect
                if (!hasLoggedPatrolWarning)
                {
                    Debug.LogWarning($"{gameObject.name}: Can't patrol - points not assigned!");
                    hasLoggedPatrolWarning = true;
                }
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            }

            DetectPlayer();
        }
    }

    private void Patrol()
    {
        // Safety check (should never happen now, but just in case)
        if (pointA == null || pointB == null || currentTargetPoint == null)
        {
            return;
        }

        float distance = Vector2.Distance(transform.position, currentTargetPoint.position);

        // Switch patrol point when close enough
        if (distance < 0.2f)
        {
            currentTargetPoint = (currentTargetPoint == pointA) ? pointB : pointA;
        }

        // Move toward current patrol point
        Vector2 direction = (currentTargetPoint.position - transform.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);

        // Face movement direction
        Flip(direction.x);
    }

    private void DetectPlayer()
    {
        Collider2D playerCollider = Physics2D.OverlapCircle(transform.position, detectionRange, playerLayer);

        if (playerCollider != null)
        {
            PlayerStatsHandler handler = playerCollider.GetComponent<PlayerStatsHandler>();
            if (handler == null)
            {
                handler = playerCollider.GetComponentInParent<PlayerStatsHandler>();
            }

            if (handler != null)
            {
                lockedTarget = handler;
                Debug.Log($"{gameObject.name} detected player!");
            }
        }
    }

    private void ChaseTarget()
    {
        if (lockedTarget == null)
        {
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, lockedTarget.transform.position);

        // Lose target if too far away
        if (distanceToTarget > detectionRange * 1.5f)
        {
            Debug.Log($"{gameObject.name} lost player target");
            lockedTarget = null;
            return;
        }

        // Attack if in range
        if (distanceToTarget <= attackRange)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            enemy.AttackPlayer(lockedTarget);
        }
        else
        {
            // Chase the player
            Vector2 direction = (lockedTarget.transform.position - transform.position).normalized;
            rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);
            Flip(direction.x);
        }
    }

    private void Flip(float direction)
    {
        if (direction > 0)
        {
            transform.localScale = new Vector3(1, 1, 1);
        }
        else if (direction < 0)
        {
            transform.localScale = new Vector3(-1, 1, 1);
        }
    }

    // Public method for spawner to assign points after spawn
    public void SetPatrolPoints(Transform pointA, Transform pointB)
    {
        this.pointA = pointA;
        this.pointB = pointB;
        this.currentTargetPoint = pointA;
        Debug.Log($"{gameObject.name}: Patrol points assigned by spawner!");
    }

    /// <summary>
    /// NEW: Call this method when the enemy dies (from your Enemy.cs script or health system)
    /// Add this call to wherever your enemy death is currently handled
    /// </summary>
    public void OnDeath()
    {
        Debug.Log($"{gameObject.name} has been defeated!");

        // Drop coin if enabled
        if (dropsCoin)
        {
            DropCoin();
        }

        // Your existing death logic can go here:
        // - Play death animation
        // - Spawn death particles/effects
        // - Award XP to player
        // - etc.

        // Destroy the enemy GameObject
        Destroy(gameObject);
    }

    /// <summary>
    /// NEW: Spawns a coin at the enemy's position when defeated
    /// </summary>
    private void DropCoin()
    {
        if (coinPrefab == null)
        {
            Debug.LogError($"Cannot drop coin - no prefab assigned on {gameObject.name}");
            return;
        }

        // Calculate spawn position with offset
        Vector3 spawnPosition = transform.position + new Vector3(coinDropOffset.x, coinDropOffset.y, 0);

        // Instantiate the coin
        GameObject droppedCoin = Instantiate(coinPrefab, spawnPosition, Quaternion.identity);

        // Optional: Add a small upward force for visual effect (bouncy coin drop)
        Rigidbody2D coinRb = droppedCoin.GetComponent<Rigidbody2D>();
        if (coinRb != null)
        {
            // Random horizontal spread for variety
            float randomX = Random.Range(-1f, 1f);
            coinRb.AddForce(new Vector2(randomX, 3f), ForceMode2D.Impulse);
        }

        Debug.Log($"{gameObject.name} dropped a {enemyTeam} coin!");
    }

    // Show detection and attack ranges in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Draw patrol path if points are assigned
        if (pointA != null && pointB != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawWireSphere(pointA.position, 0.3f);
            Gizmos.DrawWireSphere(pointB.position, 0.3f);
        }

        // NEW: Show coin drop position
        if (dropsCoin)
        {
            Gizmos.color = Color.yellow;
            Vector3 coinDropPos = transform.position + new Vector3(coinDropOffset.x, coinDropOffset.y, 0);
            Gizmos.DrawWireSphere(coinDropPos, 0.2f);
        }
    }
}