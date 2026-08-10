namespace Game.Audio.Core
{
    /// <summary>
    /// Every sound the game can play, named by the EVENT rather than by the asset. Gameplay code
    /// references these; it never holds an AudioClip. Values are explicit so a reordering of this
    /// file cannot silently remap an authored SoundBank entry.
    ///
    /// Adding a value here without adding a matching SoundBank entry FAILS the bank-completeness
    /// EditMode test (see Assets/Tests/EditMode/Audio/SoundBankIntegrityTests.cs) -- that is the
    /// point: this project's dominant failure mode is an unassigned reference that fails silently.
    /// </summary>
    public enum AudioCueId
    {
        None = 0,

        // --- Combat (Combat bus) ---
        MeleeSwing = 100,
        MeleeSwingHeavy = 101,
        HitConfirm = 102,
        HitConfirmHeavy = 103,
        TookDamage = 104,
        ProjectileFire = 105,
        ProjectileImpact = 106,
        PlayerDeath = 107,
        PlayerRespawn = 108,

        // --- Movement (World bus) ---
        Jump = 200,
        Land = 201,
        LandHeavy = 202,
        Dash = 203,
        WallOrLedgeScuff = 204,

        // --- Coins and economy ---
        CoinPickupWorld = 300,
        CoinPickupSelf = 301,
        DepositWorld = 302,
        DepositSelf = 303,
        ScoreTick = 304,

        // --- Flags ---
        FlagTaken = 400,
        FlagDropped = 401,
        FlagReturned = 402,
        FlagPickupSelf = 403,
        AlertOwnFlagTaken = 404,
        // 405 (FlagCaptured) intentionally unused — a capture already fires MatchEnd via
        // MatchPhase.PostMatch on every peer. See Global Constraints.

        // --- Buffs ---
        BuffTierUp = 500,
        TeamBuffUnlocked = 501,
        StealthEnter = 502,
        StealthExit = 503,

        // --- Enemies (Enemy bus) ---
        EnemyTelegraph = 600,
        EnemyAttack = 601,
        EnemyHurt = 602,
        EnemyDeath = 603,
        EnemySpawn = 604,

        // --- Match and stingers ---
        CountdownTick = 700,
        CountdownGo = 701,
        MatchStart = 702,
        SuddenDeathAlert = 703,
        MatchEnd = 704,
        VictoryStinger = 705,
        DefeatStinger = 706,
        DrawStinger = 707,

        // --- UI (Ui bus, always flat, always local) ---
        UiHover = 800,
        UiClick = 801,
        UiBack = 802,
        UiToggle = 803,
        UiSliderTick = 804,
        PanelOpen = 805,
        PanelClose = 806,
        ToastNotification = 807,
        // 808/809 (KillfeedEntry, KillConfirmSelf) intentionally unused — there is no per-kill
        // broadcast to clients, and adding one would need a new RPC. See Global Constraints.
    }
}
