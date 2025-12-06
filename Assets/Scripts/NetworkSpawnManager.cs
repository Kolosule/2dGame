using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawn positions for networked players
/// SETUP: Attach this to a GameObject in your Gameplay scene
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour
{
    public static NetworkedSpawnManager Instance { get; private set; }

    [Header("IMPORTANT: Drag your Main Camera here")]
    public CameraFollow cameraFollow;

    [System.Serializable]
    public class TeamSpawnPoints
    {
        public string teamName = "Team1";
        [Tooltip("Create empty GameObjects in your scene and drag them here")]
        public Transform[] spawnPoints;
    }

    [Header("Team 1 Spawn Setup")]
    [SerializeField] private TeamSpawnPoints team1;

    [Header("Team 2 Spawn Setup")]
    [SerializeField] private TeamSpawnPoints team2;

    [Header("Settings")]
    [SerializeField] private bool autoBalanceTeams = true;

    private int team1PlayerCount = 0;
    private int team2PlayerCount = 0;

    private void Awake()
    {
        // Initialize singleton FIRST before anything else
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Make this persist across scenes
        DontDestroyOnLoad(gameObject);

        Debug.Log("✓ NetworkedSpawnManager initialized and will persist across scenes");

        // Validate camera reference
        if (cameraFollow == null)
        {
            Debug.LogWarning("⚠️ CameraFollow not assigned in Inspector, will search when needed");
        }

        // IMPORTANT: Register connection approval callback in Awake, before anything spawns
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
            Debug.Log("✓ Connection approval callback registered in Awake");
        }
        else
        {
            Debug.LogWarning("⚠️ NetworkManager.Singleton not found in Awake, will retry in Start");
        }
    }

    private void Start()
    {
        // Search for camera if not already found
        if (cameraFollow == null)
        {
            cameraFollow = FindObjectOfType<CameraFollow>();
            if (cameraFollow != null)
            {
                Debug.Log("✓ CameraFollow found");
            }
        }

        // Validate spawn points
        if (team1.spawnPoints == null || team1.spawnPoints.Length == 0)
        {
            Debug.LogError("⚠️ NO SPAWN POINTS SET FOR TEAM 1! Add spawn points in Inspector.");
        }
        else
        {
            Debug.Log($"✓ Team 1 has {team1.spawnPoints.Length} spawn point(s) at positions:");
            foreach (var sp in team1.spawnPoints)
            {
                if (sp != null) Debug.Log($"  - {sp.name}: {sp.position}");
            }
        }

        if (team2.spawnPoints == null || team2.spawnPoints.Length == 0)
        {
            Debug.LogError("⚠️ NO SPAWN POINTS SET FOR TEAM 2! Add spawn points in Inspector.");
        }
        else
        {
            Debug.Log($"✓ Team 2 has {team2.spawnPoints.Length} spawn point(s) at positions:");
            foreach (var sp in team2.spawnPoints)
            {
                if (sp != null) Debug.Log($"  - {sp.name}: {sp.position}");
            }
        }

        // Double-check that callback is registered (in case NetworkManager wasn't ready in Awake)
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.ConnectionApprovalCallback == null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
                Debug.Log("✓ Connection approval callback registered in Start (fallback)");
            }
            else
            {
                Debug.Log("✓ Connection approval callback already registered");
            }
        }
        else
        {
            Debug.LogError("⚠️ NetworkManager.Singleton is still null in Start!");
        }
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log("Server: Approving player connection...");

        // Approve the connection
        response.Approved = true;
        response.CreatePlayerObject = true;

        // Determine which team
        string assignedTeam = autoBalanceTeams ? GetBalancedTeam() : team1.teamName;
        Vector3 spawnPosition = GetSpawnPosition(assignedTeam);

        // Set spawn position
        response.Position = spawnPosition;
        response.Rotation = Quaternion.identity;

        Debug.Log($"✓ Player spawning at {spawnPosition} on {assignedTeam}");

        // Store the team assignment for this player
        // We'll set this after spawn in a different way
    }

    private string GetBalancedTeam()
    {
        if (team1PlayerCount <= team2PlayerCount)
        {
            team1PlayerCount++;
            Debug.Log($"Assigned to {team1.teamName}. Count: Team1={team1PlayerCount}, Team2={team2PlayerCount}");
            return team1.teamName;
        }
        else
        {
            team2PlayerCount++;
            Debug.Log($"Assigned to {team2.teamName}. Count: Team1={team1PlayerCount}, Team2={team2PlayerCount}");
            return team2.teamName;
        }
    }

    public Vector3 GetSpawnPosition(string teamName)
    {
        TeamSpawnPoints teamSpawns = teamName == team1.teamName ? team1 : team2;

        if (teamSpawns.spawnPoints == null || teamSpawns.spawnPoints.Length == 0)
        {
            Debug.LogError($"⚠️ No spawn points for {teamName}! Using Vector3.zero");
            return Vector3.zero;
        }

        int teamCount = teamName == team1.teamName ? team1PlayerCount : team2PlayerCount;
        int spawnIndex = (teamCount - 1) % teamSpawns.spawnPoints.Length;

        if (teamSpawns.spawnPoints[spawnIndex] == null)
        {
            Debug.LogError($"⚠️ Spawn point at index {spawnIndex} is null for {teamName}!");
            return Vector3.zero;
        }

        Vector3 position = teamSpawns.spawnPoints[spawnIndex].position;
        Debug.Log($"✓ Spawn position for {teamName}: {position} (using spawn point index {spawnIndex})");

        return position;
    }

    /// <summary>
    /// Call this when a player spawns to set camera target
    /// </summary>
    public void SetupPlayerCamera(GameObject player)
    {
        // Try to use assigned camera first
        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(player.transform);
            Debug.Log($"✓ Camera now following {player.name}");
            return;
        }

        // If not assigned, search for it
        Debug.LogWarning("⚠️ CameraFollow not assigned in SpawnManager - searching for camera...");

        CameraFollow cam = FindObjectOfType<CameraFollow>();
        if (cam != null)
        {
            cam.SetTarget(player.transform);
            cameraFollow = cam; // Cache it for next time
            Debug.Log($"✓ Found camera and set target to {player.name}");
        }
        else
        {
            Debug.LogError("⚠️ Could not find CameraFollow script anywhere in scene!");
        }
    }

    /// <summary>
    /// Get which team a player should be on based on their spawn position
    /// </summary>
    public int GetTeamIDForPosition(Vector3 position)
    {
        // Check which team's spawn points are closest
        float minDistTeam1 = float.MaxValue;
        float minDistTeam2 = float.MaxValue;

        if (team1.spawnPoints != null)
        {
            foreach (var spawnPoint in team1.spawnPoints)
            {
                if (spawnPoint != null)
                {
                    float dist = Vector3.Distance(position, spawnPoint.position);
                    if (dist < minDistTeam1) minDistTeam1 = dist;
                }
            }
        }

        if (team2.spawnPoints != null)
        {
            foreach (var spawnPoint in team2.spawnPoints)
            {
                if (spawnPoint != null)
                {
                    float dist = Vector3.Distance(position, spawnPoint.position);
                    if (dist < minDistTeam2) minDistTeam2 = dist;
                }
            }
        }

        return minDistTeam1 < minDistTeam2 ? 0 : 1;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        }
    }
}