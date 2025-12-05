using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [SerializeField] private EnemyStats stats;

    // 👇 These must be public or [SerializeField] to show in Inspector
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [SerializeField] private float detectionRange = 5f;
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
        if (pointA == null || pointB == null) return; // safety check

        float distance = Vector2.Distance(transform.position, currentTargetPoint.position);

        if (distance < 0.2f)
        {
            currentTargetPoint = currentTargetPoint == pointA ? pointB : pointA;
        }

        Vector2 direction = (currentTargetPoint.position - transform.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);
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
                Debug.Log($"Enemy locked onto {handler.name}");
            }
            else
            {
                Debug.LogWarning($"Detected {playerCollider.name} but no PlayerStatsHandler found!");
            }
        }
    }

    private void ChaseTarget()
    {
        if (lockedTarget == null) return;

        float distance = Vector2.Distance(transform.position, lockedTarget.transform.position);

        if (distance > detectionRange * 1.5f)
        {
            lockedTarget = null;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 direction = (lockedTarget.transform.position - transform.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * stats.moveSpeed, rb.linearVelocity.y);

        if (distance < 1.5f)
        {
            enemy.AttackPlayer(lockedTarget);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}