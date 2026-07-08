namespace Game.PlayerMovement.Core
{
    /// <summary>Per-tick horizontal movement parameters, already converted to per-tick units
    /// by the caller (PlayerMovement picks the ground or air set each tick).</summary>
    public struct MoveParams
    {
        public float WalkSpeed;
        public float AccelPerTick;          // rate toward target while input is held
        public float DecelPerTick;          // rate toward zero with no input
        public float MomentumDecayPerTick;  // bleed rate of speed ABOVE WalkSpeed
    }

    /// <summary>
    /// Pure, engine-free movement math (no UnityEngine — this asmdef has noEngineReferences).
    /// Called every simulation tick by PlayerMovement; must stay a pure function of its inputs
    /// so prediction and resimulation agree.
    /// </summary>
    public static class MovementMath
    {
        /// <summary>
        /// Moves currentVx toward inputDir * WalkSpeed. Speed above WalkSpeed (dash carry-over)
        /// is never clamped instantly: with input along travel or neutral, only the excess bleeds
        /// off at MomentumDecayPerTick; counter-input always brakes at the normal rate.
        /// </summary>
        public static float StepHorizontalVelocity(float currentVx, int inputDir, in MoveParams p)
        {
            float speed = System.Math.Abs(currentVx);
            float velDir = currentVx >= 0f ? 1f : -1f;

            // Over-speed momentum rule (spec 1.2).
            if (speed > p.WalkSpeed && (inputDir == 0 || inputDir * velDir > 0f))
            {
                float newSpeed = System.Math.Max(p.WalkSpeed, speed - p.MomentumDecayPerTick);
                return velDir * newSpeed;
            }

            float target = inputDir * p.WalkSpeed;
            float rate = inputDir != 0 ? p.AccelPerTick : p.DecelPerTick;
            return MoveToward(currentVx, target, rate);
        }

        /// <summary>Engine-free Mathf.MoveTowards equivalent.</summary>
        public static float MoveToward(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (System.Math.Abs(delta) <= maxDelta) return target;
            return current + (delta > 0f ? maxDelta : -maxDelta);
        }

        /// <summary>
        /// Gravity-scale multiplier for the jump arc (spec 1.4). Rising = neutral; small |vy| near
        /// the top of an actual jump = apex hang; everything else = heavier fall. A jump-cut or
        /// fast-fall disqualifies the hang; walking off a ledge (jumping=false) never hangs.
        /// </summary>
        public static float SelectGravityMultiplier(
            bool grounded, float vy, float apexThreshold,
            bool jumping, bool jumpCut, bool fastFalling,
            float apexMultiplier, float fallMultiplier)
        {
            if (grounded) return 1f;
            if (vy > apexThreshold) return jumpCut ? fallMultiplier : 1f;
            bool apexEligible = jumping && !jumpCut && !fastFalling && vy > -apexThreshold;
            return apexEligible ? apexMultiplier : fallMultiplier;
        }

        /// <summary>Fast-fall trigger (spec 1.5): airborne, down pressed (edge), at/past the apex,
        /// not already fast-falling.</summary>
        public static bool ShouldStartFastFall(
            bool grounded, bool downPressed, float vy, float apexThreshold, bool alreadyFastFalling)
        {
            return !grounded && downPressed && !alreadyFastFalling && vy <= apexThreshold;
        }

        /// <summary>Terminal velocity: clamps downward speed only.</summary>
        public static float ClampFallSpeed(float vy, float maxFallSpeed)
        {
            return vy < -maxFallSpeed ? -maxFallSpeed : vy;
        }
    }
}
