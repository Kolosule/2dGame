namespace Game.Match.Core
{
    /// <summary>
    /// Pure, engine-free match-phase rules. The single place that answers "is play running?"
    /// and "are all buffs force-unlocked?", so the several gameplay gates that used to test
    /// Phase == Live directly cannot drift apart from each other.
    /// </summary>
    public static class MatchRules
    {
        /// <summary>
        /// Play is running: player input is live, enemies think, flags can be carried, and a
        /// capture counts. True in Live AND SuddenDeath — Sudden Death is normal play with no
        /// clock, so every gate that means "the match is being played" must use this.
        /// </summary>
        public static bool IsPlayActive(MatchPhase phase) =>
            phase == MatchPhase.Live || phase == MatchPhase.SuddenDeath;

        /// <summary>
        /// Every buff tier is forced to its maximum for every player. Applied as a READ-TIME
        /// override on tier resolution, so no per-player state is written, nothing is mutated,
        /// and there is nothing to reset or replay on resimulation.
        /// </summary>
        public static bool AllBuffsMaxed(MatchPhase phase) => phase == MatchPhase.SuddenDeath;
    }
}
