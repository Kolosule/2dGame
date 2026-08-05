using UnityEngine;

/// <summary>
/// Host/match configuration holder: the shared config assets every gameplay system reads, plus the
/// two server-authoritative match rules MatchManager consumes.
///
/// This is NOT where client preferences live. Volume, resolution, camera shake and damage numbers
/// are client-local and belong to SettingsStore / the options menu — they are per-player, must not
/// affect simulation, and are needed in MainMenu.unity, where this Gameplay-scene singleton does
/// not exist. See docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// The fields below are authored by whoever runs the host or dedicated server. A client must never
/// be able to write them: they decide when the match ends for everyone.
/// </summary>
public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance { get; private set; }

    [Header("Combat Configuration")]
    [SerializeField] private CombatConfig combatConfig;

    [Header("Enemy Difficulty")]
    [Tooltip("Concentric difficulty rings applied to enemies by distance from center.")]
    [SerializeField] private DifficultyRingConfig difficultyRingConfig;

    [Header("Match Settings")]
    [Tooltip("Match time limit in minutes (0 = no limit)")]
    public float matchTimeLimit = 0f;

    [Tooltip("Sudden Death hard cap in minutes (0 = off). Operations safety valve only: on " +
             "expiry the match resolves as a draw so a headless server cannot wedge on an " +
             "unwinnable match. Leave at 0 — draws are unreachable in default play.")]
    public float suddenDeathHardCap = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Get the combat configuration
    /// </summary>
    public CombatConfig GetCombatConfig()
    {
        return combatConfig;
    }

    /// <summary>
    /// Get the shared enemy difficulty ring configuration (may be null if unassigned).
    /// </summary>
    public DifficultyRingConfig GetDifficultyRingConfig()
    {
        return difficultyRingConfig;
    }
}
