using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;

/// <summary>
/// One row of the scoreboard panel: one player's rank, identity, state, and stats.
///
/// The row belongs to a merged, rank-ordered list of every player -- team membership is shown only
/// by the thin colour stripe down the row's left edge, so the row background stays neutral and the
/// stat text keeps its contrast. The self outline marks the viewing client's own row so it is
/// findable in a 20-player list; it is a bright neutral on purpose, so it never reads as a third team.
/// </summary>
public class ScoreboardRowView : MonoBehaviour
{
    [Tooltip("Thin vertical bar at the far left, tinted with the player's team colour.")]
    [SerializeField] private Image teamStripe;

    [Tooltip("Highlight behind the local player's row. Toggled by Paint; never tinted a team colour.")]
    [SerializeField] private Image selfOutline;

    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI kdText;
    [SerializeField] private TextMeshProUGUI capturesText;
    [SerializeField] private TextMeshProUGUI coinsText;
    [SerializeField] private TextMeshProUGUI carryTimeText;
    [SerializeField] private TextMeshProUGUI returnsText;
    [SerializeField] private Image deadIcon;
    [SerializeField] private Image carryIcon;

    public void Paint(ScoreboardRow row)
    {
        // Full alpha regardless of what the team asset authored: the stripe is the only team signal
        // left on the board, so it must not be washed out by a translucent teamColor.
        if (teamStripe != null) teamStripe.color = new Color(row.TeamColorR, row.TeamColorG, row.TeamColorB, 1f);
        if (selfOutline != null) selfOutline.enabled = row.IsLocalPlayer;
        if (rankText != null) rankText.text = $"{row.Rank}.";
        if (nameText != null) nameText.text = row.DisplayName;
        if (scoreText != null) scoreText.text = Mathf.RoundToInt(row.OverallScore).ToString();
        if (kdText != null) kdText.text = $"{row.Kills}/{row.Deaths}";
        if (capturesText != null) capturesText.text = row.Captures.ToString();
        if (coinsText != null) coinsText.text = row.CoinsDeposited.ToString();
        if (carryTimeText != null) carryTimeText.text = FormatSeconds(row.FlagCarrySeconds);
        if (returnsText != null) returnsText.text = row.FlagReturns.ToString();
        if (deadIcon != null) deadIcon.enabled = row.IsDead;
        if (carryIcon != null) carryIcon.enabled = row.IsCarryingFlag;
    }

    private static string FormatSeconds(int totalSeconds)
    {
        int m = totalSeconds / 60;
        int s = totalSeconds % 60;
        return $"{m}:{s:00}";
    }
}
