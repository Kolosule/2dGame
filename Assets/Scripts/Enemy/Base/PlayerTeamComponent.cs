using UnityEngine;

/// <summary>
/// Player gameplay/visual logic keyed off the networked team. The team value itself lives on the
/// co-located PlayerTeamData (single source of truth); this component derives from it.
/// </summary>
[RequireComponent(typeof(PlayerTeamData))]
public class PlayerTeamComponent : MonoBehaviour
{
    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool colorizePlayer = true;

    private PlayerTeamData teamData;

    /// <summary>The player's team, read from the networked source of truth.</summary>
    public Team Team => teamData != null ? teamData.Team : Team.None;

    private void Awake()
    {
        teamData = GetComponent<PlayerTeamData>();
    }

    /// <summary>Called by PlayerTeamData when the networked team changes (or on spawn).</summary>
    public void OnTeamChanged()
    {
        if (Team == Team.None) return;
        ApplyTeamColor();
    }

    private void ApplyTeamColor()
    {
        if (!colorizePlayer || spriteRenderer == null || TeamManager.Instance == null) return;

        TeamData data = TeamManager.Instance.GetTeamData(Team);
        if (data != null)
        {
            spriteRenderer.color = data.teamColor;
        }
    }

    public bool IsSameTeam(PlayerTeamComponent other)
    {
        return other != null && Team != Team.None && Team == other.Team;
    }

    public bool IsOnTeam(Team checkTeam)
    {
        return Team == checkTeam;
    }
}
