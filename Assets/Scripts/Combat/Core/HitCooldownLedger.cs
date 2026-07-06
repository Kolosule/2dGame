using System.Collections.Generic;

namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, Fusion-free per-attacker hit-cooldown tracking. Replaces the old single global
    /// hit-cooldown TickTimer on PlayerStatsHandler, which silently ate a second attacker's
    /// hit landing within the window. Keyed by an opaque attacker id (NetworkId.Raw of the
    /// attacking object); ticks are plain ints so this is unit-testable in EditMode.
    /// Server-only state — never networked, cleared on respawn.
    /// </summary>
    public class HitCooldownLedger
    {
        private readonly Dictionary<ulong, int> lastHitTick = new Dictionary<ulong, int>();

        /// <summary>
        /// True if this attacker may hit now; records the hit tick when allowed.
        /// A blocked hit does NOT re-arm the window.
        /// </summary>
        public bool TryRegisterHit(ulong attackerKey, int currentTick, int cooldownTicks)
        {
            if (lastHitTick.TryGetValue(attackerKey, out int last) &&
                currentTick - last < cooldownTicks)
            {
                return false;
            }
            lastHitTick[attackerKey] = currentTick;
            return true;
        }

        public void Clear() => lastHitTick.Clear();
    }
}
