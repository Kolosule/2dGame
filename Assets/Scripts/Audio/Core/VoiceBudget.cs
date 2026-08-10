namespace Game.Audio.Core
{
    /// <summary>
    /// Gate 3 of 3 in the playback path (cull -> dedupe -> BUDGET). Owns a fixed set of voice slots
    /// and decides which one an incoming cue gets, or that it gets none. Never grows, never
    /// allocates after construction: worst-case concurrent voices is a constant regardless of how
    /// many players are in the match.
    ///
    /// Slot indices map 1:1 onto the AudioSource array the Unity layer preallocates, so this type
    /// stays engine-free and fully unit-testable.
    /// </summary>
    public sealed class VoiceBudget
    {
        private struct Voice
        {
            public bool Active;
            public int CueId;
            public int Priority;
            public float StartTime;
        }

        private readonly Voice[] voices;

        public VoiceBudget(int capacity)
        {
            if (capacity < 1) capacity = 1;
            voices = new Voice[capacity];
        }

        public int Capacity => voices.Length;

        public bool IsActive(int slot) => slot >= 0 && slot < voices.Length && voices[slot].Active;

        /// <summary>
        /// Returns the slot the cue should play on, or -1 if it must be dropped.
        ///
        /// Order: (1) per-cue concurrency -- once a cue is at maxConcurrent, it recycles its OWN
        /// oldest voice rather than adding another, so the newest instance is always the audible
        /// one; (2) any free slot; (3) steal the lowest-priority active voice, oldest first among
        /// ties, but only if its priority is <= the incoming cue's. Nothing steals from a
        /// higher-priority voice -- that is what keeps EnemyTelegraph and match stingers audible
        /// while a scrum is saturating the pool.
        /// </summary>
        public int TryAcquire(int cueId, int priority, int maxConcurrent, float now)
        {
            if (maxConcurrent > 0)
            {
                int sameCue = 0;
                int oldestSame = -1;
                for (int i = 0; i < voices.Length; i++)
                {
                    if (!voices[i].Active || voices[i].CueId != cueId) continue;
                    sameCue++;
                    if (oldestSame < 0 || voices[i].StartTime < voices[oldestSame].StartTime)
                        oldestSame = i;
                }

                if (sameCue >= maxConcurrent && oldestSame >= 0)
                {
                    Occupy(oldestSame, cueId, priority, now);
                    return oldestSame;
                }
            }

            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Active) continue;
                Occupy(i, cueId, priority, now);
                return i;
            }

            int victim = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Priority > priority) continue;

                if (victim < 0
                    || voices[i].Priority < voices[victim].Priority
                    || (voices[i].Priority == voices[victim].Priority
                        && voices[i].StartTime < voices[victim].StartTime))
                {
                    victim = i;
                }
            }

            if (victim < 0) return -1;

            Occupy(victim, cueId, priority, now);
            return victim;
        }

        public void Release(int slot)
        {
            if (slot < 0 || slot >= voices.Length) return;
            voices[slot].Active = false;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < voices.Length; i++) voices[i].Active = false;
        }

        private void Occupy(int slot, int cueId, int priority, float now)
        {
            voices[slot].Active = true;
            voices[slot].CueId = cueId;
            voices[slot].Priority = priority;
            voices[slot].StartTime = now;
        }
    }
}
