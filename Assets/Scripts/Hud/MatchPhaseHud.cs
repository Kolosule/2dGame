using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Match.Core;

/// <summary>
/// Local presentation of the match life cycle. Binds once to MatchManager, toggles panels on
/// PhaseChanged, and reads the countdown/timer number each LateUpdate (local render-path read of
/// networked state — not networked polling). No authoritative state here.
/// </summary>
public class MatchPhaseHud : MonoBehaviour
{
    [Header("Countdown / warmup (center)")]
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownText;

    [Header("Live match timer (top)")]
    [SerializeField] private GameObject matchTimerRoot;
    [SerializeField] private TMP_Text matchTimerText;

    [Header("Sudden Death banner")]
    [Tooltip("Shown for the whole SuddenDeath phase. The buff row and Team Power strip need no " +
             "special case — they derive maxed tiers from Phase like everything else.")]
    [SerializeField] private GameObject suddenDeathRoot;

    [Header("Results panel")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private TMP_Text returnCountdownText;
    [SerializeField] private Button returnToLobbyButton;

    private MatchManager bound;

    private void Awake()
    {
        HideAll();
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
    }

    private void OnDestroy()
    {
        if (bound != null) bound.PhaseChanged -= Render;
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.RemoveListener(OnReturnToLobbyClicked);
    }

    private void LateUpdate()
    {
        // Bind lazily: MatchManager spawns after the scene loads.
        if (bound == null)
        {
            if (MatchManager.Instance == null) return;
            bound = MatchManager.Instance;
            bound.PhaseChanged += Render;
            Render();
        }

        // Per-frame numeric read for the ticking display only.
        float? remaining = bound.PhaseTimeRemaining;
        switch (bound.Phase)
        {
            case MatchPhase.Countdown:
                if (countdownText != null)
                    countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f)).ToString();
                break;
            case MatchPhase.Live:
            case MatchPhase.SuddenDeath:
                if (matchTimerRoot != null) matchTimerRoot.SetActive(remaining.HasValue);
                if (remaining.HasValue && matchTimerText != null)
                    matchTimerText.text = FormatClock(remaining.Value);
                break;
            case MatchPhase.PostMatch:
                if (returnCountdownText != null)
                    returnCountdownText.text =
                        $"Returning to lobby in {Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f))}…";
                break;
        }
    }

    /// <summary>Toggle which panel is visible for the current phase. Called on every PhaseChanged.</summary>
    private void Render()
    {
        if (bound == null) return;
        MatchPhase phase = bound.Phase;

        if (countdownRoot != null)
            countdownRoot.SetActive(phase == MatchPhase.Warmup || phase == MatchPhase.Countdown);
        if (countdownText != null && phase == MatchPhase.Warmup)
            countdownText.text = "Get ready…";

        if (matchTimerRoot != null)
            matchTimerRoot.SetActive(
                (phase == MatchPhase.Live || phase == MatchPhase.SuddenDeath) &&
                bound.PhaseTimeRemaining.HasValue);

        if (suddenDeathRoot != null) suddenDeathRoot.SetActive(phase == MatchPhase.SuddenDeath);

        bool results = phase == MatchPhase.PostMatch || phase == MatchPhase.Intermission;
        if (resultsPanel != null) resultsPanel.SetActive(results);
        if (results)
        {
            if (winnerText != null) winnerText.text = MatchResolver.WinnerLabel(bound.Winner);
            if (finalScoreText != null)
            {
                int t1 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team1Score : 0;
                int t2 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team2Score : 0;
                finalScoreText.text = $"Team 1  {t1}   —   {t2}  Team 2";
            }
            if (returnToLobbyButton != null)
                returnToLobbyButton.gameObject.SetActive(bound.LocalPlayerIsHost());
        }
    }

    private void OnReturnToLobbyClicked()
    {
        if (bound != null) bound.RequestReturnToLobby();
    }

    private void HideAll()
    {
        if (countdownRoot != null) countdownRoot.SetActive(false);
        if (matchTimerRoot != null) matchTimerRoot.SetActive(false);
        if (suddenDeathRoot != null) suddenDeathRoot.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
    }

    private static string FormatClock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60:0}:{s % 60:00}";
    }
}
