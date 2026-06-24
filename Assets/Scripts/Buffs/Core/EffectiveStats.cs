namespace Game.Buffs.Core
{
    /// <summary>
    /// Derived per-player stat contributions, built fresh each query by summing every
    /// loadout buff's ContributeStats at its current tier. Never persisted/networked.
    /// </summary>
    public struct EffectiveStats
    {
        public int BonusAirJumps;
        public bool UnlimitedAirJumps;
        public float DashCooldownMultiplier;
        public float DashTimeMultiplier;
        public bool DashDealsDamage;

        public static EffectiveStats Default() => new EffectiveStats
        {
            BonusAirJumps = 0,
            UnlimitedAirJumps = false,
            DashCooldownMultiplier = 1f,
            DashTimeMultiplier = 1f,
            DashDealsDamage = false,
        };
    }
}
