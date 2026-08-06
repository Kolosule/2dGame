namespace Game.Combat.Core
{
    /// <summary>
    /// The single gate every player-damaging source must pass before dealing player-vs-player
    /// damage. Takes team NUMBERS (TeamUtil.ToNumber convention: 0 = unassigned) rather than the
    /// Team enum itself -- this assembly is engine-free (Game.Combat.Core.asmdef has
    /// noEngineReferences: true and references: []) and Team/TeamUtil live in the default
    /// assembly, which this cannot reference. Convert at the call site via TeamUtil.ToNumber.
    ///
    /// isSelf always blocks, regardless of team. A team number of 0 (unassigned, or not yet
    /// replicated to this peer) is non-hostile on either side -- matches TeamUtil.AreEnemies's
    /// treatment of Team.None -- so a hit can never land on a player whose team hasn't
    /// replicated yet (the spawn/late-join/reconnect window).
    /// </summary>
    public static class FriendlyFire
    {
        public static bool CanDamagePlayer(int attackerTeam, int defenderTeam, bool isSelf)
        {
            if (isSelf) return false;
            if (attackerTeam == 0 || defenderTeam == 0) return false;
            return attackerTeam != defenderTeam;
        }
    }
}
