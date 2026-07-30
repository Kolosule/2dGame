using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure, Fusion-free conversion between a priority order and the byte payload that travels
    /// over reliable-data and lands in PlayerBuffs.LoadoutOrder. One implementation so the lobby
    /// UI and the server cannot drift apart on encoding or on the capacity cap.
    /// </summary>
    public static class LoadoutCodec
    {
        /// <summary>Matches the NetworkArray capacity on PlayerBuffs.LoadoutOrder.</summary>
        public const int MaxEntries = 8;

        public static byte[] ToBytes(IReadOnlyList<BuffId> order)
        {
            if (order == null) return new byte[0];
            int n = order.Count < MaxEntries ? order.Count : MaxEntries;
            var bytes = new byte[n];
            for (int i = 0; i < n; i++) bytes[i] = (byte)order[i];
            return bytes;
        }

        public static BuffId[] FromBytes(IReadOnlyList<byte> bytes)
        {
            if (bytes == null) return new BuffId[0];
            int n = bytes.Count < MaxEntries ? bytes.Count : MaxEntries;
            var order = new BuffId[n];
            for (int i = 0; i < n; i++) order[i] = (BuffId)bytes[i];
            return order;
        }
    }
}
