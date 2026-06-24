namespace Game.Buffs.Core
{
    /// <summary>Runtime parameters for an active buff at a given tier (0 = locked).</summary>
    public struct ActiveBuffParams
    {
        public bool Unlocked;
        public float Duration;
        public float Cooldown;
        public bool UsableWhileCarryingFlag;
    }
}
