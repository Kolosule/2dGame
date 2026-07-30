namespace Game.Hud.Core
{
    /// <summary>
    /// Client-side rising-edge detector for tier-ups. A tier-up is a discrete EVENT, but the only
    /// thing replicated is the state it derives from, so the client has to spot the edge itself
    /// inside its OnChangedRender repaint.
    ///
    /// It primes on its first observation and reports nothing for it. That is what stops a late
    /// joiner who arrives already at tier 3 from being greeted by three toasts — the same reason
    /// server-side UnityEvents were the wrong mechanism (they fire behind HasStateAuthority, so on
    /// a dedicated server they fire headless where no client can see them).
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public struct TierUpEdge
    {
        private int previous;
        private bool primed;

        /// <summary>Record the current tier; true only on a genuine rise after priming.</summary>
        public bool Observe(int tier)
        {
            if (!primed)
            {
                previous = tier;
                primed = true;
                return false;
            }

            bool rose = tier > previous;
            previous = tier;
            return rose;
        }

        /// <summary>Forget history so the next observation primes silently (call on Unbind).</summary>
        public void Reset()
        {
            previous = 0;
            primed = false;
        }
    }
}
