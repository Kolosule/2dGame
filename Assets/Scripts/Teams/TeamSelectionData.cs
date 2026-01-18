using Fusion;
using UnityEngine;

/// <summary>
/// Stores the player's team selection before spawning.
/// This is a simple static storage that persists the team choice
/// from the MainMenu scene to the Gameplay scene.
/// 
/// HOW IT WORKS:
/// 1. Player clicks "Join Team 1" or "Join Team 2" in MainMenu
/// 2. We store that choice here using SetLocalPlayerTeam()
/// 3. When NetworkedSpawnManager spawns the player, it reads this choice
/// 4. The team choice is then cleared for the next game session
/// 
/// IMPORTANT: This uses static storage which means it persists between scenes
/// but NOT between play sessions (which is exactly what we want!)
/// </summary>
public static class TeamSelectionData
{
    #region Team Storage

    /// <summary>
    /// Stores the local player's chosen team (1 or 2)
    /// 0 = no team chosen yet
    /// </summary>
    private static int localPlayerChosenTeam = 0;

    /// <summary>
    /// Tracks if the player has made a team selection
    /// </summary>
    private static bool hasTeamBeenChosen = false;

    #endregion

    #region Public Methods

    /// <summary>
    /// Call this when the player clicks a team button in the MainMenu.
    /// Stores their team choice to be used when spawning.
    /// 
    /// PARAMS:
    ///   teamNumber - 1 for Team 1, 2 for Team 2
    /// </summary>
    public static void SetLocalPlayerTeam(int teamNumber)
    {
        // Validate the team number
        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"❌ Invalid team number: {teamNumber}. Must be 1 or 2.");
            return;
        }

        // Store the team choice
        localPlayerChosenTeam = teamNumber;
        hasTeamBeenChosen = true;

        Debug.Log($"✅ ========================================");
        Debug.Log($"✅ TEAM SELECTED: Team {teamNumber}");
        Debug.Log($"✅ This choice will be used when spawning");
        Debug.Log($"✅ ========================================");
    }

    /// <summary>
    /// Gets the local player's chosen team.
    /// Called by NetworkedSpawnManager when spawning the player.
    /// 
    /// RETURNS: 1 for Team 1, 2 for Team 2, or 0 if no team chosen
    /// </summary>
    public static int GetLocalPlayerTeam()
    {
        if (!hasTeamBeenChosen)
        {
            Debug.LogWarning("⚠️ No team has been chosen yet! Returning 0.");
            return 0;
        }

        Debug.Log($"📖 Retrieved team choice: Team {localPlayerChosenTeam}");
        return localPlayerChosenTeam;
    }

    /// <summary>
    /// Checks if the player has chosen a team.
    /// Useful for validation before starting the game.
    /// 
    /// RETURNS: true if team has been chosen, false otherwise
    /// </summary>
    public static bool HasChosenTeam()
    {
        return hasTeamBeenChosen;
    }

    /// <summary>
    /// Clears the team selection.
    /// Call this after the player spawns or when returning to menu.
    /// </summary>
    public static void ClearTeamSelection()
    {
        localPlayerChosenTeam = 0;
        hasTeamBeenChosen = false;
        Debug.Log("🧹 Team selection cleared");
    }

    /// <summary>
    /// Resets all data. Call this when starting a new game session.
    /// </summary>
    public static void Reset()
    {
        ClearTeamSelection();
        Debug.Log("🔄 TeamSelectionData reset");
    }

    #endregion

    #region Debug Information

    /// <summary>
    /// Gets a debug string showing the current state.
    /// Useful for troubleshooting.
    /// </summary>
    public static string GetDebugInfo()
    {
        return $"Team Choice: {localPlayerChosenTeam}, Has Chosen: {hasTeamBeenChosen}";
    }

    #endregion
}