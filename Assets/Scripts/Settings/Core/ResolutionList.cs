using System;
using System.Collections.Generic;

namespace Game.Settings.Core
{
    /// <summary>A width x height pair, refresh rate deliberately excluded.</summary>
    public readonly struct ResolutionOption : IEquatable<ResolutionOption>
    {
        public readonly int Width;
        public readonly int Height;

        public ResolutionOption(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool Equals(ResolutionOption other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is ResolutionOption other && Equals(other);
        public override int GetHashCode() => (Width * 397) ^ Height;
        public override string ToString() => Width + " x " + Height;
    }

    /// <summary>
    /// Pure list handling for the resolution dropdown. Screen.resolutions contains one entry per
    /// refresh-rate variant, so a raw listing shows the same size several times; refresh rate is
    /// not a user-facing setting here, so the list is collapsed by size.
    /// </summary>
    public static class ResolutionList
    {
        public static List<ResolutionOption> Deduplicate(IReadOnlyList<ResolutionOption> raw)
        {
            var result = new List<ResolutionOption>();
            if (raw == null) return result;

            var seen = new HashSet<ResolutionOption>();
            for (int i = 0; i < raw.Count; i++)
            {
                ResolutionOption option = raw[i];
                if (option.Width <= 0 || option.Height <= 0) continue;
                if (!seen.Add(option)) continue;
                result.Add(option);
            }

            return result;
        }

        /// <summary>Index of the given size, or -1 when the list does not offer it.</summary>
        public static int IndexOf(IReadOnlyList<ResolutionOption> options, int width, int height)
        {
            if (options == null) return -1;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Width == width && options[i].Height == height) return i;
            }

            return -1;
        }

        /// <summary>
        /// Which entry the dropdown should select: the stored size when it is still offered,
        /// otherwise the display's native size, otherwise the largest available. The last fallback
        /// exists so an unrecognised display never lands the player on a postage-stamp window;
        /// in practice native is always enumerated. Returns -1 only for an empty list.
        /// </summary>
        public static int ResolveStoredIndex(
            IReadOnlyList<ResolutionOption> options,
            int storedWidth, int storedHeight,
            int nativeWidth, int nativeHeight)
        {
            if (options == null || options.Count == 0) return -1;

            int index = IndexOf(options, storedWidth, storedHeight);
            if (index >= 0) return index;

            index = IndexOf(options, nativeWidth, nativeHeight);
            if (index >= 0) return index;

            int best = 0;
            long bestArea = (long)options[0].Width * options[0].Height;
            for (int i = 1; i < options.Count; i++)
            {
                long area = (long)options[i].Width * options[i].Height;
                if (area <= bestArea) continue;
                bestArea = area;
                best = i;
            }

            return best;
        }
    }
}
