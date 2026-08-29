namespace Game.Combat.Core
{
    public enum PlayerHitQueryMode
    {
        CurrentTick,
        Historical
    }

    /// <summary>
    /// Keeps the lag-compensation switch and its safe fallback independent from Fusion.
    /// </summary>
    public static class LagCompensationPolicy
    {
        public static PlayerHitQueryMode Resolve(
            bool featureEnabled,
            bool historyManagerAvailable,
            bool attackerHasValidAuthority)
        {
            return featureEnabled && historyManagerAvailable && attackerHasValidAuthority
                ? PlayerHitQueryMode.Historical
                : PlayerHitQueryMode.CurrentTick;
        }
    }
}
