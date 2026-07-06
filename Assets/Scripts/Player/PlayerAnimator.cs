using UnityEngine;
using Fusion;
using Game.PlayerAnimation.Core;

/// <summary>
/// Networked, state-driven player animation. Split into two concerns so remote players animate
/// smoothly (see docs/player-animation-guide.md):
///
/// - AUTHORITATIVE states that a client CANNOT infer from motion — Dead, the latched one-shots
///   (Attack/GroundPound/Shoot), Stunned, Dash — are computed on the state authority (and predicted
///   on the local input authority) and REPLICATED as a single networked <see cref="OverrideState"/>
///   enum. <see cref="AnimState.None"/> means "no override — derive locomotion locally".
///
/// - LOCOMOTION (Idle/Walk/Jump/Fall) is NOT networked. Every client derives it in <see cref="Render"/>
///   from THIS object's own rendered (interpolated) motion, via the pure
///   <see cref="LocomotionResolver"/>. That keeps a proxy's legs in lockstep with the smooth
///   interpolated position the viewer actually sees, at render frame-rate, instead of snapping to a
///   low-send-rate networked enum — which is what made remote animation choppy. Hysteresis + a
///   grounded dwell in the resolver remove Walk/Idle flicker at threshold speeds.
///
/// - The one locomotion input that can't be read from smoothed motion (jump apex / landing are
///   ambiguous) is <see cref="Grounded"/>, replicated as a single bool.
///
/// - The resolved pose is applied on EVERY client in Render() via a single Animator integer
///   parameter ("State"). No triggers, no per-state bools.
/// </summary>
public class PlayerAnimator : NetworkBehaviour
{
    [Header("Action clip durations (seconds)")]
    [Tooltip("How long a melee swing is held as the Attack state.")]
    [SerializeField] private float attackDuration = 0.3f;
    [Tooltip("How long a ground-pound is held as the GroundPound state.")]
    [SerializeField] private float groundPoundDuration = 0.3f;
    [Tooltip("How long a shot is held as the Shoot state.")]
    [SerializeField] private float shootDuration = 0.3f;

    [Header("Locomotion tuning (render-side, NOT networked)")]
    [Tooltip("|horizontal render speed| must EXCEED this (while grounded) to start the Walk pose.")]
    [SerializeField] private float walkEnterSpeed = 0.15f;
    [Tooltip("|horizontal render speed| must DROP BELOW this to return to Idle. Keep it below " +
             "walkEnterSpeed — the gap is the hysteresis band that stops Walk/Idle flicker at low speed.")]
    [SerializeField] private float walkStopSpeed = 0.05f;
    [Tooltip("Vertical render speed ABOVE which an airborne player shows Jump.")]
    [SerializeField] private float riseSpeed = 0.10f;
    [Tooltip("Vertical render speed BELOW which an airborne player shows Fall (negative).")]
    [SerializeField] private float fallSpeed = -0.10f;
    [Tooltip("Minimum seconds a grounded Walk/Idle change must persist before it is shown (dwell; " +
             "extra anti-flicker on top of the speed hysteresis).")]
    [SerializeField] private float minGroundedDwell = 0.06f;

    [Header("Animator")]
    [Tooltip("Animator for the VISIBLE body (Player.controller). Wired in the prefab " +
             "to the Sprite child, because the prefab has a second Animator " +
             "(Weapon.controller on SideAttackTransform) that GetComponentInChildren " +
             "would return first. Auto-resolved as a fallback if left unset.")]
    [SerializeField] private Animator anim;

    [Tooltip("Optional second track for the portal/weapon (Weapon.controller on the " +
             "SideAttackTransform child). Driven off the SAME resolved State int, so it " +
             "shows the weapon emerging during Attack/GroundPound/Shoot and stays hidden " +
             "otherwise. Leave unset to disable the weapon track.")]
    [SerializeField] private Animator weaponAnim;

    [Header("SFX (optional — null-safe)")]
    [Tooltip("Plays jump/land one-shots. Auto-resolved from this GameObject if unset.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip landClip;

    // Networked animation state.
    // OverrideState: the authoritative, non-locomotion pose, or AnimState.None when locomotion
    // should be derived locally. Grounded: the one locomotion input that can't be read from
    // smoothed render motion (jump apex / landing are ambiguous).
    [Networked] private AnimState OverrideState { get; set; }
    [Networked] private NetworkBool Grounded { get; set; }
    [Networked] private TickTimer ActionTimer { get; set; }
    [Networked] private AnimState ActionState { get; set; }

    // Component refs (cached in Spawned).
    private PlayerMovement movement;
    private PlayerStatsHandler stats;

    private static readonly int StateParam = Animator.StringToHash("State");

    // Render-side locomotion derivation (runs on every client).
    private LocomotionResolver locomotion;
    private Vector3 lastRenderPos;
    private bool renderMotionPrimed;

    // Render-side SFX edge detection (visual only, runs on every client).
    private AnimState lastRenderedState;
    private bool sfxPrimed;

    public override void Spawned()
    {
        movement = GetComponent<PlayerMovement>();
        stats = GetComponent<PlayerStatsHandler>();

        // Prefer the explicitly-wired body Animator. Fallback keeps things working if
        // a future prefab variant forgets to assign it.
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        // A [Networked] enum defaults to 0 (= Idle), which Render would read as an authoritative
        // Idle override before the first Simulate. Seed it to None so proxies derive locomotion
        // from the very first frame.
        if (HasStateAuthority) OverrideState = AnimState.None;

        // Seed render-motion tracking so the first frame doesn't produce a velocity spike.
        lastRenderPos = transform.position;
        renderMotionPrimed = true;
    }

    /// <summary>
    /// STATE AUTHORITY + LOCAL INPUT AUTHORITY: recompute the replicated, authoritative animation
    /// inputs once per tick — the non-locomotion override pose and the grounded flag. The host writes
    /// the authoritative values; the local player ALSO writes them predictively (Fusion reconciles),
    /// so its own actions animate on the input tick rather than a round-trip later. Proxies never get
    /// here (PlayerController only calls Simulate when GetInput succeeds) — they read the replicated
    /// values in Render(). Locomotion (Idle/Walk/Jump/Fall) is intentionally NOT decided here; every
    /// client derives it from rendered motion in Render(). Called by PlayerController.FixedUpdateNetwork
    /// AFTER movement/combat simulate (and also while dead, so Dead latches).
    /// </summary>
    public void Simulate()
    {
        if (!HasStateAuthority && !HasInputAuthority) return;
        OverrideState = ComputeOverride();
        Grounded = movement != null && movement.IsGrounded();
    }

    /// <summary>
    /// Priority-ordered resolution of the AUTHORITATIVE (non-locomotion) pose. Returns
    /// <see cref="AnimState.None"/> when nothing overrides locomotion, so Render() derives the
    /// Idle/Walk/Jump/Fall pose locally.
    /// </summary>
    private AnimState ComputeOverride()
    {
        // 1. Dead overrides everything.
        if (stats != null && stats.IsDead) return AnimState.Dead;
        // 2. A latched one-shot action (Attack/Shoot/GroundPound) holds for its duration.
        if (!ActionTimer.ExpiredOrNotRunning(Runner)) return ActionState;
        // 3. Stun, 4. Dash.
        if (movement != null && movement.IsStunned()) return AnimState.Stunned;
        if (movement != null && movement.IsDashing()) return AnimState.Dash;
        // 5. Otherwise locomotion is derived locally in Render().
        return AnimState.None;
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
        // State authority owns the authoritative latch; the local input authority also latches
        // predictively so its attack/shoot/ground-pound animation fires on the input tick instead
        // of after a server round-trip. Fusion reconciles the networked latch against the host.
        // Proxies never call this (combat's Trigger* run only during predicted/authoritative ticks).
        if (!HasStateAuthority && !HasInputAuthority) return;
        ActionState = action;
        ActionTimer = TickTimer.CreateFromSeconds(Runner, duration);
    }

    /// <summary>ALL CLIENTS: resolve the pose and apply it to the visible Animator(s).</summary>
    public override void Render()
    {
        AnimState resolved = ResolveRenderState();
        int state = (int)resolved;

        // Body track (locomotion + action poses).
        if (anim != null) anim.SetInteger(StateParam, state);
        // Weapon/portal track, driven off the same resolved State.
        if (weaponAnim != null) weaponAnim.SetInteger(StateParam, state);

        if (anim == null) return;
        HandleSfx(resolved);
    }

    /// <summary>
    /// Resolve the pose to show this frame: a replicated authoritative override if one is active,
    /// otherwise a locally-derived locomotion pose from this object's rendered (interpolated) motion.
    /// </summary>
    private AnimState ResolveRenderState()
    {
        // Track render motion EVERY frame (even under an override) so the velocity estimate is fresh
        // the instant we return to locomotion — no stale-position spike.
        Vector3 pos = transform.position;
        float dt = Time.deltaTime;
        Vector2 vel = Vector2.zero;
        if (renderMotionPrimed && dt > 1e-6f)
            vel = (Vector2)(pos - lastRenderPos) / dt;
        lastRenderPos = pos;
        renderMotionPrimed = true;

        // Authoritative, replicated states win and bypass local locomotion derivation.
        if (OverrideState != AnimState.None)
            return OverrideState;

        LocomotionTuning tuning = new LocomotionTuning
        {
            WalkEnterSpeed = walkEnterSpeed,
            WalkStopSpeed = walkStopSpeed,
            RiseSpeed = riseSpeed,
            FallSpeed = fallSpeed,
            MinGroundedDwellSeconds = minGroundedDwell
        };
        return locomotion.Step((bool)Grounded, vel.x, vel.y, dt, in tuning);
    }

    /// <summary>
    /// Jump/land SFX, edge-detected off the resolved pose so every client hears it. Fully null-safe:
    /// no clips assigned today, so nothing plays until a designer wires them up.
    /// </summary>
    private void HandleSfx(AnimState state)
    {
        if (!sfxPrimed)
        {
            lastRenderedState = state;
            sfxPrimed = true;
            return;
        }

        if (state == lastRenderedState) return;

        if (audioSource != null)
        {
            if (state == AnimState.Jump && jumpClip != null)
                audioSource.PlayOneShot(jumpClip);

            bool wasAirborne = lastRenderedState == AnimState.Jump || lastRenderedState == AnimState.Fall;
            bool nowGrounded = state == AnimState.Idle || state == AnimState.Walk;
            if (wasAirborne && nowGrounded && landClip != null)
                audioSource.PlayOneShot(landClip);
        }

        lastRenderedState = state;
    }
}
