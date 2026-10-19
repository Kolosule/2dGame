using System;

namespace Game.Combat.Core
{
    public static class MeleeAttackRules
    {
        public static bool CanStart(int verticalAim, bool grounded) =>
            verticalAim >= 0 || !grounded;

        public static bool CancelDownHits(int attackAim, bool grounded, bool alreadyCancelled) =>
            alreadyCancelled || (attackAim < 0 && grounded);

        public static float SpikeVelocity(float currentVelocity, float downwardSpeed)
        {
            if (downwardSpeed <= 0f || float.IsNaN(downwardSpeed) ||
                float.IsInfinity(downwardSpeed))
                throw new ArgumentOutOfRangeException(nameof(downwardSpeed));

            return Math.Min(currentVelocity, -downwardSpeed);
        }
    }
}
