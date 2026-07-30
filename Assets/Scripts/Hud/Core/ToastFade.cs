namespace Game.Hud.Core
{
    /// <summary>
    /// Pure hold-then-fade alpha for transient notifications: opaque through holdSeconds, then
    /// linear to 0 across fadeSeconds, then 0 forever. Callers drive it with their own elapsed
    /// time and set CanvasGroup.alpha.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public static class ToastFade
    {
        public static float Alpha01(float elapsed, float holdSeconds, float fadeSeconds)
        {
            if (elapsed <= holdSeconds) return 1f;
            if (fadeSeconds <= 0f) return 0f;

            float t = (elapsed - holdSeconds) / fadeSeconds;
            if (t >= 1f) return 0f;
            return 1f - t;
        }
    }
}
