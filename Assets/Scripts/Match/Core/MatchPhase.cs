namespace Game.Match.Core
{
    /// <summary>
    /// Explicit match life-cycle phases. Lives in the engine-free core assembly so the pure
    /// phase rules in MatchRules are unit-testable outside Unity.
    ///
    /// SuddenDeath is appended LAST on purpose: MatchManager.Phase is a [Networked] byte enum,
    /// so inserting a value between Live and PostMatch would renumber every phase on the wire.
    /// Nothing compares phases by ordering — only by equality — so declaration order is free.
    /// </summary>
    public enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission, SuddenDeath }
}
