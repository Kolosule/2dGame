using UnityEngine;

/// <summary>
/// Temporary diagnostic script - attach to any GameObject to check TeamScoreManager
/// </summary>
public class TeamScoreManagerDiagnostic : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("=== TEAMSCOREMANAGER DIAGNOSTIC ===");

        // Find all TeamScoreManagers in scene
        TeamScoreManager[] managers = FindObjectsOfType<TeamScoreManager>();

        Debug.Log($"Number of TeamScoreManagers found: {managers.Length}");

        if (managers.Length == 0)
        {
            Debug.LogError("❌ NO TeamScoreManager found in scene!");
            Debug.LogError("FIX: Create an empty GameObject and add TeamScoreManager component");
        }
        else if (managers.Length > 1)
        {
            Debug.LogWarning($"⚠️ Found {managers.Length} TeamScoreManagers - should only have 1!");
            for (int i = 0; i < managers.Length; i++)
            {
                Debug.LogWarning($"  Manager {i + 1}: {managers[i].gameObject.name}");
            }
        }
        else
        {
            Debug.Log($"✓ Found exactly 1 TeamScoreManager: {managers[0].gameObject.name}");
            Debug.Log($"  - Team1 Score: {managers[0].Team1Score}");
            Debug.Log($"  - Team2 Score: {managers[0].Team2Score}");
            Debug.Log($"  - Damage Buff (Team1): {managers[0].Team1DamageBuff}");
            Debug.Log($"  - Defense Buff (Team1): {managers[0].Team1DefenseBuff}");
        }

        Debug.Log("=== END DIAGNOSTIC ===");

        // Destroy this script after diagnostic
        Destroy(this);
    }
}