using UnityEngine;
using Fusion;
using Game.Buffs.Core;
using Game.Hud.Core;
using Game.Audio.Core;

/// <summary>
/// Per-player, server-authoritative buff state. Tiers are DERIVED from TotalDepositedValue +
/// LoadoutOrder (both networked) via BuffUnlock — nothing to replay on resimulation. The one
/// active buff (Stealth) is a TickTimer-driven networked flag, activated in Simulate (mirrors dash).
/// </summary>
public class PlayerBuffs : NetworkBehaviour
{
    [SerializeField] private BuffLoadoutConfig config;

    [Networked, Capacity(8)] private NetworkArray<byte> LoadoutOrder { get; }
    [Networked] private int LoadoutLength { get; set; }
    [Networked, OnChangedRender(nameof(OnBuffsChanged))] public int TotalDepositedValue { get; private set; }
    [Networked, OnChangedRender(nameof(OnStealthChanged))] public NetworkBool IsStealthed { get; private set; }
    [Networked] private TickTimer StealthDurationTimer { get; set; }
    [Networked] private TickTimer StealthCooldownTimer { get; set; }

    public int TotalDeposited => TotalDepositedValue;

    /// <summary>Fires when TotalDepositedValue changes (tiers re-derive). HUD subscribes.</summary>
    public event System.Action BuffsChanged;

    /// <summary>Fires when the networked stealth-active flag flips. HUD subscribes.</summary>
    public event System.Action StealthStateChanged;

    // Cached at subscribe time: MatchManager.Instance can already be null during scene teardown,
    // so Despawned must unsubscribe via this reference rather than re-resolving the static.
    private MatchManager subscribedMatchManager;

    // Render-side only: one rising-edge detector per BuffId (the loadout always holds the whole
    // catalog per TierOf's own doc comment, regardless of what's equipped), so BuffTierUp fires
    // once on a genuine tier crossing for ANY equipped buff, not on every deposit — most deposits
    // land between thresholds. Reuses Game.Hud.Core.TierUpEdge, the same detector the HUD's own
    // per-icon toast already uses, per the design spec's explicit "via the existing TierUpEdge
    // detector" requirement. Never networked; primes silently so a late joiner or a rematch
    // scene-reload reset never plays a false tier-up.
    private readonly TierUpEdge[] tierEdges = new TierUpEdge[4];

    private void OnBuffsChanged()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        // Self-only: your own progression, not a broadcast. Suppressed during Sudden Death,
        // which forces every tier to MaxTier without a real deposit — matches
        // BuffIconDisplay.RepaintTier's identical suppression for the same case.
        bool anyTierRose = false;
        for (int i = 0; i < tierEdges.Length; i++)
            if (tierEdges[i].Observe(TierOf((BuffId)i))) anyTierRose = true;

        if (HasInputAuthority && anyTierRose && !SuddenDeathMaxesTiers) Audio.PlayUi(AudioCueId.BuffTierUp);

        BuffsChanged?.Invoke();
    }

    private void OnStealthChanged()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        // Flat for the stealthed player. For everyone else, a quiet shimmer on a deliberately
        // SHORT radius (SoundCue.maxDistance on the cue asset, ~0.35x the normal world radius):
        // opponents standing right next to you get counterplay, but the buff is not announced
        // across the arena. That radius is the balance lever and lives in the asset, not here.
        AudioCueId cue = IsStealthed ? AudioCueId.StealthEnter : AudioCueId.StealthExit;
        if (HasInputAuthority) Audio.Play2D(cue);
        else Audio.PlayAt(cue, transform.position);

        StealthStateChanged?.Invoke();
    }

    // TierOf now reads MatchManager.Phase (via SuddenDeathMaxesTiers) as well as
    // TotalDepositedValue, so a phase change is a tier change too — re-raise BuffsChanged on it.
    // PhaseChanged fires on every peer via OnChangedRender, so this needs no new networking.
    private void OnMatchPhaseChanged() => BuffsChanged?.Invoke();

    /// <summary>
    /// Radial fill for the stealth icon: 1 = ready, 0 = just used, ramping up while it recharges.
    /// While stealth is ACTIVE the ability is unavailable, so report 0 (not ready).
    /// </summary>
    public float StealthCooldownFill01()
    {
        if (IsStealthed) return 0f;
        float total = CurrentStealthCooldown();
        float remaining = StealthCooldownTimer.RemainingTime(Runner) ?? 0f;
        return CooldownFill.Fill01(remaining, total);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            // Loud on the server too: OnValidate only fires in the editor, so a headless
            // dedicated server with a mis-authored curve must still say so.
            if (config != null) config.LogIfCurveInvalid();
            if (LoadoutLength == 0) ApplyDefaultLoadout();
        }

        if (DedicatedServerPresentation.IsHeadless) return;

        // Clients subscribe because the Sudden Death tier override is a render-time read.
        if (MatchManager.Instance != null)
        {
            subscribedMatchManager = MatchManager.Instance;
            subscribedMatchManager.PhaseChanged += OnMatchPhaseChanged;
        }

        // Prime every tier edge at the CURRENT tier (0 for a fresh spawn, or whatever a
        // reconnecting/restored player already has) so the first REAL tier-up after this point
        // is reported as a rise, not silently consumed as the priming observation. Mirrors the
        // identical priming call BuffIconDisplay.Bind already makes for the same struct.
        for (int i = 0; i < tierEdges.Length; i++) tierEdges[i].Observe(TierOf((BuffId)i));
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (subscribedMatchManager != null)
            subscribedMatchManager.PhaseChanged -= OnMatchPhaseChanged;
        subscribedMatchManager = null;
    }

    private void ApplyDefaultLoadout()
    {
        if (config == null || config.DefaultOrder == null) return;
        ServerInitLoadout(LoadoutCodec.ToBytes(config.DefaultOrder));
    }

    /// <summary>SERVER: set this player's priority order (from the lobby choice or default).</summary>
    public void ServerInitLoadout(byte[] order)
    {
        if (!HasStateAuthority || order == null) return;
        if (order.Length == 0) return; // empty choice: keep the default loadout applied in Spawned
        int n = Mathf.Min(order.Length, LoadoutCodec.MaxEntries);
        for (int i = 0; i < n; i++) LoadoutOrder.Set(i, order[i]);
        LoadoutLength = n;
    }

    /// <summary>SERVER: add deposited point value; tiers re-derive automatically from this.</summary>
    public void ServerAddDepositedValue(int points)
    {
        if (!HasStateAuthority || points <= 0) return;
        TotalDepositedValue += points;
    }

    /// <summary>
    /// SERVER: set the deposited total outright when restoring a reconnecting player. Separate from
    /// ServerAddDepositedValue because restore is an assignment, not an accumulation, and 0 is a
    /// legal restored value. Called from the Runner.Spawn callback, before replication, so the
    /// rejoiner's first snapshot already carries their tiers.
    /// </summary>
    public void ServerRestoreDeposited(int total)
    {
        if (!HasStateAuthority || total < 0) return;
        TotalDepositedValue = total;
    }

    /// <summary>Priority position of a buff in this player's loadout, or -1 if not equipped.</summary>
    private int PositionOf(BuffId id)
    {
        for (int i = 0; i < LoadoutLength; i++)
            if ((BuffId)LoadoutOrder.Get(i) == id) return i;
        return -1;
    }

    /// <summary>Current tier (0 = locked) of the given buff for this player.</summary>
    public int TierOf(BuffId id)
    {
        if (config == null) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0; // not equipped — the loadout always holds the whole catalog
        int steps = BuffUnlock.UnlockedSteps(config.Thresholds, TotalDepositedValue);
        return BuffUnlock.ResolveTier(steps, pos, config.BuffCount, config.MaxTier,
                                      allUnlocked: SuddenDeathMaxesTiers);
    }

    /// <summary>Top tier any buff can reach, from the shared loadout config (0 if unconfigured).</summary>
    public int MaxTier => config != null ? config.MaxTier : 0;

    /// <summary>
    /// HUD fill 0..1 toward the deposit that next raises this buff's tier. 1 means "nothing left
    /// to earn" — already at the top of the curve, or Sudden Death has maxed everything. A buff
    /// with no economy (no config, or not in this player's loadout) reads 0, not 1: TierOf reports
    /// tier 0 for it in every phase, and a full bar beside empty pips is the contradiction this
    /// ordering exists to prevent.
    /// Read-only derivation from the same networked state TierOf uses.
    /// </summary>
    public float NextUnlockProgress01(BuffId id)
    {
        if (config == null) return 0f;
        int pos = PositionOf(id);
        if (pos < 0) return 0f;
        if (SuddenDeathMaxesTiers) return 1f;
        return BuffProgress.ToNextTier01(config.Thresholds, TotalDepositedValue, pos, config.BuffCount);
    }

    /// <summary>
    /// Total deposited value at which this buff next tiers up; 0 when it can rise no further
    /// (top of the curve, not equipped, or Sudden Death).
    /// </summary>
    public int NextUnlockThreshold(BuffId id)
    {
        if (config == null) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0;
        if (SuddenDeathMaxesTiers) return 0;
        return BuffProgress.NextThresholdFor(config.Thresholds, TotalDepositedValue, pos, config.BuffCount);
    }

    /// <summary>
    /// Sudden Death forces every tier to MaxTier. Derived from MatchManager's [Networked] Phase,
    /// so it resolves identically on clients and during resimulation. Deliberately adds no
    /// per-player state and never mutates TotalDepositedValue — tiers stay derived, so leaving
    /// Sudden Death (scene reload on rematch) restores normal tiers with nothing to reset.
    /// </summary>
    private bool SuddenDeathMaxesTiers =>
        MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;

    /// <summary>Sum every equipped buff's passive contribution at its current tier.</summary>
    public void BuildEffectiveStats(ref EffectiveStats stats)
    {
        if (config == null) return;
        for (int i = 0; i < LoadoutLength; i++)
        {
            BuffId id = (BuffId)LoadoutOrder.Get(i);
            BuffDefinition def = config.GetById(id);
            if (def == null) continue;
            def.ContributeStats(ref stats, TierOf(id));
        }
    }

    /// <summary>Called from PlayerController.FixedUpdateNetwork after movement/combat.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        // Stealth expiry first (pure function of the networked timer).
        if (IsStealthed && StealthDurationTimer.ExpiredOrNotRunning(Runner))
        {
            IsStealthed = false;
            StealthCooldownTimer = TickTimer.CreateFromSeconds(Runner, CurrentStealthCooldown());
        }

        // Activation.
        if (pressed.IsSet((int)PlayerButton.Stealth) && CanActivateStealth())
        {
            ActiveBuffParams p = StealthParams();
            IsStealthed = true;
            StealthDurationTimer = TickTimer.CreateFromSeconds(Runner, p.Duration);
        }
    }

    private ActiveBuffParams StealthParams()
    {
        BuffDefinition def = config != null ? config.GetById(BuffId.Stealth) : null;
        return def != null ? def.GetActiveParams(TierOf(BuffId.Stealth)) : default;
    }

    private float CurrentStealthCooldown()
    {
        ActiveBuffParams p = StealthParams();
        return p.Cooldown;
    }

    private bool CanActivateStealth()
    {
        if (IsStealthed) return false;
        if (!StealthCooldownTimer.ExpiredOrNotRunning(Runner)) return false;
        ActiveBuffParams p = StealthParams();
        if (!p.Unlocked) return false;
        bool carrying = CTFGameManager.Instance != null &&
                        CTFGameManager.Instance.IsCarrying(Object.InputAuthority);
        if (carrying && !p.UsableWhileCarryingFlag) return false;
        return true;
    }
}
