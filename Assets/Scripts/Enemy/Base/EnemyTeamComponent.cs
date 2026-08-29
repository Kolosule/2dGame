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

    /// <summary>Stores the authoritative fallback without doing presentation work.</summary>
    public void SetTeam(Team team)
    {
        teamID = TeamUtil.ToId(team);
    }

    private void Start()
    {
        ApplyTeamVisual(Team);
    }

    /// <summary>Client presentation only: color the enemy for its replicated team.</summary>
    public void ApplyTeamVisual(Team team)
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        if (spriteRenderer == null || TeamManager.Instance == null) return;
        TeamData teamData = TeamManager.Instance.GetTeamData(team);
        if (teamData != null)
        {
            spriteRenderer.color = teamData.teamColor;
        }
    }
}
