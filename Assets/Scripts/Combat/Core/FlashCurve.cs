using System;

namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, engine-free flash intensity. Returns 1 at the moment of impact and
    /// decays linearly to 0 over <paramref name="duration"/> seconds. Callers
    /// map this to Color.Lerp(baseColor, white, intensity). Unit-testable.
    /// </summary>
    public static class FlashCurve
    {
        public static float Intensity(float elapsed, float duration)
        {
            if (duration <= 0f) return 0f;
            float t = 1f - (elapsed / duration);
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }
    }
}
