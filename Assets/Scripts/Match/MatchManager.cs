using System;
using Fusion;
using UnityEngine;
using Game.Match.Core;

/// <summary>Explicit match life-cycle phases. Replaces CTFGameManager's lone GameIsOver bool.</summary>
public enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission }

/// <summary>
/// Server-authoritative match life cycle. Owns the phase enum, one reused TickTimer, and the
/// single "who won" resolver (CTF capture + timer expiry both feed it). One per Gameplay scene.
/// Must be always-interested under AoI so every player sees the phase/timer/results.
/// </summary>
public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

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

    public bool IsLive => Phase == MatchPhase.Live;
    public bool InputEnabled => Phase == MatchPhase.Live;

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
                // Timer-expiry resolution is added in Task 3.
                break;
            case MatchPhase.PostMatch:
                // Auto-advance is added in Task 4.
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
            case MatchPhase.PostMatch:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, postMatchSeconds);
                break;
            case MatchPhase.Intermission:
                PhaseTimer = TickTimer.None;
                break;
        }
    }

    /// <summary>Server-only. A team captured the enemy flag. Filled in Task 3.</summary>
    public void ReportCapture(Team winningTeam)
    {
        if (!HasStateAuthority || Phase != MatchPhase.Live) return;
        // Body added in Task 3.
    }

    private void OnPhaseChanged() => PhaseChanged?.Invoke();
}
