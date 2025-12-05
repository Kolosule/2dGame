using UnityEngine;

/// <summary>
/// Temporary script to display team info on screen
/// Attach this to PlayerPrefab for testing
/// </summary>
public class DebugTeamDisplay : MonoBehaviour
{
    private PlayerTeamComponent teamComponent;

    void Start()
    {
        teamComponent = GetComponent<PlayerTeamComponent>();
    }

    void OnGUI()
    {
        if (teamComponent == null) return;

        // Display team info in top-left corner
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;

        string teamInfo = $"Team: {teamComponent.teamID}\n";
        teamInfo += $"Position: {transform.position}\n";

        float territorial = teamComponent.GetCurrentTerritorialAdvantage();
        teamInfo += $"Territorial Advantage: {territorial:F2}\n";

        float damageDealt = teamComponent.GetDamageDealtModifier();
        teamInfo += $"Damage Dealt Modifier: {damageDealt:F2}x\n";

        float damageReceived = teamComponent.GetDamageReceivedModifier();
        teamInfo += $"Damage Received Modifier: {damageReceived:F2}x";

        GUI.Label(new Rect(10, 10, 400, 150), teamInfo, style);
    }
}