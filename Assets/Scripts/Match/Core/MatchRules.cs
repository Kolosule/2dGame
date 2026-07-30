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

        /// <summary>
        /// The phase a timer expiry advances to. An untimed/terminal phase (Intermission) maps to
        /// itself, meaning "no transition" — callers must treat phase == NextOnTimerExpiry(phase)
        /// as a no-op rather than a self re-entry.
        ///
        /// Live -> SuddenDeath (not a winner resolution): capture is the only win condition, so a
        /// Live timer expiry must never crown a winner from coin score. It hands off to Sudden
        /// Death, where play continues with no clock until the next capture ends the match.
        /// </summary>
        public static MatchPhase NextOnTimerExpiry(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Warmup: return MatchPhase.Countdown;
                case MatchPhase.Countdown: return MatchPhase.Live;
                case MatchPhase.Live: return MatchPhase.SuddenDeath;
                case MatchPhase.SuddenDeath: return MatchPhase.PostMatch;
                case MatchPhase.PostMatch: return MatchPhase.Intermission;
                case MatchPhase.Intermission: return MatchPhase.Intermission;
                default: return phase;
            }
        }

        /// <summary>
        /// True only for SuddenDeath: this is the operator hard-cap's ops safety valve (a headless
        /// dedicated server must not wedge on an unwinnable match forever), not a game rule — the
        /// hard cap defaults to off, so in default play this is unreachable and capture remains the
        /// only way a match ends.
        /// </summary>
        public static bool ResolvesAsDrawOnTimerExpiry(MatchPhase phase) => phase == MatchPhase.SuddenDeath;
    }
}
