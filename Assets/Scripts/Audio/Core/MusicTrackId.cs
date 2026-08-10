namespace Game.Audio.Core
{
    /// <summary>Looping beds owned by MusicDirector. One-shot stingers are AudioCueId values on
    /// the Music bus, not entries here -- they are fired and forgotten, never crossfaded.</summary>
    public enum MusicTrackId : byte
    {
        None = 0,
        MenuLoop = 1,
        LobbyLoop = 2,
        GameplayLoop = 3,
        SuddenDeathLoop = 4,
        ArenaAmbientBed = 5,
    }

    /// <summary>Mixer snapshots. None of these may animate MasterVolume/MusicVolume/SfxVolume/
    /// UiVolume -- those are the player's, and a snapshot transition would stomp them.</summary>
    public enum MixerSnapshotId : byte
    {
        Default = 0,
        Menu = 1,
        SuddenDeath = 2,
        Stinger = 3,
    }
}
