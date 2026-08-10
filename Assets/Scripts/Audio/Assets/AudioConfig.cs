using System;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The single asset the audio system loads at boot, from Resources. This is the whole reason the
/// audio system needs no scene wiring: there is one asset, found by name, and nothing to forget to
/// drag into an inspector slot in each scene.
/// </summary>
[CreateAssetMenu(fileName = "AudioConfig", menuName = "Audio/Audio Config")]
public class AudioConfig : ScriptableObject
{
    [Serializable]
    public struct MusicEntry
    {
        public MusicTrackId id;
        public AudioClip clip;
    }

    [Header("Assets")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private SoundBank bank;
    [SerializeField] private MusicEntry[] musicTracks = new MusicEntry[0];

    [Header("Voice pools")]
    [Tooltip("Concurrent world/combat voices. Hard cap regardless of player count.")]
    [SerializeField] private int sfxVoices = 32;

    [Tooltip("Separate pool so a menu click is never starved by a combat scrum.")]
    [SerializeField] private int uiVoices = 4;

    [Header("2D spatialization")]
    [Tooltip("Distance at which a positional cue is silent and culled. Roughly 1.3x the camera " +
             "half-width, so off-screen fights are inaudible without a separate range check.")]
    [SerializeField] private float defaultWorldMaxDistance = 14f;

    [Tooltip("Hard pan limit. Full ±1 panning is fatiguing and can make a cue inaudible on one ear.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxPan = 0.7f;

    [Header("Music")]
    [SerializeField] private float musicCrossfadeSeconds = 1.5f;

    public AudioMixer Mixer => mixer;
    public SoundBank Bank => bank;
    public int SfxVoices => Mathf.Max(1, sfxVoices);
    public int UiVoices => Mathf.Max(1, uiVoices);
    public float DefaultWorldMaxDistance => Mathf.Max(0.01f, defaultWorldMaxDistance);
    public float MaxPan => maxPan;
    public float MusicCrossfadeSeconds => Mathf.Max(0f, musicCrossfadeSeconds);

    public AudioClip GetMusicClip(MusicTrackId id)
    {
        if (id == MusicTrackId.None || musicTracks == null) return null;
        foreach (MusicEntry entry in musicTracks)
            if (entry.id == id) return entry.clip;
        return null;
    }
}
