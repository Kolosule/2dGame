using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>The approved upgrade order for every player, repeated once per tier.</summary>
    public static class BuffUpgradePath
    {
        public static IReadOnlyList<BuffId> Order { get; } = System.Array.AsReadOnly(new[]
        {
            BuffId.ExtraJump, BuffId.QuickerDash, BuffId.Stealth, BuffId.FlagRunner
        });

        public static int PositionOf(BuffId id)
        {
            for (int i = 0; i < Order.Count; i++)
                if (Order[i] == id) return i;
            return -1;
        }
    }
}
