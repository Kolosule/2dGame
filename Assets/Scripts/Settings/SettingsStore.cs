using System;
using Game.Settings.Core;
using UnityEngine;

/// <summary>
/// The one place raw PlayerPrefs is touched for client-local settings. Values are read once into
/// memory at first access, so a consumer reading a property every frame costs a field read rather
/// than a registry hit; writes update the cache, write through, and raise Changed.
///
/// CLIENT-LOCAL ONLY. Nothing here is networked, replicated, or read by simulation code — see the
/// networking-safety section of docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// PlayerPrefs is per-PRODUCT, not per-process: multiple editor peers (MPPM) on one machine share
/// one settings store, exactly as they share one identity token (see PlayerIdentity.cs). Harmless
/// for a volume slider in a way it is not for identity.
/// </summary>
public static class SettingsStore
{
    /// <summary>Raised after any write. The settings UI uses it; gameplay consumers just read.</summary>
    public static event Action Changed;

    private static bool loaded;

    private static float masterVolume;
    private static float musicVolume;
    private static float sfxVolume;
    private static float uiVolume;

    private static int resolutionWidth;
    private static int resolutionHeight;
    private static int displayMode;
    private static int vsync;
    private static int fpsCap;

    private static float cameraShake;
    private static int damageNumbers;

    public static float MasterVolume
    {
        get { EnsureLoaded(); return masterVolume; }
        set { EnsureLoaded(); masterVolume = WriteFloat(SettingsCatalog.MasterVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float MusicVolume
    {
        get { EnsureLoaded(); return musicVolume; }
        set { EnsureLoaded(); musicVolume = WriteFloat(SettingsCatalog.MusicVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float SfxVolume
    {
        get { EnsureLoaded(); return sfxVolume; }
        set { EnsureLoaded(); sfxVolume = WriteFloat(SettingsCatalog.SfxVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float UiVolume
    {
        get { EnsureLoaded(); return uiVolume; }
        set { EnsureLoaded(); uiVolume = WriteFloat(SettingsCatalog.UiVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static int ResolutionWidth { get { EnsureLoaded(); return resolutionWidth; } }

    public static int ResolutionHeight { get { EnsureLoaded(); return resolutionHeight; } }

    /// <summary>Width and height move together — a half-applied resolution is never valid.</summary>
    public static void SetResolution(int width, int height)
    {
        EnsureLoaded();
        if (!SetResolutionNoNotify(width, height)) return;
        PlayerPrefs.Save();
        RaiseChanged();
    }

    /// <summary>
    /// Core of <see cref="SetResolution"/> without the save/notify, so batch writers
    /// (the Reset*ToDefaults methods) can fold a resolution change into a single flush. Keeps the
    /// same width/height-move-together guard; returns false (no-op) if it rejects the values.
    /// </summary>
    private static bool SetResolutionNoNotify(int width, int height)
    {
        if (width <= 0 || height <= 0) return false;

        resolutionWidth = width;
        resolutionHeight = height;
        PlayerPrefs.SetInt(SettingsCatalog.WidthKey, width);
        PlayerPrefs.SetInt(SettingsCatalog.HeightKey, height);
        return true;
    }

    public static int DisplayMode
    {
        get { EnsureLoaded(); return displayMode; }
        set { EnsureLoaded(); displayMode = WriteInt(SettingsCatalog.DisplayModeKey, SettingsCatalog.ClampDisplayMode(value)); }
    }

    public static int VSync
    {
        get { EnsureLoaded(); return vsync; }
        set { EnsureLoaded(); vsync = WriteInt(SettingsCatalog.VSyncKey, SettingsCatalog.ClampVSync(value)); }
    }

    public static int FpsCap
    {
        get { EnsureLoaded(); return fpsCap; }
        set { EnsureLoaded(); fpsCap = WriteInt(SettingsCatalog.FpsCapKey, SettingsCatalog.ClampFpsCap(value)); }
    }

    public static float CameraShakeIntensity
    {
        get { EnsureLoaded(); return cameraShake; }
        set { EnsureLoaded(); cameraShake = WriteFloat(SettingsCatalog.CameraShakeKey, SettingsCatalog.ClampCameraShake(value)); }
    }

    public static bool ShowDamageNumbers
    {
        get { EnsureLoaded(); return damageNumbers != 0; }
        set { EnsureLoaded(); damageNumbers = WriteInt(SettingsCatalog.DamageNumbersKey, value ? 1 : 0); }
    }

    /// <summary>
    /// The display's native size, with a fallback for a build that has no display at all (a
    /// headless server reports 0x0). SettingsService never applies video there, but the store must
    /// still return something sane if anything reads it.
    /// </summary>
    public static void NativeResolution(out int width, out int height)
    {
        Resolution current = Screen.currentResolution;
        width = current.width > 0 ? current.width : 1920;
        height = current.height > 0 ? current.height : 1080;
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        int storedVersion = PlayerPrefs.GetInt(SettingsCatalog.VersionKey, 0);
        if (SettingsMigration.Resolve(storedVersion, SettingsCatalog.CurrentVersion)
            == SettingsMigrationAction.ResetToDefaults)
        {
            DeleteAllSettingsKeys();
        }

        masterVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.MasterVolumeKey, SettingsCatalog.MasterVolumeDefault));
        musicVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.MusicVolumeKey, SettingsCatalog.MusicVolumeDefault));
        sfxVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.SfxVolumeKey, SettingsCatalog.SfxVolumeDefault));
        uiVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.UiVolumeKey, SettingsCatalog.UiVolumeDefault));

        NativeResolution(out int nativeWidth, out int nativeHeight);
        resolutionWidth = PlayerPrefs.GetInt(SettingsCatalog.WidthKey, nativeWidth);
        resolutionHeight = PlayerPrefs.GetInt(SettingsCatalog.HeightKey, nativeHeight);
        if (resolutionWidth <= 0 || resolutionHeight <= 0)
        {
            resolutionWidth = nativeWidth;
            resolutionHeight = nativeHeight;
        }

        displayMode = SettingsCatalog.ClampDisplayMode(PlayerPrefs.GetInt(SettingsCatalog.DisplayModeKey, SettingsCatalog.DisplayModeDefault));
        vsync = SettingsCatalog.ClampVSync(PlayerPrefs.GetInt(SettingsCatalog.VSyncKey, SettingsCatalog.VSyncDefault));
        fpsCap = SettingsCatalog.ClampFpsCap(PlayerPrefs.GetInt(SettingsCatalog.FpsCapKey, SettingsCatalog.FpsCapDefault));

        cameraShake = SettingsCatalog.ClampCameraShake(PlayerPrefs.GetFloat(SettingsCatalog.CameraShakeKey, SettingsCatalog.CameraShakeDefault));
        damageNumbers = SettingsCatalog.ClampFlag(PlayerPrefs.GetInt(SettingsCatalog.DamageNumbersKey, SettingsCatalog.DamageNumbersDefault));

        PlayerPrefs.SetInt(SettingsCatalog.VersionKey, SettingsCatalog.CurrentVersion);
        PlayerPrefs.Save();
    }

    /// <summary>Resets all audio fields and flushes/notifies exactly once for the whole batch.</summary>
    public static void ResetAudioToDefaults()
    {
        EnsureLoaded();
        masterVolume = WriteFloatNoNotify(SettingsCatalog.MasterVolumeKey, SettingsCatalog.MasterVolumeDefault);
        musicVolume = WriteFloatNoNotify(SettingsCatalog.MusicVolumeKey, SettingsCatalog.MusicVolumeDefault);
        sfxVolume = WriteFloatNoNotify(SettingsCatalog.SfxVolumeKey, SettingsCatalog.SfxVolumeDefault);
        uiVolume = WriteFloatNoNotify(SettingsCatalog.UiVolumeKey, SettingsCatalog.UiVolumeDefault);
        PlayerPrefs.Save();
        RaiseChanged();
    }

    /// <summary>
    /// Resets all video fields, including resolution, and flushes/notifies exactly once for the
    /// whole batch (the resolution write goes through the no-notify core, not the public setter).
    /// </summary>
    public static void ResetVideoToDefaults()
    {
        EnsureLoaded();
        NativeResolution(out int nativeWidth, out int nativeHeight);
        SetResolutionNoNotify(nativeWidth, nativeHeight);
        displayMode = WriteIntNoNotify(SettingsCatalog.DisplayModeKey, SettingsCatalog.DisplayModeDefault);
        vsync = WriteIntNoNotify(SettingsCatalog.VSyncKey, SettingsCatalog.VSyncDefault);
        fpsCap = WriteIntNoNotify(SettingsCatalog.FpsCapKey, SettingsCatalog.FpsCapDefault);
        PlayerPrefs.Save();
        RaiseChanged();
    }

    /// <summary>Resets all gameplay fields and flushes/notifies exactly once for the whole batch.</summary>
    public static void ResetGameplayToDefaults()
    {
        EnsureLoaded();
        cameraShake = WriteFloatNoNotify(SettingsCatalog.CameraShakeKey, SettingsCatalog.CameraShakeDefault);
        damageNumbers = WriteIntNoNotify(SettingsCatalog.DamageNumbersKey, SettingsCatalog.DamageNumbersDefault);
        PlayerPrefs.Save();
        RaiseChanged();
    }

    /// <summary>
    /// Deletes ONLY the keys this feature owns. Never PlayerPrefs.DeleteAll() — lobby.nickname and
    /// the reconnection identity token share this store and must survive a settings reset.
    /// </summary>
    private static void DeleteAllSettingsKeys()
    {
        foreach (string key in SettingsCatalog.AllKeys)
        {
            PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
    }

    private static float WriteFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
        RaiseChanged();
        return value;
    }

    private static int WriteInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
        RaiseChanged();
        return value;
    }

    /// <summary>
    /// Same field-and-PlayerPrefs write as <see cref="WriteFloat"/> but without the save/notify, for
    /// batch writers (the Reset*ToDefaults methods) that flush and raise once for several fields.
    /// </summary>
    private static float WriteFloatNoNotify(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        return value;
    }

    /// <summary>Int counterpart of <see cref="WriteFloatNoNotify"/>.</summary>
    private static int WriteIntNoNotify(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        return value;
    }

    private static void RaiseChanged()
    {
        Action handler = Changed;
        if (handler != null) handler();
    }
}
