using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float knockbackStrength = 10f;
    [SerializeField] private float knockbackUpward = 3f;

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
            Debug.LogWarning("PlayerCombat: No PlayerTeamComponent found!");
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

            // Choose attack based on vertical input
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

    private void PerformAttack(Transform attackPoint, Vector2 attackArea)
    {
        // Apply territorial damage modifier
        int finalDamage = damageAmount;

        if (teamComponent != null)
        {
            float damageModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(damageAmount * damageModifier);

            string territoryStatus = GetTerritoryStatus();
            Debug.Log($"Attacking in {territoryStatus}: {damageAmount} → {finalDamage} damage");
        }

        // Detect all enemies in attack area
        Collider2D[] hitEnemies = Physics2D.OverlapBoxAll(
            attackPoint.position,
            attackArea,
            0f,
            attackableLayer
        );

        Debug.Log($"Attack detected {hitEnemies.Length} enemies");

        foreach (Collider2D hit in hitEnemies)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                // Calculate knockback direction (away from player)
                Vector2 knockbackDirection = new Vector2(transform.localScale.x, 0).normalized;
                Vector2 knockbackForce = new Vector2(
                    knockbackDirection.x * knockbackStrength,
                    knockbackUpward
                );

                Vector2 hitPoint = hit.bounds.center;

                // Apply damage with knockback
                enemy.TakeDamage(finalDamage, knockbackForce, hitPoint);
                Debug.Log($"Hit {enemy.name} for {finalDamage} damage!");
            }
        }
    }

    private bool IsGrounded()
    {
        return Physics2D.Raycast(transform.position, Vector2.down, 0.1f, LayerMask.GetMask("Ground"));
    }

    private string GetTerritoryStatus()
    {
        if (teamComponent == null) return "unknown";

        float advantage = teamComponent.GetCurrentTerritorialAdvantage();
        if (advantage > 0.3f) return "own territory";
        if (advantage < -0.3f) return "enemy territory";
        return "neutral ground";
    }

    // Visualize attack areas in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (sideAttackPoint != null)
            Gizmos.DrawWireCube(sideAttackPoint.position, sideAttackArea);
        if (upAttackPoint != null)
            Gizmos.DrawWireCube(upAttackPoint.position, upAttackArea);
        if (downAttackPoint != null)
            Gizmos.DrawWireCube(downAttackPoint.position, downAttackArea);
    }
}