using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;

/// <summary>One row of the scoreboard panel: one player's identity, state, and stats.</summary>
public class ScoreboardRowView : MonoBehaviour
{
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
