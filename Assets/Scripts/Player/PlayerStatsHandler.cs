using UnityEngine;
using Unity.Netcode;

public class PlayerStatsHandler : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;

    private float currentHealth;
    private float lastAttackTime;
    private bool isDead = false;
    private NetworkObject networkObject;
    private int playerID = -1; // Track player ID for MultiplayerRespawnManager

    void Awake()
    {
        currentHealth = stats.maxHealth;
        networkObject = GetComponent<NetworkObject>();
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

    public void TakeDamage(float amount)
    {
        // Don't take damage if already dead
        if (isDead) return;

        // Apply defensive territorial modifier if available
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null)
        {
            float defenseModifier = teamComponent.GetDamageReceivedModifier();
            amount = amount * defenseModifier;
            Debug.Log($"Player damage modified by territory: {defenseModifier:F2}x");
        }

        currentHealth -= amount;
        Debug.Log($"Player took {amount} damage. Health = {currentHealth}/{stats.maxHealth}");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
    }

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

            // Apply damage with knockback
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

        // Trigger respawn after 2 seconds
        Invoke(nameof(Respawn), 2f);
    }

    private void Respawn()
    {
        Debug.Log("=== RESPAWN DEBUG START ===");
        Debug.Log("Respawning player...");

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

        // OPTION 1: Use MultiplayerRespawnManager if available
        MultiplayerRespawnManager respawnManager = FindObjectOfType<MultiplayerRespawnManager>();
        Debug.Log($"MultiplayerRespawnManager found: {respawnManager != null}, PlayerID: {playerID}");

        if (respawnManager != null && playerID >= 0)
        {
            respawnManager.RespawnPlayer(playerID);
            Debug.Log($"Using MultiplayerRespawnManager to respawn player {playerID}");
            Debug.Log("=== RESPAWN DEBUG END ===");
            return;
        }

        // OPTION 2: NETWORK MODE - Use NetworkedSpawnManager if available
        Debug.Log($"NetworkedSpawnManager.Instance: {NetworkedSpawnManager.Instance != null}");
        Debug.Log($"NetworkObject: {networkObject != null}");

        if (NetworkedSpawnManager.Instance != null && networkObject != null)
        {
            // IMPORTANT: Refresh spawn points before getting position
            Debug.Log("Calling RefreshSpawnPoints...");
            NetworkedSpawnManager.Instance.RefreshSpawnPoints();

            // Get spawn position from the spawner based on team
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
            Debug.Log($"PlayerTeamComponent found: {teamComponent != null}");

            if (teamComponent != null)
            {
                Debug.Log($"Player teamID: {teamComponent.teamID}");
                Vector3 spawnPos = NetworkedSpawnManager.Instance.GetSpawnPosition(teamComponent.teamID);
                Debug.Log($"Got spawn position from NetworkedSpawnManager: {spawnPos}");
                transform.position = spawnPos;
                Debug.Log($"Network respawn at team spawn: {spawnPos}");
                Debug.Log("=== RESPAWN DEBUG END ===");
                return;
            }
        }

        // OPTION 3: STANDALONE MODE - Try to respawn at team base if available
        Debug.Log($"TeamManager.Instance: {TeamManager.Instance != null}");

        PlayerTeamComponent teamComp = GetComponent<PlayerTeamComponent>();
        Debug.Log($"PlayerTeamComponent (standalone check): {teamComp != null}");

        if (teamComp != null && TeamManager.Instance != null)
        {
            Debug.Log($"Getting team data for: {teamComp.teamID}");
            TeamData teamData = TeamManager.Instance.GetTeamData(teamComp.teamID);
            Debug.Log($"TeamData found: {teamData != null}");

            if (teamData != null)
            {
                Debug.Log($"Team base position: {teamData.basePosition}");
                transform.position = teamData.basePosition;
                Debug.Log($"Respawned at team base: {teamData.basePosition}");
                Debug.Log("=== RESPAWN DEBUG END ===");
                return;
            }
            else
            {
                Debug.LogError($"Could not find TeamData for teamID: '{teamComp.teamID}'");
            }
        }

        // Fallback: respawn at current position
        Debug.LogWarning("All respawn methods failed! Using fallback (current position)");
        Debug.Log($"Current position: {transform.position}");
        Debug.Log("=== RESPAWN DEBUG END ===");
    }

    // Public getters for UI
    public float GetCurrentHealth()
    {
        return currentHealth;
    }

    public float GetMaxHealth()
    {
        return stats.maxHealth;
    }

    public bool IsDead()
    {
        return isDead;
    }
}