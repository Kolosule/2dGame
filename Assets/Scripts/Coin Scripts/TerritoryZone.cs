using UnityEngine;

/// <summary>
/// Example script showing how to implement territory damage modifiers.
/// Attach this to territory zone GameObjects.
/// This is an EXAMPLE - integrate this logic into your existing damage system.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TerritoryZone : MonoBehaviour
{
    [Header("Territory Settings")]
    [Tooltip("Which team owns this territory")]
    [SerializeField] private string territoryTeam; // "Red"/"team2" or "Blue"/"team1"
    
    private void Start()
    {
        // Ensure collider is a trigger
        GetComponent<Collider2D>().isTrigger = true;
    }
    
    /// <summary>
    /// Example method: Calculate damage dealt BY a character in this territory
    /// Call this in your damage dealing code
    /// </summary>
    /// <param name="attackerTeam">The team of the character dealing damage</param>
    /// <param name="baseDamage">The base damage amount</param>
    /// <returns>Modified damage based on territory buffs</returns>
    public float CalculateOutgoingDamage(string attackerTeam, float baseDamage)
    {
        // Only modify damage if attacker is in their own territory
        bool inOwnTerritory = (attackerTeam == territoryTeam) ||
                              (attackerTeam == "Red" && territoryTeam == "team2") ||
                              (attackerTeam == "team2" && territoryTeam == "Red") ||
                              (attackerTeam == "Blue" && territoryTeam == "team1") ||
                              (attackerTeam == "team1" && territoryTeam == "Blue");
        
        if (!inOwnTerritory)
        {
            return baseDamage; // No territory bonus if not in own territory
        }
        
        // Get the damage multiplier from TeamScoreManager
        TeamScoreManager scoreManager = FindObjectOfType<TeamScoreManager>();
        if (scoreManager == null)
        {
            Debug.LogWarning("TeamScoreManager not found! Using default multiplier.");
            return baseDamage * 0.5f; // Default debuff
        }
        
        float multiplier = scoreManager.GetTerritoryDamageMultiplier(attackerTeam);
        float modifiedDamage = baseDamage * multiplier;
        
        Debug.Log($"Territory damage: {baseDamage} x {multiplier} = {modifiedDamage}");
        
        return modifiedDamage;
    }
    
    /// <summary>
    /// Example method: Calculate damage taken BY a character in this territory
    /// Call this in your damage receiving code
    /// </summary>
    /// <param name="defenderTeam">The team of the character taking damage</param>
    /// <param name="incomingDamage">The damage about to be received</param>
    /// <returns>Modified damage based on territory buffs</returns>
    public float CalculateIncomingDamage(string defenderTeam, float incomingDamage)
    {
        // Only modify damage if defender is in their own territory
        bool inOwnTerritory = (defenderTeam == territoryTeam) ||
                              (defenderTeam == "Red" && territoryTeam == "team2") ||
                              (defenderTeam == "team2" && territoryTeam == "Red") ||
                              (defenderTeam == "Blue" && territoryTeam == "team1") ||
                              (defenderTeam == "team1" && territoryTeam == "Blue");
        
        if (!inOwnTerritory)
        {
            return incomingDamage; // No territory bonus if not in own territory
        }
        
        // Get the defense multiplier from TeamScoreManager
        TeamScoreManager scoreManager = FindObjectOfType<TeamScoreManager>();
        if (scoreManager == null)
        {
            Debug.LogWarning("TeamScoreManager not found! Using default multiplier.");
            return incomingDamage * 0.5f; // Default debuff
        }
        
        float multiplier = scoreManager.GetTerritoryDefenseMultiplier(defenderTeam);
        float modifiedDamage = incomingDamage * multiplier;
        
        Debug.Log($"Territory defense: {incomingDamage} x {multiplier} = {modifiedDamage}");
        
        return modifiedDamage;
    }
    
    // ===== INTEGRATION EXAMPLE =====
    // In your existing combat/damage script, you might have something like:
    //
    // public void DealDamage(GameObject target, float damage)
    // {
    //     // Check if attacker is in a territory
    //     TerritoryZone territory = GetTerritoryAtPosition(transform.position);
    //     if (territory != null)
    //     {
    //         damage = territory.CalculateOutgoingDamage(myTeam, damage);
    //     }
    //     
    //     // Apply the damage...
    //     target.GetComponent<Health>().TakeDamage(damage);
    // }
    //
    // public void TakeDamage(float damage)
    // {
    //     // Check if defender is in a territory
    //     TerritoryZone territory = GetTerritoryAtPosition(transform.position);
    //     if (territory != null)
    //     {
    //         damage = territory.CalculateIncomingDamage(myTeam, damage);
    //     }
    //     
    //     currentHealth -= damage;
    // }
}
