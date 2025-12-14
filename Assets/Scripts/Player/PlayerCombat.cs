using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private int damageAmount = 10;   // base damage
    [SerializeField] private float knockbackStrength = 10f; // Horizontal knockback force
    [SerializeField] private float knockbackUpward = 3f;    // Upward knockback force

    [Header("Attack Points")]
    [SerializeField] private Transform sideAttackPoint;
    [SerializeField] private Transform upAttackPoint;
    [SerializeField] private Transform downAttackPoint;

    [Header("Attack Areas")]
    [SerializeField] private Vector2 sideAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 upAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 downAttackArea = new Vector2(1f, 1f);

    private Animator anim;
    private PlayerTeamComponent teamComponent;
    private float timeSinceAttack;
    private float yAxis;

    void Awake()
    {
        anim = GetComponent<Animator>();
        teamComponent = GetComponent<PlayerTeamComponent>();

        if (teamComponent == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerTeamComponent found! Territorial bonuses won't apply.");
        }
    }

    public void HandleInput()
    {
        timeSinceAttack += Time.deltaTime;
        yAxis = Input.GetAxisRaw("Vertical");

        if (Input.GetMouseButtonDown(0) && timeSinceAttack >= stats.attackCooldown)
        {
            timeSinceAttack = 0;
            anim.SetTrigger("Attack");

            if (yAxis == 0 || (yAxis < 0 && IsGrounded()))
            {
                PerformAttack(sideAttackPoint, sideAttackArea);
            }
            else if (yAxis > 0)
            {
                PerformAttack(upAttackPoint, upAttackArea);
            }
            else if (yAxis < 0 && !IsGrounded())
            {
                PerformAttack(downAttackPoint, downAttackArea);
            }
        }
    }

    // ========== FIX #3: PROPER COLLISION DETECTION ==========
    private void PerformAttack(Transform attackPoint, Vector2 attackArea)
    {
        // Apply territorial damage modifier
        int finalDamage = damageAmount;

        if (teamComponent != null)
        {
            float damageModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(damageAmount * damageModifier);

            float territoryAdvantage = teamComponent.GetCurrentTerritorialAdvantage();
            string territoryStatus = territoryAdvantage > 0.3f ? "own territory" :
                                    territoryAdvantage < -0.3f ? "enemy territory" : "neutral ground";

            Debug.Log($"Player attacking in {territoryStatus}: {damageAmount} → {finalDamage} damage (×{damageModifier:F2})");
        }

        // FIX: Use OverlapBoxAll to detect enemies in the attack area
        Collider2D[] hitEnemies = Physics2D.OverlapBoxAll(attackPoint.position, attackArea, 0f, attackableLayer);

        Debug.Log($"Attack detected {hitEnemies.Length} potential targets at {attackPoint.position}");

        foreach (Collider2D hit in hitEnemies)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                // Calculate knockback direction based on player facing
                Vector2 knockbackDirection = new Vector2(transform.localScale.x, 0).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);

                // Get hit point (center of the enemy collider)
                Vector2 hitPoint = hit.bounds.center;

                // Apply damage with knockback
                enemy.TakeDamage(finalDamage, knockbackForce, hitPoint);
                Debug.Log($"Player dealt {finalDamage} damage to {enemy.name}");
            }
            else
            {
                Debug.Log($"Hit {hit.name} but it has no Enemy component");
            }
        }

        // Visual feedback - draw the attack area briefly
        Debug.DrawLine(attackPoint.position - (Vector3)attackArea / 2, attackPoint.position + (Vector3)attackArea / 2, Color.red, 0.2f);
    }

    private bool IsGrounded()
    {
        return Physics2D.Raycast(transform.position, Vector2.down, 0.1f, LayerMask.GetMask("Ground"));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (sideAttackPoint != null) Gizmos.DrawWireCube(sideAttackPoint.position, sideAttackArea);
        if (upAttackPoint != null) Gizmos.DrawWireCube(upAttackPoint.position, upAttackArea);
        if (downAttackPoint != null) Gizmos.DrawWireCube(downAttackPoint.position, downAttackArea);
    }
}