using Game.Combat.Core;

namespace Game.Hud.Core
{
    /// <summary>What the zone indicator should show for the local player right now.</summary>
    public enum TerritoryDisplay
    {
        /// <summary>Own half or midfield — no territorial tax applies here.</summary>
        Clear,
        /// <summary>Deep in the enemy third with the debuff still biting.</summary>
        Penalised,
        /// <summary>Deep in the enemy third, but the team's Vanguard has lifted the debuff entirely.</summary>
        Lifted
    }

    /// <summary>
    /// Pure zone-to-display mapping that FOLDS IN the team's Vanguard tier. Deliberately derived
    /// from TerritorialCombat.DealtMultiplier rather than re-deriving the thresholds, so it cannot
    /// drift from the zone/Vanguard math WHILE TERRITORIAL ADVANTAGE IS ENABLED. It does not
    /// consult CombatConfig.territorialAdvantageEnabled or TeamManager's Team3AI exemption, both of
    /// which gate the real damage path — if the flag is ever turned off, the real path applies
    /// ×1.0 while this can still resolve to Penalised. Low risk today: the flag defaults true, no
    /// CombatConfig asset overrides it, and a loud warning fires when it is off.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Merged Team Power strip".
    /// </summary>
    public static class TerritoryReadout
    {
        public static TerritoryDisplay Resolve(float territorialAdvantage, int vanguardTier)
        {
            if (!TerritorialCombat.InEnemyThird(territorialAdvantage)) return TerritoryDisplay.Clear;

            return TerritorialCombat.DealtMultiplier(territorialAdvantage, vanguardTier) >= 1f
                ? TerritoryDisplay.Lifted
                : TerritoryDisplay.Penalised;
        }
    }
}
