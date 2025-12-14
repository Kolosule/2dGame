using UnityEngine;

public class PlayerStatsHandler : MonoBehaviour
{
    [SerializeField] private PlayerStats stats; // reference to your ScriptableObject

    private float currentHealth;
    private float lastAttackTime;
    private int playerID = -1; // Track this player's ID
    private bool isDead = false; // Track if player is dead

    void Awake()
    {
        currentHealth = stats.maxHealth;
        Debug.Log($"Player initialized with {currentHealth} health");
    }

    /// <summary>
    /// Set the player ID (called by MultiplayerRespawnManager when spawning)
    /// </summary>
    public void SetPlayerID(int id)
    {
        playerID = id;
        Debug.Log($"Player ID set to: {playerID}");
    }

    // ========== FIX #2: PROPER DEATH CHECK ==========
    // Called when the player takes damage
    public void TakeDamage(float amount)
    {
        // Don't take damage if already dead
        if (isDead) return;

        // Apply defensive territorial modifier
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null)
        {
            float defenseModifier = teamComponent.GetDamageReceivedModifier();
            amount = amount * defenseModifier;
            Debug.Log($"Player damage modified by territory: {defenseModifier:F2}x");
        }

        currentHealth -= amount;
        Debug.Log($"Player took {amount} damage. Health = {currentHealth}/{stats.maxHealth}");

        // FIX: Check if health has dropped to 0 or below
        if (currentHealth <= 0)
        {
            currentHealth = 0; // Clamp to 0
            Die();
        }
    }

    // Called when the player attacks an enemy
    public void Attack(Enemy enemy)
    {
        if (Time.time - lastAttackTime < stats.attackCooldown) return;

        if (enemy != null)
        {
            // Apply offensive territorial modifier
            float attackDamage = stats.attackDamage;
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
            if (teamComponent != null)
            {
                float damageModifier = teamComponent.GetDamageDealtModifier();
                attackDamage = attackDamage * damageModifier;
                Debug.Log($"Player attack modified by territory: {damageModifier:F2}x");
            }

            // If your Enemy has the 3-argument TakeDamage, pass knockback + hitPoint
            Vector2 knockbackForce = new Vector2(transform.localScale.x * stats.attackForce, 2f);
            Vector2 hitPoint = enemy.transform.position;

            enemy.TakeDamage((int)attackDamage, knockbackForce, hitPoint);
            Debug.Log($"Player attacked {enemy.name} for {attackDamage} damage.");
        }

        lastAttackTime = Time.time;
    }

    private void Die()
    {
        isDead = true;
        Debug.Log("Player died!");

        // Disable combat/movement
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = false;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetTrigger("Die");
        }

        // Make player semi-transparent
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 0.5f;
            sprite.color = color;
        }

        // Trigger respawn after short delay
        Invoke(nameof(Respawn), 2f);
    }

    private void Respawn()
    {
        // Try to find MultiplayerRespawnManager first
        MultiplayerRespawnManager respawnManager = FindObjectOfType<MultiplayerRespawnManager>();

        if (respawnManager != null && playerID >= 0)
        {
            // Use multiplayer respawn if available
            respawnManager.RespawnPlayer(playerID);
            return;
        }

        // Simple fallback respawn - just reset the player
        Debug.Log("Using simple respawn (MultiplayerRespawnManager not found)");

        isDead = false;
        currentHealth = stats.maxHealth;

        // Re-enable scripts
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = true;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = true;

        // Reset transparency
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }

        // Move to spawn point (try to find PlayerTeamComponent for team spawn)
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null && TeamManager.Instance != null)
        {
            TeamData teamData = TeamManager.Instance.GetTeamData(teamComponent.teamID);
            if (teamData != null)
            {
                transform.position = teamData.basePosition;
                Debug.Log($"Respawned at team base: {teamData.basePosition}");
            }
        }

        Debug.Log($"Player respawned with {currentHealth} health");
    }

    // Public method to get current health (useful for UI)
    public float GetCurrentHealth()
    {
        return currentHealth;
    }

    // Public method to get max health
    public float GetMaxHealth()
    {
        return stats.maxHealth;
    }
}