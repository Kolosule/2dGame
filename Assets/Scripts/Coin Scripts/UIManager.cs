using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

/// <summary>
/// Manages UI display for team scores, player coins, and player health.
/// UPDATED: Now includes smooth health bar animation.
/// Attach this to a UI Canvas GameObject.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Team Score Display")]
    [SerializeField] private TextMeshProUGUI team1ScoreText;
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Header("Player Coin Display")]
    [SerializeField] private TextMeshProUGUI playerCoinText;
    [SerializeField] private TextMeshProUGUI playerCoinValueText;

    [Header("Player Health Display")]
    [Tooltip("Slider for the player's health bar")]
    [SerializeField] private Slider healthSlider;

    [Tooltip("Text showing numeric health (optional)")]
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Buff Indicators (Optional)")]
    [SerializeField] private GameObject team1DamageBuffIcon;
    [SerializeField] private GameObject team1DefenseBuffIcon;
    [SerializeField] private GameObject team2DamageBuffIcon;
    [SerializeField] private GameObject team2DefenseBuffIcon;

    [Header("Update Settings")]
    [SerializeField] private float updateInterval = 0.1f;

    [Header("Dash Cooldown Display")]
    [SerializeField] private Image dashCooldownRadial; // radial fill image
    [SerializeField] private TextMeshProUGUI dashCooldownText; // optional countdown text

    // Local player references
    private NetworkedPlayerInventory localPlayer;
    private PlayerStatsHandler localStats;
    private PlayerMovement localMovement;
    private float nextUpdateTime;

    // Smooth health animation
    private float smoothHealth;

    private void Start()
    {
        // Hide buff icons initially
        if (team1DamageBuffIcon != null) team1DamageBuffIcon.SetActive(false);
        if (team1DefenseBuffIcon != null) team1DefenseBuffIcon.SetActive(false);
        if (team2DamageBuffIcon != null) team2DamageBuffIcon.SetActive(false);
        if (team2DefenseBuffIcon != null) team2DefenseBuffIcon.SetActive(false);

        FindLocalPlayer();

        // Initialize UI
        UpdateTeamScores();
        UpdatePlayerCoinDisplay();
        UpdatePlayerHealthDisplay(true);
    }

    private void Update()
    {
        if (Time.time >= nextUpdateTime)
        {
            UpdateTeamScores();
            UpdatePlayerCoinDisplay();
            UpdatePlayerHealthDisplay(false);
            UpdateDashDisplay(); // <--- NEW

            nextUpdateTime = Time.time + updateInterval;
        }
    }

    // -------------------------
    // FIND LOCAL PLAYER
    // -------------------------
    private void FindLocalPlayer()
    {
        if (localPlayer != null && localStats != null && localMovement != null)
            return;

        NetworkedPlayerInventory[] allPlayers = FindObjectsByType<NetworkedPlayerInventory>(FindObjectsSortMode.None);

        foreach (NetworkedPlayerInventory player in allPlayers)
        {
            if (player.HasInputAuthority)
            {
                localPlayer = player;
                localStats = player.GetComponent<PlayerStatsHandler>();
                localMovement = player.GetComponent<PlayerMovement>(); // <-- FIXED

                if (localStats != null)
                    smoothHealth = localStats.GetCurrentHealth();

                Debug.Log("UIManager: Found local player + stats + movement");
                break;
            }
        }
    }

    // -------------------------
    // TEAM SCORES
    // -------------------------
    public void UpdateTeamScores()
    {
        TeamScoreManager scoreManager = TeamScoreManager.Instance;
        if (scoreManager == null) return;

        if (team1ScoreText != null)
            team1ScoreText.text = $"Team1: {scoreManager.Team1Score}";

        if (team2ScoreText != null)
            team2ScoreText.text = $"Team2: {scoreManager.Team2Score}";

        UpdateBuffIndicators(scoreManager);
    }

    private void UpdateDashDisplay()
    {
        if (localMovement == null)
            return;

        float percent = localMovement.GetDashCooldownPercent();
        float remaining = localMovement.GetDashCooldownRemaining();

        // Radial fill
        if (dashCooldownRadial != null)
            dashCooldownRadial.fillAmount = percent;

        // Text countdown
        if (dashCooldownText != null)
        {
            if (percent >= 1f)
            {
                dashCooldownText.text = "READY";
                dashCooldownText.color = Color.yellow;
            }
            else
            {
                dashCooldownText.text = remaining.ToString("0.0");
                dashCooldownText.color = Color.white;
            }
        }
    }
    private void UpdateBuffIndicators(TeamScoreManager scoreManager)
    {
        if (team1DamageBuffIcon != null)
            team1DamageBuffIcon.SetActive(scoreManager.Team1DamageBuff);

        if (team1DefenseBuffIcon != null)
            team1DefenseBuffIcon.SetActive(scoreManager.Team1DefenseBuff);

        if (team2DamageBuffIcon != null)
            team2DamageBuffIcon.SetActive(scoreManager.Team2DamageBuff);

        if (team2DefenseBuffIcon != null)
            team2DefenseBuffIcon.SetActive(scoreManager.Team2DefenseBuff);
    }

    // -------------------------
    // COINS
    // -------------------------
    public void UpdatePlayerCoinDisplay()
    {
        if (localPlayer == null)
            FindLocalPlayer();

        if (playerCoinText != null)
            playerCoinText.text = localPlayer != null ? $"Coins: {localPlayer.CoinCount}" : "Coins: 0";

        if (playerCoinValueText != null)
            playerCoinValueText.text = localPlayer != null ? $"Value: {localPlayer.TotalCoinValue}" : "Value: 0";
    }

    // -------------------------
    // HEALTH (NEW)
    // -------------------------
    private void UpdatePlayerHealthDisplay(bool instant)
    {
        if (localStats == null)
            FindLocalPlayer();

        if (localStats == null)
            return;

        float current = localStats.GetCurrentHealth();
        float max = localStats.GetMaxHealth();
        if (Mathf.Abs(smoothHealth - current) > 20f)
        {
            smoothHealth = current; // snap
        }
        else
        {
            smoothHealth = Mathf.Lerp(smoothHealth, current, Time.deltaTime * 15f);
        }
        if (instant)
        {
            smoothHealth = current;
        }
        else
        {
            smoothHealth = Mathf.Lerp(smoothHealth, current, Time.deltaTime * 20f);
        }

        if (healthSlider != null)
            healthSlider.value = smoothHealth / max;

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }  

    // -------------------------
    // LEGACY SUPPORT
    // -------------------------
    public void SetTrackedPlayer(NetworkedPlayerInventory player)
    {
        localPlayer = player;
        localStats = player.GetComponent<PlayerStatsHandler>();
        UpdatePlayerCoinDisplay();
        UpdatePlayerHealthDisplay(true);
    }

    public void UpdatePlayerCoinDisplay(NetworkedPlayerInventory player)
    {
        if (player != null && player.HasInputAuthority)
            localPlayer = player;

        UpdatePlayerCoinDisplay();
    }
}