namespace Game.Hud.Core
{
    /// <summary>
    /// Maps a carried coin value to a carrier-aura tier: 0 = no aura, 1..N = highest
    /// (ascending) threshold crossed, inclusive. Pure and engine-free for EditMode tests.
    /// </summary>
    public static class AuraTiers
    {
        public static int Resolve(int totalValue, int[] thresholds)
        {
            if (thresholds == null) return 0;
            int tier = 0;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (totalValue >= thresholds[i]) tier = i + 1;
            }
            return tier;
        }
    }
}
