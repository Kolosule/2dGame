using UnityEngine;
using Fusion;

/// <summary>
/// Server-spawned networked projectile. Velocity is set on the server and synced by
/// NetworkRigidbody2D; hit detection, damage, stun, and despawn run on the state authority.
/// Full friendly-fire/effects polish is a later pass — this is the minimal correct version.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Projectile : NetworkBehaviour
{
    [Header("Stun")]
    [SerializeField] private float stunDuration = 1.5f;
    [SerializeField] private bool stunPlayers = true;

    [Header("Visual Effects")]
    [SerializeField] private GameObject impactEffect;

    [Networked] private Vector2 Direction { get; set; }
    [Networked] private float Speed { get; set; }
    [Networked] private int Damage { get; set; }
    [Networked] private Team ShooterTeam { get; set; }

    private Rigidbody2D rb;
    private bool hasHit;

    /// <summary>SERVER: set from PlayerCombat's spawn callback before Spawned runs.</summary>
    public void ServerInitialize(Vector2 dir, float speed, int damage, Team team)
    {
        Direction = dir.normalized;
        Speed = speed;
        Damage = damage;
        ShooterTeam = team;
    }

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        var col = GetComponent<CircleCollider2D>();
        if (col != null) col.isTrigger = true;
        if (rb != null) rb.gravityScale = 1f;

        Debug.Log($"[PROJ-DIAG] Spawned | StateAuth={HasStateAuthority} dir={Direction} speed={Speed} pos={transform.position} hasRB={(rb != null)} hasNetRB={(GetComponent<Fusion.Addons.Physics.NetworkRigidbody2D>() != null)}");

        if (HasStateAuthority && rb != null)
            rb.linearVelocity = Direction * Speed;
    }

    public override void Render()
    {
        if (rb != null && rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!HasStateAuthority || hasHit) return;

        Debug.Log($"[PROJ-DIAG] trigger with '{other.name}' layer={LayerMask.LayerToName(other.gameObject.layer)}");

        // Player hit (skip same team)
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamComponent pt = other.GetComponent<PlayerTeamComponent>();
            Team targetTeam = pt != null ? pt.Team : Team.None;
            bool friendly = targetTeam != Team.None && targetTeam == ShooterTeam;
            if (!friendly)
            {
                playerStats.RPC_TakeDamage(Damage);
                if (stunPlayers)
                {
                    PlayerMovement pm = other.GetComponent<PlayerMovement>();
                    if (pm != null) pm.ApplyStun(stunDuration);
                }
                Hit();
            }
            return;
        }

        // Enemy hit
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            Vector2 dir = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
            enemy.TakeDamage(Damage, dir * 5f, other.transform.position);
            Hit();
            return;
        }

        // Ground / wall
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") || other.CompareTag("Wall"))
            Hit();
    }

    private void Hit()
    {
        if (hasHit) return;
        Debug.Log("[PROJ-DIAG] Hit -> Runner.Despawn");
        hasHit = true;
        if (impactEffect != null) RPC_Impact(transform.position);
        Runner.Despawn(Object);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position)
    {
        if (impactEffect != null)
        {
            GameObject fx = Instantiate(impactEffect, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
}
