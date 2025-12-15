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

    private Transform currentTargetPoint;
    private Rigidbody2D rb;
    private Enemy enemy;
    private PlayerStatsHandler lockedTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();
        currentTargetPoint = pointA;
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
            Patrol();
            DetectPlayer();
        }
    }

    private void Patrol()
    {
        if (pointA == null || pointB == null)
        {
            Debug.LogWarning($"{gameObject.name}: Patrol points not set!");
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

    // Show detection and attack ranges in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}