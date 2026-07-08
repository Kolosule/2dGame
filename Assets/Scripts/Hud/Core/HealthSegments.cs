namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free segmented-health-bar math. A bar of <c>segmentCount</c> discrete blocks
    /// shows <c>FilledSegments</c> fully-lit blocks plus one partially-lit block at <c>PartialFill01</c>.
    /// Unit-testable; no UnityEngine dependency.
    /// </summary>
    public static class HealthSegments
    {
        private static float Fraction(float current, float max)
        {
            if (max <= 0f) return 0f;
            float f = current / max;
            if (f < 0f) return 0f;
            if (f > 1f) return 1f;
            return f;
        }

        /// <summary>Number of fully-lit segments (0..segmentCount).</summary>
        public static int FilledSegments(float current, float max, int segmentCount)
        {
            if (segmentCount <= 0) return 0;
            int filled = (int)(Fraction(current, max) * segmentCount);
            if (filled > segmentCount) filled = segmentCount;
            return filled;
        }

        /// <summary>Fractional fill (0..1) of the single partially-lit segment above the filled ones.</summary>
        public static float PartialFill01(float current, float max, int segmentCount)
        {
            if (segmentCount <= 0) return 0f;
            float exact = Fraction(current, max) * segmentCount;
            float partial = exact - (int)exact;
            if (partial < 0f) return 0f;
            if (partial > 1f) return 1f;
            return partial;
        }
    }
}
