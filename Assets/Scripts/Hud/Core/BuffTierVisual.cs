namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free mapping from a buff's tier to its visual intensity. Tier 0 is locked
    /// (dim/gray); intensity ramps linearly to 1 at maxTier. Callers map intensity onto icon
    /// color/glow, e.g. Color.Lerp(dimColor, accentColor, Intensity01(tier, maxTier)).
    /// </summary>
    public static class BuffTierVisual
    {
        public static bool IsLocked(int tier) => tier <= 0;

        public static float Intensity01(int tier, int maxTier)
        {
            if (maxTier <= 0 || tier <= 0) return 0f;
            float t = (float)tier / maxTier;
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }

        /// <summary>
        /// Whether the pip at this index (0-based, ascending) is filled at the given tier. Pips
        /// make the tier readable EXACTLY, instead of inferred from a colour lerp.
        /// </summary>
        public static bool PipFilled(int pipIndex, int tier) => pipIndex >= 0 && pipIndex < tier;
    }
}
