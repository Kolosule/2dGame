using UnityEngine;

/// <summary>
/// AI controller for standard (non-networked) enemies.
/// Handles patrol, chase, and attack behaviors.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyStats stats;

    [Header("Patrol Points")]
    [Tooltip("First patrol point")]
    [SerializeField] private Transform pointA;

    [Tooltip("Second patrol point")]
    [SerializeField] private Transform pointB;

    [Header("Detection")]
    [Tooltip("How far the enemy can detect players")]
    [SerializeField] private float detectionRange = 5f;

    [Tooltip("Layer mask for detecting players")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Combat")]
    [Tooltip("How close enemy needs to be to attack")]
    [SerializeField] private float attackRange = 1.5f;

    // Component references
    private Rigidbody2D rb;
    private Enemy enemy;

    // AI state
    private Transform currentTargetPoint;
    private PlayerStatsHandler lockedTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();

        if (enemy == null)
        {
            Debug.LogError($"{gameObject.name} is missing Enemy component!");
        }

        // Default patrol target
        if (pointA != null)
        {
            currentTargetPoint = pointA;
        }
    }

    void FixedUpdate()
    {
        // Don't move if knocked back
        if (enemy != null && enemy.IsKnockedBack())
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // AI behavior
        if (lockedTarget == null)
        {
            Patrol();
            DetectPlayer();
        }
        else
        {
            ChaseTarget();
        }
    }

    /// <summary>
    /// Patrol between two points
    /// </summary>
    private void Patrol()
    {
        if (pointA == null || pointB == null)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // Check if reached patrol point (X only for 2D platformer)
        float distanceX = Mathf.Abs(transform.position.x - currentTargetPoint.position.x);
        if (distanceX < 0.5f)
        {
            // Switch to other patrol point
            currentTargetPoint = currentTargetPoint == pointA ? pointB : pointA;
        }

        // Move toward current patrol point
        Vector2 direction = (currentTargetPoint.position - transform.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);

        // Face movement direction
        Flip(direction.x);
    }

    /// <summary>
    /// Detect nearby players
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
                Debug.Log($"{gameObject.name} detected player: {handler.name}");
            }
        }
    }

    /// <summary>
    /// Chase the locked target
    /// </summary>
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
            // Stop moving
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

            // Attack the player
            if (enemy != null)
            {
                enemy.AttackPlayer(lockedTarget);
            }
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
    /// Flip sprite to face movement direction
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
    /// Public method for spawner to assign patrol points after spawn
    /// </summary>
    public void SetPatrolPoints(Transform pointA, Transform pointB)
    {
        this.pointA = pointA;
        this.pointB = pointB;
        this.currentTargetPoint = pointA;
        Debug.Log($"{gameObject.name}: Patrol points assigned by spawner!");
    }

    // Visualize detection and attack ranges in editor
    private void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}