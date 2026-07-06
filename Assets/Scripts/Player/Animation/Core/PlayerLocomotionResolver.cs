namespace Game.PlayerAnimation.Core
{
    /// <summary>
    /// Tunable thresholds for locomotion resolution. Plain struct, no Unity types, so the resolver
    /// stays unit-testable. Mirrored by [SerializeField] fields on PlayerAnimator.
    /// </summary>
    public struct LocomotionTuning
    {
        /// <summary>|horizontal speed| must EXCEED this (while grounded) to start the Walk pose.</summary>
        public float WalkEnterSpeed;

        /// <summary>|horizontal speed| must DROP BELOW this to return to Idle. Keep it below
        /// <see cref="WalkEnterSpeed"/> — the gap is the hysteresis band that kills Walk/Idle flicker.</summary>
        public float WalkStopSpeed;

        /// <summary>Vertical speed ABOVE which an airborne player shows Jump.</summary>
        public float RiseSpeed;

        /// <summary>Vertical speed BELOW which an airborne player shows Fall (negative).</summary>
        public float FallSpeed;

        /// <summary>Minimum seconds a grounded Walk↔Idle change must persist before it is shown.
        /// Belt-and-suspenders on top of the speed hysteresis for very brief velocity blips.</summary>
        public float MinGroundedDwellSeconds;

        public static LocomotionTuning Default => new LocomotionTuning
        {
            WalkEnterSpeed = 0.15f,
            WalkStopSpeed = 0.05f,
            RiseSpeed = 0.10f,
            FallSpeed = -0.10f,
            MinGroundedDwellSeconds = 0.06f
        };
    }

    /// <summary>
    /// Derives the LOCOMOTION pose (Idle / Walk / Jump / Fall) from observed motion, on every
    /// client, once per rendered frame. Pure and Fusion-free: the caller feeds in the render-space
    /// velocity (position delta / dt) and a replicated grounded flag, and the resolver returns a
    /// stable pose.
    ///
    /// Why this exists (P0): locomotion used to be a low-send-rate networked enum layered on top of
    /// the smoothly interpolated proxy position, so a remote player's legs snapped and lagged
    /// relative to the glide the viewer actually saw. Deriving it here from the SAME rendered motion
    /// keeps them in lockstep at render frame-rate.
    ///
    /// Why the hysteresis + dwell (P1): a single velocity threshold buzzes between Walk and Idle when
    /// speed hovers near it. Asymmetric enter/stop thresholds plus a minimum grounded dwell remove
    /// that flicker. Airborne poses and any air↔ground change commit immediately for responsiveness;
    /// only the grounded Walk↔Idle flip is dwell-gated.
    ///
    /// Mutable struct holding a little smoothing state — hold ONE per PlayerAnimator instance and
    /// call <see cref="Step"/> from Render. State-mutating, so keep it in a field, not a local copy.
    /// </summary>
    public struct LocomotionResolver
    {
        private AnimState _current;
        private AnimState _pending;
        private float _pendingSeconds;
        private bool _initialised;

        /// <summary>The pose currently being shown (last committed).</summary>
        public AnimState Current => _current;

        /// <summary>
        /// Advance one rendered frame and return the pose to show.
        /// </summary>
        /// <param name="grounded">Replicated grounded flag (authoritative; can't be inferred from smoothed motion).</param>
        /// <param name="velocityX">Render-space horizontal speed (world units / second).</param>
        /// <param name="velocityY">Render-space vertical speed (world units / second).</param>
        /// <param name="deltaSeconds">Seconds since the previous Step (render dt).</param>
        /// <param name="t">Tuning thresholds.</param>
        public AnimState Step(bool grounded, float velocityX, float velocityY, float deltaSeconds, in LocomotionTuning t)
        {
            if (!_initialised)
            {
                _current = grounded ? AnimState.Idle : AnimState.Fall;
                _pending = _current;
                _pendingSeconds = 0f;
                _initialised = true;
                return _current;
            }

            AnimState candidate = Resolve(_current, grounded, velocityX, velocityY, in t);

            if (candidate == _current)
            {
                _pending = _current;
                _pendingSeconds = 0f;
                return _current;
            }

            // Only the grounded Walk↔Idle flip is dwell-gated (that's the flicker source). Airborne
            // poses and any air↔ground transition commit immediately so jumps/landings feel snappy.
            bool groundedWalkIdleFlip =
                grounded &&
                (candidate == AnimState.Walk || candidate == AnimState.Idle) &&
                (_current == AnimState.Walk || _current == AnimState.Idle);

            if (!groundedWalkIdleFlip)
            {
                _current = candidate;
                _pending = candidate;
                _pendingSeconds = 0f;
                return _current;
            }

            if (candidate != _pending)
            {
                _pending = candidate;
                _pendingSeconds = 0f;
            }
            _pendingSeconds += deltaSeconds;
            if (_pendingSeconds >= t.MinGroundedDwellSeconds)
            {
                _current = candidate;
                _pendingSeconds = 0f;
            }
            return _current;
        }

        /// <summary>
        /// Pure hysteresis resolution with no dwell/history beyond the previous pose. Exposed static
        /// for unit testing; <see cref="Step"/> layers the dwell gate on top.
        /// </summary>
        public static AnimState Resolve(AnimState previous, bool grounded, float velocityX, float velocityY, in LocomotionTuning t)
        {
            if (!grounded)
            {
                if (velocityY > t.RiseSpeed) return AnimState.Jump;
                if (velocityY < t.FallSpeed) return AnimState.Fall;
                // Near-zero vertical speed mid-air (jump apex): hold the current airborne pose,
                // defaulting to Fall, so we never resolve to a grounded pose while airborne.
                return previous == AnimState.Jump ? AnimState.Jump : AnimState.Fall;
            }

            float speed = velocityX < 0f ? -velocityX : velocityX; // abs without a UnityEngine dep
            if (previous == AnimState.Walk)
                return speed < t.WalkStopSpeed ? AnimState.Idle : AnimState.Walk;
            return speed > t.WalkEnterSpeed ? AnimState.Walk : AnimState.Idle;
        }
    }
}
