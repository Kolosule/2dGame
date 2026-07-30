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
    /// from TerritorialCombat.DealtMultiplier rather than re-deriving the thresholds: the indicator
    /// is penalised exactly when combat actually penalises you, so the display can never drift from
    /// the damage math.
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
