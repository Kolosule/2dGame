using UnityEngine;
using UnityEngine.InputSystem;
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
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            DumpState();
        }
    }

    private void DumpState()
    {

        if (NetworkedSpawnManager.Instance == null)
        {
            Debug.LogError("❌ NetworkedSpawnManager.Instance is NULL!");
            return;
        }


        // Check spawn points
        var manager = NetworkedSpawnManager.Instance;

        // We can't access private fields directly, but we can check if spawn points are assigned
        // by trying to get a spawn position
        Vector3 team1Spawn = Vector3.zero;
        Vector3 team2Spawn = Vector3.zero;

        try
        {
            team1Spawn = manager.GetSpawnPosition(1);
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

        foreach (var player in players)
        {
        }

    }
}