using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure progress math for the unlock curves: where the next tier-up sits, and how far along
    /// the way a total is. Used by the HUD only — nothing here decides a tier (BuffUnlock does),
    /// so a bug here cannot desync gameplay.
    /// The individual layer calls it with the player's priority position and buffCount 4; the team
    /// layer with position 0 and buffCount 1, matching TeamBuffUnlock's convention. One
    /// implementation serves both.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Feedback surfaces".
    /// </summary>
    public static class BuffProgress
    {
        /// <summary>
        /// Index of the next unlock step that would raise the buff at this priority position, or
        /// -1 once the curve is exhausted. Steps for a position are position, position + buffCount,
        /// position + 2*buffCount, ... under the round-robin.
        /// </summary>
        public static int NextStepIndexFor(int unlockedSteps, int priorityPosition, int buffCount,
                                           int thresholdCount)
        {
            if (buffCount <= 0 || priorityPosition < 0) return -1;
            int i = priorityPosition;
            while (i < unlockedSteps) i += buffCount;
            return i < thresholdCount ? i : -1;
        }

        /// <summary>Highest threshold at or below the value, or 0 when none is crossed yet.</summary>
        public static int HighestCrossed(IReadOnlyList<int> thresholds, int value)
        {
            if (thresholds == null) return 0;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            return steps <= 0 ? 0 : thresholds[steps - 1];
        }

        /// <summary>
        /// Where value sits between lower and upper, clamped to 0..1. A degenerate range
        /// (upper &lt;= lower) reads as full, so an exhausted curve never renders as an empty bar.
        /// </summary>
        public static float Fraction01(int value, int lower, int upper)
        {
            if (upper <= lower) return 1f;
            if (value <= lower) return 0f;
            if (value >= upper) return 1f;
            return (float)(value - lower) / (upper - lower);
        }

        /// <summary>
        /// The deposited value at which the buff at this position next tiers up; 0 when it can
        /// rise no further.
        /// </summary>
        public static int NextThresholdFor(IReadOnlyList<int> thresholds, int value,
                                           int priorityPosition, int buffCount)
        {
            if (thresholds == null) return 0;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            int next = NextStepIndexFor(steps, priorityPosition, buffCount, thresholds.Count);
            return next < 0 ? 0 : thresholds[next];
        }

        /// <summary>
        /// Fill 0..1 from the last threshold crossed by ANY buff to the next one that raises THIS
        /// buff. Reaches exactly 1 on the deposit that tiers it up, and reads 1 when nothing is
        /// left to unlock.
        /// </summary>
        public static float ToNextTier01(IReadOnlyList<int> thresholds, int value,
                                         int priorityPosition, int buffCount)
        {
            if (thresholds == null) return 1f;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            int next = NextStepIndexFor(steps, priorityPosition, buffCount, thresholds.Count);
            if (next < 0) return 1f;
            return Fraction01(value, HighestCrossed(thresholds, value), thresholds[next]);
        }
    }
}
