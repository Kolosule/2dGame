using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Team-side unlock math, sharing the individual layer's vocabulary:
    /// cumulative deposited value -> ordered unlock steps -> tiers, via the same BuffUnlock helper
    /// (buffCount == 1, because the team catalog holds exactly one buff and has no ordering to pick).
    ///
    /// Team score is the sum of a whole roster's deposits, so a raw threshold is meaningless across
    /// roster sizes. Thresholds are therefore authored as PER-PLAYER-AVERAGE deposited value and
    /// compared against teamScore / rosterSize. On a 10-player team, {12, 45} means absolute team
    /// scores of 120 and 450.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public static class TeamBuffUnlock
    {
        /// <summary>
        /// Floor of the per-player average deposited value; 0 for an empty roster or a
        /// non-positive score. Integer division keeps derivation deterministic across peers.
        /// </summary>
        public static int PerPlayerAverage(int teamScore, int rosterSize)
        {
            if (rosterSize <= 0 || teamScore <= 0) return 0;
            return teamScore / rosterSize;
        }

        /// <summary>
        /// Tier (0 = locked, up to maxTier) of the single team buff. Pure: same inputs, same tier,
        /// which is what keeps the team layer resimulation-safe with no stored tier state.
        /// </summary>
        public static int TeamTier(IReadOnlyList<int> thresholds, int teamScore, int rosterSize, int maxTier)
        {
            int average = PerPlayerAverage(teamScore, rosterSize);
            int steps = BuffUnlock.UnlockedSteps(thresholds, average);
            return BuffUnlock.TierLevel(steps, priorityPosition: 0, buffCount: 1, maxTier: maxTier);
        }
    }
}
