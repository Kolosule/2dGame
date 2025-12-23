using UnityEngine;

/// <summary>
/// Handles player combat including directional attacks, damage dealing, and visual feedback.
/// Works with standard Enemy class (non-networked enemies).
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float knockbackStrength = 10f;
    [SerializeField] private float knockbackUpward = 3f;

    [Header("Attack Speed")]
    [Tooltip("Time in seconds between attacks (lower = faster attacks)")]
    [SerializeField] private float attackCooldown = 0.5f;

    [Header("Attack Points")]
    [SerializeField] private Transform sideAttackPoint;
    [SerializeField] private Transform upAttackPoint;
    [SerializeField] private Transform downAttackPoint;

    [Header("Attack Areas")]
    [SerializeField] private Vector2 sideAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 upAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 downAttackArea = new Vector2(1f, 1f);

    [Header("Hit Marker Effects")]
    [Tooltip("Particle effect to spawn when attacks hit enemies")]
    [SerializeField] private GameObject hitMarkerPrefab;
    [Tooltip("Color of hit marker for successful hits")]
    [SerializeField] private Color hitMarkerColor = Color.white;
    [Tooltip("Duration of hit marker effect in seconds")]
    [SerializeField] private float hitMarkerDuration = 0.3f;

    // Component references
    private Animator anim;
    private PlayerTeamComponent teamComponent;
    private PlayerStatsHandler statsHandler;

    // Combat state
    private float timeSinceAttack;
    private float yAxis;

    void Awake()
    {
        anim = GetComponent<Animator>();
        teamComponent = GetComponent<PlayerTeamComponent>();
        statsHandler = GetComponent<PlayerStatsHandler>();

        if (teamComponent == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerTeamComponent found! Territory modifiers won't work.");
        }

        if (statsHandler == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerStatsHandler found! Direct enemy attacks won't work.");
        }
    }

    void Update()
    {
        timeSinceAttack += Time.deltaTime;
    }

    public void HandleInput()
    {
        yAxis = Input.GetAxisRaw("Vertical");

        // Attack input
        if (Input.GetButtonDown("Fire1") && timeSinceAttack >= attackCooldown)
        {
            Attack();
        }
    }

    private void Attack()
    {
        timeSinceAttack = 0;

        Transform attackTransform;
        Vector2 attackArea;

        // Determine attack direction based on input
        if (yAxis > 0)
        {
            // Up attack
            anim.SetTrigger("Attack");
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
        }
        else if (yAxis < 0)
        {
            // Down attack
            anim.SetTrigger("Attack");
            attackTransform = downAttackPoint;
            attackArea = downAttackArea;
        }
        else
        {
            // Side attack
            anim.SetTrigger("Attack");
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;
        }

        // Perform attack detection and damage
        Hit(attackTransform, attackArea);
    }

    private void Hit(Transform attackTransform, Vector2 attackArea)
    {
        // Detect all colliders in the attack area
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position,
            attackArea,
            0f,
            attackableLayer
        );

        bool hitSomething = false;

        // Process each hit object
        foreach (Collider2D hit in objectsHit)
        {
            // Try to get Enemy component (standard non-networked version)
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                // Check if this enemy is on a different team
                EnemyTeamComponent enemyTeam = enemy.GetComponent<EnemyTeamComponent>();
                if (teamComponent != null && enemyTeam != null)
                {
                    if (teamComponent.teamID == enemyTeam.teamID)
                    {
                        Debug.Log("Skipping teammate enemy");
                        continue; // Don't attack teammates
                    }
                }

                // Attack the enemy through PlayerStatsHandler
                if (statsHandler != null)
                {
                    statsHandler.AttackEnemy(enemy);
                    hitSomething = true;

                    Debug.Log($"Player hit enemy: {enemy.name}");

                    // Spawn hit marker effect
                    if (hitMarkerPrefab != null)
                    {
                        GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                        Destroy(marker, hitMarkerDuration);
                    }
                }
            }

            // Also check for other player (PvP)
            PlayerStatsHandler otherPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (otherPlayer != null && otherPlayer != statsHandler)
            {
                // Check if on different team
                PlayerTeamComponent otherTeam = otherPlayer.GetComponent<PlayerTeamComponent>();
                if (teamComponent != null && otherTeam != null)
                {
                    if (teamComponent.teamID != otherTeam.teamID)
                    {
                        // Apply damage with territorial modifier
                        float attackDamage = damageAmount;
                        if (teamComponent != null)
                        {
                            float damageModifier = teamComponent.GetDamageDealtModifier();
                            attackDamage = attackDamage * damageModifier;
                        }

                        otherPlayer.TakeDamage(attackDamage);
                        hitSomething = true;

                        Debug.Log($"Player hit other player: {otherPlayer.name} for {attackDamage} damage");

                        // Spawn hit marker
                        if (hitMarkerPrefab != null)
                        {
                            GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                            Destroy(marker, hitMarkerDuration);
                        }
                    }
                }
            }
        }

        if (!hitSomething)
        {
            Debug.Log("Attack missed - no valid targets hit");
        }
    }

    // Visualize attack ranges in editor
    private void OnDrawGizmosSelected()
    {
        // Side attack
        if (sideAttackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(sideAttackPoint.position, sideAttackArea);
        }

        // Up attack
        if (upAttackPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(upAttackPoint.position, upAttackArea);
        }

        // Down attack
        if (downAttackPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(downAttackPoint.position, downAttackArea);
        }
    }
}