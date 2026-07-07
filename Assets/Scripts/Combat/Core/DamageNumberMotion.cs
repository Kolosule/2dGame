namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, engine-free motion for a floating damage number: constant upward
    /// drift plus a linear alpha fade over its lifetime. Unit-testable.
    /// </summary>
    public static class DamageNumberMotion
    {
        public static float YOffset(float elapsed, float riseSpeed) => riseSpeed * elapsed;

        public static float Alpha(float elapsed, float lifetime)
        {
            if (lifetime <= 0f) return 0f;
            float a = 1f - (elapsed / lifetime);
            if (a < 0f) return 0f;
            if (a > 1f) return 1f;
            return a;
        }
    }
}
