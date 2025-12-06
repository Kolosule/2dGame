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

    // Track which team each client should be on
    private System.Collections.Generic.Dictionary<ulong, int> clientTeams = new System.Collections.Generic.Dictionary<ulong, int>();

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

        // Double-check that callback is registered
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
        // 12/5/2025 AI-Tag
        // This was created with the help of Assistant, a Unity Artificial Intelligence product.

        if (team1.spawnPoints != null)
        {
            foreach (var sp in team1.spawnPoints)
            {
                if (sp != null)
                {
                    Debug.Log($"Spawn Point: {sp.name}, Position: {sp.position}");
                }
                else
                {
                    Debug.LogError("One of the spawn points in Team 1 is null!");
                }
            }
        }
        else
        {
            Debug.LogError("Team 1 spawn points array is null!");
        }
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log($"Server: Approving player connection for client {request.ClientNetworkId}...");

        // Approve the connection
        response.Approved = true;
        response.CreatePlayerObject = true;

        // Determine which team (0 = Team1, 1 = Team2)
        int teamId = autoBalanceTeams ? GetBalancedTeamId() : 0;
        string assignedTeam = teamId == 0 ? team1.teamName : team2.teamName;

        // Store this client's team assignment
        clientTeams[request.ClientNetworkId] = teamId;

        // Get spawn position for this team
        Vector3 spawnPosition = GetSpawnPositionForTeam(teamId);

        // Set spawn position
        response.Position = spawnPosition;
        response.Rotation = Quaternion.identity;

        Debug.Log($"✓ Client {request.ClientNetworkId} assigned to {assignedTeam} (ID: {teamId}) at {spawnPosition}");
    }

    private int GetBalancedTeamId()
    {
        if (team1PlayerCount <= team2PlayerCount)
        {
            team1PlayerCount++;
            Debug.Log($"Assigned to Team1. Count: Team1={team1PlayerCount}, Team2={team2PlayerCount}");
            return 0;
        }
        else
        {
            team2PlayerCount++;
            Debug.Log($"Assigned to Team2. Count: Team1={team1PlayerCount}, Team2={team2PlayerCount}");
            return 1;
        }
    }

    private Vector3 GetSpawnPositionForTeam(int teamId)
    {
        TeamSpawnPoints teamSpawns = teamId == 0 ? team1 : team2;
        int teamCount = teamId == 0 ? team1PlayerCount : team2PlayerCount;

        if (teamSpawns.spawnPoints == null || teamSpawns.spawnPoints.Length == 0)
        {
            Debug.LogError($"⚠️ No spawn points for team {teamId}! Using Vector3.zero");
            return Vector3.zero;
        }

        int spawnIndex = teamCount % teamSpawns.spawnPoints.Length;

        if (teamSpawns.spawnPoints[spawnIndex] == null)
        {
            Debug.LogError($"⚠️ Spawn point at index {spawnIndex} is null for team {teamId}!");
            return Vector3.zero;
        }

        Vector3 position = teamSpawns.spawnPoints[spawnIndex].position;
        Debug.Log($"✓ Spawn position for team {teamId}: {position} (using spawn point index {spawnIndex})");

        return position;
    }

    /// <summary>
    /// Get the team ID that was assigned to a specific client
    /// </summary>
    public int GetTeamForClient(ulong clientId)
    {
        if (clientTeams.ContainsKey(clientId))
        {
            return clientTeams[clientId];
        }

        Debug.LogWarning($"No team assignment found for client {clientId}, defaulting to Team1");
        return 0;
    }

    public Vector3 GetSpawnPosition(string teamName)
    {
        int teamId = teamName == team1.teamName ? 0 : 1;
        return GetSpawnPositionForTeam(teamId);
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
            cameraFollow = cam;
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