using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The client-local options window: three tabs (Audio / Video / Gameplay), apply-on-change, and a
/// Reset to Defaults per tab. Video lives in its own component (VideoSettingsSection) because it is
/// the only tab with real state — a pending value and a confirm countdown.
///
/// NOTHING here writes networked or simulation state. It changes local audio, local window/present
/// settings, and two local cosmetic reads. See
/// docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// SCENE REQUIREMENT: put this component on an object that stays ACTIVE and point panelRoot at a
/// child window object. If the component sat on the object it deactivates, Awake would never run.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The window object toggled on/off. Must NOT be this component's own GameObject.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Tabs")]
    [SerializeField] private Button audioTabButton;
    [SerializeField] private Button videoTabButton;
    [SerializeField] private Button gameplayTabButton;
    [SerializeField] private GameObject audioTab;
    [SerializeField] private GameObject videoTab;
    [SerializeField] private GameObject gameplayTab;

    [Header("Audio")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider uiSlider;
    [SerializeField] private TMP_Text masterValueLabel;
    [SerializeField] private TMP_Text musicValueLabel;
    [SerializeField] private TMP_Text sfxValueLabel;
    [SerializeField] private TMP_Text uiValueLabel;
    [SerializeField] private Button audioResetButton;

    [Header("Gameplay")]
    [SerializeField] private Slider cameraShakeSlider;
    [SerializeField] private TMP_Text cameraShakeValueLabel;
    [SerializeField] private Toggle damageNumbersToggle;
    [SerializeField] private Button gameplayResetButton;

    [Header("Video")]
    [SerializeField] private VideoSettingsSection video;

    private Action onClosed;

    // Set while the UI is writing its own controls, so onValueChanged callbacks triggered by
    // RefreshFromStore do not write straight back into the store.
    private bool suppressCallbacks;

    private void Awake()
    {
        if (panelRoot == null)
        {
            Debug.LogError("❌ SettingsPanel: panelRoot not assigned!");
            return;
        }

        if (panelRoot == gameObject)
        {
            Debug.LogError("❌ SettingsPanel: panelRoot must be a CHILD window object, not this " +
                           "component's own GameObject — deactivating self would stop Awake from " +
                           "ever having run.");
            return;
        }

        if (closeButton != null) closeButton.onClick.AddListener(Close);

        if (audioTabButton != null) audioTabButton.onClick.AddListener(() => ShowTab(0));
        if (videoTabButton != null) videoTabButton.onClick.AddListener(() => ShowTab(1));
        if (gameplayTabButton != null) gameplayTabButton.onClick.AddListener(() => ShowTab(2));

        WireVolumeSlider(masterSlider, masterValueLabel, v => SettingsStore.MasterVolume = v);
        WireVolumeSlider(musicSlider, musicValueLabel, v => SettingsStore.MusicVolume = v);
        WireVolumeSlider(sfxSlider, sfxValueLabel, v => SettingsStore.SfxVolume = v);
        WireVolumeSlider(uiSlider, uiValueLabel, v => SettingsStore.UiVolume = v);

        if (cameraShakeSlider != null)
        {
            cameraShakeSlider.minValue = Game.Settings.Core.SettingsCatalog.CameraShakeMin;
            cameraShakeSlider.maxValue = Game.Settings.Core.SettingsCatalog.CameraShakeMax;
            cameraShakeSlider.wholeNumbers = false;
            cameraShakeSlider.onValueChanged.AddListener(value =>
            {
                if (suppressCallbacks) return;
                SettingsStore.CameraShakeIntensity = value;
                SetPercentLabel(cameraShakeValueLabel, value);
            });
        }

        if (damageNumbersToggle != null)
        {
            damageNumbersToggle.onValueChanged.AddListener(value =>
            {
                if (suppressCallbacks) return;
                SettingsStore.ShowDamageNumbers = value;
            });
        }

        if (audioResetButton != null)
        {
            audioResetButton.onClick.AddListener(() =>
            {
                SettingsStore.ResetAudioToDefaults();
                SettingsService.ApplyAudio();
                RefreshFromStore();
            });
        }

        if (gameplayResetButton != null)
        {
            gameplayResetButton.onClick.AddListener(() =>
            {
                SettingsStore.ResetGameplayToDefaults();
                RefreshFromStore();
            });
        }

        panelRoot.SetActive(false);
    }

    /// <summary>Show the window. onClosed fires when it closes, so the caller can restore itself.</summary>
    public void Open(Action closedCallback)
    {
        if (panelRoot == null) return;

        onClosed = closedCallback;
        RefreshFromStore();
        if (video != null) video.RefreshFromStore();
        ShowTab(0);
        panelRoot.SetActive(true);
    }

    /// <summary>Hide the window. Safe to call when it is already closed.</summary>
    public void Close()
    {
        if (panelRoot == null || !panelRoot.activeSelf) return;

        // Leaving a pending resolution unconfirmed must revert it, not strand the player in it.
        if (video != null) video.CancelPendingConfirm();

        panelRoot.SetActive(false);

        Action callback = onClosed;
        onClosed = null;
        if (callback != null) callback();
    }

    private void ShowTab(int index)
    {
        if (audioTab != null) audioTab.SetActive(index == 0);
        if (videoTab != null) videoTab.SetActive(index == 1);
        if (gameplayTab != null) gameplayTab.SetActive(index == 2);
    }

    private void WireVolumeSlider(Slider slider, TMP_Text label, Action<float> write)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(value =>
        {
            if (suppressCallbacks) return;
            write(value);
            SettingsService.ApplyAudio();
            SetPercentLabel(label, value);
        });
    }

    private void RefreshFromStore()
    {
        suppressCallbacks = true;

        SetSlider(masterSlider, masterValueLabel, SettingsStore.MasterVolume);
        SetSlider(musicSlider, musicValueLabel, SettingsStore.MusicVolume);
        SetSlider(sfxSlider, sfxValueLabel, SettingsStore.SfxVolume);
        SetSlider(uiSlider, uiValueLabel, SettingsStore.UiVolume);
        SetSlider(cameraShakeSlider, cameraShakeValueLabel, SettingsStore.CameraShakeIntensity);

        if (damageNumbersToggle != null)
            damageNumbersToggle.SetIsOnWithoutNotify(SettingsStore.ShowDamageNumbers);

        suppressCallbacks = false;
    }

    private static void SetSlider(Slider slider, TMP_Text label, float value)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
        SetPercentLabel(label, value);
    }

    private static void SetPercentLabel(TMP_Text label, float value)
    {
        if (label == null) return;
        label.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
