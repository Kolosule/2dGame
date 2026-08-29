using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// The only audio surface gameplay code touches. Deliberately narrow and void-returning: a caller
/// cannot obtain, hold, or leak an AudioSource, which is what makes the voice budget enforceable
/// rather than advisory.
///
/// Every method is safe to call unconditionally — before boot, in the menu, and on the dedicated
/// server, where no AudioManager exists at all. Do NOT wrap these in null or platform checks at
/// the call site.
/// </summary>
public static class Audio
{
    /// <summary>World event at a position: attenuated and panned by distance from the camera, and
    /// culled entirely when off-screen. Use for anything another player caused.</summary>
    public static void PlayAt(AudioCueId id, Vector3 worldPosition)
    {
        if (DedicatedServerPresentation.IsHeadless) return;
#if !UNITY_SERVER
        AudioManager.Instance?.PlayAt(id, worldPosition);
#endif
    }

    /// <summary>Flat, full-volume, centred. Use for the local player's OWN actions — they should
    /// feel immediate and should not fade as the camera drifts.</summary>
    public static void Play2D(AudioCueId id)
    {
        if (DedicatedServerPresentation.IsHeadless) return;
#if !UNITY_SERVER
        AudioManager.Instance?.Play2D(id);
#endif
    }

    /// <summary>Flat, on the UI bus, from a pool combat can never starve. Always local-only.</summary>
    public static void PlayUi(AudioCueId id)
    {
        if (DedicatedServerPresentation.IsHeadless) return;
#if !UNITY_SERVER
        AudioManager.Instance?.PlayUi(id);
#endif
    }
}
