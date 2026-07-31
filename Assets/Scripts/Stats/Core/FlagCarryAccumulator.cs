namespace Game.Stats.Core
{
    /// <summary>
    /// Converts a per-tick delta time into whole seconds to flush to the networked stat table,
    /// keeping the sub-second remainder for the next tick. Bounds the networked write rate to at
    /// most once per second per carried flag, regardless of tick rate.
    /// </summary>
    public static class FlagCarryAccumulator
    {
        public static int Tick(ref float remainderSeconds, float deltaTime)
        {
            remainderSeconds += deltaTime;
            int whole = (int)remainderSeconds;
            remainderSeconds -= whole;
            return whole;
        }
    }
}
