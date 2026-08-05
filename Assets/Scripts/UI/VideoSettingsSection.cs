using System.Collections.Generic;
using Game.Settings.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Video tab. Resolution and display mode apply immediately but are only PERSISTED once the
/// player confirms — they are the only settings that can leave the window unusable, off-screen or
/// on an unsupported mode, a state the player cannot fix from inside the game and which
/// apply-on-change would otherwise have already written to disk. VSync and the framerate cap are
/// plain apply-on-change.
///
/// Purely local. Nothing here is networked.
/// </summary>
public class VideoSettingsSection : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private TMP_Dropdown fpsCapDropdown;
    [SerializeField] private Button videoResetButton;

    [Header("Confirm prompt")]
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmCountdownLabel;
    [SerializeField] private Button confirmKeepButton;
    [SerializeField] private Button confirmRevertButton;
    [Tooltip("Seconds before an unconfirmed resolution/display change reverts itself.")]
    [SerializeField] private float confirmSeconds = 10f;

    // Offered display modes. MaximizedWindow is deliberately omitted — on Windows it behaves like
    // Windowed, so listing it would present a distinction that does not exist for our players.
    private static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.FullScreenWindow,
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.Windowed,
    };

    private static readonly string[] DisplayModeLabels =
    {
        "Fullscreen (Borderless)",
        "Fullscreen (Exclusive)",
        "Windowed",
    };

    // 0 = uncapped, then the common desktop refresh rates.
    private static readonly int[] FpsCapOptions = { 0, 30, 60, 120, 144, 240 };

    private readonly List<ResolutionOption> resolutions = new List<ResolutionOption>();

    private bool suppressCallbacks;

    private bool awaitingConfirm;
    private float confirmRemaining;
    private int previousWidth, previousHeight, previousDisplayMode;

    private void Awake()
    {
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);

        if (vsyncToggle != null)
            vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);

        if (fpsCapDropdown != null)
            fpsCapDropdown.onValueChanged.AddListener(OnFpsCapChanged);

        if (videoResetButton != null)
        {
            videoResetButton.onClick.AddListener(() =>
            {
                CancelPendingConfirm();
                SettingsStore.ResetVideoToDefaults();
                SettingsService.ApplyVideo();
                RefreshFromStore();
            });
        }

        if (confirmKeepButton != null) confirmKeepButton.onClick.AddListener(KeepPendingDisplay);
        if (confirmRevertButton != null) confirmRevertButton.onClick.AddListener(CancelPendingConfirm);

        if (confirmPanel != null) confirmPanel.SetActive(false);
    }

    private void Update()
    {
        if (!awaitingConfirm) return;

        confirmRemaining -= Time.unscaledDeltaTime;

        if (confirmCountdownLabel != null)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(confirmRemaining));
            confirmCountdownLabel.text = "Keep these display settings? Reverting in " + seconds + "s";
        }

        if (confirmRemaining <= 0f) CancelPendingConfirm();
    }

    /// <summary>Repopulate every control from the store. Called whenever the panel opens.</summary>
    public void RefreshFromStore()
    {
        suppressCallbacks = true;

        BuildResolutionOptions();
        BuildDisplayModeOptions();
        BuildFpsCapOptions();

        if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(SettingsStore.VSync != 0);
        ApplyVSyncInteractability();

        suppressCallbacks = false;
    }

    /// <summary>
    /// Revert an unconfirmed display change. Safe to call when nothing is pending — the panel calls
    /// it unconditionally on close so a player cannot escape the prompt by closing the window.
    /// </summary>
    public void CancelPendingConfirm()
    {
        if (!awaitingConfirm)
        {
            if (confirmPanel != null) confirmPanel.SetActive(false);
            return;
        }

        awaitingConfirm = false;
        if (confirmPanel != null) confirmPanel.SetActive(false);

        SettingsService.ApplyDisplayPreview(previousWidth, previousHeight, previousDisplayMode);
        RefreshFromStore();
    }

    private void KeepPendingDisplay()
    {
        if (!awaitingConfirm) return;

        awaitingConfirm = false;
        if (confirmPanel != null) confirmPanel.SetActive(false);

        // Only now does the new value reach disk.
        int index = resolutionDropdown != null ? resolutionDropdown.value : -1;
        if (index >= 0 && index < resolutions.Count)
            SettingsStore.SetResolution(resolutions[index].Width, resolutions[index].Height);

        int modeIndex = displayModeDropdown != null ? displayModeDropdown.value : -1;
        if (modeIndex >= 0 && modeIndex < DisplayModes.Length)
            SettingsStore.DisplayMode = (int)DisplayModes[modeIndex];
    }

    private void OnResolutionChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= resolutions.Count) return;

        BeginConfirm(resolutions[index].Width, resolutions[index].Height, CurrentDropdownDisplayMode());
    }

    private void OnDisplayModeChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= DisplayModes.Length) return;

        int resolutionIndex = resolutionDropdown != null ? resolutionDropdown.value : -1;
        int width = resolutionIndex >= 0 && resolutionIndex < resolutions.Count
            ? resolutions[resolutionIndex].Width
            : SettingsStore.ResolutionWidth;
        int height = resolutionIndex >= 0 && resolutionIndex < resolutions.Count
            ? resolutions[resolutionIndex].Height
            : SettingsStore.ResolutionHeight;

        BeginConfirm(width, height, (int)DisplayModes[index]);
    }

    /// <summary>
    /// Apply the new display state without persisting it, and start the revert countdown. If a
    /// confirm was already pending, the ORIGINAL previous value is kept as the revert target, so
    /// two changes in a row still fall back to a known-good state rather than to the intermediate
    /// one the player may never have been able to see.
    /// </summary>
    private void BeginConfirm(int width, int height, int displayMode)
    {
        if (!awaitingConfirm)
        {
            previousWidth = SettingsStore.ResolutionWidth;
            previousHeight = SettingsStore.ResolutionHeight;
            previousDisplayMode = SettingsStore.DisplayMode;
        }

        awaitingConfirm = true;
        confirmRemaining = confirmSeconds;

        SettingsService.ApplyDisplayPreview(width, height, displayMode);

        if (confirmPanel != null) confirmPanel.SetActive(true);
    }

    private void OnVSyncChanged(bool on)
    {
        if (suppressCallbacks) return;

        SettingsStore.VSync = on ? 1 : 0;
        SettingsService.ApplyVideo();
        ApplyVSyncInteractability();
    }

    private void OnFpsCapChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= FpsCapOptions.Length) return;

        SettingsStore.FpsCap = FpsCapOptions[index];
        SettingsService.ApplyVideo();
    }

    /// <summary>
    /// With vSyncCount > 0 Unity ignores targetFrameRate on desktop, so the cap control is disabled
    /// rather than left presenting a setting that silently does nothing. The stored value is
    /// untouched and comes back when VSync is turned off.
    /// </summary>
    private void ApplyVSyncInteractability()
    {
        if (fpsCapDropdown != null) fpsCapDropdown.interactable = SettingsStore.VSync == 0;
    }

    private void BuildResolutionOptions()
    {
        if (resolutionDropdown == null) return;

        var raw = new List<ResolutionOption>();
        Resolution[] available = Screen.resolutions;
        for (int i = 0; i < available.Length; i++)
            raw.Add(new ResolutionOption(available[i].width, available[i].height));

        resolutions.Clear();
        resolutions.AddRange(ResolutionList.Deduplicate(raw));

        // A platform that enumerates nothing must still offer the current size.
        if (resolutions.Count == 0)
            resolutions.Add(new ResolutionOption(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight));

        var labels = new List<string>(resolutions.Count);
        for (int i = 0; i < resolutions.Count; i++) labels.Add(resolutions[i].ToString());

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);

        SettingsStore.NativeResolution(out int nativeWidth, out int nativeHeight);
        int selected = ResolutionList.ResolveStoredIndex(
            resolutions,
            SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight,
            nativeWidth, nativeHeight);

        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
        resolutionDropdown.RefreshShownValue();
    }

    private void BuildDisplayModeOptions()
    {
        if (displayModeDropdown == null) return;

        displayModeDropdown.ClearOptions();
        displayModeDropdown.AddOptions(new List<string>(DisplayModeLabels));

        int stored = SettingsStore.DisplayMode;
        int selected = 0;
        for (int i = 0; i < DisplayModes.Length; i++)
        {
            if ((int)DisplayModes[i] != stored) continue;
            selected = i;
            break;
        }

        displayModeDropdown.SetValueWithoutNotify(selected);
        displayModeDropdown.RefreshShownValue();
    }

    private void BuildFpsCapOptions()
    {
        if (fpsCapDropdown == null) return;

        var labels = new List<string>(FpsCapOptions.Length);
        for (int i = 0; i < FpsCapOptions.Length; i++)
            labels.Add(FpsCapOptions[i] == 0 ? "Uncapped" : FpsCapOptions[i] + " FPS");

        fpsCapDropdown.ClearOptions();
        fpsCapDropdown.AddOptions(labels);

        int stored = SettingsStore.FpsCap;
        int selected = 0;
        for (int i = 0; i < FpsCapOptions.Length; i++)
        {
            if (FpsCapOptions[i] != stored) continue;
            selected = i;
            break;
        }

        fpsCapDropdown.SetValueWithoutNotify(selected);
        fpsCapDropdown.RefreshShownValue();
    }

    private int CurrentDropdownDisplayMode()
    {
        if (displayModeDropdown == null) return SettingsStore.DisplayMode;

        int index = displayModeDropdown.value;
        if (index < 0 || index >= DisplayModes.Length) return SettingsStore.DisplayMode;
        return (int)DisplayModes[index];
    }
}
