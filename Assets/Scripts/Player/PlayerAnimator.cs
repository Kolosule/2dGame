using UnityEngine;
using Fusion;

/// <summary>
/// Networked, state-driven player animation. Replaces the old input-triggered
/// approach (which only ran for input/state authority, so remote players never
/// saw jumps/attacks/shots).
///
/// Design:
/// - The animator is described by a single networked <see cref="AnimState"/> enum
///   (mirrors the movement/combat pattern of syncing state, not input).
/// - State is COMPUTED on state authority once per tick (<see cref="Simulate"/>),
///   derived from networked movement/combat/stats + the Rigidbody2D velocity.
/// - State is APPLIED on EVERY client in <see cref="Render"/> via a single
///   Animator integer parameter ("State"). No triggers, no per-state bools.
/// - One-shot actions (Attack/Shoot/GroundPound) are LATCHED for a clip duration
///   using a networked <see cref="TickTimer"/> so they replicate without triggers.
/// </summary>
public class PlayerAnimator : NetworkBehaviour
{
    /// <summary>
    /// One value per Animator state. Order MUST match the integer values wired into
    /// Player.controller's "State Equals n" transitions.
    /// NOTE: GroundPound (6) and Shoot (7) currently reuse AttackGround.anim as
    /// PLACEHOLDER art — there is no dedicated clip for either yet (TODO).
    /// </summary>
    public enum AnimState : byte
    {
        Idle = 0,
        Walk = 1,
        Jump = 2,
        Fall = 3,
        Dash = 4,
        Attack = 5,
        GroundPound = 6, // placeholder clip
        Shoot = 7,       // placeholder clip
        Stunned = 8,
        Dead = 9
    }

    [Header("Action clip durations (seconds)")]
    [Tooltip("How long a melee swing is held as the Attack state.")]
    [SerializeField] private float attackDuration = 0.3f;
    [Tooltip("How long a ground-pound is held as the GroundPound state.")]
    [SerializeField] private float groundPoundDuration = 0.3f;
    [Tooltip("How long a shot is held as the Shoot state.")]
    [SerializeField] private float shootDuration = 0.3f;

    [Header("Animator")]
    [Tooltip("Animator for the VISIBLE body (Player.controller). Wired in the prefab " +
             "to the Sprite child, because the prefab has a second Animator " +
             "(Weapon.controller on SideAttackTransform) that GetComponentInChildren " +
             "would return first. Auto-resolved as a fallback if left unset.")]
    [SerializeField] private Animator anim;

    [Tooltip("Optional second track for the portal/weapon (Weapon.controller on the " +
             "SideAttackTransform child). Driven off the SAME networked State int, so it " +
             "shows the weapon emerging during Attack/GroundPound/Shoot and stays hidden " +
             "otherwise. Leave unset to disable the weapon track.")]
    [SerializeField] private Animator weaponAnim;

    [Header("SFX (optional — null-safe)")]
    [Tooltip("Plays jump/land one-shots. Auto-resolved from this GameObject if unset.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip landClip;

    // Networked animation state.
    [Networked] public AnimState State { get; private set; }
    [Networked] private TickTimer ActionTimer { get; set; }
    [Networked] private AnimState ActionState { get; set; }

    // Component refs (cached in Spawned).
    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerStatsHandler stats;
    private Rigidbody2D rb;

    private static readonly int StateParam = Animator.StringToHash("State");

    // Render-side SFX edge detection (visual only, runs on every client).
    private AnimState lastRenderedState;
    private bool sfxPrimed;

    public override void Spawned()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        stats = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();

        // Prefer the explicitly-wired body Animator. Fallback keeps things working if
        // a future prefab variant forgets to assign it.
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    /// <summary>
    /// STATE AUTHORITY: recompute the networked animation state once per tick.
    /// Called by PlayerController.FixedUpdateNetwork AFTER movement/combat simulate
    /// (and also while dead, so the Dead state latches).
    /// </summary>
    public void Simulate()
    {
        if (!HasStateAuthority) return;
        State = ComputeState();
    }

    /// <summary>Priority-ordered state resolution (first match wins).</summary>
    private AnimState ComputeState()
    {
        // 1. Dead overrides everything.
        if (stats != null && stats.IsDead) return AnimState.Dead;

        // 2. A latched one-shot action (Attack/Shoot/GroundPound) holds for its duration.
        if (!ActionTimer.ExpiredOrNotRunning(Runner)) return ActionState;

        // 3. Stun, 4. Dash.
        if (movement != null && movement.IsStunned()) return AnimState.Stunned;
        if (movement != null && movement.IsDashing()) return AnimState.Dash;

        // 5/6. Airborne: rising vs falling.
        bool grounded = movement != null && movement.IsGrounded();
        float vy = rb != null ? rb.linearVelocity.y : 0f;
        if (!grounded && vy > 0.1f) return AnimState.Jump;
        if (!grounded) return AnimState.Fall;

        // 7. Walking, 8. Idle.
        float vx = rb != null ? rb.linearVelocity.x : 0f;
        if (Mathf.Abs(vx) > 0.1f) return AnimState.Walk;
        return AnimState.Idle;
    }

    // ---- Action latches (called from PlayerCombat instead of SetTrigger) ----

    /// <summary>STATE AUTHORITY: latch a melee-attack animation for its clip duration.</summary>
    public void TriggerAttack() => LatchAction(AnimState.Attack, attackDuration);

    /// <summary>STATE AUTHORITY: latch a ground-pound animation for its clip duration.</summary>
    public void TriggerGroundPound() => LatchAction(AnimState.GroundPound, groundPoundDuration);

    /// <summary>STATE AUTHORITY: latch a shoot animation for its clip duration.</summary>
    public void TriggerShoot() => LatchAction(AnimState.Shoot, shootDuration);

    private void LatchAction(AnimState action, float duration)
    {
        // Only the authority owns the networked latch; it replicates to all clients.
        if (!HasStateAuthority) return;
        ActionState = action;
        ActionTimer = TickTimer.CreateFromSeconds(Runner, duration);
    }

    /// <summary>ALL CLIENTS: apply the replicated state to the visible Animator(s).</summary>
    public override void Render()
    {
        int state = (int)State;
        // Body track (locomotion + action poses).
        if (anim != null) anim.SetInteger(StateParam, state);
        // Weapon/portal track, driven off the same State so it replicates for free.
        if (weaponAnim != null) weaponAnim.SetInteger(StateParam, state);

        if (anim == null) return;
        HandleSfx();
    }

    /// <summary>
    /// Re-homed jump/land SFX (the old JumpStateBehaviour/PlaySoundBehaviour read
    /// removed params and were never attached to any state). Edge-detected off the
    /// replicated State so every client hears it. Fully null-safe: no clips assigned
    /// today, so nothing plays until a designer wires them up.
    /// </summary>
    private void HandleSfx()
    {
        if (!sfxPrimed)
        {
            lastRenderedState = State;
            sfxPrimed = true;
            return;
        }

        if (State == lastRenderedState) return;

        if (audioSource != null)
        {
            if (State == AnimState.Jump && jumpClip != null)
                audioSource.PlayOneShot(jumpClip);

            bool wasAirborne = lastRenderedState == AnimState.Jump || lastRenderedState == AnimState.Fall;
            bool nowGrounded = State == AnimState.Idle || State == AnimState.Walk;
            if (wasAirborne && nowGrounded && landClip != null)
                audioSource.PlayOneShot(landClip);
        }

        lastRenderedState = State;
    }
}
