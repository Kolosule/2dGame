using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using Game.Hud.Core;
using Game.Stats.Core;

/// <summary>
/// Renders every active player's stats as ONE merged, rank-ordered individual leaderboard --
/// deliberately not two team columns. A player's team is conveyed by the colour stripe down the
/// left edge of their row, so the board reads as "here is where I stand among everyone" rather than
/// "here are two rosters". A compact team-score line above the list keeps team standing visible now
/// that the BLUE/RED column headers are gone.
///
/// Shown on-demand (hold Tab, wired by ScoreboardInputReader) and auto-shown during
/// MatchPhase.PostMatch (wired by MatchPhaseHud). Reads MatchStatsManager.Entries directly on the
/// render path while visible -- no per-tick simulation work, no polling while hidden.
/// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md.
/// </summary>
public class ScoreboardPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private ScoreboardRowView rowTemplate;

    [Header("Team score summary")]
    [Tooltip("Team 1's half of the summary above the list. Tinted with that team's colour.")]
    [SerializeField] private TextMeshProUGUI team1ScoreText;

    [Tooltip("Team 2's half of the summary above the list. Tinted with that team's colour.")]
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Tooltip("Label prefixed before each team's score -- matches TeamScoreDisplay's wording.")]
    [SerializeField] private string team1Label = "BLUE";
    [SerializeField] private string team2Label = "RED";

    /// <summary>Stripe colour used when TeamManager or a TeamData asset is missing. A neutral grey,
    /// so an unwired scene degrades to "no team signal" instead of null-reffing or blanking rows.</summary>
    private static readonly Color FallbackTeamColor = new Color(0.62f, 0.64f, 0.70f, 1f);

    private readonly List<ScoreboardRowView> pool = new List<ScoreboardRowView>();

    // Reused across paints so the per-frame collect pass allocates nothing.
    private readonly List<ScoreboardRow> rows = new List<ScoreboardRow>();

    // Indexed by the Team enum's underlying int (0 = None .. 3 = Team3AI), refreshed once per paint
    // so a 20-row board costs 4 TeamData lookups, not 20.
    private readonly Color[] teamColors = new Color[4];

    // Last team scores painted. int.MinValue means "nothing painted yet", which is distinct from a
    // real 0 and so forces the first paint.
    private int lastTeam1Score = int.MinValue;
    private int lastTeam2Score = int.MinValue;

    private bool forcedVisible; // PostMatch auto-show
    private bool heldVisible;   // Tab held

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        SetVisible(false);
    }

    /// <summary>Local input reader calls this on the Scoreboard action's performed/canceled.</summary>
    public void SetHeld(bool held)
    {
        heldVisible = held;
        Repaint();
    }

    /// <summary>MatchPhaseHud calls this to force the board open for the whole PostMatch phase.</summary>
    public void SetForcedVisible(bool visible)
    {
        forcedVisible = visible;
        Repaint();
    }

    /// <summary>
    /// Per-frame render-path read of already-replicated stats while the panel is held open --
    /// same "read networked state on the render path, gated on visibility" pattern as
    /// MatchPhaseHud.LateUpdate's countdown/timer text. Costs nothing while hidden (the default).
    /// </summary>
    private void LateUpdate()
    {
        if (!IsShowing()) return;
        PaintRows();
    }

    private void Repaint()
    {
        bool visible = heldVisible || forcedVisible;
        SetVisible(visible);
        // Paint immediately on the transition too, so there's no one-frame blank/stale flash
        // before LateUpdate runs.
        if (IsShowing()) PaintRows();
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    /// <summary>True only when the panel can actually display: wired up and currently requested
    /// visible. Guards both Repaint's immediate paint and LateUpdate's per-frame paint so a
    /// misconfigured (panelRoot == null) panel never pays for roster/sort/pool work it can't show.</summary>
    private bool IsShowing()
    {
        return panelRoot != null && (heldVisible || forcedVisible);
    }

    private void PaintRows()
    {
        MatchStatsManager manager = MatchStatsManager.Instance;
        // MatchStatsManager.Instance is assigned in Awake -- at scene load, strictly before Fusion
        // spawns scene NetworkObjects. NullChecksForNetworkedProperties is enabled for this project,
        // so reading Entries before the state block is allocated throws; Object.IsValid is the
        // project's established "has this NetworkObject actually spawned yet" guard (see
        // NetworkedSpawnManager.cs and TeamScoreDisplay.cs's lazy-bind checks).
        if (manager == null || manager.Object == null || !manager.Object.IsValid || manager.Runner == null) return;

        RefreshTeamColors();
        PaintTeamScores();

        rows.Clear();
        PlayerRef localPlayer = manager.Runner.LocalPlayer;

        foreach (PlayerRef player in manager.Runner.ActivePlayers)
        {
            if (!manager.TryGetEntry(player.PlayerId, out PlayerStatEntry entry)) continue;

            Color stripe = StripeColorFor(entry.Team);

            rows.Add(new ScoreboardRow
            {
                PlayerId = player.PlayerId,
                Team = entry.Team,
                DisplayName = entry.DisplayName.Value,
                IsDead = entry.IsDead,
                IsCarryingFlag = CTFGameManager.Instance != null && CTFGameManager.Instance.IsCarrying(player),
                IsLocalPlayer = player == localPlayer,
                Kills = entry.Kills,
                Deaths = entry.Deaths,
                Captures = entry.Captures,
                CoinsDeposited = entry.CoinsDeposited,
                FlagCarrySeconds = entry.FlagCarrySeconds,
                FlagReturns = entry.FlagReturns,
                TeamColorR = stripe.r,
                TeamColorG = stripe.g,
                TeamColorB = stripe.b,
                OverallScore = ScoreFormula.Compute(entry.Kills, entry.Deaths, entry.CoinsDeposited,
                    entry.FlagCarrySeconds, entry.FlagReturns, manager.Weights)
            });
        }

        PaintPool(ScoreboardSort.SortByScoreDescending(rows));
    }

    /// <summary>
    /// Paints the single merged list into the single pool. Views are hidden rather than destroyed,
    /// so a roster that shrinks and grows again reuses the same objects.
    /// </summary>
    private void PaintPool(List<ScoreboardRow> sorted)
    {
        if (rowTemplate == null || rowContainer == null) return;

        while (pool.Count < sorted.Count)
        {
            ScoreboardRowView view = Instantiate(rowTemplate, rowContainer);
            view.gameObject.SetActive(true);
            pool.Add(view);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            bool active = i < sorted.Count;
            pool[i].gameObject.SetActive(active);
            if (!active) continue;

            // Rank is position in the merged list, so it can only be known after the sort.
            ScoreboardRow row = sorted[i];
            row.Rank = i + 1;
            pool[i].Paint(row);
        }
    }

    /// <summary>
    /// Resolves every team's stripe colour once per paint. TeamManager is a runtime singleton that
    /// may not exist yet (or at all, in a stripped test scene), and GetTeamData returns null for an
    /// unconfigured slot -- both fall back to a neutral grey rather than blanking the board.
    /// Follows FlagDirectionHud.ApplyColor's TeamManager.Instance -> GetTeamData -> teamColor lookup.
    /// </summary>
    private void RefreshTeamColors()
    {
        TeamManager teams = TeamManager.Instance;
        for (int i = 0; i < teamColors.Length; i++)
        {
            TeamData data = teams != null ? teams.GetTeamData((Team)i) : null;
            teamColors[i] = data != null ? data.teamColor : FallbackTeamColor;
        }
    }

    private Color StripeColorFor(int team)
    {
        return team >= 0 && team < teamColors.Length ? teamColors[team] : FallbackTeamColor;
    }

    /// <summary>
    /// The compact "BLUE 2 - RED 1" line above the list, reading the same TeamScoreManager the
    /// in-match TeamScoreDisplay strip reads. Guarded exactly like MatchStatsManager above, because
    /// it is the same class of scene NetworkObject. Text is change-gated so a held-open board costs
    /// no string building while the scores sit still; the tint is reapplied every paint (Graphic's
    /// colour setter early-outs on an unchanged value) so a late-binding TeamManager still lands.
    /// </summary>
    private void PaintTeamScores()
    {
        if (team1ScoreText != null) team1ScoreText.color = StripeColorFor((int)Team.Team1);
        if (team2ScoreText != null) team2ScoreText.color = StripeColorFor((int)Team.Team2);

        TeamScoreManager scores = TeamScoreManager.Instance;
        if (scores == null || scores.Object == null || !scores.Object.IsValid) return;

        int team1 = scores.ScoreOf(Team.Team1);
        int team2 = scores.ScoreOf(Team.Team2);
        if (team1 == lastTeam1Score && team2 == lastTeam2Score) return;

        lastTeam1Score = team1;
        lastTeam2Score = team2;

        if (team1ScoreText != null) team1ScoreText.text = $"{team1Label}  {team1}";
        if (team2ScoreText != null) team2ScoreText.text = $"{team2Label}  {team2}";
    }
}
