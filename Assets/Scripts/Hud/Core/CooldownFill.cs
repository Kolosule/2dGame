namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free cooldown fill fraction. 1 = ready, 0 = just used, ramping up while it
    /// recharges. Mirrors PlayerMovement.GetDashCooldownPercent so dash and stealth radials read
    /// identically. Callers set Image.fillAmount = Fill01(remaining, total).
    /// </summary>
    public static class CooldownFill
    {
        public static float Fill01(float remaining, float total)
        {
            if (total <= 0f) return 1f;
            float frac = remaining / total;
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;
            return 1f - frac;
        }
    }
}
