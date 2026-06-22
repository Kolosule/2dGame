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
    }

    public override void Spawned()
    {

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
            if (teamData.Team == Team.None)
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
            if (teamComp.Team == Team.None)
            {
                Debug.LogError("⚠️ Team is None!");
            }
        }
        else
        {
            Debug.LogError("❌ NO PlayerTeamComponent!");
        }

    }
}