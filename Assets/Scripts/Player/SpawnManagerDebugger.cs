using UnityEngine;
using Fusion;

/// <summary>
/// Add this script to the same GameObject as NetworkedSpawnManager
/// to get detailed server-side spawn logs
/// </summary>
public class SpawnManagerDebugger : MonoBehaviour
{
    private void Update()
    {
        // Press F1 to dump current state
        if (Input.GetKeyDown(KeyCode.F1))
        {
            DumpState();
        }
    }

    private void DumpState()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log("📊 SPAWN MANAGER STATE DUMP");

        if (NetworkedSpawnManager.Instance == null)
        {
            Debug.LogError("❌ NetworkedSpawnManager.Instance is NULL!");
            return;
        }

        Debug.Log("✅ NetworkedSpawnManager exists");

        // Check spawn points
        var manager = NetworkedSpawnManager.Instance;

        // We can't access private fields directly, but we can check if spawn points are assigned
        // by trying to get a spawn position
        Vector3 team1Spawn = Vector3.zero;
        Vector3 team2Spawn = Vector3.zero;

        try
        {
            team1Spawn = manager.GetSpawnPosition(1);
            Debug.Log($"Team 1 Spawn Position: {team1Spawn}");
            if (team1Spawn == Vector3.zero)
            {
                Debug.LogError("⚠️ Team 1 spawn position is (0,0,0)! Check spawn points!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error getting Team 1 spawn: {e.Message}");
        }

        try
        {
            team2Spawn = manager.GetSpawnPosition(2);
            Debug.Log($"Team 2 Spawn Position: {team2Spawn}");
            if (team2Spawn == Vector3.zero)
            {
                Debug.LogError("⚠️ Team 2 spawn position is (0,0,0)! Check spawn points!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error getting Team 2 spawn: {e.Message}");
        }

        // Check for all players in scene
        var players = FindObjectsByType<PlayerTeamData>(FindObjectsSortMode.None);
        Debug.Log($"Players in scene: {players.Length}");

        foreach (var player in players)
        {
            Debug.Log($"  - {player.gameObject.name}: Team {player.Team}, Position {player.transform.position}");
        }

        Debug.Log("═══════════════════════════════════════");
    }
}