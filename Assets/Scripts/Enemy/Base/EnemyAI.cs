using UnityEngine;
using Fusion;

/// <summary>
/// Networked Enemy AI for Photon Fusion.
/// Handles patrol, detection, and combat with network synchronization.
/// Drops networked coins when defeated.
/// </summary>
public class NetworkedEnemyAI : NetworkBehaviour
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

    [Tooltip("Networked coin prefab to spawn on death (must have NetworkObject!)")]
    [SerializeField] private NetworkObject coinPrefab;

    [Tooltip("Number of coins to drop")]
    [SerializeField] private int coinDropCount = 1;

    [Tooltip("Scatter radius for multiple coin drops")]
    [SerializeField] private float scatterRadius = 0.5f;

    // Component references
    private Rigidbody2D rb;
    private NetworkedEnemy enemy;

    // AI state - synced across network
    private enum AIState { Patrolling, Chasing, Attacking }

    [Networked]
    private AIState CurrentState { get; set; }

    [Networked]
    private NetworkBool IsFacingRight { get; set; }

    private Transform currentTargetPoint;
    private PlayerStatsHandler lockedTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<NetworkedEnemy>();

        // Default patrol target
        if (pointA != null)
        {
            currentTargetPoint = pointA;
        }
    }

    public override void Spawned()
    {
        // Initialize state on server
        if (HasStateAuthority)
        {
            CurrentState = AIState.Patrolling;
            IsFacingRight = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Only server controls AI
        if (!HasStateAuthority) return;

        // Don't move if knocked back or telegraphing attack
        if (enemy != null && (enemy.IsKnockedBack() || enemy.IsTelegraphing()))
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // Update AI behavior based on state
        switch (CurrentState)
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
    /// Patrol between two points (SERVER ONLY)
    /// </summary>
    private void Patrol()
    {
        if (pointA == null || pointB == null)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            return;
        }

        // Check if reached patrol point (X only)
        float distanceX = Mathf.Abs(transform.position.x - currentTargetPoint.position.x);
        if (distanceX < 0.5f)
        {
            currentTargetPoint = currentTargetPoint == pointA ? pointB : pointA;
        }

        // Move toward target point
        float direction = Mathf.Sign(currentTargetPoint.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(direction * stats.moveSpeed, rb.linearVelocity.y);

        // Face the movement direction
        FlipSprite(direction);
    }

    /// <summary>
    /// Detect nearby players (SERVER ONLY)
    /// </summary>
    private void DetectPlayer()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayer);

        if (hits.Length > 0)
        {
            // Find closest player
            Transform closestPlayer = null;
            float closestDistance = Mathf.Infinity;

            foreach (Collider2D hit in hits)
            {
                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPlayer = hit.transform;
                }
            }

            if (closestPlayer != null)
            {
                lockedTarget = closestPlayer.GetComponent<PlayerStatsHandler>();
                CurrentState = AIState.Chasing;
            }
        }
    }

    /// <summary>
    /// Chase the locked target (SERVER ONLY)
    /// </summary>
    private void ChaseTarget()
    {
        if (lockedTarget == null)
        {
            CurrentState = AIState.Patrolling;
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, lockedTarget.transform.position);

        // Lost sight of target
        if (distanceToTarget > detectionRange * 1.5f)
        {
            lockedTarget = null;
            CurrentState = AIState.Patrolling;
            return;
        }

        // Close enough to attack
        if (distanceToTarget <= attackRange)
        {
            CurrentState = AIState.Attacking;
            return;
        }

        // Move toward target
        float direction = Mathf.Sign(lockedTarget.transform.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(direction * stats.moveSpeed, rb.linearVelocity.y);

        FlipSprite(direction);
    }

    /// <summary>
    /// Attack the locked target (SERVER ONLY)
    /// </summary>
    private void AttackTarget()
    {
        if (lockedTarget == null)
        {
            CurrentState = AIState.Patrolling;
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, lockedTarget.transform.position);

        // Return to chasing if target moves out of attack range
        if (distanceToTarget > attackRange * 1.2f)
        {
            CurrentState = AIState.Chasing;
            return;
        }

        // Stop movement while attacking
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        // Face the target
        float direction = lockedTarget.transform.position.x - transform.position.x;
        FlipSprite(direction);

        // Attempt attack
        if (enemy != null)
        {
            enemy.AttackPlayer(lockedTarget);
        }
    }

    /// <summary>
    /// Flip sprite to face movement/attack direction (SERVER ONLY)
    /// </summary>
    private void FlipSprite(float direction)
    {
        if (direction > 0 && !IsFacingRight)
        {
            IsFacingRight = true;
            transform.localScale = new Vector3(1, 1, 1);
        }
        else if (direction < 0 && IsFacingRight)
        {
            IsFacingRight = false;
            transform.localScale = new Vector3(-1, 1, 1);
        }
    }

    /// <summary>
    /// Set patrol points (called by spawner after instantiation)
    /// </summary>
    public void SetPatrolPoints(Transform pointA, Transform pointB)
    {
        this.pointA = pointA;
        this.pointB = pointB;
        this.currentTargetPoint = pointA;
        Debug.Log($"{gameObject.name}: Patrol points assigned!");
    }

    /// <summary>
    /// Called when enemy dies (from NetworkedEnemy.cs)
    /// SERVER ONLY - spawns networked coins
    /// </summary>
    public void OnDeath()
    {
        // Only server spawns coins
        if (!HasStateAuthority) return;

        Debug.Log($"[SERVER] {gameObject.name} has been defeated!");

        // Drop coins if enabled
        if (dropsCoin && coinPrefab != null)
        {
            DropCoins();
        }
    }

    /// <summary>
    /// Spawn networked coins at enemy's death location (SERVER ONLY)
    /// </summary>
    private void DropCoins()
    {
        if (coinPrefab == null)
        {
            Debug.LogWarning($"{gameObject.name} wants to drop coins but coinPrefab is not assigned!");
            return;
        }

        for (int i = 0; i < coinDropCount; i++)
        {
            // Calculate scatter position
            Vector2 scatterOffset = Random.insideUnitCircle * scatterRadius;
            Vector3 spawnPosition = transform.position + new Vector3(scatterOffset.x, scatterOffset.y, 0);

            // Spawn networked coin (only server can spawn)
            Runner.Spawn(
                coinPrefab,
                spawnPosition,
                Quaternion.identity
            );
        }

        Debug.Log($"[SERVER] {gameObject.name} dropped {coinDropCount} coin(s)!");
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