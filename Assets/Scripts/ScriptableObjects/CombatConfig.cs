using UnityEngine;

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

    [Tooltip("Minimum damage multiplier at enemy base")]
    [Range(0.1f, 1.0f)]
    public float minTerritorialDamage = 0.5f;

    [Tooltip("Maximum damage multiplier at own base")]
    [Range(1.0f, 3.0f)]
    public float maxTerritorialDamage = 1.5f;

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
    /// Apply all combat modifiers to base damage
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float territorialModifier, bool isCritical = false)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= territorialModifier;
        }

        if (isCritical)
        {
            damage *= criticalMultiplier;
        }

        return damage;
    }
}