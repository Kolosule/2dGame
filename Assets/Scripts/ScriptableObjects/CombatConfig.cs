using UnityEngine;

// META-LAYER DAMAGE MODEL (review item #4): every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x dealtModifier(attacker) x receivedModifier(defender) x crit.
// dealtModifier/receivedModifier come from TeamManager's distance-based territorial system;
// coin-milestone buffs (TeamScoreManager) lift the nerf: DamageBuff floors the outgoing
// modifier at 1.0, DefenseBuff caps the incoming modifier at 1.0.
// See docs/superpowers/specs/2026-06-22-unified-damage-pipeline-design.md.
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
    [Tooltip("Enable territorial advantage system")]
    public bool territorialAdvantageEnabled = true;

    [Header("Visual Feedback")]
    [Tooltip("Show damage numbers")]
    public bool showDamageNumbers = true;

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

    /// <summary>
    /// Calculate if an attack is a critical hit
    /// </summary>
    public bool RollCritical()
    {
        return Random.value < criticalChance;
    }

    /// <summary>
    /// Pure-math composition from already-resolved modifiers. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float dealtModifier, float receivedModifier, bool isCritical = false)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= dealtModifier * receivedModifier;
        }

        if (isCritical)
        {
            damage *= criticalMultiplier;
        }

        return damage;
    }

    /// <summary>
    /// THE single entry point for all combat damage (review item #4). Gathers the distance-based
    /// territorial modifiers from TeamManager, applies the coin-economy buff lift from
    /// TeamScoreManager, rolls crit, and composes via CalculateFinalDamage. Returns a rounded,
    /// non-negative int. Call only on StateAuthority (the call sites already gate on it).
    /// </summary>
    public int ResolveDamage(float baseDamage,
                             Team attackerTeam, Vector2 attackerPos,
                             Team defenderTeam, Vector2 defenderPos)
    {
        float dealt = 1f;
        float received = 1f;

        TeamManager teams = TeamManager.Instance;
        if (teams != null)
        {
            float attackerAdvantage = teams.GetTerritorialAdvantage(attackerTeam, attackerPos);
            dealt = teams.GetDamageDealtModifier(attackerTeam, attackerAdvantage);

            float defenderAdvantage = teams.GetTerritorialAdvantage(defenderTeam, defenderPos);
            received = teams.GetDamageReceivedModifier(defenderTeam, defenderAdvantage);
        }

        TeamScoreManager scores = TeamScoreManager.Instance;
        if (scores != null && scores.Object != null && scores.Object.IsValid)
        {
            // DamageBuff lifts the outgoing nerf: never below neutral 1.0x.
            if (scores.HasDamageBuff(attackerTeam)) dealt = Mathf.Max(dealt, 1f);
            // DefenseBuff removes enemy-territory vulnerability: never above neutral 1.0x.
            if (scores.HasDefenseBuff(defenderTeam)) received = Mathf.Min(received, 1f);
        }

        bool isCritical = RollCritical();
        float finalDamage = CalculateFinalDamage(baseDamage, dealt, received, isCritical);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }
}