using UnityEngine;

/// <summary>
/// FULLY FIXED VERSION - Attack telegraphing with proper range checking!
/// Now the enemy only attacks if the player is still in range after the telegraph completes.
/// Controls enemy AI behavior: patrolling, chasing, and attacking players.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Enemy Configuration")]
    [SerializeField] private EnemyStats stats;

    [Header("Patrol Points")]
    [Tooltip("First patrol point")]
    [SerializeField] private Transform pointA;
    [Tooltip("Second patrol point")]
    [SerializeField] private Transform pointB;

    [Header("Detection Settings")]
    [Tooltip("How far the enemy can detect players")]
    [SerializeField] private float detectionRange = 10f;

    [Tooltip("Layer mask for detecting players")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Attack Settings")]
    [Tooltip("How close enemy needs to be to attack")]
    [SerializeField] private float attackRange = 1.5f;

    [Header("Attack Telegraph Settings")]
    [Tooltip("How long to show attack warning before attacking (in seconds)")]
    [SerializeField] private float attackTelegraphDuration = 0.5f;

    [Tooltip("Color to flash when telegraphing attack")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.3f, 0.3f, 1f); // Red tint

    [Tooltip("Should the enemy freeze during attack telegraph?")]
    [SerializeField] private bool freezeDuringTelegraph = true;

    // AI State
    private enum State
    {
        Patrolling,
        Chasing,
        Telegraphing,  // Showing attack warning
        Attacking
    }

    private State currentState = State.Patrolling;
    private Transform currentTarget; // Current patrol point
    private Transform currentPlayer; // Player being chased/attacked
    private float moveSpeed;

    // Components
    private Rigidbody2D rb;
    private Enemy enemyComponent;
    private SpriteRenderer spriteRenderer;

    // Telegraph tracking
    private bool isTelegraphing = false;
    private float telegraphStartTime;
    private Color originalColor;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyComponent = GetComponent<Enemy>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (stats != null)
        {
            moveSpeed = stats.moveSpeed;
        }

        // Start patrolling at point A
        if (pointA != null)
        {
            currentTarget = pointA;
        }

        // Store original sprite color for telegraph effect
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: No SpriteRenderer found - telegraph effect won't work!");
        }

        Debug.Log($"{stats.enemyName} AI started. State: {currentState}");
    }

    private void Update()
    {
        // Don't move if being knocked back
        if (enemyComponent != null && enemyComponent.IsKnockedBack())
        {
            return;
        }

        // Handle telegraph effect
        if (isTelegraphing)
        {
            UpdateTelegraphEffect();

            // Check if telegraph duration is complete
            if (Time.time >= telegraphStartTime + attackTelegraphDuration)
            {
                CompleteTelegraph();
            }
            return; // Don't do anything else while telegraphing
        }

        // State machine
        switch (currentState)
        {
            case State.Patrolling:
                Patrol();
                CheckForPlayers();
                break;

            case State.Chasing:
                ChasePlayer();
                CheckAttackRange();
                CheckIfPlayerEscaped();
                break;

            case State.Attacking:
                Attack();
                break;
        }
    }

    /// <summary>
    /// Patrol between two points
    /// </summary>
    private void Patrol()
    {
        if (currentTarget == null)
        {
            Debug.LogWarning($"{stats.enemyName}: No patrol target!");
            return;
        }

        // Move toward current target
        Vector2 direction = ((Vector2)currentTarget.position - rb.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * moveSpeed, rb.linearVelocity.y);

        // Flip sprite to face movement direction
        FlipSprite(direction.x);

        // Check if reached target
        float distance = Vector2.Distance(rb.position, currentTarget.position);
        if (distance < 0.5f)
        {
            // Switch to other patrol point
            currentTarget = (currentTarget == pointA) ? pointB : pointA;
        }
    }

    /// <summary>
    /// Check if any players are in detection range
    /// </summary>
    private void CheckForPlayers()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayer);

        foreach (Collider2D hit in hits)
        {
            PlayerStatsHandler player = hit.GetComponent<PlayerStatsHandler>();
            if (player != null && !player.IsPlayerDead())
            {
                // Found a living player - start chasing
                currentPlayer = player.transform;
                currentState = State.Chasing;
                Debug.Log($"{stats.enemyName} detected {player.name}!");
                return;
            }
        }
    }

    /// <summary>
    /// Chase the current player
    /// </summary>
    private void ChasePlayer()
    {
        if (currentPlayer == null)
        {
            // Lost player - return to patrol
            currentState = State.Patrolling;
            return;
        }

        // Move toward player
        Vector2 direction = ((Vector2)currentPlayer.position - rb.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * moveSpeed, rb.linearVelocity.y);

        // Flip sprite to face player
        FlipSprite(direction.x);
    }

    /// <summary>
    /// Check if player is in attack range
    /// </summary>
    private void CheckAttackRange()
    {
        if (currentPlayer == null) return;

        float distance = Vector2.Distance(transform.position, currentPlayer.position);

        if (distance <= attackRange)
        {
            // Start attack telegraph
            StartTelegraph();
        }
    }

    /// <summary>
    /// Starts the attack telegraph (visual warning)
    /// </summary>
    private void StartTelegraph()
    {
        isTelegraphing = true;
        telegraphStartTime = Time.time;
        currentState = State.Telegraphing;

        // Stop movement if configured to freeze
        if (freezeDuringTelegraph)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }

        Debug.Log($"{stats.enemyName} is telegraphing attack!");
    }

    /// <summary>
    /// Updates the visual telegraph effect (color flashing)
    /// </summary>
    private void UpdateTelegraphEffect()
    {
        if (spriteRenderer == null) return;

        // Flash between telegraph color and original color
        float flashSpeed = 8f; // How fast to flash
        float t = Mathf.PingPong(Time.time * flashSpeed, 1f);
        spriteRenderer.color = Color.Lerp(originalColor, telegraphColor, t);
    }

    /// <summary>
    /// FIXED - Completes the telegraph and checks if player is still in range before attacking
    /// </summary>
    private void CompleteTelegraph()
    {
        isTelegraphing = false;

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        // IMPORTANT FIX: Check if player is STILL in attack range
        if (currentPlayer != null)
        {
            float distance = Vector2.Distance(transform.position, currentPlayer.position);

            // Only attack if player is still in range
            if (distance <= attackRange)
            {
                // Player didn't dodge - perform attack
                currentState = State.Attacking;
                Debug.Log($"{stats.enemyName} telegraph complete - player is still in range, attacking!");
            }
            else
            {
                // Player successfully dodged - return to chasing
                currentState = State.Chasing;
                Debug.Log($"{stats.enemyName} telegraph complete - player dodged! Distance: {distance:F2}");
            }
        }
        else
        {
            // Player is gone - return to patrol
            currentState = State.Patrolling;
            Debug.Log($"{stats.enemyName} telegraph complete - player is gone!");
        }
    }

    /// <summary>
    /// Attack the player
    /// </summary>
    private void Attack()
    {
        if (currentPlayer == null)
        {
            currentState = State.Patrolling;
            return;
        }

        // Stop movement
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);

        // Try to attack
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (player != null && enemyComponent != null)
        {
            enemyComponent.AttackPlayer(player);
        }

        // Return to chasing state after attack
        currentState = State.Chasing;
    }

    /// <summary>
    /// Check if player escaped detection range
    /// </summary>
    private void CheckIfPlayerEscaped()
    {
        if (currentPlayer == null) return;

        float distance = Vector2.Distance(transform.position, currentPlayer.position);

        // If player escaped or died, return to patrol
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (distance > detectionRange || (player != null && player.IsPlayerDead()))
        {
            currentPlayer = null;
            currentState = State.Patrolling;
            Debug.Log($"{stats.enemyName} lost sight of player");
        }
    }

    /// <summary>
    /// Flip sprite to face movement direction
    /// </summary>
    private void FlipSprite(float directionX)
    {
        if (spriteRenderer == null) return;

        if (directionX > 0)
        {
            spriteRenderer.flipX = false;
        }
        else if (directionX < 0)
        {
            spriteRenderer.flipX = true;
        }
    }

    /// <summary>
    /// Set patrol points (called by spawner)
    /// </summary>
    public void SetPatrolPoints(Transform a, Transform b)
    {
        pointA = a;
        pointB = b;

        if (pointA != null)
        {
            currentTarget = pointA;
        }

        Debug.Log($"{stats.enemyName} patrol points set: {pointA.name} and {pointB.name}");
    }

    // Show detection and attack ranges in editor
    private void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Telegraph color indicator
        Gizmos.color = telegraphColor;
        Gizmos.DrawWireSphere(transform.position, attackRange * 0.8f);

        // Draw patrol path if points are assigned
        if (pointA != null && pointB != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawWireSphere(pointA.position, 0.3f);
            Gizmos.DrawWireSphere(pointB.position, 0.3f);
        }
    }
}