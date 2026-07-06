namespace Game.PlayerAnimation.Core
{
    /// <summary>
    /// One value per Animator state. The integer values 0..9 are a serialized CONTRACT: they are
    /// baked into Player.controller / Weapon.controller as "State Equals n" transition conditions.
    /// Add new real states at the END so existing ints never shift.
    ///
    /// <see cref="None"/> (255) is NOT an Animator state — it is a sentinel meaning "no
    /// authoritative override is active, derive the locomotion pose locally from rendered motion".
    /// It is never written to the Animator.
    ///
    /// Lives in its own Fusion-free assembly (Game.PlayerAnimation.Core) so the locomotion
    /// resolver that consumes it stays unit-testable in EditMode.
    /// </summary>
    public enum AnimState : byte
    {
        Idle = 0,
        Walk = 1,
        Jump = 2,
        Fall = 3,
        Dash = 4,
        Attack = 5,
        GroundPound = 6, // placeholder clip
        Shoot = 7,       // placeholder clip
        Stunned = 8,
        Dead = 9,

        /// <summary>Sentinel: no networked override — locomotion is derived locally. Never applied.</summary>
        None = 255
    }
}
