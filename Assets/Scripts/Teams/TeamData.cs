using UnityEngine;

[CreateAssetMenu(fileName = "NewTeam", menuName = "Game/Team Data")]
public class TeamData : ScriptableObject
{
    [Header("Team Identity")]
    public string teamName;
    public Color teamColor = Color.white;

    [Header("Base Location")]
    public Vector2 basePosition;

    [Header("Team Identifier")]
    [Tooltip("Use 'Team1', 'Team2', or 'Team3' (AI)")]
    public string teamID;

    [Header("Team Type")]
    [Tooltip("Is this an AI/NPC team?")]
    public bool isAITeam = false;

    [Header("Damage Modifiers")]
    [Tooltip("Custom damage scaling for this team (leave at 1.0 to use global settings)")]
    [Range(0.1f, 3.0f)]
    public float damageMultiplier = 1.0f;

    [Tooltip("Custom defense scaling for this team (leave at 1.0 to use global settings)")]
    [Range(0.1f, 3.0f)]
    public float defenseMultiplier = 1.0f;

    [Header("Spawn Settings")]
    [Tooltip("Respawn delay in seconds")]
    public float respawnDelay = 3f;

    [Tooltip("Should players on this team use territorial advantage?")]
    public bool usesTerritorialAdvantage = true;

    [Header("Visual Settings")]
    public Sprite teamIcon;
    public Material teamMaterial;

    [TextArea(3, 5)]
    public string teamDescription;
}