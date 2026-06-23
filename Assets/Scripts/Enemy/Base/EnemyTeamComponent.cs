using UnityEngine;

/// <summary>
/// Attach this to each enemy to define their team and territorial position
/// </summary>
public class EnemyTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Which team this enemy belongs to (Team1, Team2, or Team3 for AI)")]
    public string teamID = "Team1";

    [Header("Territorial Advantage")]
    [Tooltip("Territorial advantage: -1 (at enemy base) to +1 (at own base). 0 = neutral ground")]
    [Range(-1f, 1f)]
    public float territorialAdvantage = 0f;

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    /// <summary>The team this enemy belongs to, derived from the authored teamID string.</summary>
    public Team Team => TeamUtil.Normalize(teamID);

    private void Start()
    {
        // Optional: Color the enemy based on team
        if (spriteRenderer != null && TeamManager.Instance != null)
        {
            TeamData teamData = TeamManager.Instance.GetTeamData(Team);
            if (teamData != null)
            {
                spriteRenderer.color = teamData.teamColor;
            }
        }
    }
}