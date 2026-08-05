using Game.Combat.Core;

namespace Game.Hud.Core
{
    /// <summary>What the zone indicator should show for the local player right now.</summary>
    public enum TerritoryDisplay
    {
        /// <summary>Close enough to your own base that the vulnerability is negligible.</summary>
        Clear,
        /// <summary>Far enough from your own base to be taking bonus damage from any attacker.</summary>
        Penalised,
        /// <summary>Far from your own base, but your team's Vanguard has removed the malus entirely.</summary>
        Lifted
    }

    /// <summary>
    /// Pure own-base-distance-to-display mapping that FOLDS IN the team's Vanguard tier.
    /// Deliberately calls TerritorialCombat.ReceivedMultiplier rather than re-deriving the malus,
    /// so "am I currently taking bonus damage" can't drift from the real damage math WHILE
    /// TERRITORIAL ADVANTAGE IS ENABLED. It does not consult CombatConfig.territorialAdvantageEnabled
    /// or TeamManager's AI-exemption, both of which gate the real damage path — if the flag is ever
    /// turned off, or for a non-human defender, the real path applies x1.0 while this can still
    /// resolve to Penalised. Low risk today: the flag defaults true, the local player driving this
    /// HUD is always a human defender, and a loud warning fires when the flag is off.
    /// The near-base cutoff below which this reads Clear is display-only — the real math has no
    /// such cutoff; it is continuous from the own base outward.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
    /// </summary>
    public static class TerritoryReadout
    {
        private const float NearBaseThreshold = 0.05f;

        public static TerritoryDisplay Resolve(float ownBaseDistance01, int vanguardTier)
        {
            if (ownBaseDistance01 <= NearBaseThreshold) return TerritoryDisplay.Clear;

            float multiplier = TerritorialCombat.ReceivedMultiplier(ownBaseDistance01, vanguardTier);
            return multiplier <= 1f ? TerritoryDisplay.Lifted : TerritoryDisplay.Penalised;
        }
    }
}
