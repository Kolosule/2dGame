namespace Game.Buffs.Core
{
    /// <summary>Stable network token for each buff. Serialized as a byte in PlayerBuffs.LoadoutOrder.</summary>
    public enum BuffId : byte
    {
        ExtraJump = 0,
        Stealth = 1,
        QuickerDash = 2,
        FlagRunner = 3,
    }
}
