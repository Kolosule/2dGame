using Game.Settings.Core;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

/// <summary>
/// Pushes stored client-local settings into the engine. Runs once before the first scene loads, so
/// the first rendered frame is already at the chosen resolution / vsync / cap — no visible flash of
/// the wrong mode, and no dependency on any scene object's Awake order.
///
/// Every entry point is a no-op on a build with no graphics device (the dedicated server), which is
/// what makes this whole subsystem client-only by construction rather than by convention.
///
/// Nothing here is networked. See docs/superpowers/specs/2026-07-29-options-settings-design.md.
/// </summary>
public static class SettingsService
{
    /// <summary>
    /// Assigned by the audio system when it ships; null until then, which is why the four volume
    /// sliders currently persist but are inaudible. Not a bug — see the spec's audio section.
    /// </summary>
    public static AudioMixer Mixer { get; set; }

    // The exposed-parameter names the future AudioMixer must publish. Fixed by the spec so both
    // sides agree without either having shipped yet.
    public const string MasterVolumeParam = "MasterVolume";
    public const string MusicVolumeParam = "MusicVolume";
    public const string SfxVolumeParam = "SfxVolume";
    public const string UiVolumeParam = "UiVolume";

    public static bool HasDisplay =>
        !DedicatedServerPresentation.IsHeadless
        && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyAtBoot()
    {
        ApplyAll();
    }

    public static void ApplyAll()
    {
        if (!HasDisplay) return;

        SettingsStore.EnsureLoaded();
        ApplyVideo();
        ApplyAudio();
    }

    public static void ApplyVideo()
    {
        if (!HasDisplay) return;

        SettingsStore.EnsureLoaded();
        Screen.SetResolution(
            SettingsStore.ResolutionWidth,
            SettingsStore.ResolutionHeight,
            (FullScreenMode)SettingsStore.DisplayMode);

        ApplyPresentation();
    }

    /// <summary>
    /// VSync + FPS cap only — deliberately never touches <see cref="Screen.SetResolution"/>. Callers
    /// that change only these two values (the Video tab's VSync toggle and FPS cap dropdown) must use
    /// this instead of <see cref="ApplyVideo"/>, which re-applies resolution/display mode from the
    /// STORED values and would silently stomp an in-flight, unconfirmed display preview from
    /// VideoSettingsSection's confirm prompt.
    /// </summary>
    public static void ApplyPresentation()
    {
        if (!HasDisplay) return;

        SettingsStore.EnsureLoaded();
        QualitySettings.vSyncCount = SettingsStore.VSync;

        // Unity's "uncapped" sentinel is -1, not 0.
        Application.targetFrameRate = SettingsStore.FpsCap <= 0 ? -1 : SettingsStore.FpsCap;
    }

    /// <summary>
    /// Applies a resolution/mode WITHOUT persisting it. The video settings UI uses this so a change
    /// that leaves the window unusable is never written to disk before the player confirms it.
    /// </summary>
    public static void ApplyDisplayPreview(int width, int height, int displayMode)
    {
        if (!HasDisplay) return;
        if (width <= 0 || height <= 0) return;

        Screen.SetResolution(width, height, (FullScreenMode)SettingsCatalog.ClampDisplayMode(displayMode));
    }

    public static void ApplyAudio()
    {
        if (!HasDisplay) return;
        if (Mixer == null) return;

        SettingsStore.EnsureLoaded();
        Mixer.SetFloat(MasterVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.MasterVolume));
        Mixer.SetFloat(MusicVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.MusicVolume));
        Mixer.SetFloat(SfxVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.SfxVolume));
        Mixer.SetFloat(UiVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.UiVolume));
    }
}
