using UnityEngine;

public class PlayerStatsHandler : MonoBehaviour
{
    [SerializeField] private PlayerStats stats; // reference to your ScriptableObject

    private float currentHealth;
    private float lastAttackTime;
    private int playerID = -1; // Track this player's ID

    void Awake()
    {
        currentHealth = stats.maxHealth;
    }

    /// <summary>
    /// Set the player ID (called by MultiplayerRespawnManager when spawning)
    /// </summary>
    public void SetPlayerID(int id)
    {
        playerID = id;
        Debug.Log($"Player ID set to: {playerID}");
    }

    // Called when the player takes damage
    public void TakeDamage(float amount)
    {
        // Apply defensive territorial modifier
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null)
        {
            float defenseModifier = teamComponent.GetDamageReceivedModifier();
            amount = amount * defenseModifier;
            Debug.Log($"Player damage modified by territory: {defenseModifier:F2}x");
        }

        currentHealth -= amount;
        Debug.Log($"Player took {amount} damage. Health = {currentHealth}");

        if (currentHealth <= 0)
        {
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
            Vector2 knockbackForce = Vector2.zero; // calculate based on player direction
            Vector2 hitPoint = enemy.transform.position; // or collision info

            enemy.TakeDamage((int)attackDamage, knockbackForce, hitPoint);
            Debug.Log($"Player attacked {enemy.name} for {attackDamage} damage.");
        }

        lastAttackTime = Time.time;
    }

    private void Die()
    {
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

        // Trigger respawn after short delay
        Invoke(nameof(Respawn), 1f);
    }

    private void Respawn()
    {
        MultiplayerRespawnManager respawnManager = FindObjectOfType<MultiplayerRespawnManager>();

        if (respawnManager != null)
        {
            if (playerID >= 0)
            {
                // Respawn this specific player
                respawnManager.RespawnPlayer(playerID);
            }
            else
            {
                Debug.LogError("Player ID not set! Cannot respawn.");
            }
        }
        else
        {
            Debug.LogError("MultiplayerRespawnManager not found in scene!");
        }

        // The respawn manager will destroy the old player and create a new one
    }
}