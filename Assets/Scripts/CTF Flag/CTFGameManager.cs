using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

/// <summary>
/// Manages Capture the Flag game mode
/// Tracks both flags and checks win conditions
/// Attach this to a GameObject in your Gameplay scene
/// </summary>
public class CTFGameManager : NetworkBehaviour
{
    public static CTFGameManager Instance { get; private set; }

    [Header("Flag References")]
    [Tooltip("Reference to Team1/Blue flag")]
    [SerializeField] private Flag team1Flag;

    [Tooltip("Reference to Team2/Red flag")]
    [SerializeField] private Flag team2Flag;

    [Header("UI References")]
    [Tooltip("Text element for notifications")]
    [SerializeField] private TextMeshProUGUI notificationText;

    [Tooltip("Panel for game over screen")]
    [SerializeField] private GameObject gameOverPanel;

    [Tooltip("Text for winner announcement")]
    [SerializeField] private TextMeshProUGUI winnerText;

    [Tooltip("Text for Team1/Blue flag status")]
    [SerializeField] private TextMeshProUGUI team1FlagStatusText;

    [Tooltip("Text for Team2/Red flag status")]
    [SerializeField] private TextMeshProUGUI team2FlagStatusText;

    [Header("Flag Indicators")]
    [Tooltip("Transform showing Team1 flag location")]
    [SerializeField] private GameObject team1FlagIndicator;

    [Tooltip("Transform showing Team2 flag location")]
    [SerializeField] private GameObject team2FlagIndicator;

    [Header("Settings")]
    [Tooltip("Time in seconds to show notifications")]
    [SerializeField] private float notificationDuration = 3f;

    // Networked properties with OnChanged callbacks
    [Networked, OnChangedRender(nameof(OnGameOverChanged))]
    public bool GameIsOver { get; set; }

    // Cached flag references so other systems (e.g. PlayerStatsHandler death drop) don't
    // have to do a scene-wide Find.
    public Flag Team1Flag => team1Flag;
    public Flag Team2Flag => team2Flag;

    // Lazily-cached base zones, used only for the rare flag-returned-home re-check.
    private NetworkedHomeBase[] homeBases;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Hide game over panel initially
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    public override void Spawned()
    {
        base.Spawned();

        // Find flags if not assigned
        if (team1Flag == null || team2Flag == null)
        {
            FindFlags();
        }

        // Initialize UI on all clients from current flag state (event-driven thereafter).
        RefreshAllFlagUI();
        OnGameOverChanged();
    }

    private void OnDestroy()
    {
        // Clean up singleton
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Indicators follow the continuously-moving flags only while shown. Their text and
        // visibility are event-driven (OnFlagStateChanged); this is a cheap transform follow,
        // no string rebuilds and no distance polling.
        if (team1FlagIndicator != null && team1FlagIndicator.activeSelf &&
            team1Flag != null && team1Flag.Object != null && team1Flag.Object.IsValid)
            team1FlagIndicator.transform.position = team1Flag.transform.position;

        if (team2FlagIndicator != null && team2FlagIndicator.activeSelf &&
            team2Flag != null && team2Flag.Object != null && team2Flag.Object.IsValid)
            team2FlagIndicator.transform.position = team2Flag.transform.position;
    }

    /// <summary>
    /// Find flag objects in the scene
    /// </summary>
    private void FindFlags()
    {
        Flag[] flags = FindObjectsByType<Flag>(FindObjectsSortMode.None);
        foreach (Flag flag in flags)
        {
            if (flag.OwningTeam == "Team1" || flag.OwningTeam == "Blue")
                team1Flag = flag;
            else if (flag.OwningTeam == "Team2" || flag.OwningTeam == "Red")
                team2Flag = flag;
        }

        if (team1Flag == null)
            Debug.LogError("⚠️ Team1 flag not found!");

        if (team2Flag == null)
            Debug.LogError("⚠️ Team2 flag not found!");
    }

    /// <summary>
    /// SERVER: Event-driven capture check. Called from a base trigger when a player enters
    /// their own base. A team scores by carrying the ENEMY flag into their base while their
    /// own flag is at home - no per-tick distance polling required.
    /// </summary>
    public void OnCarrierEnteredBase(PlayerRef carrier, Team baseTeam)
    {
        if (!HasStateAuthority || GameIsOver) return;
        if (team1Flag == null || team2Flag == null) return;

        if (baseTeam == Team.Team1 &&
            team2Flag.IsCarriedBy(carrier) && team1Flag.State == Flag.FlagState.AtHome)
        {
            EndGame(1);
        }
        else if (baseTeam == Team.Team2 &&
            team1Flag.IsCarriedBy(carrier) && team2Flag.State == Flag.FlagState.AtHome)
        {
            EndGame(2);
        }
    }

    /// <summary>
    /// SERVER: called when a flag returns home (the defending flag of a possible capture).
    /// Re-checks any carrier already parked in a base so they complete the capture without
    /// having to re-enter the trigger. Rare event - not a per-tick path - so the base list is
    /// found once and cached.
    /// </summary>
    public void OnFlagReturnedHome()
    {
        if (!HasStateAuthority || GameIsOver) return;

        if (homeBases == null || homeBases.Length == 0)
            homeBases = FindObjectsByType<NetworkedHomeBase>(FindObjectsSortMode.None);

        foreach (NetworkedHomeBase baseZone in homeBases)
        {
            if (baseZone != null) baseZone.ReevaluateOccupants();
        }
    }

    private void EndGame(int winningTeam)
    {
        if (!HasStateAuthority || GameIsOver) return;

        GameIsOver = true;
        AnnounceWinnerRpc(winningTeam);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowNotification(string message)
    {
        if (notificationText != null)
        {
            notificationText.text = message;
            notificationText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideNotification));
            Invoke(nameof(HideNotification), notificationDuration);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void AnnounceWinnerRpc(int winningTeam)
    {
        if (winnerText != null)
        {
            winnerText.text = $"Team {winningTeam} Wins!";
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        // Show final notification
        if (notificationText != null)
        {
            notificationText.text = $"Game Over! Team {winningTeam} Wins!";
            notificationText.gameObject.SetActive(true);
        }
    }

    private void HideNotification()
    {
        if (notificationText != null)
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Show the game-over panel when GameIsOver flips true (driven by OnChangedRender on all
    /// clients, plus an explicit call from Spawned for late joiners).
    /// </summary>
    private void OnGameOverChanged()
    {
        if (GameIsOver && gameOverPanel != null)
            gameOverPanel.SetActive(true);
    }

    /// <summary>
    /// Called by a Flag when its networked state changes (via Flag.OnStateChanged). Refreshes
    /// only that flag's HUD - no per-frame rebuild of all flag UI.
    /// </summary>
    public void OnFlagStateChanged(Flag flag)
    {
        if (flag == team1Flag)
            RefreshFlagUI(team1Flag, team1FlagStatusText, team1FlagIndicator);
        else if (flag == team2Flag)
            RefreshFlagUI(team2Flag, team2FlagStatusText, team2FlagIndicator);
    }

    private void RefreshAllFlagUI()
    {
        RefreshFlagUI(team1Flag, team1FlagStatusText, team1FlagIndicator);
        RefreshFlagUI(team2Flag, team2FlagStatusText, team2FlagIndicator);
    }

    private void RefreshFlagUI(Flag flag, TextMeshProUGUI statusText, GameObject indicator)
    {
        if (flag == null || flag.Object == null || !flag.Object.IsValid) return;

        Flag.FlagState state = flag.State;

        if (statusText != null)
        {
            switch (state)
            {
                // NOTE: the default LiberationSans SDF atlas has no emoji glyphs, so
                // emoji here render as the missing-glyph box. Use plain text (state is
                // also conveyed by color). To restore icons, add an emoji TMP font to
                // the LiberationSans fallback list and put the emoji back.
                case Flag.FlagState.AtHome:
                    statusText.text = "At Base";
                    statusText.color = Color.green;
                    break;
                case Flag.FlagState.Carried:
                    statusText.text = "Taken!";
                    statusText.color = Color.red;
                    break;
                default:
                    statusText.text = "Dropped";
                    statusText.color = Color.yellow;
                    break;
            }
        }

        if (indicator != null)
        {
            indicator.transform.position = flag.transform.position;
            indicator.SetActive(state != Flag.FlagState.AtHome);
        }
    }

    #region Public Getters

    public bool IsGameOver() => GameIsOver;

    public int GetPlayerCount()
    {
        if (Runner == null) return 0;
        return Runner.ActivePlayers.Count();
    }

    #endregion
}