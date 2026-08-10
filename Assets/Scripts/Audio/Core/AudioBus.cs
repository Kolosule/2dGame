namespace Game.Audio.Core
{
    /// <summary>
    /// Mixer destination for a cue. Maps 1:1 onto AudioMixerGroup names in the mixer asset.
    /// Combat/World/Enemy/Ambient are CHILD groups of SFX -- they exist for mix balance and as
    /// snapshot ducking targets, and are never exposed to players. Only Master/Music/SFX/UI carry
    /// exposed parameters, and those names are fixed by SettingsService's shipped contract.
    /// </summary>
    public enum AudioBus : byte
    {
        Combat = 0,
        World = 1,
        Enemy = 2,
        Ambient = 3,
        Ui = 4,
        Music = 5,
        MusicBed = 6,
    }
}
