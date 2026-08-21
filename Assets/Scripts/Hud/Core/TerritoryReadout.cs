using Game.Combat.Core;

namespace Game.Hud.Core
{
    /// <summary>
    /// Pure own-base-distance-to-display mapping that FOLDS IN the team's Vanguard tier.
    /// Deliberately calls TerritorialCombat.ReceivedMultiplier rather than re-deriving the malus,
    /// so the percentage shown can't drift from the real damage math WHILE TERRITORIAL ADVANTAGE
    /// IS ENABLED. It does not consult CombatConfig.territorialAdvantageEnabled or TeamManager's
    /// AI-exemption, both of which gate the real damage path — if the flag is ever turned off, or
    /// for a non-human defender, the real path applies x1.0 while this still reports a penalty.
    /// Low risk today: the flag defaults true, the local player driving this HUD is always a human
    /// defender, and a loud warning fires when the flag is off. Note the displayed figure is now a
    /// precise percentage rather than a coarse band, so that mismatch would read as a specific lie
    /// rather than a vague one.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
    /// </summary>
    public static class TerritoryReadout
    {
        /// <summary>
        /// Extra damage the defender currently takes, as a whole-number percentage over baseline
        /// (0 = taking normal damage, 150 = taking x2.5). Continuous from the own base outward,
        /// with no near-base cutoff, matching the real math exactly.
        /// </summary>
        public static int ExtraDamagePercent(float ownBaseDistance01, int vanguardTier)
        {
            float multiplier = TerritorialCombat.ReceivedMultiplier(ownBaseDistance01, vanguardTier);
            float percent = (multiplier - 1f) * 100f;
            if (percent <= 0f) return 0;
            // Round half up without pulling in UnityEngine.Mathf (this asmdef is engine-free).
            return (int)(percent + 0.5f);
        }
    }
}
