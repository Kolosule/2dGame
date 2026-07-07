using UnityEngine;
using TMPro;

/// <summary>
/// Team score readout plus a single badge shown only while the local player's team has an active
/// coin-milestone buff (damage or defense). Event-driven off TeamScoreManager; the manager is a
/// runtime singleton, so subscription is deferred until Instance exists.
/// </summary>
public class TeamScoreDisplay : MonoBehaviour, IHudBindable
{
    [SerializeField] private TextMeshProUGUI team1ScoreText;
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Tooltip("Badge shown only when THIS player's team has an active team buff.")]
    [SerializeField] private GameObject teamBuffBadge;

    private Team localTeam = Team.None;
    private TeamScoreManager scoreManager;

    public void Bind(HudContext ctx)
    {
        localTeam = ctx.Team != null ? ctx.Team.Team : Team.None;
        if (teamBuffBadge != null) teamBuffBadge.SetActive(false);
        // Manager subscription happens lazily in Update once Instance is live.
    }

    public void Unbind()
    {
        if (scoreManager != null)
        {
            scoreManager.ScoresChanged -= RepaintScores;
            scoreManager.TeamBuffsChanged -= RepaintBadge;
        }
        scoreManager = null;
    }

    private void Update()
    {
        if (scoreManager != null) return;

        TeamScoreManager mgr = TeamScoreManager.Instance;
        if (mgr == null || mgr.Object == null || !mgr.Object.IsValid) return;

        scoreManager = mgr;
        scoreManager.ScoresChanged += RepaintScores;
        scoreManager.TeamBuffsChanged += RepaintBadge;
        RepaintScores();
        RepaintBadge();
    }

    private void RepaintScores()
    {
        if (scoreManager == null) return;
        if (team1ScoreText != null) team1ScoreText.text = scoreManager.Team1Score.ToString();
        if (team2ScoreText != null) team2ScoreText.text = scoreManager.Team2Score.ToString();
    }

    private void RepaintBadge()
    {
        if (scoreManager == null || teamBuffBadge == null) return;
        bool active = localTeam != Team.None &&
                      (scoreManager.HasDamageBuff(localTeam) || scoreManager.HasDefenseBuff(localTeam));
        teamBuffBadge.SetActive(active);
    }

    private void OnDisable() => Unbind();
}
