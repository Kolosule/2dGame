using System.Collections.Generic;
using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// The AudioCueId -> SoundCue lookup. Built once into a dictionary rather than searched linearly:
/// PlayAt runs on every replicated combat event, and a linear scan of 52 entries per hit at 20
/// players is real cost for no reason.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [SerializeField] private SoundCue[] cues = new SoundCue[0];

    private Dictionary<AudioCueId, SoundCue> index;

    public IReadOnlyList<SoundCue> Cues => cues;

    public bool TryGet(AudioCueId id, out SoundCue cue)
    {
        if (index == null) RebuildIndex();
        return index.TryGetValue(id, out cue);
    }

    /// <summary>Rebuilds the lookup. Called lazily on first use, and by the integrity tests after
    /// they mutate a bank in memory. A duplicate id keeps the FIRST entry and warns -- silently
    /// picking the last one would make an accidental duplicate impossible to notice.</summary>
    public void RebuildIndex()
    {
        index = new Dictionary<AudioCueId, SoundCue>(cues.Length);
        foreach (SoundCue cue in cues)
        {
            if (cue == null || cue.id == AudioCueId.None) continue;
            if (index.ContainsKey(cue.id))
            {
                Debug.LogWarning($"[Audio] SoundBank '{name}' has duplicate entries for {cue.id}; keeping the first.");
                continue;
            }
            index[cue.id] = cue;
        }
    }

    private void OnValidate() => index = null;

#if UNITY_EDITOR
    /// <summary>Editor/test-only setter so integrity tests can build a bank in memory without an
    /// authored asset. Not available in a player build.</summary>
    public void SetCuesForTests(SoundCue[] value)
    {
        cues = value ?? new SoundCue[0];
        RebuildIndex();
    }
#endif
}
