using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Hud.Core;

/// <summary>
/// The merged Team Power strip: team scores, and Vanguard's tier, next milestone and the extra
/// damage the local player is currently taking — one surface, because they are one subject (how
/// strong is my team's position right now).
///
/// The extra-damage percentage sits on the Vanguard line because Vanguard is what reduces it: a
/// player watches the number fall as the team buys the vulnerability away, and learns the buff from
/// the thing it changes. There is deliberately no in-base/out-of-base band — the underlying malus
/// is continuous from the own base outward, so a percentage alone is the honest readout and a
/// two-state band could only ever approximate it. Kept as TeamScoreDisplay (not renamed) so the
/// component already wired into the Gameplay scene keeps its score-text references.
///
/// Event-driven off TeamScoreManager + MatchManager; both are runtime singletons, so subscription
/// is deferred until Instance exists. The only per-frame work is sampling the LOCAL player's
/// position (positions are not events) and comparing the resulting whole percent — repaints happen
/// on CHANGE only, so this costs one string build per percentage point crossed.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Feedback surfaces".
/// </summary>
public class TeamScoreDisplay : MonoBehaviour, IHudBindable
{
    [Header("Scores")]
    [SerializeField] private TextMeshProUGUI team1ScoreText;
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Tooltip("Label prefixed before each team's score, e.g. \"BLUE\" / \"RED\".")]
    [SerializeField] private string team1Label = "BLUE";
    [SerializeField] private string team2Label = "RED";

    [Header("Vanguard (this player's team)")]
    [Tooltip("Tier pips, index 0 = tier 1. Vanguard has two tiers.")]
    [SerializeField] private Image[] vanguardPips;
    [SerializeField] private Color pipFilledColor = new Color(1f, 0.86f, 0.40f);
    [SerializeField] private Color pipEmptyColor = new Color(1f, 1f, 1f, 0.18f);

    [Tooltip("Image Type = Filled. Progress toward the next Vanguard milestone.")]
    [SerializeField] private Image vanguardProgressFill;

    [Tooltip("Names the next milestone in its real unit (per-player average deposited value).")]
    [SerializeField] private TextMeshProUGUI vanguardMilestoneText;

    [Header("Unlock toast")]
    [SerializeField] private HudToastFeed toastFeed;

    private Team localTeam = Team.None;
    private Transform localPlayer;

    // Cached so RepaintVanguard can re-read the team if it was still None at Bind time. Not
    // known to be reachable today (NetworkedSpawnManager sets the team before the object is
    // discoverable), but PlayerHud binds exactly once, so a late assignment would otherwise leave
    // this surface permanently blank with nothing to self-heal it.
    private PlayerTeamData teamData;

    private TeamScoreManager scoreManager;
    private MatchManager matchManager;

    private TierUpEdge vanguardEdge;

    // Last percentage rendered onto the Vanguard line. -1 means "nothing painted yet", which is
    // distinct from a real 0% and so forces the first paint.
    private int extraDamagePercent = -1;

    public void Bind(HudContext ctx)
    {
        teamData = ctx.Team;
        localTeam = ctx.Team != null ? ctx.Team.Team : Team.None;
        localPlayer = ctx.Inventory != null ? ctx.Inventory.transform : null;
        extraDamagePercent = -1;
        // Manager subscriptions happen lazily in Update once the singletons are live.
    }

    public void Unbind()
    {
        if (scoreManager != null)
        {
            scoreManager.ScoresChanged -= RepaintScores;
            scoreManager.TeamBuffsChanged -= RepaintVanguard;
        }
        if (matchManager != null) matchManager.PhaseChanged -= RepaintVanguard;

        scoreManager = null;
        matchManager = null;
        localPlayer = null;
        teamData = null;
        vanguardEdge.Reset();
    }

    private void Update()
    {
        if (scoreManager == null)
        {
            TeamScoreManager mgr = TeamScoreManager.Instance;
            if (mgr != null && mgr.Object != null && mgr.Object.IsValid)
            {
                scoreManager = mgr;
                scoreManager.ScoresChanged += RepaintScores;
                scoreManager.TeamBuffsChanged += RepaintVanguard;
                RepaintScores();
                // Primes the edge detector, so joining mid-match never toasts.
                RepaintVanguard();
            }
        }

        if (matchManager == null && MatchManager.Instance != null)
        {
            matchManager = MatchManager.Instance;
            matchManager.PhaseChanged += RepaintVanguard;
        }
    }

    /// <summary>
    /// Sample the local player's own position and repaint only when the whole-percent readout
    /// changes. Position is the one value here that is not event-driven, so it is read on the
    /// render path; everything downstream is change-gated.
    /// </summary>
    private void LateUpdate()
    {
        if (localPlayer == null || localTeam == Team.None) return;

        // Wait for the score manager rather than assuming tier 0. Painting from a default tier
        // would show a late joiner a penalty their team has already bought away, then silently
        // correct itself a frame later. Nothing is lost by waiting: Update() calls RepaintVanguard()
        // the moment the manager resolves, and that clears the cache, so the first paint happens
        // as soon as the tier is actually known.
        if (scoreManager == null) return;

        TeamManager teams = TeamManager.Instance;
        if (teams == null) return;

        int tier = scoreManager.VanguardTier(localTeam);
        float ownBaseDistance01 = teams.GetOwnBaseDistance01(localTeam, localPlayer.position);
        int next = TerritoryReadout.ExtraDamagePercent(ownBaseDistance01, tier);

        if (next == extraDamagePercent) return;
        extraDamagePercent = next;
        RepaintVanguard();
    }

    private void RepaintScores()
    {
        if (scoreManager == null) return;
        if (team1ScoreText != null) team1ScoreText.text = $"{team1Label}  {scoreManager.Team1Score}";
        if (team2ScoreText != null) team2ScoreText.text = $"{team2Label}  {scoreManager.Team2Score}";
    }

    private void RepaintVanguard()
    {
        // Defensive re-read: if the team was still None at Bind time, pick up a late assignment
        // here rather than staying permanently blank. Not known to be reachable today; see the
        // comment on the teamData field.
        if (localTeam == Team.None && teamData != null) localTeam = teamData.Team;

        if (scoreManager == null || localTeam == Team.None) return;

        int tier = scoreManager.VanguardTier(localTeam);
        int max = scoreManager.VanguardMaxTier;

        TierPipRow.Paint(vanguardPips, tier, max, pipFilledColor, pipEmptyColor);

        if (vanguardProgressFill != null)
            vanguardProgressFill.fillAmount = scoreManager.VanguardProgress01(localTeam);

        if (vanguardMilestoneText != null)
        {
            int next = scoreManager.NextVanguardAverage(localTeam);
            string milestone = next > 0
                ? $"VANGUARD T{tier}   {scoreManager.PerPlayerAverageOf(localTeam)}/{next}"
                : $"VANGUARD T{tier}   MAX";
            // Suppressed until LateUpdate has sampled a real position, so the line never briefly
            // claims +0% before the first sample lands.
            vanguardMilestoneText.text = extraDamagePercent >= 0
                ? $"{milestone}   +{extraDamagePercent}% DAMAGE TAKEN"
                : milestone;
        }

        // Sudden Death maxes Vanguard for both teams at once; its banner announces that. Resolved
        // via the static Instance (not the lazily-cached matchManager field) so this matches
        // BuffIconDisplay's check and doesn't depend on Update() having already run this frame.
        bool suddenDeath = MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
        if (vanguardEdge.Observe(tier) && !suddenDeath && toastFeed != null)
            toastFeed.Show($"VANGUARD  T{tier}");
    }

    private void OnDisable() => Unbind();
}
