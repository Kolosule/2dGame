namespace Game.Stats.Core
{
    /// <summary>Maps a Fusion PlayerId directly to a MatchStatsManager roster slot, bounds-checked.</summary>
    public static class RosterIndex
    {
        public static bool TryResolve(int playerId, int capacity, out int index)
        {
            index = playerId;
            return playerId >= 0 && playerId < capacity;
        }
    }
}
