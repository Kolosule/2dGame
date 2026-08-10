using System;
using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// One authored sound: what clips it can use, where it routes, and the three numbers that keep it
/// from flooding a 20-player match. Everything that makes a cue behave the way it does is data, so
/// retuning the mix is an asset edit, never a code change.
/// </summary>
[Serializable]
public class SoundCue
{
    [Tooltip("The event this cue answers. Must be unique within a SoundBank.")]
    public AudioCueId id = AudioCueId.None;

    [Tooltip("One or more clips. Picked round-robin so repeated plays don't comb-filter into a " +
             "buzz -- with a single clip, rapid repeats phase against each other.")]
    public AudioClip[] variants = Array.Empty<AudioClip>();

    [Tooltip("Mixer destination. Combat/World/Enemy/Ambient are child groups of SFX.")]
    public AudioBus bus = AudioBus.World;

    [Tooltip("If true, the cue is attenuated and panned by its distance from the camera, and is " +
             "dropped outright beyond maxDistance. UI and own-action cues should be false.")]
    public bool positional = true;

    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Random pitch range per play. Small values (±0.08) are enough to break up repeats.")]
    public Vector2 pitchRange = new Vector2(0.92f, 1.08f);

    [Range(0, 100)]
    [Tooltip("Voice-stealing rank. 0 is stolen first (EnemySpawn, ambience); 100 is never stolen " +
             "(EnemyTelegraph, match stingers -- the cues whose absence is a gameplay bug).")]
    public int priority = 50;

    [Tooltip("Seconds during which a repeat of this SAME cue is suppressed, regardless of who " +
             "triggered it. 0 disables. This is what collapses a 20-player scrum into one impact.")]
    public float dedupeWindow;

    [Tooltip("Maximum simultaneous voices for this cue. 0 = unlimited (still bounded by the pool).")]
    public int maxConcurrent;

    [Tooltip("Distance at which this cue is fully silent and is culled before acquiring a voice. " +
             "0 = use AudioConfig.DefaultWorldMaxDistance. Ignored when positional is false.")]
    public float maxDistance;

    public bool HasClip => variants != null && variants.Length > 0 && variants[0] != null;
}
