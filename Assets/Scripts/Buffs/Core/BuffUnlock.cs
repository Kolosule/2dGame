using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure, Fusion-free unlock math. Nine ordered unlock steps are gated by a cumulative
    /// deposited-value threshold list; step i unlocks priority[i % buffCount] to tier i / buffCount + 1.
    /// A buff's tier is therefore derivable from (unlocked steps, its priority position).
    /// </summary>
    public static class BuffUnlock
    {
        /// <summary>Number of unlock steps reached: how many thresholds are at or below the total.</summary>
        public static int UnlockedSteps(IReadOnlyList<int> thresholds, int totalValue)
        {
            if (thresholds == null) return 0;
            int count = 0;
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i] <= totalValue) count++;
                else break; // thresholds are ascending
            }
            return count;
        }

        /// <summary>
        /// Tier (0..maxTier) of the buff at the given priority position, given how many steps
        /// are unlocked. Counts how many unlocked step indices land on this position under the
        /// round-robin (i % buffCount == position).
        /// </summary>
        public static int TierLevel(int unlockedSteps, int priorityPosition, int buffCount, int maxTier)
        {
            if (buffCount <= 0 || unlockedSteps <= priorityPosition) return 0;
            int tier = (unlockedSteps - priorityPosition - 1) / buffCount + 1;
            if (tier < 0) return 0;
            return tier > maxTier ? maxTier : tier;
        }
    }
}
