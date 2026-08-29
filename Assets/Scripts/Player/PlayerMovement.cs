using UnityEngine;
using Fusion;
using Game.PlayerMovement.Core;

/// <summary>
/// Tick-based, networked player movement. Driven by PlayerController.FixedUpdateNetwork.
/// All gameplay timing uses TickTimer / networked counters so prediction + resimulation
/// reconcile correctly. NetworkRigidbody2D (on the prefab) syncs the body.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Jump Settings")]
    [SerializeField] private int coyoteTimeTicks = 6;
    [SerializeField] private int jumpBufferTicks = 6;
    [SerializeField] private float jumpCutMultiplier = 0.1f;

    [Header("UI References")]
    [SerializeField] private UnityEngine.UI.Image dashCooldownBar;

    // Component refs
    private Rigidbody2D rb;
    private PlayerStatModifiers mods;
    private PlayerCombat combat;
    private float baseGravity = 5f;

    // Networked simulation state
    [Networked] private int RemainingAirJumps { get; set; }
    [Networked] private int CoyoteCounter { get; set; }
    [Networked] private int JumpBufferCounter { get; set; }
    [Networked] private NetworkBool Jumping { get; set; }
    [Networked] private NetworkBool JumpCut { get; set; }
    [Networked] private NetworkBool Dashing { get; set; }
    [Networked] private NetworkBool FastFalling { get; set; }
    [Networked] private float DashDir { get; set; }
    [Networked] private NetworkBool FacingRight { get; set; }
    [Networked] private TickTimer DashDurationTimer { get; set; }
    [Networked] private TickTimer DashCooldownTimer { get; set; }
    [Networked] private TickTimer StunTimer { get; set; }

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        mods = GetComponent<PlayerStatModifiers>();
        combat = GetComponent<PlayerCombat>();
        if (rb != null) baseGravity = rb.gravityScale;

        if (HasStateAuthority)
        {
            FacingRight = transform.localScale.x >= 0f;
            RemainingAirJumps = mods != null ? mods.EffectiveMaxAirJumps : stats.maxAirJumps;
        }
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed, NetworkButtons released)
    {
        if (rb == null) return;

        bool grounded = groundCheck != null &&
                        Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        bool stunned = IsStunned();

        // Carrying-state must come from networked flag state (resim-safe), not the render-path
        // FlagCarrierMarker bool — see CTFGameManager.IsCarrying. Read once: both the Flag Runner
        // speed bonus and the dash gate below need it.
        bool carryingFlag = CTFGameManager.Instance != null &&
                            CTFGameManager.Instance.IsCarrying(Object.InputAuthority);

        // Resolve dash lifetime first (pure function of networked timers).
        if (Dashing && DashDurationTimer.ExpiredOrNotRunning(Runner))
            EndDash();

        // Gravity is a pure function of networked state + velocity (resimulation-safe).
        if (grounded) FastFalling = false;
        float gravityMult = MovementMath.SelectGravityMultiplier(
            grounded, rb.linearVelocity.y, stats.apexThreshold,
            Jumping, JumpCut, FastFalling,
            stats.apexGravityMult, stats.fallGravityMult);
        rb.gravityScale = Dashing ? 0f : baseGravity * gravityMult;

        // ---- Horizontal velocity ----
        if (Dashing)
        {
            rb.linearVelocity = new Vector2(DashDir * stats.dashSpeed, 0f);
        }
        else if (stunned)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else
        {
            // Flag Runner scales the walk target while carrying; accel/decel are expressed as
            // "reach walk speed in N ticks", so they scale with it and the feel stays consistent.
            float walkSpeed = mods != null ? mods.EffectiveWalkSpeed(carryingFlag) : stats.walkSpeed;
            var p = new MoveParams
            {
                WalkSpeed = walkSpeed,
                AccelPerTick = walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundAccelTicks : stats.airAccelTicks),
                DecelPerTick = walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundDecelTicks : stats.airDecelTicks),
                MomentumDecayPerTick =
                    (grounded ? stats.momentumDecayGround : stats.momentumDecayAir) * Runner.DeltaTime,
            };
            float newVx = MovementMath.StepHorizontalVelocity(rb.linearVelocity.x, input.Horizontal, p);
            rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
        }

        // ---- Facing ----
        if (input.Horizontal < 0) FacingRight = false;
        else if (input.Horizontal > 0) FacingRight = true;
        ApplyFacing();

        // ---- Coyote / air jumps ----
        if (grounded)
        {
            CoyoteCounter = coyoteTimeTicks;
            RemainingAirJumps = mods != null ? mods.EffectiveMaxAirJumps : stats.maxAirJumps;
            if (Jumping && rb.linearVelocity.y <= 0.01f) Jumping = false;
        }
        else if (CoyoteCounter > 0)
        {
            CoyoteCounter--;
        }

        // ---- Dash start / cancel ----
        if (!stunned && pressed.IsSet((int)PlayerButton.Dash) && !Dashing &&
            DashCooldownTimer.ExpiredOrNotRunning(Runner) &&
            (combat == null || !combat.IsSwingCommitted))
        {
            // Carrying blocks dash by default; Flag Runner T3 lifts that restriction.
            bool dashBlocked = carryingFlag && (mods == null || !mods.CanDashWhileCarryingFlag);
            if (!dashBlocked) StartDash();
        }
        if (released.IsSet((int)PlayerButton.Dash) && Dashing)
            EndDash();

        // ---- Jump buffer ----
        if (!stunned && pressed.IsSet((int)PlayerButton.Jump))
        {
            JumpBufferCounter = jumpBufferTicks;
            if (Dashing)
            {
                // Dash-jump (spec 1.3): cancel the dash and carry a fraction of dash speed
                // into the jump. DashDir is networked and still valid after EndDash.
                EndDash();
                rb.linearVelocity = new Vector2(
                    DashDir * stats.dashSpeed * stats.dashJumpCarryFactor, rb.linearVelocity.y);
            }
        }
        else if (JumpBufferCounter > 0)
        {
            JumpBufferCounter--;
        }

        if (!stunned && JumpBufferCounter > 0 && (CoyoteCounter > 0 || RemainingAirJumps > 0))
        {
            DoJump(grounded);
            JumpBufferCounter = 0;
        }

        // ---- Variable jump height (release cuts upward velocity) ----
        if (released.IsSet((int)PlayerButton.Jump) && rb.linearVelocity.y > 0f && Jumping && !JumpCut)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
            JumpCut = true;
        }

        // ---- Fast-fall (spec 1.5): down pressed at/past the apex snaps to fast-fall speed ----
        // Down+melee is a ground pound (PlayerCombat), which takes precedence over fast-fall (spec 1.5).
        if (!stunned && !Dashing && pressed.IsSet((int)PlayerButton.Down) &&
            !(input.VerticalAim < 0 && pressed.IsSet((int)PlayerButton.Melee)) &&
            MovementMath.ShouldStartFastFall(grounded, true, rb.linearVelocity.y,
                                             stats.apexThreshold, FastFalling))
        {
            FastFalling = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -stats.fastFallSpeed);
        }

        // ---- Terminal velocity ----
        rb.linearVelocity = new Vector2(
            rb.linearVelocity.x,
            MovementMath.ClampFallSpeed(rb.linearVelocity.y, stats.maxFallSpeed));
    }

    private void DoJump(bool grounded)
    {
        if (grounded || CoyoteCounter > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            CoyoteCounter = 0;
        }
        else if (RemainingAirJumps > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            if (mods == null || !mods.UnlimitedAirJumps) RemainingAirJumps--;
        }
        Jumping = true;
        JumpCut = false;
        // Animation is derived from networked state by PlayerAnimator (no trigger here).
    }

    private void StartDash()
    {
        Dashing = true;
        DashDir = FacingRight ? 1f : -1f;
        DashDurationTimer = TickTimer.CreateFromSeconds(Runner, mods != null ? mods.EffectiveDashTime : stats.dashTime);
        rb.linearVelocity = new Vector2(DashDir * stats.dashSpeed, 0f);
    }

    private void EndDash()
    {
        if (!Dashing) return;
        Dashing = false;
        DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, mods != null ? mods.EffectiveDashCooldown : stats.dashCooldown);
    }

    /// <summary>SERVER: stun the player for a duration (set by projectile hits).</summary>
    public void ApplyStun(float duration)
    {
        if (!HasStateAuthority) return;
        StunTimer = TickTimer.CreateFromSeconds(Runner, duration);
        if (Dashing) EndDash();
    }

    private void ApplyFacing()
    {
        Vector3 s = transform.localScale;
        float mag = Mathf.Abs(s.x);
        s.x = FacingRight ? mag : -mag;
        transform.localScale = s;
    }

    public override void Render()
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        if (rb == null) return;
        ApplyFacing();

        // Animation is no longer driven here — PlayerAnimator derives Walk/Jump/Fall/Dash
        // from networked state + velocity and applies it on every client.

        if (dashCooldownBar != null && HasInputAuthority)
            dashCooldownBar.fillAmount = GetDashCooldownPercent();
    }

    // ---- Public accessors (used by other scripts) ----
    public bool IsDashing() => Dashing;
    public bool IsStunned() => !StunTimer.ExpiredOrNotRunning(Runner);
    public bool IsFacingRight() => FacingRight;

    /// <summary>
    /// Single source of truth for grounded state, computed from the groundCheck
    /// OverlapCircle. Read by PlayerAnimator to pick Jump/Fall/Walk/Idle. Evaluated on
    /// state authority (PlayerAnimator.Simulate only runs there), matching where the
    /// internal Simulate() grounded check runs.
    /// </summary>
    public bool IsGrounded() =>
        groundCheck != null &&
        Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

    public float GetDashCooldownPercent()
    {
        float effectiveCd = mods != null ? mods.EffectiveDashCooldown : stats.dashCooldown;
        if (effectiveCd <= 0f) return 1f;
        float remaining = DashCooldownTimer.RemainingTime(Runner) ?? 0f;
        return 1f - Mathf.Clamp01(remaining / effectiveCd);
    }

    public float GetDashCooldownRemaining() => DashCooldownTimer.RemainingTime(Runner) ?? 0f;

    public bool CanDash() => !Dashing && DashCooldownTimer.ExpiredOrNotRunning(Runner);

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
