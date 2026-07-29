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
    private bool canFly;
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

    [Header("Detection")]
    [Tooltip("Layer mask used to find players.")]
    [SerializeField] private LayerMask playerLayerMask;

    [Header("Jump")]
    [Tooltip("Upward velocity applied when the enemy jumps.")]
    [SerializeField] private float jumpForce = 7f;

    [Tooltip("Layers treated as solid ground for the grounded check (e.g. Ground).")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("Layers that count as a hop-over obstacle ahead (e.g. Enemy + Ground/walls).")]
    [SerializeField] private LayerMask obstacleLayer;

    [Tooltip("Radius of the grounded overlap check at the feet.")]
    [SerializeField] private float groundCheckRadius = 0.15f;

    [Tooltip("Minimum seconds between jumps.")]
    [SerializeField] private float jumpCooldown = 0.6f;

    [Tooltip("How far ahead (beyond the body) to probe for a blocking obstacle.")]
    [SerializeField] private float obstacleProbeDistance = 0.6f;

    [Tooltip("While chasing, jump if the target is at least this much higher than the enemy.")]
    [SerializeField] private float chaseJumpHeight = 1.5f;

    [Header("Enemy Avoidance")]
    [Tooltip("Layers of other enemies to turn away from while wandering (e.g. Enemy).")]
    [SerializeField] private LayerMask enemyAvoidLayer;

    [Tooltip("How far ahead (beyond the body) to look for another enemy blocking the way.")]
    [SerializeField] private float enemyProbeDistance = 0.5f;

    [Tooltip("Minimum seconds between wander turn-arounds (prevents jitter).")]
    [SerializeField] private float turnAroundCooldown = 0.4f;

    // Jump / avoidance bookkeeping (authority only).
    private Collider2D bodyCollider;
    private TickTimer jumpCooldownTimer;
    private TickTimer turnCooldownTimer;
    private float lastProgressX;
    private int stuckCounter;

    private const float ArriveThreshold = 0.5f;
    private const int StuckSampleTicks = 12;      // ticks of no horizontal progress before a fallback hop
    private const float StuckMoveEpsilon = 0.05f; // min X movement that counts as progress

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyComponent = GetComponent<Enemy>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        bodyCollider = GetComponent<Collider2D>();

        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerLayerMask);

        if (playerLayerMask == 0)
        {
            Debug.LogWarning($"{gameObject.name}: EnemyAI playerLayerMask is unassigned - player detection will not work. Set it on the prefab.");
        }

        if (groundLayer == 0)
        {
            Debug.LogWarning($"{gameObject.name}: EnemyAI groundLayer is unassigned - jumping is disabled (enemy can never detect ground). Set it on the prefab.");
        }

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
            canFly = stats.canFly;
            attackTelegraphDuration = stats.attackTelegraphDuration;
        }
        currentState = State.Guard;
        hasWanderTarget = false;
        initialized = true;
    }

    /// <summary>Authority-only AI step (from Enemy.FixedUpdateNetwork).</summary>
    public void Tick()
    {
        // Hold enemies still during non-Live phases (countdown spawn-in, post-match freeze).
        if (MatchManager.Instance != null && !MatchManager.Instance.IsLive) return;

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
            StopMovement();
            return;
        }

        if (!hasWanderTarget)
        {
            PickWanderTarget();
        }

        // Stay solid but don't grind into another enemy: if one is directly ahead, turn
        // around to a new wander point on the other side (rate-limited to avoid jitter).
        float dirX = Mathf.Sign(wanderTarget.x - rb.position.x);
        if (turnCooldownTimer.ExpiredOrNotRunning(enemyComponent.Runner) && EnemyAhead(dirX))
        {
            FlipWanderTarget(dirX);
            turnCooldownTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, turnAroundCooldown);
        }

        MoveToward(wanderTarget);

        // Horizontal arrival: this is a gravity platformer and movement only drives X
        // velocity, so the enemy's Y is owned by physics. A 2D distance check against a
        // target with any Y delta would never satisfy, stranding the enemy at one X.
        if (Mathf.Abs(rb.position.x - wanderTarget.x) < ArriveThreshold)
        {
            hasWanderTarget = false;
            wanderPauseTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, wanderPauseDuration);
            StopMovement();
        }
    }

    private void PickWanderTarget()
    {
        // Wander horizontally within wanderRadius of home; keep the target at home's
        // height so the X-based arrival check can be reached under gravity.
        float offsetX = Random.Range(-wanderRadius, wanderRadius);
        wanderTarget = new Vector2(home.x + offsetX, home.y);
        hasWanderTarget = true;
    }

    /// <summary>True if another enemy is directly ahead in the given horizontal direction.</summary>
    private bool EnemyAhead(float dirX)
    {
        if (enemyAvoidLayer == 0 || Mathf.Approximately(dirX, 0f)) return false;
        float facing = Mathf.Sign(dirX);

        Vector2 center = rb.position;
        float extentX = 0.25f;
        if (bodyCollider != null)
        {
            center = bodyCollider.bounds.center;
            extentX = bodyCollider.bounds.extents.x;
        }
        Vector2 probeStart = new Vector2(center.x + facing * (extentX + 0.02f), center.y);
        RaycastHit2D hit = Physics2D.Raycast(probeStart, new Vector2(facing, 0f), enemyProbeDistance, enemyAvoidLayer);
        // Exclude ourselves via the rigidbody the hit collider belongs to.
        return hit.collider != null && hit.rigidbody != rb;
    }

    /// <summary>Retarget wander to the opposite side of the blocking enemy, within the zone.</summary>
    private void FlipWanderTarget(float blockedDir)
    {
        float away = -Mathf.Sign(blockedDir);
        float dist = Random.Range(wanderRadius * 0.5f, wanderRadius);
        float newX = Mathf.Clamp(rb.position.x + away * dist, home.x - wanderRadius, home.x + wanderRadius);
        wanderTarget = new Vector2(newX, home.y);
        hasWanderTarget = true;
    }

    // ---- Targeting ------------------------------------------------------

    /// <summary>
    /// Find the nearest living, non-stealthed player within detectionRange AND within
    /// leashRadius of home. Enters Chasing if found.
    /// </summary>
    private void AcquireTarget()
    {
        DetectionResults.Clear();
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

        // Jump toward a player perched above us (e.g. on a ledge). TryJump sets vertical
        // velocity; MoveToward below preserves it while driving horizontal pursuit.
        if (playerPos.y - rb.position.y > chaseJumpHeight)
        {
            TryJump();
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
            StopMovement();
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

        StopMovement();

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
        // Horizontal arrival (same reason as Wander: Y is gravity-owned).
        if (Mathf.Abs(rb.position.x - home.x) < ArriveThreshold)
        {
            StopMovement();
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
        rb.linearVelocity = EnemyAIMovement.SteeringVelocity(
            rb.position, target, moveSpeed, rb.linearVelocity.y, canFly);
        SetFacing(direction.x);

        // Flying enemies pursue freely in 2D; ground/jump logic doesn't apply to them.
        if (canFly) return;
        HandleMovementJump(direction.x);
    }

    /// <summary>
    /// Halts movement. Grounded enemies keep their gravity-owned Y velocity; flyers stop on
    /// both axes (gravityScale 0 won't bleed off residual Y).
    /// </summary>
    private void StopMovement()
    {
        rb.linearVelocity = EnemyAIMovement.StopVelocity(rb.linearVelocity.y, canFly);
    }

    // ---- Jump -----------------------------------------------------------

    /// <summary>
    /// While actively moving horizontally, hop over a blocking obstacle (primarily other
    /// enemies) detected by a short forward probe, with a stuck-detection fallback for
    /// anything the probe misses.
    /// </summary>
    private void HandleMovementJump(float dirX)
    {
        if (Mathf.Approximately(dirX, 0f)) return;
        float facing = Mathf.Sign(dirX);

        // Forward obstacle probe (primary). Origin sits just outside the body's front edge
        // so the ray never starts inside our own collider.
        if (obstacleLayer != 0)
        {
            Vector2 origin = rb.position;
            float extentX = 0.25f;
            if (bodyCollider != null)
            {
                origin = new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.center.y);
                extentX = bodyCollider.bounds.extents.x;
            }
            Vector2 probeStart = new Vector2(origin.x + facing * (extentX + 0.02f), origin.y);
            RaycastHit2D hit = Physics2D.Raycast(probeStart, new Vector2(facing, 0f), obstacleProbeDistance, obstacleLayer);
            if (hit.collider != null && hit.collider.gameObject != gameObject)
            {
                if (TryJump()) return;
            }
        }

        // Stuck fallback: no meaningful horizontal progress for a while → hop.
        if (Mathf.Abs(rb.position.x - lastProgressX) > StuckMoveEpsilon)
        {
            lastProgressX = rb.position.x;
            stuckCounter = 0;
        }
        else
        {
            stuckCounter++;
            if (stuckCounter >= StuckSampleTicks)
            {
                TryJump();
            }
        }
    }

    /// <summary>Jump if grounded and off cooldown. Returns true if a jump was performed.</summary>
    private bool TryJump()
    {
        if (!IsGrounded()) return false;
        if (!jumpCooldownTimer.ExpiredOrNotRunning(enemyComponent.Runner)) return false;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        jumpCooldownTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, jumpCooldown);
        stuckCounter = 0;
        lastProgressX = rb.position.x;
        return true;
    }

    private bool IsGrounded()
    {
        if (groundLayer == 0) return false;
        return Physics2D.OverlapCircle(GetFeetPosition(), groundCheckRadius, groundLayer);
    }

    private Vector2 GetFeetPosition()
    {
        if (bodyCollider != null)
        {
            return new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y);
        }
        return rb.position;
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

        // Ground check (feet) — only meaningful with a collider present.
        Collider2D col = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        if (col != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector2(col.bounds.center.x, col.bounds.min.y), groundCheckRadius);
        }
    }
}
