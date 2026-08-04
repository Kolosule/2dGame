namespace Game.Stats.Core
{
    /// <summary>
    /// The scoreboard's headline stat, derived on query from five networked inputs -- never
    /// stored, so there is nothing to keep in sync with its own inputs and nothing to reset.
    /// Captures are deliberately NOT an input (tracked/displayed separately) -- see
    /// docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md, "Overall score".
    /// </summary>
    public static class ScoreFormula
    {
        public static float Compute(int kills, int deaths, int coinsDeposited, int flagCarrySeconds,
                                     int flagReturns, ScoreWeights weights)
        {
            return kills * weights.Kill
                 + deaths * weights.Death
                 + coinsDeposited * weights.Coin
                 + flagCarrySeconds * weights.FlagCarrySecond
                 + flagReturns * weights.FlagReturn;
        }
    }
}
