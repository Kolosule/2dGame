using UnityEngine;
using Fusion;
using Game.Buffs.Core;
using Game.Match.Core;

/// <summary>
/// Singleton manager that tracks team scores and derives the one team buff, Vanguard.
/// META-LAYER MODEL: CTF capture is the only win condition; coin deposits buy Vanguard, which
/// lifts the territorial debuff on damage dealt in the enemy third (CombatConfig.ResolveDamage).
/// Tiers are DERIVED on query from networked score + a once-frozen roster size — never stored —
/// so there is nothing to replay on resimulation and no monotonic latch to maintain.
/// Place one on an empty GameObject in the Gameplay scene. PHOTON FUSION networked; must be
/// always-interested under interest management.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
/// </summary>
public class TeamScoreManager : NetworkBehaviour
{
    [Header("Score Tracking")]
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team1Score { get; set; }
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team2Score { get; set; }

    [Header("Vanguard (the entire team buff catalog)")]
    [Tooltip("PER-PLAYER-AVERAGE deposited value per Vanguard tier. Compared against " +
             "teamScore / roster size — NOT against the raw team score. On a 10-player team, " +
             "{12, 45} means absolute team scores of 120 and 450.")]
    [SerializeField] private int[] vanguardThresholds = { 12, 45 };

    [Tooltip("Vanguard's top tier. One team buff means one threshold per tier, so this must " +
             "equal vanguardThresholds.Length.")]
    [SerializeField] private int vanguardMaxTier = 2;

    [Header("Roster (frozen once per team, on entering Live)")]
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public byte Team1RosterSize { get; set; }
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public byte Team2RosterSize { get; set; }
    [Networked] private NetworkBool Team1RosterCaptured { get; set; }
    [Networked] private NetworkBool Team2RosterCaptured { get; set; }

    /// <summary>Fires when Team1Score / Team2Score change. HUD subscribes.</summary>
    public event System.Action ScoresChanged;

    /// <summary>
    /// Fires whenever the Vanguard tier could have moved — i.e. on score changes AND on the
    /// roster capture, since the tier derives from both. HUD subscribes.
    /// </summary>
    public event System.Action TeamBuffsChanged;

    private void OnScoresChanged()
    {
        ScoresChanged?.Invoke();
        TeamBuffsChanged?.Invoke();
    }

    private void OnTeamBuffsChanged() => TeamBuffsChanged?.Invoke();

    // Cached at subscribe time: MatchManager.Instance can already be null during scene teardown,
    // so Despawned must unsubscribe via this reference rather than re-resolving the static.
    private MatchManager subscribedMatchManager;

    // Singleton instance
    private static TeamScoreManager instance;

    public static TeamScoreManager Instance => instance;

    private void Awake()
    {
        // Ensure only one instance exists
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            // Never Destroy() a spawned NetworkObject locally — that desyncs Fusion's
            // object table on this peer. Disable the duplicate and leave it inert.
            Debug.LogWarning("Multiple TeamScoreManagers detected! Disabling duplicate.");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public override void Spawned()
    {
        // Fail loudly on a mis-authored catalog rather than silently locking or over-unlocking a tier.
        int authored = vanguardThresholds != null ? vanguardThresholds.Length : 0;
        if (authored != vanguardMaxTier)
        {
            Debug.LogError($"❌ TeamScoreManager: vanguardThresholds has {authored} entries but " +
                           $"vanguardMaxTier is {vanguardMaxTier}. One team buff needs exactly one " +
                           $"threshold per tier.");
        }

        // BuffUnlock.UnlockedSteps assumes ascending thresholds and breaks at the first entry above
        // the total, so a misordered catalog (e.g. {45, 12}) would silently lock Vanguard forever
        // instead of throwing. Fail loudly here instead.
        if (vanguardThresholds != null)
        {
            for (int i = 1; i < vanguardThresholds.Length; i++)
            {
                if (vanguardThresholds[i] <= vanguardThresholds[i - 1])
                {
                    Debug.LogError($"❌ TeamScoreManager: vanguardThresholds must be strictly " +
                                   $"ascending, but index {i} ({vanguardThresholds[i]}) is not " +
                                   $"greater than index {i - 1} ({vanguardThresholds[i - 1]}).");
                }
            }
        }

        // VanguardTier now reads MatchManager.Phase as well as score, so a phase change is a tier
        // change too. PhaseChanged fires on every peer via OnChangedRender — no new networking.
        if (MatchManager.Instance != null)
        {
            subscribedMatchManager = MatchManager.Instance;
            subscribedMatchManager.PhaseChanged += OnTeamBuffsChanged;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (subscribedMatchManager != null)
            subscribedMatchManager.PhaseChanged -= OnTeamBuffsChanged;
        subscribedMatchManager = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || (Team1RosterCaptured && Team2RosterCaptured)) return;

        // MatchManager owns the phase. If it is absent (a scene with no match loop), capture at once
        // so the team layer is never dead just because the phase machine is missing.
        MatchManager match = MatchManager.Instance;
        if (match != null && match.Phase != MatchPhase.Live) return;

        CaptureRosterSizes();
    }

    /// <summary>
    /// SERVER: freeze each team's head-count once, independently, the first tick that team's own
    /// count is greater than zero, and use it as the divisor for the rest of the match. This is
    /// what keeps tier derivation pure: roster churn afterwards cannot retroactively unlock or
    /// revoke Vanguard, so no stored tier state is needed.
    /// PER-TEAM, not combined: a single latch keyed on "any player anywhere" freezes the other
    /// team at 0 the moment one team has players and the other doesn't (everyone picked the same
    /// side; a 1v0 start; a host who joins a minute in), and PerPlayerAverage over a roster of 0
    /// permanently locks that team's Vanguard at tier 0. Two independent latches avoid that.
    /// Deliberate consequence: a team that is genuinely still empty keeps retrying every tick for
    /// as long as it stays empty. That is the correct trade — it must not lock out a team that
    /// fills in later — and the loop is bounded by session size (&lt;= 20 players) with an O(1)
    /// body per player, so the per-tick cost of the retry is negligible.
    /// </summary>
    private void CaptureRosterSizes()
    {
        int t1 = 0;
        int t2 = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
                continue;

            PlayerTeamData team = playerObject.GetComponent<PlayerTeamData>();
            if (team == null) continue;

            if (team.Team == Team.Team1) t1++;
            else if (team.Team == Team.Team2) t2++;
        }

        // Each team latches independently, and only once it has actually found someone to count.
        // Never write a team's networked fields on a tick where that team's count is zero — that
        // would be a needless networked write, and would also latch a team that is still empty.
        if (!Team1RosterCaptured && t1 > 0)
        {
            Team1RosterSize = (byte)Mathf.Clamp(t1, 0, 255);
            Team1RosterCaptured = true;
        }

        if (!Team2RosterCaptured && t2 > 0)
        {
            Team2RosterSize = (byte)Mathf.Clamp(t2, 0, 255);
            Team2RosterCaptured = true;
        }
    }

    /// <summary>
    /// Adds points to a team's score.
    /// Handles multiple team naming conventions: Team1/Blue and Team2/Red
    /// RPC so any client can request adding points, but only server executes
    /// </summary>
    /// <param name="team">The team receiving points</param>
    /// <param name="points">Number of points to add</param>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddPoints(string team, int points)
    {
        // RpcTargets.StateAuthority means this body only runs on the server.
        if (!HasStateAuthority) return;

        Team scoring = TeamUtil.Normalize(team);

        if (scoring == Team.Team1)
        {
            Team1Score += points;
        }
        else if (scoring == Team.Team2)
        {
            Team2Score += points;
        }
        else
        {
            Debug.LogError($"[SERVER] Unrecognized team: '{team}'. Expected Team1, Team2, Blue, or Red.");
        }
    }

    /// <summary>
    /// Local version for backward compatibility - calls RPC
    /// </summary>
    public void AddPoints(string team, int points)
    {
        RPC_AddPoints(team, points);
    }

    /// <summary>
    /// Current Vanguard tier (0 = locked, 1 = half the territorial debuff removed, 2 = all of it).
    /// Derived on query from networked state — never stored.
    /// </summary>
    public int VanguardTier(Team team)
    {
        int score;
        int roster;

        if (team == Team.Team1)
        {
            score = Team1Score;
            roster = Team1RosterSize;
        }
        else if (team == Team.Team2)
        {
            score = Team2Score;
            roster = Team2RosterSize;
        }
        else
        {
            return 0; // Team3AI and None have no economy.
        }

        return TeamBuffUnlock.TeamTier(vanguardThresholds, score, roster, vanguardMaxTier,
                                       allUnlocked: SuddenDeathMaxesTiers);
    }

    /// <summary>Vanguard's top tier, so the HUD can size its pip row without hard-coding 2.</summary>
    public int VanguardMaxTier => vanguardMaxTier;

    public int ScoreOf(Team team) =>
        team == Team.Team1 ? Team1Score : (team == Team.Team2 ? Team2Score : 0);

    public int RosterSizeOf(Team team) =>
        team == Team.Team1 ? Team1RosterSize : (team == Team.Team2 ? Team2RosterSize : 0);

    /// <summary>
    /// The team's deposited value PER PLAYER — the unit Vanguard thresholds are authored in, and
    /// therefore the only number the HUD should ever compare against them. The raw team score is
    /// not comparable across roster sizes.
    /// </summary>
    public int PerPlayerAverageOf(Team team) =>
        TeamBuffUnlock.PerPlayerAverage(ScoreOf(team), RosterSizeOf(team));

    /// <summary>Per-player average at which this team's Vanguard next tiers up; 0 when maxed.</summary>
    public int NextVanguardAverage(Team team) =>
        BuffProgress.NextThresholdFor(vanguardThresholds, PerPlayerAverageOf(team), 0, 1);

    /// <summary>HUD fill 0..1 toward the next Vanguard milestone; 1 when maxed.</summary>
    public float VanguardProgress01(Team team) =>
        BuffProgress.ToNextTier01(vanguardThresholds, PerPlayerAverageOf(team), 0, 1);

    /// <summary>
    /// Sudden Death forces Vanguard to its top tier, matching PlayerBuffs.TierOf on the individual
    /// side. Derived from MatchManager's [Networked] Phase, so it resolves identically on clients
    /// and during resimulation, adds no state, and needs nothing reset when the phase ends.
    /// </summary>
    private bool SuddenDeathMaxesTiers =>
        MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
}
