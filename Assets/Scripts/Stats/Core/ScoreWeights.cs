namespace Game.Stats.Core
{
    /// <summary>
    /// Per-stat weights for the derived Overall Score. Authored/tunable on MatchStatsManager --
    /// see the design spec's weight table for the starting values and their rationale
    /// (objective-first, then explicitly revised so kills/deaths sit at parity).
    /// </summary>
    public struct ScoreWeights
    {
        public float Kill;
        public float Death;
        public float Coin;
        public float FlagCarrySecond;
        public float FlagReturn;

        public static ScoreWeights Default => new ScoreWeights
        {
            Kill = 10f,
            Death = -10f,
            Coin = 0.75f,
            FlagCarrySecond = 1f,
            FlagReturn = 20f
        };
    }
}
