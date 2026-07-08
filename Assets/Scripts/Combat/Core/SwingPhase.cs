namespace Game.Combat.Core
{
    /// <summary>Melee swing phase (spec Part 2): Startup -> Active -> Recovery -> None.</summary>
    public enum SwingPhaseKind { None, Startup, Active, Recovery }

    /// <summary>
    /// Pure, engine-free swing-phase derivation. The swing is fully described by its networked
    /// start tick + tick-count windows; deriving the phase per tick (instead of storing it)
    /// makes the whole system resimulation-proof.
    /// </summary>
    public static class SwingPhase
    {
        public static SwingPhaseKind Resolve(int currentTick, int attackStartTick,
                                             int startupTicks, int activeTicks, int recoveryTicks)
        {
            if (attackStartTick <= 0) return SwingPhaseKind.None;
            int elapsed = currentTick - attackStartTick;
            if (elapsed < 0) return SwingPhaseKind.None;
            if (elapsed < startupTicks) return SwingPhaseKind.Startup;
            if (elapsed < startupTicks + activeTicks) return SwingPhaseKind.Active;
            if (elapsed < startupTicks + activeTicks + recoveryTicks) return SwingPhaseKind.Recovery;
            return SwingPhaseKind.None;
        }

        /// <summary>True exactly on the first Active tick (used for the ground-pound impulse).</summary>
        public static bool IsFirstActiveTick(int currentTick, int attackStartTick, int startupTicks)
        {
            return attackStartTick > 0 && currentTick - attackStartTick == startupTicks;
        }
    }
}
