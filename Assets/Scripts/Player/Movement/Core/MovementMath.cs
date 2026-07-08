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
    }
}
