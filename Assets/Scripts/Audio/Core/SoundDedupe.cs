using System.Collections.Generic;

namespace Game.Audio.Core
{
    /// <summary>
    /// Gate 2 of 3 in the playback path (cull -> DEDUPE -> budget). Suppresses a cue that already
    /// played within its window.
    ///
    /// Keyed by cue id and NOT by instigator, deliberately: twenty players landing hits in the same
    /// frame must produce ONE impact sound, not twenty overlapping copies. That is a correctness
    /// requirement at this game's player count, not a polish item.
    ///
    /// A SUPPRESSED attempt does not move the window. If it did, continuous fire would push the
    /// window forward forever and the cue would go permanently silent.
    /// </summary>
    public sealed class SoundDedupe
    {
        private readonly Dictionary<int, float> lastPlayTime = new Dictionary<int, float>();

        /// <summary>True if the cue may play now; records the play time as a side effect when it
        /// returns true. A window of 0 or less disables dedupe for that cue.</summary>
        public bool ShouldPlay(int cueId, float now, float window)
        {
            if (window <= 0f) return true;

            if (lastPlayTime.TryGetValue(cueId, out float last) && now - last < window)
                return false;

            lastPlayTime[cueId] = now;
            return true;
        }

        public void Clear() => lastPlayTime.Clear();
    }
}
