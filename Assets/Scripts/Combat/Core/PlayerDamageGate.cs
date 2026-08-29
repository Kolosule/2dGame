namespace Game.Combat.Core
{
    public enum DamageApplyResult
    {
        Applied,
        RejectedNoStateAuthority,
        RejectedDead,
        RejectedSpawnImmunity,
        RejectedHitCooldown
    }

    /// <summary>
    /// Pure precondition checks for server-authoritative player damage.
    /// </summary>
    public static class PlayerDamageGate
    {
        public static DamageApplyResult EvaluatePreCooldown(
            bool hasStateAuthority,
            bool isDead,
            bool hasSpawnImmunity)
        {
            if (!hasStateAuthority) return DamageApplyResult.RejectedNoStateAuthority;
            if (isDead) return DamageApplyResult.RejectedDead;
            if (hasSpawnImmunity) return DamageApplyResult.RejectedSpawnImmunity;
            return DamageApplyResult.Applied;
        }

        public static bool AllowsSecondaryEffects(DamageApplyResult result)
        {
            return result == DamageApplyResult.Applied;
        }
    }
}
