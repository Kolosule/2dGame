using UnityEngine;

// META-LAYER DAMAGE MODEL: every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x dealtModifier(attacker) x crit.
// dealtModifier is the quantized territorial debuff (x0.33 in the enemy third, x1 elsewhere),
// lifted in halves by the attacking team's Vanguard tier from TeamScoreManager. There is no
// received-side modifier: one debuff, one side, one direction.
// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
[CreateAssetMenu(fileName = "CombatConfig", menuName = "Game/Combat Configuration")]
public class CombatConfig : ScriptableObject
{
    [Header("Damage Settings")]
    [Tooltip("Base damage multiplier for all attacks")]
    [Range(0.1f, 5.0f)]
    public float globalDamageMultiplier = 1.0f;

    [Tooltip("Critical hit chance (0-1)")]
    [Range(0f, 1f)]
    public float criticalChance = 0.1f;

    [Tooltip("Critical hit damage multiplier")]
    [Range(1.0f, 5.0f)]
    public float criticalMultiplier = 2.0f;

    [Header("Knockback Settings")]
    [Tooltip("Global knockback strength multiplier")]
    [Range(0.1f, 3.0f)]
    public float knockbackMultiplier = 1.0f;

    [Tooltip("Should knockback be affected by damage dealt?")]
    public bool scaledKnockback = true;

    [Header("Attack Timing")]
    [Tooltip("Global attack speed multiplier (higher = faster)")]
    [Range(0.5f, 2.0f)]
    public float attackSpeedMultiplier = 1.0f;

    [Header("Territorial Combat")]
    [Tooltip("Enable the territorial debuff. Turning this OFF also makes the ENTIRE team-buff " +
             "layer inert, because Vanguard exists only to lift this debuff.")]
    public bool territorialAdvantageEnabled = true;

    [Header("Visual Feedback")]
    [Tooltip("Damage number prefab")]
    public GameObject damageNumberPrefab;

    [Tooltip("Color for normal damage")]
    public Color normalDamageColor = Color.white;

    [Tooltip("Color for critical damage")]
    public Color criticalDamageColor = Color.red;

    [Tooltip("Color for territorial bonus damage")]
    public Color bonusDamageColor = Color.yellow;

    [Header("Hit Effects")]
    public GameObject hitEffectPrefab;
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitSoundVolume = 0.5f;

    // Not serialized: resets on domain reload, which is exactly the cadence we want for a
    // once-per-session operator warning.
    [System.NonSerialized] private bool warnedTerritoryDisabled;

    /// <summary>
    /// Calculate if an attack is a critical hit
    /// </summary>
    public bool RollCritical()
    {
        return Random.value < criticalChance;
    }

    /// <summary>
    /// Pure-math composition from an already-resolved modifier. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float dealtModifier, bool isCritical = false)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= dealtModifier;
        }

        if (isCritical)
        {
            damage *= criticalMultiplier;
        }

        return damage;
    }

    /// <summary>
    /// THE single entry point for all combat damage. Reads the attacker's territorial advantage and
    /// its team's Vanguard tier, rolls crit, and composes via CalculateFinalDamage. Returns a
    /// rounded, non-negative int. Call only on StateAuthority (the call sites already gate on it).
    /// The defender no longer participates: the received-side modifier was deleted with the old
    /// two-sided model.
    /// </summary>
    public int ResolveDamage(float baseDamage, Team attackerTeam, Vector2 attackerPos)
    {
        float dealt = 1f;

        if (territorialAdvantageEnabled)
        {
            TeamManager teams = TeamManager.Instance;
            if (teams != null)
            {
                int vanguardTier = 0;

                TeamScoreManager scores = TeamScoreManager.Instance;
                if (scores != null && scores.Object != null && scores.Object.IsValid)
                    vanguardTier = scores.VanguardTier(attackerTeam);

                float advantage = teams.GetTerritorialAdvantage(attackerTeam, attackerPos);
                dealt = teams.GetDamageDealtModifier(attackerTeam, advantage, vanguardTier);
            }
        }
        else
        {
            WarnTerritoryDisabledOnce();
        }

        bool isCritical = RollCritical();
        float finalDamage = CalculateFinalDamage(baseDamage, dealt, isCritical);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }

    /// <summary>
    /// The old team buffs were silent no-ops whenever territorialAdvantageEnabled was false
    /// (they were only ever multiplied in inside that flag's branch). Say so out loud instead.
    /// </summary>
    private void WarnTerritoryDisabledOnce()
    {
        if (warnedTerritoryDisabled) return;
        warnedTerritoryDisabled = true;
        Debug.LogWarning("⚠️ CombatConfig.territorialAdvantageEnabled is FALSE — the territorial " +
                         "debuff and the entire Vanguard team-buff layer are inert. Coin deposits " +
                         "then buy nothing at the team level.");
    }
}