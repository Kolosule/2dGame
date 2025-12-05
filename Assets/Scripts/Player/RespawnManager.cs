using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages spawning and respawning for multiple players across teams
/// </summary>
public class MultiplayerRespawnManager : MonoBehaviour
{
    [System.Serializable]
    public class TeamSpawnData
    {
        public string teamID;
        public Transform[] spawnPoints;
        public GameObject playerPrefab;
    }

    [Header("Team Spawn Configuration")]
    [SerializeField] private TeamSpawnData team1SpawnData;
    [SerializeField] private TeamSpawnData team2SpawnData;

    [Header("Player Management")]
    [SerializeField] private int maxPlayersPerTeam = 5;

    // Track all active players
    private Dictionary<int, GameObject> activePlayers = new Dictionary<int, GameObject>();
    private int nextPlayerID = 0;

    void Start()
    {
        // For testing: spawn one player on each team
        GameObject player1 = SpawnPlayer("Team1", 0);

        // Set camera to follow first player
        CameraFollow cam = FindObjectOfType<CameraFollow>();
        if (cam != null && player1 != null)
        {
            cam.SetTarget(player1.transform);
        }

        SpawnPlayer("Team2", 0);
    }

    /// <summary>
    /// Spawn a player on a specific team
    /// </summary>
    /// <param name="teamID">Team to spawn on (Team1 or Team2)</param>
    /// <param name="playerIndex">Which player number (0-4 for up to 5 players)</param>
    /// <returns>The spawned player GameObject</returns>
    public GameObject SpawnPlayer(string teamID, int playerIndex)
    {
        TeamSpawnData spawnData = GetTeamSpawnData(teamID);

        if (spawnData == null)
        {
            Debug.LogError($"No spawn data found for team: {teamID}");
            return null;
        }

        if (spawnData.playerPrefab == null)
        {
            Debug.LogError($"Player prefab not assigned for team: {teamID}");
            return null;
        }

        if (spawnData.spawnPoints == null || spawnData.spawnPoints.Length == 0)
        {
            Debug.LogError($"No spawn points assigned for team: {teamID}");
            return null;
        }

        // Get spawn position (cycle through spawn points if multiple players)
        Transform spawnPoint = spawnData.spawnPoints[playerIndex % spawnData.spawnPoints.Length];

        // Spawn the player
        GameObject player = Instantiate(spawnData.playerPrefab, spawnPoint.position, Quaternion.identity);
        player.name = $"Player_{teamID}_{playerIndex}";

        // Assign team to player
        PlayerTeamComponent teamComponent = player.GetComponent<PlayerTeamComponent>();
        if (teamComponent == null)
        {
            teamComponent = player.AddComponent<PlayerTeamComponent>();
        }
        teamComponent.teamID = teamID;

        // Track player
        int playerID = nextPlayerID++;
        activePlayers[playerID] = player;

        // Set player ID on the PlayerStatsHandler
        PlayerStatsHandler statsHandler = player.GetComponent<PlayerStatsHandler>();
        if (statsHandler != null)
        {
            statsHandler.SetPlayerID(playerID);
        }

        Debug.Log($"Spawned {player.name} at {spawnPoint.position} for team {teamID}");

        return player;
    }

    /// <summary>
    /// Respawn a specific player at their team's spawn point
    /// </summary>
    public void RespawnPlayer(int playerID)
    {
        if (!activePlayers.ContainsKey(playerID))
        {
            Debug.LogWarning($"Player ID {playerID} not found!");
            return;
        }

        GameObject oldPlayer = activePlayers[playerID];
        if (oldPlayer == null)
        {
            Debug.LogWarning($"Player {playerID} reference is null!");
            return;
        }

        // Get team from old player
        PlayerTeamComponent teamComponent = oldPlayer.GetComponent<PlayerTeamComponent>();
        string teamID = teamComponent != null ? teamComponent.teamID : "Team1";

        // Store camera reference before destroying
        CameraFollow cam = FindObjectOfType<CameraFollow>();
        bool wasFollowingThisPlayer = cam != null && cam.Target == oldPlayer.transform;

        // Destroy old player
        Destroy(oldPlayer);

        // Spawn new player
        GameObject newPlayer = SpawnPlayer(teamID, playerID % maxPlayersPerTeam);

        // Update tracked player
        activePlayers[playerID] = newPlayer;

        // Update camera if it was following this player
        if (wasFollowingThisPlayer && cam != null)
        {
            cam.SetTarget(newPlayer.transform);
        }

        Debug.Log($"Respawned player {playerID} on team {teamID}");
    }

    /// <summary>
    /// Get the spawn data for a specific team
    /// </summary>
    private TeamSpawnData GetTeamSpawnData(string teamID)
    {
        if (team1SpawnData != null && team1SpawnData.teamID == teamID)
            return team1SpawnData;

        if (team2SpawnData != null && team2SpawnData.teamID == teamID)
            return team2SpawnData;

        return null;
    }

    /// <summary>
    /// Get all active players on a specific team
    /// </summary>
    public List<GameObject> GetTeamPlayers(string teamID)
    {
        List<GameObject> teamPlayers = new List<GameObject>();

        foreach (var player in activePlayers.Values)
        {
            if (player != null)
            {
                PlayerTeamComponent teamComponent = player.GetComponent<PlayerTeamComponent>();
                if (teamComponent != null && teamComponent.teamID == teamID)
                {
                    teamPlayers.Add(player);
                }
            }
        }

        return teamPlayers;
    }

    /// <summary>
    /// Remove a player from tracking (when they disconnect)
    /// </summary>
    public void RemovePlayer(int playerID)
    {
        if (activePlayers.ContainsKey(playerID))
        {
            GameObject player = activePlayers[playerID];
            if (player != null)
            {
                Destroy(player);
            }
            activePlayers.Remove(playerID);
        }
    }
}