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
    [SerializeField] private ScoreboardPanel scoreboardPanel;

    private MatchManager bound;
    private int lastCountdownSeconds = -1;
    private int lastMatchTimerSeconds = -1;
    private int lastReturnCountdownSeconds = -1;

    private void Awake()
    {
        if (DedicatedServerPresentation.IsHeadless)
        {
            enabled = false;
            return;
        }

        HideAll();
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
    }

    private void OnEnable()
    {
        InvalidateTimerText();
        if (bound != null)
            UpdateTimerText(bound.Phase, bound.PhaseTimeRemaining);
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
        UpdateTimerText(bound.Phase, remaining);
    }

    private void UpdateTimerText(MatchPhase phase, float? remaining)
    {
        switch (phase)
        {
            case MatchPhase.Countdown:
                if (countdownText != null)
                {
                    int seconds = Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f));
                    if (seconds != lastCountdownSeconds)
                    {
                        countdownText.text = seconds.ToString();
                        lastCountdownSeconds = seconds;
                    }
                }
                break;
            case MatchPhase.Live:
            case MatchPhase.SuddenDeath:
                if (matchTimerRoot != null) matchTimerRoot.SetActive(remaining.HasValue);
                if (remaining.HasValue && matchTimerText != null)
                {
                    int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining.Value));
                    if (seconds != lastMatchTimerSeconds)
                    {
                        matchTimerText.text = FormatClock(seconds);
                        lastMatchTimerSeconds = seconds;
                    }
                }
                else
                    lastMatchTimerSeconds = -1;
                break;
            case MatchPhase.PostMatch:
                if (returnCountdownText != null)
                {
                    int seconds = Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f));
                    if (seconds != lastReturnCountdownSeconds)
                    {
                        returnCountdownText.text = $"Returning to lobby in {seconds}…";
                        lastReturnCountdownSeconds = seconds;
                    }
                }
                break;
        }
    }

    private void InvalidateTimerText()
    {
        lastCountdownSeconds = -1;
        lastMatchTimerSeconds = -1;
        lastReturnCountdownSeconds = -1;
    }

    /// <summary>Toggle which panel is visible for the current phase. Called on every PhaseChanged.</summary>
    private void Render()
    {
        InvalidateTimerText();
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
            // The scoreboard auto-shows for this same PostMatch phase (see the SetForcedVisible
            // call below) and can end up a later sibling on the canvas; force the results panel
            // (winner banner + Return-to-Lobby button) above it so the board never occludes the
            // banner or swallows the host's button clicks, regardless of hierarchy order.
            if (resultsPanel != null) resultsPanel.transform.SetAsLastSibling();
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

        if (scoreboardPanel != null) scoreboardPanel.SetForcedVisible(phase == MatchPhase.PostMatch);
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

    private static string FormatClock(int seconds)
    {
        return $"{seconds / 60:0}:{seconds % 60:00}";
    }
}
