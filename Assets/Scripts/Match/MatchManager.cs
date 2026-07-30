using System;
using Fusion;
using UnityEngine;
using Game.Match.Core;

/// <summary>
/// Server-authoritative match life cycle. Owns the phase enum, one reused TickTimer, and the
/// results banner's winner code. Capture is the ONLY win condition: a Live timer expiry hands off
/// to SuddenDeath rather than resolving a winner from coin score. One per Gameplay scene. Must be
/// always-interested under AoI so every player sees the phase/timer/results.
/// </summary>
public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    // Sudden Death's own hard-cap timer is not a duration serialized here: it is a match rule,
    // so it lives with matchTimeLimit on GameSettingsManager (suddenDeathHardCap, minutes, 0 = off).
    [Header("Phase Durations (seconds)")]
    [SerializeField] private float warmupSeconds = 3f;
    [SerializeField] private float countdownSeconds = 3f;
    [Tooltip("How long the results panel holds before auto-returning to the lobby.")]
    [SerializeField] private float postMatchSeconds = 20f;

    [Networked, OnChangedRender(nameof(OnPhaseChanged))]
    public MatchPhase Phase { get; set; }

    [Networked] public byte Winner { get; set; } // 0 = draw, 1 = Team1, 2 = Team2

    // One timer reused per timed phase; its networked so clients/late-joiners see remaining time.
    [Networked] private TickTimer PhaseTimer { get; set; }

    /// <summary>Fires on every phase change (all peers, via OnChangedRender). HUD subscribes.</summary>
    public event Action PhaseChanged;

    /// <summary>
    /// Play is running: input live, enemies thinking, flags carryable, captures counted. TRUE in
    /// SuddenDeath as well as Live — every gameplay gate must use this rather than testing
    /// Phase == Live, or Sudden Death would freeze the match it is supposed to decide.
    /// </summary>
    public bool IsPlayActive => MatchRules.IsPlayActive(Phase);

    /// <summary>
    /// Sudden Death forces every buff to max tier. PlayerBuffs reads this at tier-resolve time;
    /// no tier is stored, so this costs no networked state.
    /// </summary>
    public bool AllBuffsMaxed => MatchRules.AllBuffsMaxed(Phase);

    public bool InputEnabled => IsPlayActive;

    /// <summary>Seconds left in the current timed phase, or null when the phase has no running timer.</summary>
    public float? PhaseTimeRemaining => PhaseTimer.RemainingTime(Runner);

    private void Awake()
    {
        // Never Destroy() a spawned NetworkObject locally (desyncs Fusion's object table); disable
        // the duplicate and leave it inert, matching TeamScoreManager's guard.
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            EnterPhase(MatchPhase.Warmup);

        // Late-joiner reconcile: render the current phase immediately (mirrors the old
        // OnGameOverChanged-from-Spawned pattern).
        OnPhaseChanged();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        switch (Phase)
        {
            case MatchPhase.Warmup:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Countdown);
                break;
            case MatchPhase.Countdown:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Live);
                break;
            case MatchPhase.Live:
                // Timer expiry no longer resolves a winner: coins cannot decide a match.
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.SuddenDeath);
                break;
            case MatchPhase.SuddenDeath:
                // Only armed when an operator sets suddenDeathHardCap; TickTimer.None never expires.
                if (PhaseTimer.Expired(Runner)) ResolveAsDraw();
                break;
            case MatchPhase.PostMatch:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Intermission);
                break;
            case MatchPhase.Intermission:
                break;
        }
    }

    /// <summary>Server-only. Sets Phase and arms the timer for the new phase.</summary>
    private void EnterPhase(MatchPhase next)
    {
        Phase = next;

        switch (next)
        {
            case MatchPhase.Warmup:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, warmupSeconds);
                break;
            case MatchPhase.Countdown:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, countdownSeconds);
                break;
            case MatchPhase.Live:
                float limit = (GameSettingsManager.Instance != null)
                    ? GameSettingsManager.Instance.matchTimeLimit * 60f
                    : 0f;
                // matchTimeLimit == 0 means "no timer": capture is then the only end condition.
                PhaseTimer = limit > 0f ? TickTimer.CreateFromSeconds(Runner, limit) : TickTimer.None;
                break;
            case MatchPhase.SuddenDeath:
                float cap = (GameSettingsManager.Instance != null)
                    ? GameSettingsManager.Instance.suddenDeathHardCap * 60f
                    : 0f;
                // Default 0 = off: no timer at all, so the next capture is the only end condition.
                PhaseTimer = cap > 0f ? TickTimer.CreateFromSeconds(Runner, cap) : TickTimer.None;
                break;
            case MatchPhase.PostMatch:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, postMatchSeconds);
                break;
            case MatchPhase.Intermission:
                PhaseTimer = TickTimer.None;
                if (GameNetworkManager.Instance != null)
                    GameNetworkManager.Instance.BeginReturnToLobby();
                break;
        }
    }

    /// <summary>Server-only. A team carried the enemy flag home while play was active — instant win.</summary>
    public void ReportCapture(Team winningTeam)
    {
        if (!HasStateAuthority || !MatchRules.IsPlayActive(Phase)) return;
        Winner = (byte)TeamUtil.ToNumber(winningTeam);
        EnterPhase(MatchPhase.PostMatch);
    }

    /// <summary>
    /// Host-triggered early advance from the results screen (the HUD button, shown only to the
    /// designated host). Mirrors GameNetworkManager.RequestStartMatch's split: in host mode the
    /// local peer IS the state authority and advances directly (a host-invoked RPC's info.Source
    /// does not survive the host check, so we must not round-trip); a dedicated-server host-client
    /// has no state authority and routes through the server-validated RPC. Auto-advance still fires
    /// regardless.
    /// </summary>
    public void RequestReturnToLobby()
    {
        if (Phase != MatchPhase.PostMatch) return;
        if (HasStateAuthority) EnterPhase(MatchPhase.Intermission);
        else RPC_RequestReturnToLobby();
    }

    /// <summary>Dedicated-server path: a host-client asks the server to advance; server host-validates.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestReturnToLobby(RpcInfo info = default)
    {
        if (!HasStateAuthority || Phase != MatchPhase.PostMatch) return;
        if (!IsHost(info.Source)) return;
        EnterPhase(MatchPhase.Intermission);
    }

    /// <summary>True on the peer whose local player is the designated host (lowest active PlayerId).</summary>
    public bool LocalPlayerIsHost() => Runner != null && IsHost(Runner.LocalPlayer);

    private bool IsHost(PlayerRef p)
    {
        if (Runner == null || p == PlayerRef.None) return false;
        var ids = new System.Collections.Generic.List<int>();
        foreach (var active in Runner.ActivePlayers) ids.Add(active.PlayerId);
        return LobbyHostPolicy.DesignateHostId(ids) == p.PlayerId;
    }

    /// <summary>
    /// Server-only OPS SAFETY VALVE: the operator-set Sudden Death hard cap elapsed, so end the
    /// match as a draw rather than let a headless dedicated server wedge on an unwinnable match.
    /// Unreachable in default play — suddenDeathHardCap defaults to 0 = off.
    /// </summary>
    private void ResolveAsDraw()
    {
        Winner = 0; // MatchResolver.WinnerLabel(0) reads as a draw.
        EnterPhase(MatchPhase.PostMatch);
    }

    private void OnPhaseChanged() => PhaseChanged?.Invoke();
}
