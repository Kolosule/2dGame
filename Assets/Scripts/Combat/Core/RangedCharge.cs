using System;

namespace Game.Combat.Core
{
    /// <summary>Tick-based charge state, copied to/from Fusion's rollback state by PlayerCombat.</summary>
    public struct RangedCharge
    {
        public bool HasInput { get; private set; }
        public uint InputSequence { get; private set; }
        public uint CancellationSequence { get; private set; }
        public bool IsCharging { get; private set; }
        public int StartTick { get; private set; }

        public RangedCharge(
            bool hasInput, uint inputSequence, bool isCharging, int startTick, uint cancellationSequence = 0)
        {
            HasInput = hasInput;
            InputSequence = inputSequence;
            CancellationSequence = cancellationSequence;
            IsCharging = isCharging;
            StartTick = startTick;
        }

        /// <summary>Returns charge ticks for a released shot, or null when no shot was released.</summary>
        public int? Sample(int currentTick, uint inputSequence, bool held, uint cancellationSequence = 0)
        {
            if (!HasInput || cancellationSequence != CancellationSequence)
            {
                Cancel(inputSequence, cancellationSequence);
                return null;
            }

            uint transitions = unchecked(inputSequence - InputSequence);
            InputSequence = inputSequence;
            if (transitions == 0) return null;

            int? chargeTicks = null;
            if (!held || transitions > 1)
            {
                if (IsCharging)
                    chargeTicks = Math.Max(0, unchecked(currentTick - StartTick));
                // A held sample needs press/release/press; release/repress can follow cancellation.
                else if (transitions >= (held ? 3u : 2u))
                    chargeTicks = 0;
            }

            // A release/repress fires the old charge and starts the next one on this tick.
            IsCharging = held;
            StartTick = held ? currentTick : 0;
            return chargeTicks;
        }

        public void Cancel(uint? inputSequence, uint cancellationSequence = 0)
        {
            HasInput = inputSequence.HasValue;
            if (HasInput)
            {
                InputSequence = inputSequence.Value;
                CancellationSequence = cancellationSequence;
            }
            IsCharging = false;
            StartTick = 0;
        }

        public static bool IsConfigurationValid(float minimumSpeed, float maximumSpeed, float chargeTime)
        {
            return IsFinite(minimumSpeed) && minimumSpeed >= 0f &&
                   IsFinite(maximumSpeed) && maximumSpeed > 0f && maximumSpeed >= minimumSpeed &&
                   IsFinite(chargeTime) && chargeTime > 0f;
        }

        public static float ResolveSpeed(
            int chargeTicks, float tickDuration, float minimumSpeed, float maximumSpeed, float chargeTime)
        {
            if (!IsConfigurationValid(minimumSpeed, maximumSpeed, chargeTime))
                throw new ArgumentException("Ranged charge requires finite, ordered speeds and a positive charge time.");
            if (!IsFinite(tickDuration) || tickDuration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(tickDuration));

            double fraction = Math.Min(1d, Math.Max(0, chargeTicks) * (double)tickDuration / chargeTime);
            return minimumSpeed + (maximumSpeed - minimumSpeed) * (float)fraction;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
