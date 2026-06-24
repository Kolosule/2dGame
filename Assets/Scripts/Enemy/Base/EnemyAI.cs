using UnityEngine;
using Fusion;

/// <summary>
/// Networked, zone-bound enemy AI. Wanders around a home anchor, engages the nearest
/// valid player who enters detection range, leashes back to home, and reuses the
/// telegraph/attack flow.
///
/// The state machine and Rigidbody2D are driven ONLY on the state authority via
/// <see cref="Tick"/> (from Enemy.FixedUpdateNetwork). Proxies interpolate position via
/// NetworkRigidbody2D and reproduce facing + telegraph from networked state via
/// <see cref="RenderVisuals"/> (from Enemy.Render).
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Attack Telegraph Settings")]
    [Tooltip("How long to show the attack warning before attacking (seconds).")]
    [SerializeField] private float attackTelegraphDuration = 0.5f;

    [Tooltip("Color to flash when telegraphing an attack.")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Tooltip("Freeze movement during the attack telegraph?")]
    [SerializeField] private bool freezeDuringTelegraph = true;

    [Header("Wander")]
    [Tooltip("Seconds to pause at a wander point before picking the next one.")]
    [SerializeField] private float wanderPauseDuration = 1f;

    private enum State { Guard, Chasing, Telegraphing, Attacking, Returning }

    // Config resolved at Initialize (authority only).
    private Vector2 home;
    private float moveSpeed;
    private float detectionRange;
    private float attackRange;
    private float leashRadius;
    private float wanderRadius;
    private bool initialized;

    private State currentState = State.Guard;
    private Transform currentPlayer;

    // Wander bookkeeping.
    private Vector2 wanderTarget;
    private TickTimer wanderPauseTimer;
    private bool hasWanderTarget;

    // Components.
    private Rigidbody2D rb;
    private Enemy enemyComponent;
    private SpriteRenderer spriteRenderer;

    // Telegraph.
    private TickTimer telegraphTimer;
    private Color originalColor;

    // Allocation-free detection buffer (authority-only, results consumed immediately).
    private static readonly System.Collections.Generic.List<Collider2D> DetectionResults =
        new System.Collections.Generic.List<Collider2D>(32);
    private ContactFilter2D playerFilter;
    private LayerMask playerLayer;

    [Header("Detection")]
    [Tooltip("Layer mask used to find players.")]
    [SerializeField] private LayerMask playerLayerMask;

    private const float ArriveThreshold = 0.5f;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyComponent = GetComponent<Enemy>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        playerLayer = playerLayerMask;
        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerLayer);

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: No SpriteRenderer - telegraph flash disabled.");
        }
    }

    /// <summary>
    /// Authority-only setup from Enemy.Spawned. Captures home + effective speed and the
    /// per-archetype ranges from stats.
    /// </summary>
    public void Initialize(Vector2 homeAnchor, float effectiveMoveSpeed, EnemyStats stats)
    {
        home = homeAnchor;
        moveSpeed = effectiveMoveSpeed;
        if (stats != null)
        {
            detectionRange = stats.detectionRange;
            attackRange = stats.attackRange;
            leashRadius = stats.leashRadius;
            wanderRadius = Mathf.Min(stats.wanderRadius, stats.leashRadius);
        }
        currentState = State.Guard;
        hasWanderTarget = false;
        initialized = true;
    }

    /// <summary>Authority-only AI step (from Enemy.FixedUpdateNetwork).</summary>
    public void Tick()
    {
        if (!initialized || rb == null || enemyComponent == null) return;

        if (enemyComponent.IsKnockedBack()) return;

        if (currentState == State.Telegraphing)
        {
            if (telegraphTimer.Expired(enemyComponent.Runner))
            {
                CompleteTelegraph();
            }
            return;
        }

        switch (currentState)
        {
            case State.Guard:
                Wander();
                AcquireTarget();
                break;

            case State.Chasing:
                ChasePlayer();
                break;

            case State.Attacking:
                Attack();
                break;

            case State.Returning:
                ReturnHome();
                AcquireTarget();
                break;
        }
    }

    /// <summary>Runs on every client (from Enemy.Render): facing + telegraph flash.</summary>
    public void RenderVisuals()
    {
        if (spriteRenderer == null || enemyComponent == null) return;

        spriteRenderer.flipX = enemyComponent.FacingLeft;

        if (enemyComponent.IsTelegraphing)
        {
            float t = Mathf.PingPong(Time.time * 8f, 1f);
            spriteRenderer.color = Color.Lerp(originalColor, telegraphColor, t);
        }
        else
        {
            spriteRenderer.color = originalColor;
        }
    }

    // ---- Guard / wander -------------------------------------------------

    private void Wander()
    {
        // Pausing between wander points.
        if (!wanderPauseTimer.ExpiredOrNotRunning(enemyComponent.Runner))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (!hasWanderTarget)
        {
            PickWanderTarget();
        }

        MoveToward(wanderTarget);

        if (Vector2.Distance(rb.position, wanderTarget) < ArriveThreshold)
        {
            hasWanderTarget = false;
            wanderPauseTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, wanderPauseDuration);
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    private void PickWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = home + offset;
        hasWanderTarget = true;
    }

    // ---- Targeting ------------------------------------------------------

    /// <summary>
    /// Find the nearest living, non-stealthed player within detectionRange AND within
    /// leashRadius of home. Enters Chasing if found.
    /// </summary>
    private void AcquireTarget()
    {
        int count = Physics2D.OverlapCircle(transform.position, detectionRange, playerFilter, DetectionResults);

        Transform best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            PlayerStatsHandler player = DetectionResults[i].GetComponent<PlayerStatsHandler>();
            if (player == null || player.IsPlayerDead()) continue;

            PlayerBuffs buffs = DetectionResults[i].GetComponent<PlayerBuffs>();
            if (buffs != null && buffs.IsStealthed) continue;

            Vector2 playerPos = player.transform.position;
            if ((playerPos - home).sqrMagnitude > leashRadius * leashRadius) continue;

            float sqr = (playerPos - (Vector2)transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = player.transform;
            }
        }

        if (best != null)
        {
            currentPlayer = best;
            currentState = State.Chasing;
        }
    }

    // ---- Chase / attack -------------------------------------------------

    private void ChasePlayer()
    {
        if (currentPlayer == null)
        {
            currentState = State.Returning;
            return;
        }

        Vector2 playerPos = currentPlayer.position;

        if (EnemyAILeash.ShouldDisengage(rb.position, home, playerPos, detectionRange, leashRadius)
            || IsTargetInvalid())
        {
            currentPlayer = null;
            currentState = State.Returning;
            return;
        }

        // Close enough to attack?
        if (Vector2.Distance(transform.position, playerPos) <= attackRange)
        {
            StartTelegraph();
            return;
        }

        Vector2 steer = EnemyAILeash.ClampToLeash(home, playerPos, leashRadius);
        MoveToward(steer);
    }

    private bool IsTargetInvalid()
    {
        if (currentPlayer == null) return true;
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (player == null || player.IsPlayerDead()) return true;
        PlayerBuffs buffs = currentPlayer.GetComponent<PlayerBuffs>();
        return buffs != null && buffs.IsStealthed;
    }

    private void StartTelegraph()
    {
        currentState = State.Telegraphing;
        telegraphTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, attackTelegraphDuration);
        enemyComponent.IsTelegraphing = true;

        if (freezeDuringTelegraph)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    private void CompleteTelegraph()
    {
        enemyComponent.IsTelegraphing = false;

        if (currentPlayer == null || IsTargetInvalid())
        {
            currentPlayer = null;
            currentState = State.Returning;
            return;
        }

        float distance = Vector2.Distance(transform.position, currentPlayer.position);
        currentState = distance <= attackRange ? State.Attacking : State.Chasing;
    }

    private void Attack()
    {
        if (currentPlayer == null)
        {
            currentState = State.Returning;
            return;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (player != null && enemyComponent != null)
        {
            enemyComponent.AttackPlayer(player);
        }

        currentState = State.Chasing;
    }

    // ---- Return ---------------------------------------------------------

    private void ReturnHome()
    {
        if (Vector2.Distance(rb.position, home) < ArriveThreshold)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            hasWanderTarget = false;
            currentState = State.Guard;
            return;
        }
        MoveToward(home);
    }

    // ---- Movement / facing ---------------------------------------------

    private void MoveToward(Vector2 target)
    {
        Vector2 direction = (target - rb.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * moveSpeed, rb.linearVelocity.y);
        SetFacing(direction.x);
    }

    private void SetFacing(float directionX)
    {
        if (enemyComponent == null) return;
        if (directionX > 0f) enemyComponent.FacingLeft = false;
        else if (directionX < 0f) enemyComponent.FacingLeft = true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying && initialized ? home : (Vector2)transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, detectionRange > 0f ? detectionRange : 10f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange > 0f ? attackRange : 1.5f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, leashRadius > 0f ? leashRadius : 12f);

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(center, wanderRadius > 0f ? wanderRadius : 5f);
    }
}
