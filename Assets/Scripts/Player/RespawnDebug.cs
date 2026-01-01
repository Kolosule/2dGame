using UnityEngine;
using Fusion;

/// <summary>
/// Add this to your Player prefab to diagnose spawn issues
/// Will print detailed info about spawn position and team assignment
/// </summary>
public class PlayerSpawnDebugger : NetworkBehaviour
{
    private void Start()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log($"🎮 PLAYER START (Not networked yet)");
        Debug.Log($"Position: {transform.position}");
        Debug.Log($"Name: {gameObject.name}");
        Debug.Log("═══════════════════════════════════════");
    }

    public override void Spawned()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log($"🌐 PLAYER SPAWNED ON NETWORK");
        Debug.Log($"Time: {Time.time:F2}");
        Debug.Log($"Position: {transform.position}");
        Debug.Log($"Name: {gameObject.name}");
        Debug.Log($"HasInputAuthority: {HasInputAuthority}");
        Debug.Log($"HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"Object.InputAuthority: {Object.InputAuthority}");

        // Check if this is spawning at origin
        if (transform.position == Vector3.zero || transform.position.magnitude < 1f)
        {
            Debug.LogError("⚠️ PLAYER SPAWNED AT ORIGIN OR NEAR ORIGIN!");
            Debug.LogError("This suggests spawn position wasn't set correctly!");
        }

        // Check team assignment
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null)
        {
            Debug.Log($"PlayerTeamData.Team: {teamData.Team}");
            if (teamData.Team == 0)
            {
                Debug.LogError("⚠️ Team is 0! Team assignment hasn't happened yet!");
            }
        }
        else
        {
            Debug.LogError("❌ NO PlayerTeamData component!");
        }

        PlayerTeamComponent teamComp = GetComponent<PlayerTeamComponent>();
        if (teamComp != null)
        {
            Debug.Log($"PlayerTeamComponent.teamID: '{teamComp.teamID}'");
            if (string.IsNullOrEmpty(teamComp.teamID))
            {
                Debug.LogError("⚠️ TeamID is empty!");
            }
        }
        else
        {
            Debug.LogError("❌ NO PlayerTeamComponent!");
        }

        Debug.Log("═══════════════════════════════════════");
    }
}