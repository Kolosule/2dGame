namespace Game.Settings.Core
{
    public enum SettingsMigrationAction
    {
        /// <summary>The stored data matches the current version; read it as-is.</summary>
        None,

        /// <summary>Delete every settings key and re-write defaults.</summary>
        ResetToDefaults,
    }

    /// <summary>
    /// Decides what to do with a stored settings version. There are no migrations yet (version 1 is
    /// the first), so anything that is not an exact match re-defaults — including 0, which is what
    /// PlayerPrefs returns for a store written before settings existed.
    ///
    /// When a real migration is added later, this is the one place that branches on version.
    /// </summary>
    public static class SettingsMigration
    {
        public static SettingsMigrationAction Resolve(int storedVersion, int currentVersion)
        {
            return storedVersion == currentVersion
                ? SettingsMigrationAction.None
                : SettingsMigrationAction.ResetToDefaults;
        }
    }
}
