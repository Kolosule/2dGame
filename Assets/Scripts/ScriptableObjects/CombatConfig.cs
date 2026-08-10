using UnityEngine;

// META-LAYER DAMAGE MODEL: every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x receivedModifier(defender).
// receivedModifier is the own-base-distance vulnerability: a DEFENDER takes more damage the
// farther they are from their OWN base, from any attacker — enemy AI and the opposing human
// team alike. Only Team1/Team2 are vulnerable defenders (TeamManager.GetDamageReceivedModifier
// exempts any defender that isn't Team1 or Team2); non-human teams have no meaningful home
// base and are exempt (always x1.0). Vanguard tiers reduce a team's own vulnerability: tier 0
// = full malus, tier 1 = half, tier 2 = none. There is no attacker-side modifier and no crit —
// one modifier, one side, applied to the character taking the hit.
// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
[CreateAssetMenu(fileName = "CombatConfig", menuName = "Game/Combat Configuration")]
public class CombatConfig : ScriptableObject
{
    [Header("Damage Settings")]
    [Tooltip("Base damage multiplier for all attacks")]
    [Range(0.1f, 5.0f)]
    public float globalDamageMultiplier = 1.0f;

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
    [Tooltip("Enable the own-base-distance vulnerability. Turning this OFF also makes the ENTIRE " +
             "team-buff layer inert, because Vanguard exists only to reduce this vulnerability.")]
    public bool territorialAdvantageEnabled = true;

    [Header("Visual Feedback")]
    [Tooltip("Damage number prefab")]
    public GameObject damageNumberPrefab;

    [Tooltip("Color for normal damage")]
    public Color normalDamageColor = Color.white;

    [Tooltip("Color for territorial bonus damage")]
    public Color bonusDamageColor = Color.yellow;

    [Header("Hit Effects")]
    public GameObject hitEffectPrefab;

    // Not serialized: resets on domain reload, which is exactly the cadence we want for a
    // once-per-session operator warning.
    [System.NonSerialized] private bool warnedTerritoryDisabled;
    private static bool warnedConfigMissing;

    /// <summary>
    /// Pure-math composition from an already-resolved modifier. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float receivedModifier)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= receivedModifier;
        }

        return damage;
    }

    /// <summary>
    /// THE single entry point for all combat damage. Reads the DEFENDER's own-base-distance
    /// vulnerability and their team's Vanguard tier, and composes via CalculateFinalDamage.
    /// Returns a rounded, non-negative int. Call only on StateAuthority (the call sites already
    /// gate on it).
    /// </summary>
    public int ResolveDamage(float baseDamage, Team defenderTeam, Vector2 defenderPos)
    {
        float received = 1f;

        if (territorialAdvantageEnabled)
        {
            TeamManager teams = TeamManager.Instance;
            if (teams != null)
            {
                int vanguardTier = 0;

                TeamScoreManager scores = TeamScoreManager.Instance;
                if (scores != null && scores.Object != null && scores.Object.IsValid)
                    vanguardTier = scores.VanguardTier(defenderTeam);

                float distance01 = teams.GetOwnBaseDistance01(defenderTeam, defenderPos);
                received = teams.GetDamageReceivedModifier(defenderTeam, distance01, vanguardTier);
            }
        }
        else
        {
            WarnTerritoryDisabledOnce();
        }

        float finalDamage = CalculateFinalDamage(baseDamage, received);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }

    /// <summary>
    /// The team buffs are silent no-ops whenever territorialAdvantageEnabled is false (they are
    /// only ever multiplied in inside that flag's branch). Say so out loud instead.
    /// </summary>
    private void WarnTerritoryDisabledOnce()
    {
        if (warnedTerritoryDisabled) return;
        warnedTerritoryDisabled = true;
        Debug.LogWarning("⚠️ CombatConfig.territorialAdvantageEnabled is FALSE — the own-base " +
                         "vulnerability and the entire Vanguard team-buff layer are inert. Coin " +
                         "deposits then buy nothing at the team level.");
    }

    /// <summary>
    /// GameSettingsManager.combatConfig is unassigned. Loud, once-per-session: this exact silent-
    /// fallback shape (raw base damage, no global multiplier, no vulnerability, no Vanguard) is
    /// what let the whole meta-damage layer no-op for weeks behind a green test suite.
    /// </summary>
    public static void WarnMissingOnce()
    {
        if (warnedConfigMissing) return;
        warnedConfigMissing = true;
        Debug.LogWarning("⚠️ GameSettingsManager.combatConfig is unassigned — combat damage is " +
                         "falling back to raw base values: no global multiplier, no own-base " +
                         "vulnerability, no Vanguard scaling.");
    }
}
