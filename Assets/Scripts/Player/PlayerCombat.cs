using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Fusion;
using Game.Audio.Core;
using Game.Combat.Core;

/// <summary>
/// Tick-based, networked combat. Driven by PlayerController.FixedUpdateNetwork.
/// Melee detection/damage and projectile spawning run under StateAuthority only.
/// Cooldowns are TickTimers so they predict and reconcile correctly.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    private const int InitialHitCollectionCapacity = 32;
    private const float LagCompensationQueryHalfDepth = 0.25f;

    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Attack Settings")]
    [SerializeField] private LayerMask attackableLayer;
    // Melee damage, knockback strength, and cooldown are driven by PlayerStats
    // (attackDamage / attackForce / attackCooldown) so designers tune them in one place.
    // Upward knockback has no PlayerStats equivalent and stays a per-prefab tuning value.
    [SerializeField] private float knockbackUpward = 5f;

    [Header("Attack Points")]
    [SerializeField] private Transform sideAttackPoint;
    [SerializeField] private Transform upAttackPoint;
    [SerializeField] private Transform downAttackPoint;

    [Header("Attack Areas")]
    [SerializeField] private Vector2 sideAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 upAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 downAttackArea = new Vector2(1f, 1f);

    [Header("Down Attack")]
    [Tooltip("Minimum downward speed applied to a target hit by an airborne down attack.")]
    [FormerlySerializedAs("groundPoundForce")]
    [SerializeField, Min(0.01f)] private float downAttackSpikeSpeed = 20f;

    [Header("Projectile Settings")]
    [SerializeField] private NetworkObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private int projectileDamage = 15;
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float projectileScale = 1f;
    [SerializeField] private float projectileCooldown = 0.5f;

    [Header("Shoot Prediction (cosmetic, firing client only)")]
    [Tooltip("Optional muzzle-flash prefab spawned instantly on the firing client. Null = tracer only.")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private float muzzleFlashLifetime = 0.2f;
    [Tooltip("Code-generated tracer shown instantly on fire (no art needed).")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private float tracerLength = 1.5f;
    [SerializeField] private float tracerWidth = 0.08f;
    [SerializeField] private float tracerDuration = 0.1f;

    private PlayerAnimator playerAnimator;
    private PlayerTeamData teamComponent;
    private PlayerStatsHandler statsHandler;
    private PlayerMovement playerMovement;
    private PlayerStatModifiers mods;
    private int verticalAim;
    private Vector2 lastAimWorldPoint;

    // Server-only reusable query storage and target-id deduplication. The initial capacities cover
    // all 20 players without allocating in the attack hot path.
    private readonly List<Collider2D> currentTickHits =
        new List<Collider2D>(InitialHitCollectionCapacity);
    private readonly List<LagCompensatedHit> historicalPlayerHits =
        new List<LagCompensatedHit>(InitialHitCollectionCapacity);
    private readonly AttackHitRegistry dashStruck =
        new AttackHitRegistry(InitialHitCollectionCapacity);
    private readonly AttackHitRegistry swingStruck =
        new AttackHitRegistry(InitialHitCollectionCapacity);
    private ContactFilter2D enemyContactFilter;
    private ContactFilter2D playerContactFilter;
    private int enemyLayerMask;
    private int playerLayerMask;
    private bool warnedLagCompensationFallback;
    private bool wasDashing;

    [Networked] private TickTimer AttackCooldownTimer { get; set; }
    [Networked] private TickTimer ShootCooldownTimer { get; set; }

    // Swing state (spec 2.2): the swing is its start tick + latched aim/facing; the phase is
    // derived per tick via SwingPhase.Resolve, so it predicts and resimulates correctly.
    [Networked, OnChangedRender(nameof(OnAttackStartTickChanged))] private int AttackStartTick { get; set; }
    [Networked] private int AttackAim { get; set; }
    [Networked] private NetworkBool AttackFacingRight { get; set; }
    [Networked] private NetworkBool DownAttackCancelled { get; set; }

    public bool SwingFacingRight => AttackFacingRight;

    void Awake()
    {
        playerAnimator = GetComponent<PlayerAnimator>();
        teamComponent = GetComponent<PlayerTeamData>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        playerMovement = GetComponent<PlayerMovement>();
        mods = GetComponent<PlayerStatModifiers>();

        int configuredMask = attackableLayer.value;
        enemyLayerMask = configuredMask & LayerMask.GetMask("Enemy");
        playerLayerMask = configuredMask & LayerMask.GetMask("Player");
        enemyContactFilter = CreateLayerFilter(enemyLayerMask);
        playerContactFilter = CreateLayerFilter(playerLayerMask);

        if (enemyLayerMask == 0)
            Debug.LogError("PlayerCombat: attackableLayer must include the Enemy layer.");
        if (playerLayerMask == 0)
            Debug.LogError("PlayerCombat: attackableLayer must include the Player layer.");
        if (playerMovement == null)
            Debug.LogError("PlayerCombat: PlayerMovement is required for attack grounding.", this);
        if (sideAttackPoint == null || upAttackPoint == null || downAttackPoint == null)
            Debug.LogError("PlayerCombat: side, up, and down attack points must all be assigned.", this);
    }

    private static ContactFilter2D CreateLayerFilter(int layerMask)
    {
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = Physics2D.queriesHitTriggers
        };
        filter.SetLayerMask(layerMask);
        return filter;
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        verticalAim = input.VerticalAim;
        lastAimWorldPoint = input.AimWorldPoint;

        SwingPhaseKind phase = CurrentSwingPhase();
        bool grounded = playerMovement != null && playerMovement.IsGrounded();

        if (pressed.IsSet((int)PlayerButton.Melee) && phase == SwingPhaseKind.None &&
            AttackCooldownTimer.ExpiredOrNotRunning(Runner) &&
            playerMovement != null && GetAttackPoint(verticalAim) != null &&
            MeleeAttackRules.CanStart(verticalAim, grounded))
        {
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.attackCooldown);
            BeginSwing();
            phase = CurrentSwingPhase(); // now Startup (or Active if startupTicks is 0)
        }

        if (phase != SwingPhaseKind.None && !DownAttackCancelled &&
            MeleeAttackRules.CancelDownHits(AttackAim, grounded, DownAttackCancelled))
        {
            DownAttackCancelled = true;
            if (playerAnimator != null) playerAnimator.CancelDownAttack();
        }

        if (phase == SwingPhaseKind.Active && !DownAttackCancelled)
            SimulateSwingTick();

        if (pressed.IsSet((int)PlayerButton.Shoot))
        {
            if (ShootCooldownTimer.ExpiredOrNotRunning(Runner))
            {
                ShootCooldownTimer = TickTimer.CreateFromSeconds(Runner, projectileCooldown);
                ShootProjectile(input.AimWorldPoint);
            }
        }

        // Quicker Dash tier 3: deal melee damage in the front swing box while dashing.
        // Each target is hit AT MOST ONCE per dash (dashStruck dedup set cleared on rising edge).
        bool dashing = playerMovement != null && playerMovement.IsDashing();
        if (HasStateAuthority && dashing && mods != null && mods.DashDealsDamage && sideAttackPoint != null)
        {
            if (!wasDashing) dashStruck.Clear();
            ApplyMeleeHits(sideAttackPoint.position, sideAttackArea, dashStruck, attackAim: 0);
        }
        wasDashing = dashing;
    }

    private SwingPhaseKind CurrentSwingPhase()
    {
        return SwingPhase.Resolve(Runner.Tick, AttackStartTick,
            stats.attackStartupTicks, stats.attackActiveTicks, stats.attackRecoveryTicks);
    }

    /// <summary>True while a swing owns the player's offense (Startup/Active/Recovery).
    /// PlayerMovement reads this to block dash starts (spec 2.3).</summary>
    public bool IsSwingCommitted => CurrentSwingPhase() != SwingPhaseKind.None;

    /// <summary>Latch a new swing: start tick + aim/facing frozen at commit
    /// (spec 2.2 — the swing never flips mid-animation). Runs on state authority and the
    /// predicting input authority, like the old Attack().</summary>
    private void BeginSwing()
    {
        AttackStartTick = Runner.Tick;
        AttackAim = System.Math.Sign(verticalAim);
        AttackFacingRight = playerMovement != null ? playerMovement.IsFacingRight()
                                                   : transform.localScale.x >= 0f;
        DownAttackCancelled = false;

        swingStruck.Clear();

        if (playerAnimator != null)
            playerAnimator.TriggerAttack(AttackAim);
    }

    /// <summary>
    /// Fires the swing whoosh on every peer the moment a new swing is latched — including the
    /// predicting input authority, which is what makes your own melee feel instant. This is a
    /// DIFFERENT cue from the hit confirm in HitFeedback (which arrives later, only on the
    /// attacker, only when the swing actually connected), so a landed hit plays two distinct
    /// sounds and a whiff plays one. There is nothing to reconcile and nothing to suppress.
    ///
    /// Adding OnChangedRender to an already-[Networked] property changes nothing on the wire.
    /// </summary>
    private void OnAttackStartTickChanged()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        // Tick 0 is the never-swung default; a pooled or freshly spawned player must not whoosh.
        if (AttackStartTick <= 0) return;

        AudioCueId cue = AttackAim < 0 ? AudioCueId.MeleeSwingHeavy : AudioCueId.MeleeSwing;
        if (HasInputAuthority) Audio.Play2D(cue);
        else Audio.PlayAt(cue, transform.position);
    }

    /// <summary>Server-only hit detection on every uncancelled Active tick,
    /// at most one hit per target per swing.</summary>
    private void SimulateSwingTick()
    {
        if (!HasStateAuthority) return;

        ResolveSwingBox(out Vector2 center, out Vector2 area);
        ApplyMeleeHits(center, area, swingStruck, AttackAim);
    }

    private Transform GetAttackPoint(int aim) =>
        aim > 0 ? upAttackPoint : aim < 0 ? downAttackPoint : sideAttackPoint;

    /// <summary>Hitbox from the LATCHED aim/facing. The attack-point children flip with
    /// localScale (current facing); if facing changed mid-swing, mirror the offset back
    /// to the facing latched at commit.</summary>
    private void ResolveSwingBox(out Vector2 center, out Vector2 area)
    {
        Transform point = GetAttackPoint(AttackAim);
        area = AttackAim > 0 ? upAttackArea : AttackAim < 0 ? downAttackArea : sideAttackArea;

        Vector2 offset = (Vector2)point.position - (Vector2)transform.position;
        bool facingRightNow = playerMovement != null ? playerMovement.IsFacingRight() : transform.localScale.x >= 0f;
        if (facingRightNow != (bool)AttackFacingRight) offset.x = -offset.x;
        center = (Vector2)transform.position + offset;
    }

    /// <summary>
    /// SERVER: detect enemies at the current tick and players either from Fusion history or the
    /// documented current-tick fallback. Both paths share one target-id registry.
    /// </summary>
    private void ApplyMeleeHits(Vector2 center, Vector2 area,
                                AttackHitRegistry alreadyHit, int attackAim)
    {
        Vector2 normalizedArea = new Vector2(Mathf.Abs(area.x), Mathf.Abs(area.y));
        CombatConfig config = GetCombatConfig();
        bool diagnosticsEnabled = config != null && config.logLagCompensationDiagnostics;
        float diagnosticInterval = config != null
            ? config.lagCompensationDiagnosticInterval
            : 30f;

        ApplyCurrentTickEnemyHits(center, normalizedArea, alreadyHit, diagnosticsEnabled, attackAim);

        HitboxManager historyManager = Runner != null ? Runner.LagCompensation : null;
        PlayerRef attacker = Object != null ? Object.InputAuthority : PlayerRef.None;
        bool featureEnabled = config != null && config.enableLagCompensation;
        PlayerHitQueryMode queryMode = LagCompensationPolicy.Resolve(
            featureEnabled,
            historyManager != null,
            attacker.IsRealPlayer);

        if (queryMode == PlayerHitQueryMode.Historical)
        {
            ApplyHistoricalPlayerHits(
                historyManager, attacker, center, normalizedArea, alreadyHit, diagnosticsEnabled, attackAim);
        }
        else
        {
            if (featureEnabled)
                WarnLagCompensationFallbackOnce(historyManager == null
                    ? "Fusion's history manager is unavailable"
                    : "the attacker has no valid input authority");
            else if (config == null)
                WarnLagCompensationFallbackOnce("CombatConfig is unavailable");

            ApplyCurrentTickPlayerHits(
                center, normalizedArea, alreadyHit, diagnosticsEnabled, attackAim);
        }

        CombatLagCompensationDiagnostics.MaybeLog(
            diagnosticsEnabled, diagnosticInterval);
    }

    private void ApplyCurrentTickEnemyHits(
        Vector2 center,
        Vector2 area,
        AttackHitRegistry alreadyHit,
        bool diagnosticsEnabled,
        int attackAim)
    {
        currentTickHits.Clear();
        Physics2D.OverlapBox(center, area, 0f, enemyContactFilter, currentTickHits);

        foreach (Collider2D hit in currentTickHits)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy == null || !TryRegisterTarget(enemy.Object, alreadyHit, diagnosticsEnabled))
                continue;

            int finalDamage = ResolveMeleeDamage(enemy.Team, hit.transform.position);
            NetworkId targetId = enemy.Object.Id;
            Vector2 hitPoint = hit.transform.position;
            if (attackAim < 0)
                enemy.ServerTakeSpikeDamage(finalDamage, downAttackSpikeSpeed);
            else
                enemy.TakeDamage(finalDamage, ResolveKnockback(hitPoint), hitPoint);
            RPC_HitFeedback(targetId, hitPoint, finalDamage);
            CombatLagCompensationDiagnostics.RecordCurrentTickEnemyHit(diagnosticsEnabled);
        }
    }

    private void ApplyHistoricalPlayerHits(
        HitboxManager historyManager,
        PlayerRef attacker,
        Vector2 center,
        Vector2 area,
        AttackHitRegistry alreadyHit,
        bool diagnosticsEnabled,
        int attackAim)
    {
        Vector3 queryCenter = new Vector3(center.x, center.y, transform.position.z);
        Vector3 queryExtents =
            new Vector3(area.x * 0.5f, area.y * 0.5f, LagCompensationQueryHalfDepth);

        long queryStartedAt =
            CombatLagCompensationDiagnostics.BeginQuery(diagnosticsEnabled);
        historyManager.OverlapBox(
            queryCenter,
            queryExtents,
            Quaternion.identity,
            attacker,
            historicalPlayerHits,
            layerMask: playerLayerMask,
            options: HitOptions.SubtickAccuracy | HitOptions.IgnoreInputAuthority,
            clearHits: true,
            queryTriggerInteraction: QueryTriggerInteraction.Ignore);
        CombatLagCompensationDiagnostics.RecordQuery(
            diagnosticsEnabled, queryStartedAt);

        foreach (LagCompensatedHit hit in historicalPlayerHits)
        {
            HitboxRoot root = hit.Hitbox != null ? hit.Hitbox.Root : null;
            PlayerStatsHandler targetPlayer =
                root != null ? root.GetComponent<PlayerStatsHandler>() : null;
            TryApplyPlayerMeleeHit(
                targetPlayer, alreadyHit, diagnosticsEnabled, attackAim, historicalHit: true);
        }
    }

    private void ApplyCurrentTickPlayerHits(
        Vector2 center,
        Vector2 area,
        AttackHitRegistry alreadyHit,
        bool diagnosticsEnabled,
        int attackAim)
    {
        currentTickHits.Clear();
        Physics2D.OverlapBox(center, area, 0f, playerContactFilter, currentTickHits);

        foreach (Collider2D hit in currentTickHits)
        {
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            TryApplyPlayerMeleeHit(
                targetPlayer, alreadyHit, diagnosticsEnabled, attackAim, historicalHit: false);
        }
    }

    private void TryApplyPlayerMeleeHit(
        PlayerStatsHandler targetPlayer,
        AttackHitRegistry alreadyHit,
        bool diagnosticsEnabled,
        int attackAim,
        bool historicalHit)
    {
        if (targetPlayer == null || targetPlayer.IsDead) return;

        PlayerTeamData targetTeam = targetPlayer.GetComponent<PlayerTeamData>();
        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
        Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
        bool isSelf = targetPlayer == statsHandler;
        if (!FriendlyFire.CanDamagePlayer(
                TeamUtil.ToNumber(myTeam), TeamUtil.ToNumber(otherTeam), isSelf))
            return;

        if (!TryRegisterTarget(targetPlayer.Object, alreadyHit, diagnosticsEnabled))
            return;

        Vector2 currentTargetPosition = targetPlayer.transform.position;
        int finalDamage = ResolveMeleeDamage(otherTeam, currentTargetPosition);
        DamageApplyResult damageResult =
            targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
        if (!PlayerDamageGate.AllowsSecondaryEffects(damageResult)) return;

        RPC_HitFeedback(
            targetPlayer.Object.Id, currentTargetPosition, finalDamage);

        if (attackAim < 0)
        {
            PlayerMovement targetMovement = targetPlayer.GetComponent<PlayerMovement>();
            if (targetMovement != null)
                targetMovement.ApplySpike(downAttackSpikeSpeed);
            else
                Debug.LogError("PlayerCombat: spike target has no PlayerMovement.", targetPlayer);
        }
        else
        {
            Rigidbody2D targetRb = targetPlayer.GetComponent<Rigidbody2D>();
            if (targetRb != null)
                targetRb.AddForce(ResolveKnockback(currentTargetPosition), ForceMode2D.Impulse);
        }

        if (historicalHit)
            CombatLagCompensationDiagnostics.RecordHistoricalPlayerHit(diagnosticsEnabled);
    }

    private Vector2 ResolveKnockback(Vector2 targetPosition)
    {
        Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
        return new Vector2(direction.x * stats.attackForce, knockbackUpward);
    }

    private static bool TryRegisterTarget(
        NetworkObject target,
        AttackHitRegistry alreadyHit,
        bool diagnosticsEnabled)
    {
        if (target == null || !target.IsValid) return false;
        if (alreadyHit.TryRegister((ulong)target.Id.Raw)) return true;

        CombatLagCompensationDiagnostics.RecordRejectedDuplicate(diagnosticsEnabled);
        return false;
    }

    private void WarnLagCompensationFallbackOnce(string reason)
    {
        if (warnedLagCompensationFallback) return;
        warnedLagCompensationFallback = true;
        Debug.LogWarning(
            $"PlayerCombat: using current-tick player hit detection because {reason}.");
    }

    /// <summary>
    /// Attacker-only hit feedback. Server calls this on the attacker's client after
    /// a landed melee hit; it resolves the target locally and plays cosmetic FX.
    /// No networked state — mirrors Projectile.RPC_Impact / Enemy.RPC_TakeDamage.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)
    {
        if (HitFeedback.Instance == null) return;
        GameObject targetGo = null;
        if (Runner.TryFindObject(targetId, out NetworkObject targetObj) && targetObj != null)
            targetGo = targetObj.gameObject;
        HitFeedback.Instance.Play(targetGo, hitPoint, damage);
    }

    /// <summary>
    /// Resolves melee damage through the unified pipeline, keyed by the DEFENDER's team and
    /// position — a defender takes more damage the farther they are from their own base.
    /// Falls back to raw base damage (with a loud one-time warning) if no CombatConfig is assigned.
    /// </summary>
    private int ResolveMeleeDamage(Team defenderTeam, Vector2 defenderPos)
    {
        CombatConfig config = GetCombatConfig();
        if (config == null)
        {
            CombatConfig.WarnMissingOnce();
            return Mathf.RoundToInt(stats.attackDamage);
        }

        return config.ResolveDamage(stats.attackDamage, defenderTeam, defenderPos);
    }

    private static CombatConfig GetCombatConfig()
    {
        return GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
    }

    private void ShootProjectile(Vector2 aimWorldPoint)
    {
        if (playerAnimator != null) playerAnimator.TriggerShoot();
        if (projectilePrefab == null || projectileSpawnPoint == null) return;

        // Cosmetic local prediction: instant muzzle/tracer on the firing client only. IsForward
        // fires it exactly once (not on resimulation); !HasStateAuthority skips a host-as-player
        // whose real projectile is already instant. The server still spawns the authoritative one.
        if (HasInputAuthority && !HasStateAuthority && Runner.IsForward)
        {
            Vector2 dir = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;
            PlayLocalShootFx(projectileSpawnPoint.position, dir);
        }

        if (!HasStateAuthority)
        {
            return; // only the server spawns networked objects
        }

        Vector2 aimDirection = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;

        // Degenerate aim (no aim data yet, or aim point exactly on the muzzle) would spawn a
        // stationary projectile. Fall back to the player's facing direction; localScale.x tracks
        // the networked FacingRight, so this is consistent on the server.
        if (aimDirection == Vector2.zero)
            aimDirection = transform.localScale.x >= 0f ? Vector2.right : Vector2.left;

        Team shooterTeam = teamComponent != null ? teamComponent.Team : Team.None;

        // The raw base damage travels with the projectile; final damage is resolved at impact
        // (Projectile.cs) against the DEFENDER's team+position, since the defender isn't known
        // until something is actually hit.
        NetworkObject spawned = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam, projectileScale);
            });
    }

    /// <summary>
    /// Client-local, non-networked shot feedback (muzzle flash + tracer). No gameplay effect — the
    /// server's networked projectile is authoritative. Called only on the firing input-authority
    /// client, once per shot.
    /// </summary>
    private void PlayLocalShootFx(Vector3 origin, Vector2 dir)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, origin, Quaternion.identity);
            Destroy(flash, muzzleFlashLifetime);
        }
        CosmeticTracer.Spawn(origin, dir, tracerLength, tracerWidth, tracerColor, tracerDuration);
    }

    /// <summary>
    /// Local aim direction (unit vector) from this player toward the last mouse aim point. Used by
    /// PlayerCamera for the aim lean. Returns Vector2.zero before any input or if degenerate.
    /// </summary>
    public Vector2 GetAimDirection()
    {
        Vector2 d = lastAimWorldPoint - (Vector2)transform.position;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.zero;
    }

    void OnDrawGizmosSelected()
    {
        if (sideAttackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(sideAttackPoint.position, sideAttackArea);
        }

        if (upAttackPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(upAttackPoint.position, upAttackArea);
        }

        if (downAttackPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(downAttackPoint.position, downAttackArea);
        }
    }
}
