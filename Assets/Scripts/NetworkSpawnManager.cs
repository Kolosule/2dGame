// 12/4/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawn positions for networked players
/// Works with NetworkManager's automatic player spawning
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour
{
    public CameraFollow cameraFollow; // Reference to the CameraFollow script
    [System.Serializable]
    public class TeamSpawnPoints
    {
        public string teamName = "Team1";
        public Transform[] spawnPoints;
    }

    [Header("Team Spawn Configuration")]
    [SerializeField] private TeamSpawnPoints team1;
    [SerializeField] private TeamSpawnPoints team2;

    [Header("Team Assignment")]
    [SerializeField] private bool autoBalanceTeams = true;

    private int team1PlayerCount = 0;
    private int team2PlayerCount = 0;

    public static NetworkedSpawnManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Only the server/host handles connection approval and spawn positions
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        }
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        // Approve the connection
        response.Approved = true;
        response.CreatePlayerObject = true;

        // Determine which team this player joins
        string assignedTeam = autoBalanceTeams ? GetBalancedTeam() : "Team1";
        Vector3 spawnPosition = GetSpawnPosition(assignedTeam);

        // Set the spawn position
        response.Position = spawnPosition;
        response.Rotation = Quaternion.identity;

        Debug.Log($"Player approved. Spawning at {spawnPosition} on {assignedTeam}");
    }

    /// <summary>
    /// Get the team with fewer players for auto-balancing
    /// </summary>
    // 12/4/2025 AI-Tag
    // This was created with the help of Assistant, a Unity Artificial Intelligence product.

    private string GetBalancedTeam()
    {
        if (team1PlayerCount <= team2PlayerCount)
        {
            team1PlayerCount++;
            Debug.Log($"Assigned to Team1. Team1 count: {team1PlayerCount}, Team2 count: {team2PlayerCount}");
            return team1.teamName;
        }
        else
        {
            team2PlayerCount++;
            Debug.Log($"Assigned to Team2. Team1 count: {team1PlayerCount}, Team2 count: {team2PlayerCount}");
            return team2.teamName;
        }
    }

    /// <summary>
    /// Get a spawn position for the specified team
    /// </summary>
    public Vector3 GetSpawnPosition(string teamName)
    {
        TeamSpawnPoints teamSpawns = teamName == team1.teamName ? team1 : team2;

        if (teamSpawns.spawnPoints == null || teamSpawns.spawnPoints.Length == 0)
        {
            Debug.LogWarning($"No spawn points for {teamName}, using default position");
            return Vector3.zero;
        }

        // Get the current team count to cycle through spawn points
        int teamCount = teamName == team1.teamName ? team1PlayerCount : team2PlayerCount;
        int spawnIndex = (teamCount - 1) % teamSpawns.spawnPoints.Length;

        return teamSpawns.spawnPoints[spawnIndex].position;
    }

    /// <summary>
    /// Get a respawn position for a player's team
    /// Call this when a player dies and needs to respawn
    /// </summary>
    public Vector3 GetRespawnPosition(string teamName)
    {
        return GetSpawnPosition(teamName);
    }

    /// <summary>
    /// Updates the camera target to follow the newly spawned player
    /// </summary>
    public void UpdateCameraTarget(GameObject player)
    {
        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(player.transform);
        }
        else
        {
            Debug.LogWarning("CameraFollow reference is not set in NetworkedSpawnManager.");
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        }
    }
}