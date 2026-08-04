using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using Game.Match.Core;

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

    [Header("Settings")]
    [Tooltip("Time in seconds to show notifications")]
    [SerializeField] private float notificationDuration = 3f;

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
    }

    public override void Spawned()
    {
        base.Spawned();

        // Find flags if not assigned
        if (team1Flag == null || team2Flag == null)
        {
            FindFlags();
        }
    }

    private void OnDestroy()
    {
        // Clean up singleton
        if (Instance == this)
        {
            Instance = null;
        }
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
        if (!HasStateAuthority) return;
        if (MatchManager.Instance == null || !MatchManager.Instance.IsPlayActive) return;
        if (team1Flag == null || team2Flag == null) return;

        if (baseTeam == Team.Team1 &&
            team2Flag.IsCarriedBy(carrier) && team1Flag.State == Flag.FlagState.AtHome)
        {
            if (MatchStatsManager.Instance != null) MatchStatsManager.Instance.RecordCapture(carrier);
            MatchManager.Instance.ReportCapture(Team.Team1);
        }
        else if (baseTeam == Team.Team2 &&
            team1Flag.IsCarriedBy(carrier) && team2Flag.State == Flag.FlagState.AtHome)
        {
            if (MatchStatsManager.Instance != null) MatchStatsManager.Instance.RecordCapture(carrier);
            MatchManager.Instance.ReportCapture(Team.Team2);
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
        if (!HasStateAuthority) return;
        if (MatchManager.Instance != null && !MatchManager.Instance.IsPlayActive) return;

        if (homeBases == null || homeBases.Length == 0)
            homeBases = FindObjectsByType<NetworkedHomeBase>(FindObjectsSortMode.None);

        foreach (NetworkedHomeBase baseZone in homeBases)
        {
            if (baseZone != null) baseZone.ReevaluateOccupants();
        }
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

    private void HideNotification()
    {
        if (notificationText != null)
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    #region Public Getters

    public bool IsGameOver() =>
        MatchManager.Instance != null &&
        (MatchManager.Instance.Phase == MatchPhase.PostMatch ||
         MatchManager.Instance.Phase == MatchPhase.Intermission);

    /// <summary>
    /// Is this player currently carrying either flag? Derived purely from the flags'
    /// [Networked] state (CurrentState + CarrierPlayerRef), so it is safe to read inside
    /// FixedUpdateNetwork/Simulate on a predicting client — unlike FlagCarrierMarker's
    /// local bool, which is render-path state and lags/never rewinds on resimulation.
    /// </summary>
    public bool IsCarrying(PlayerRef player)
    {
        return (team1Flag != null && team1Flag.IsCarriedBy(player)) ||
               (team2Flag != null && team2Flag.IsCarriedBy(player));
    }

    public int GetPlayerCount()
    {
        if (Runner == null) return 0;
        return Runner.ActivePlayers.Count();
    }

    #endregion
}