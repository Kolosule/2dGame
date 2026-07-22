namespace Game.Sky.Core
{
    /// <summary>
    /// Pulse curve for constellation glow/scale. Returns a multiplier oscillating in
    /// [1 - amplitude, 1 + amplitude]. <paramref name="frequency"/> is the raw radian rate
    /// (matches sin(time * frequency + phase)); ~0.9 gives a slow, calm pulse. Engine-free.
    /// </summary>
    public static class PulseMath
    {
        public static float Multiplier(float time, float frequency, float amplitude, float phase)
        {
            return 1f + amplitude * (float)System.Math.Sin(time * frequency + phase);
        }
    }
}
