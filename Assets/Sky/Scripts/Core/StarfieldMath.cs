namespace Game.Sky.Core
{
    /// <summary>A single generated star in world space. Engine-free for EditMode tests.</summary>
    public struct StarPoint
    {
        public float X;
        public float Y;
        public float Size;       // world-unit edge length of the star quad
        public float Brightness; // 0..maxBrightness, applied as vertex alpha
    }

    /// <summary>
    /// Deterministic sparse-starfield generator. Pure math (no UnityEngine) so it runs in plain
    /// EditMode tests. Scatters <paramref name="count"/> stars uniformly inside the rectangle
    /// (minX, minY, width, height); size and brightness are seeded-random within their ranges.
    /// </summary>
    public static class StarfieldMath
    {
        public static StarPoint[] Generate(
            float minX, float minY, float width, float height,
            int count, int seed, float minSize, float maxSize, float maxBrightness)
        {
            if (count < 0) count = 0;
            if (width < 0f) width = 0f;
            if (height < 0f) height = 0f;
            if (maxSize < minSize) maxSize = minSize;
            if (maxBrightness < 0f) maxBrightness = 0f;

            var stars = new StarPoint[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float fx = (float)rng.NextDouble();
                float fy = (float)rng.NextDouble();
                float fs = (float)rng.NextDouble();
                float fb = (float)rng.NextDouble();
                stars[i] = new StarPoint
                {
                    X = minX + fx * width,
                    Y = minY + fy * height,
                    Size = minSize + fs * (maxSize - minSize),
                    Brightness = fb * maxBrightness,
                };
            }
            return stars;
        }
    }
}
