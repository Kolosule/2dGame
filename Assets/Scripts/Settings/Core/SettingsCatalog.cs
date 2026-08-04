namespace Game.Settings.Core
{
    /// <summary>
    /// The single source of truth for every client-local setting: its PlayerPrefs key, its default,
    /// and its valid range. SettingsStore, the reset paths and the UI all read from here, so a
    /// default can never drift between "what boot applies" and "what Reset to Defaults writes".
    ///
    /// Engine-free on purpose (Game.Settings.Core has noEngineReferences) — no Mathf, no Screen.
    /// See docs/superpowers/specs/2026-07-29-options-settings-design.md.
    /// </summary>
    public static class SettingsCatalog
    {
        /// <summary>Bumped whenever a stored value's meaning changes. See SettingsMigration.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Every settings key starts with this. The reset path deletes only keys under this prefix,
        /// so it can never touch lobby.nickname or the reconnection identity token, which live in
        /// the same per-product PlayerPrefs store.
        /// </summary>
        public const string KeyPrefix = "settings.";

        public const string VersionKey = "settings.version";

        public const string MasterVolumeKey = "settings.audio.master";
        public const string MusicVolumeKey = "settings.audio.music";
        public const string SfxVolumeKey = "settings.audio.sfx";
        public const string UiVolumeKey = "settings.audio.ui";

        public const string WidthKey = "settings.video.width";
        public const string HeightKey = "settings.video.height";
        public const string DisplayModeKey = "settings.video.displayMode";
        public const string VSyncKey = "settings.video.vsync";
        public const string FpsCapKey = "settings.video.fpsCap";

        public const string CameraShakeKey = "settings.gameplay.cameraShake";
        public const string DamageNumbersKey = "settings.gameplay.damageNumbers";

        public const float MasterVolumeDefault = 0.8f;
        public const float MusicVolumeDefault = 0.7f;
        public const float SfxVolumeDefault = 1.0f;
        public const float UiVolumeDefault = 1.0f;

        /// <summary>
        /// UnityEngine.FullScreenMode.FullScreenWindow == 1 (borderless). Written as a bare int
        /// because this assembly cannot reference UnityEngine; SettingsService casts it back.
        /// </summary>
        public const int DisplayModeDefault = 1;
        public const int DisplayModeMin = 0;
        public const int DisplayModeMax = 3;

        public const int VSyncDefault = 1;

        /// <summary>0 means uncapped (SettingsService translates it to targetFrameRate = -1).</summary>
        public const int FpsCapDefault = 0;
        public const int FpsCapFloor = 30;
        public const int FpsCapCeiling = 1000;

        public const float CameraShakeDefault = 1.0f;
        public const float CameraShakeMin = 0f;
        public const float CameraShakeMax = 2f;

        public const int DamageNumbersDefault = 1;

        /// <summary>
        /// Every key this feature owns. The reset path iterates exactly this array — a key missing
        /// from it survives a reset and silently resurrects a stale value.
        /// </summary>
        public static readonly string[] AllKeys =
        {
            VersionKey,
            MasterVolumeKey, MusicVolumeKey, SfxVolumeKey, UiVolumeKey,
            WidthKey, HeightKey, DisplayModeKey, VSyncKey, FpsCapKey,
            CameraShakeKey, DamageNumbersKey,
        };

        public static float ClampVolume(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        public static float ClampCameraShake(float value)
        {
            if (value < CameraShakeMin) return CameraShakeMin;
            return value > CameraShakeMax ? CameraShakeMax : value;
        }

        public static int ClampVSync(int value)
        {
            return value == 0 ? 0 : 1;
        }

        /// <summary>
        /// 0 (uncapped) is preserved exactly; anything else is pulled into [30, 1000]. A 3fps cap
        /// would be indistinguishable from a hang, so the floor is a usability guard, not a
        /// hardware limit.
        /// </summary>
        public static int ClampFpsCap(int value)
        {
            if (value <= 0) return 0;
            if (value < FpsCapFloor) return FpsCapFloor;
            return value > FpsCapCeiling ? FpsCapCeiling : value;
        }

        public static int ClampDisplayMode(int value)
        {
            if (value < DisplayModeMin || value > DisplayModeMax) return DisplayModeDefault;
            return value;
        }

        public static int ClampFlag(int value)
        {
            return value == 0 ? 0 : 1;
        }
    }
}
