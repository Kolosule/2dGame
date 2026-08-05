namespace Game.Combat.Core
{
    /// <summary>
    /// Pure territorial-combat math: a DEFENDER takes more damage the farther they are from
    /// their OWN base — from enemy AI and the opposing human team alike. Vanguard tiers reduce
    /// a team's own vulnerability: tier 0 = full malus, tier 1 = half, tier 2 = none.
    /// Continuous, not quantized: the malus scales smoothly with distance from 1.0x at the own
    /// base up to a capped maximum at (or beyond) the enemy base.
    /// Replaces the old attacker-side "enemy third" model, which was one-sided (damage DEALT
    /// only) and quantized into two discrete states specifically for HUD legibility. This model
    /// is defender-side and continuous; Game.Hud.Core.TerritoryReadout buckets it back down for
    /// display without re-deriving the thresholds.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
    /// </summary>
    public static class TerritorialCombat
    {
        /// <summary>
        /// Extra damage-taken fraction at maximum distance from own base, before Vanguard.
        /// A defender at their own base always takes x1.0; at max distance with no Vanguard
        /// (tier 0) they take x(1 + this) = x2.5.
        /// </summary>
        public const float MaxVulnerabilityMalus = 1.5f;

        /// <summary>Vanguard's top tier. Each tier removes half of the remaining malus.</summary>
        public const int VanguardMaxTier = 2;

        /// <summary>
        /// Damage-taken multiplier for a defender at the given normalized own-base distance
        /// (0 = at their own base, 1 = at or beyond the enemy base, clamped) and Vanguard tier
        /// (clamped to [0, VanguardMaxTier]).
        /// </summary>
        public static float ReceivedMultiplier(float ownBaseDistance01, int vanguardTier)
        {
            float distance = Clamp01(ownBaseDistance01);
            int tier = ClampTier(vanguardTier);
            float remaining = 1f - 0.5f * tier;
            return 1f + MaxVulnerabilityMalus * distance * remaining;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static int ClampTier(int tier)
        {
            return tier < 0 ? 0 : (tier > VanguardMaxTier ? VanguardMaxTier : tier);
        }
    }
}
