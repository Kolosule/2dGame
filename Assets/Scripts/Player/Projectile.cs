using UnityEngine;
using Fusion;
using Game.Combat.Core;

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
    [Networked] private int BaseDamage { get; set; }
    [Networked] private Team ShooterTeam { get; set; }

    // Networked so every peer (and every pooled reuse) applies the same visual scale.
    // The old spawn-callback localScale write was server-only and leaked across pool reuses.
    [Networked] private float Scale { get; set; }

    private Rigidbody2D rb;
    private bool hasHit;

    /// <summary>SERVER: set from PlayerCombat's spawn callback before Spawned runs. baseDamage is
    /// the RAW, unresolved damage — final damage is resolved at impact against the defender.</summary>
    public void ServerInitialize(Vector2 dir, float speed, int baseDamage, Team team, float scale)
    {
        Direction = dir.normalized;
        Speed = speed;
        BaseDamage = baseDamage;
        ShooterTeam = team;
        Scale = scale > 0f ? scale : 1f;
    }

    public override void Spawned()
    {
        // Pooled reuse: a recycled instance keeps its previous hasHit value, so clear transient
        // runtime state here. Every per-spawn field below is re-initialised regardless.
        hasHit = false;

        // Apply the replicated scale on every peer (and reset any stale pooled scale).
        // Scale is 0 only before the first server write reaches a client — keep prefab scale then.
        if (Scale > 0f) transform.localScale = Vector3.one * Scale;

        rb = GetComponent<Rigidbody2D>();
        var col = GetComponent<CircleCollider2D>();
        if (col != null) col.isTrigger = true;
        if (rb != null) rb.gravityScale = 1f;


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


        // Player hit. Friendly-fire and self-hit are both gated by FriendlyFire, the same
        // predicate PlayerCombat uses for melee, so the two damage sources agree. A friendly
        // (or self) hit falls through without calling Hit() -- the projectile keeps travelling
        // and can still hit an enemy behind the teammate.
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamData pt = other.GetComponent<PlayerTeamData>();
            Team targetTeam = pt != null ? pt.Team : Team.None;
            bool isSelf = playerStats.Object != null && playerStats.Object.InputAuthority == Object.InputAuthority;

            if (FriendlyFire.CanDamagePlayer(TeamUtil.ToNumber(ShooterTeam), TeamUtil.ToNumber(targetTeam), isSelf))
            {
                // Attribute the hit to the SHOOTER (so their next projectile respects the same
                // per-attacker window), falling back to this projectile's own id if the shooter's
                // player object can't be resolved (e.g. they disconnected mid-flight).
                NetworkId attackerId = Object.Id;
                if (Runner.TryGetPlayerObject(Object.InputAuthority, out NetworkObject shooterObj))
                    attackerId = shooterObj.Id;

                int finalDamage = ResolveDamage(targetTeam, other.transform.position);
                playerStats.ServerApplyDamage(finalDamage, attackerId);
                RPC_HitFeedback(playerStats.Object.Id, other.transform.position, finalDamage);
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
            int finalDamage = ResolveDamage(enemy.Team, other.transform.position);
            enemy.TakeDamage(finalDamage, dir * 5f, other.transform.position);
            RPC_HitFeedback(enemy.Object.Id, other.transform.position, finalDamage);
            Hit();
            return;
        }

        // Ground / wall
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") || other.CompareTag("Wall"))
            Hit();
    }

    /// <summary>
    /// Resolves impact damage through the unified pipeline, keyed by the DEFENDER's team and
    /// position at the moment of impact — a defender takes more damage the farther they are from
    /// their own base. Resolved here (not at fire time) because the defender is only known on
    /// hit. Falls back to the raw authored base damage (with a loud one-time warning) if no
    /// CombatConfig is assigned.
    /// </summary>
    private int ResolveDamage(Team defenderTeam, Vector2 defenderPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null)
        {
            CombatConfig.WarnMissingOnce();
            return BaseDamage;
        }

        return config.ResolveDamage(BaseDamage, defenderTeam, defenderPos);
    }

    private void Hit()
    {
        if (hasHit) return;
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

    /// <summary>
    /// Attacker-only hit feedback, delivered to the shooter's client. Resolves the
    /// target locally and plays cosmetic FX. No networked state.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)
    {
        if (HitFeedback.Instance == null) return;
        GameObject targetGo = null;
        if (Runner.TryFindObject(targetId, out NetworkObject targetObj) && targetObj != null)
            targetGo = targetObj.gameObject;
        HitFeedback.Instance.Play(targetGo, hitPoint, damage);
    }
}
