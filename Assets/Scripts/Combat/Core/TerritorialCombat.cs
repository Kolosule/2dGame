namespace Game.Combat.Core
{
    /// <summary>
    /// Pure territorial-combat math: ONE debuff, on ONE side (damage dealt), in ONE direction.
    /// Replaces the old lerped two-sided model whose modifiers compounded to a 9x swing
    /// (dealt 1.5 x received 1.5 at own base vs 0.5 x 0.5 at the enemy base) that was invisible
    /// to players and never tuned. Two discrete states, not a gradient — that is what makes it
    /// displayable as an icon.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
    /// </summary>
    public static class TerritorialCombat
    {
        /// <summary>
        /// Territorial advantage strictly below this is the enemy third. Advantage is +1 at your own
        /// base, -1 at the enemy base, 0 at the midpoint (TeamManager.GetTerritorialAdvantage).
        /// The boundary is the enemy THIRD, not the midline, so midfield fighting stays neutral and
        /// only committing deep — where the enemy flag sits — carries the tax.
        /// </summary>
        public const float EnemyThirdBoundary = -0.33f;

        /// <summary>Damage dealt multiplier inside the enemy third with Vanguard locked. Total swing ~3x.</summary>
        public const float FullDebuff = 0.33f;

        /// <summary>Vanguard's top tier. Each tier removes half of the debuff.</summary>
        public const int VanguardMaxTier = 2;

        /// <summary>True when the attacker is deep enough in enemy territory to take the debuff.</summary>
        public static bool InEnemyThird(float territorialAdvantage)
        {
            return territorialAdvantage < EnemyThirdBoundary;
        }

        /// <summary>
        /// Debuff strength after the team's Vanguard tier: 1 - 0.67 * (1 - 0.5 * tier),
        /// giving even thirds 0.33 -> 0.665 -> 1.00 across tiers 0/1/2.
        /// </summary>
        public static float DebuffWithVanguard(int vanguardTier)
        {
            int tier = vanguardTier < 0 ? 0 : (vanguardTier > VanguardMaxTier ? VanguardMaxTier : vanguardTier);
            return 1f - (1f - FullDebuff) * (1f - 0.5f * tier);
        }

        /// <summary>Final damage-dealt multiplier for an attacker at the given advantage.</summary>
        public static float DealtMultiplier(float territorialAdvantage, int vanguardTier)
        {
            return InEnemyThird(territorialAdvantage) ? DebuffWithVanguard(vanguardTier) : 1f;
        }
    }
}
