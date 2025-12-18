using UnityEngine;

/// <summary>
/// AI controller for enemy patrol, detection, and combat behavior.
/// Improved version with better state management and telegraph support.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyStats stats;
    [SerializeField] private LayerMask playerLayer;

    [Header("Patrol Points")]
    public Transform pointA;
    public Transform pointB;

    [Header("Detection")]
    [Tooltip("Distance at which enemy can detect players")]
    [SerializeField] private float detectionRange = 5f;

    [Tooltip("Distance at which enemy attacks players")]
    [SerializeField] private float attackRange = 1.5f;

    [Header("Coin Dropping")]
    [Tooltip("Does this enemy drop a coin when defeated?")]
    [SerializeField] private bool dropsCoin = true;

    [Tooltip("Coin prefab to spawn on death")]
    [SerializeField] private GameObject coinPrefab;

    // Component references
    private Rigidbody2D rb;
    private Enemy enemy;

    // AI state
    private enum AIState { Patrolling, Chasing, Attacking }
    private AIState currentState = AIState.Patrolling;

    private Transform currentTargetPoint;
    private PlayerStatsHandler lockedTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();

        // Default patrol target
        if (pointA != null)
        {
            currentTargetPoint = pointA;
        }
    }

    void FixedUpdate()
    {
        // Don't move if knocked back or telegraphing attack
        if (enemy != null && (enemy.IsKnockedBack() || enemy.IsTelegraphing()))
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // Update AI behavior based on state
        switch (currentState)
        {
            case AIState.Patrolling:
                Patrol();
                DetectPlayer();
                break;

            case AIState.Chasing:
                ChaseTarget();
                break;

            case AIState.Attacking:
                AttackTarget();
                break;
        }
    }

    /// <summary>
    /// Patrol between two points.
    /// </summary>
    private void Patrol()
    {
        if (pointA == null || pointB == null)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // Check if reached patrol point
        float distance = Vector2.Distance(transform.position, currentTargetPoint.position);
        if (distance < 0.5f)
        {
            currentTargetPoint = currentTargetPoint == pointA ? pointB : pointA;
        }

        // Move toward current patrol point
        Vector2 direction = (currentTargetPoint.position - transform.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);

        // Face movement direction
        Flip(direction.x);
    }

    /// <summary>
    /// Scan for nearby players.
    /// </summary>
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
                currentState = AIState.Chasing;
                Debug.Log($"{gameObject.name} detected player!");
            }
        }
    }

    /// <summary>
    /// Chase the locked target player.
    /// </summary>
    private void ChaseTarget()
    {
        if (lockedTarget == null)
        {
            currentState = AIState.Patrolling;
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, lockedTarget.transform.position);

        // Lose target if too far away
        if (distanceToTarget > detectionRange * 1.5f)
        {
            Debug.Log($"{gameObject.name} lost player target");
            lockedTarget = null;
            currentState = AIState.Patrolling;
            return;
        }

        // Switch to attacking if in range
        if (distanceToTarget <= attackRange)
        {
            currentState = AIState.Attacking;
        }
        else
        {
            // Chase the player
            Vector2 direction = (lockedTarget.transform.position - transform.position).normalized;
            rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);
            Flip(direction.x);
        }
    }

    /// <summary>
    /// Attack the target when in range.
    /// </summary>
    private void AttackTarget()
    {
        if (lockedTarget == null)
        {
            currentState = AIState.Patrolling;
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, lockedTarget.transform.position);

        // Return to chasing if target moves out of attack range
        if (distanceToTarget > attackRange * 1.2f)
        {
            currentState = AIState.Chasing;
            return;
        }

        // Stop movement while attacking
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        // Face the target
        float direction = lockedTarget.transform.position.x - transform.position.x;
        Flip(direction);

        // Attempt attack
        if (enemy != null)
        {
            enemy.AttackPlayer(lockedTarget);
        }
    }

    /// <summary>
    /// Flip sprite to face movement/attack direction.
    /// </summary>
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

    /// <summary>
    /// Set patrol points (called by spawner after instantiation).
    /// </summary>
    public void SetPatrolPoints(Transform pointA, Transform pointB)
    {
        this.pointA = pointA;
        this.pointB = pointB;
        this.currentTargetPoint = pointA;
        Debug.Log($"{gameObject.name}: Patrol points assigned by spawner!");
    }

    /// <summary>
    /// Called when enemy dies (from Enemy.cs).
    /// </summary>
    public void OnDeath()
    {
        Debug.Log($"{gameObject.name} has been defeated!");

        // Drop coin if enabled
        if (dropsCoin && coinPrefab != null)
        {
            DropCoin();
        }
    }

    /// <summary>
    /// Spawn a coin at enemy's death location.
    /// </summary>
    private void DropCoin()
    {
        if (coinPrefab != null)
        {
            Instantiate(coinPrefab, transform.position, Quaternion.identity);
            Debug.Log($"{gameObject.name} dropped a coin!");
        }
        else
        {
            Debug.LogWarning($"{gameObject.name} wants to drop coin but coinPrefab is not assigned!");
        }
    }

    // Visual debugging
    private void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Patrol points
        if (pointA != null && pointB != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawWireSphere(pointA.position, 0.3f);
            Gizmos.DrawWireSphere(pointB.position, 0.3f);
        }
    }
}