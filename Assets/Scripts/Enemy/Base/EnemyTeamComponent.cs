using UnityEngine;

/// <summary>
/// Attach this to each enemy to define their team and territorial position
/// </summary>
public class EnemyTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Authored fallback (scene-placed enemies). Spawned enemies get their team " +
             "from Enemy's [Networked] Team via ApplyTeam.")]
    public string teamID = "Team1";

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Enemy enemy;

    /// <summary>
    /// This enemy's team. Prefers the networked Enemy.Team (correct on every client);
    /// falls back to the authored teamID string for scene-placed/unspawned enemies.
    /// </summary>
    public Team Team
    {
        get
        {
            if (enemy == null) enemy = GetComponent<Enemy>();
            if (enemy != null && enemy.Object != null && enemy.Object.IsValid &&
                enemy.Team != Team.None)
            {
                return enemy.Team;
            }
            return TeamUtil.Normalize(teamID);
        }
    }

    /// <summary>Called by Enemy when the networked team changes: sync the string + color.</summary>
    public void ApplyTeam(Team team)
    {
        teamID = TeamUtil.ToId(team);
        ApplyTeamColor(team);
    }

    private void Start()
    {
        ApplyTeamColor(Team);
    }

    private void ApplyTeamColor(Team team)
    {
        if (spriteRenderer == null || TeamManager.Instance == null) return;
        TeamData teamData = TeamManager.Instance.GetTeamData(team);
        if (teamData != null)
        {
            spriteRenderer.color = teamData.teamColor;
        }
    }
}
