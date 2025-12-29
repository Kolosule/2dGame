using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

/// <summary>
/// Manages UI display for team scores, player coins, player health, and dash cooldown.
/// CLEAN VERSION - All UI logic centralized here, reads data from gameplay scripts
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

    [Header("Dash Cooldown Display")]
    [Tooltip("Image with Image Type set to 'Filled' for radial cooldown")]
    [SerializeField] private Image dashCooldownRadial;

    [Tooltip("Optional text showing countdown or 'READY'")]
    [SerializeField] private TextMeshProUGUI dashCooldownText;

    [Header("Buff Indicators (Optional)")]
    [SerializeField] private GameObject team1DamageBuffIcon;
    [SerializeField] private GameObject team1DefenseBuffIcon;
    [SerializeField] private GameObject team2DamageBuffIcon;
    [SerializeField] private GameObject team2DefenseBuffIcon;

    [Header("Update Settings")]
    [SerializeField] private float updateInterval = 0.1f;

    // Local player references
    private NetworkedPlayerInventory localPlayer;
    private PlayerStatsHandler localStats;
    private PlayerMovement localMovement;
    private float nextUpdateTime;

    // Smooth health animation
    private float smoothHealth;

    // ============================
    // UNITY LIFECYCLE
    // ============================

    private void Start()
    {
        // Hide buff icons initially
        if (team1DamageBuffIcon != null) team1DamageBuffIcon.SetActive(false);
        if (team1DefenseBuffIcon != null) team1DefenseBuffIcon.SetActive(false);
        if (team2DamageBuffIcon != null) team2DamageBuffIcon.SetActive(false);
        if (team2DefenseBuffIcon != null) team2DefenseBuffIcon.SetActive(false);

        // Initialize dash UI
        if (dashCooldownRadial != null)
        {
            dashCooldownRadial.fillAmount = 1f; // Start ready
        }

        if (dashCooldownText != null)
        {
            dashCooldownText.text = "READY";
            dashCooldownText.color = Color.yellow;
        }
    }

    private void Update()
    {
        // Throttle updates for performance
        if (Time.time < nextUpdateTime)
            return;

        nextUpdateTime = Time.time + updateInterval;

        // Find local player if needed
        if (localPlayer == null || localStats == null || localMovement == null)
            FindLocalPlayer();

        // Update all UI elements
        UpdateTeamScoreDisplay();
        UpdatePlayerCoinDisplay();
        UpdatePlayerHealthDisplay(false);
        UpdateDashDisplay();
    }

    // ============================
    // PLAYER FINDING
    // ============================

    private void FindLocalPlayer()
    {
        NetworkedPlayerInventory[] players = FindObjectsByType<NetworkedPlayerInventory>(FindObjectsSortMode.None);

        foreach (var player in players)
        {
            if (player.HasInputAuthority)
            {
                localPlayer = player;
                localStats = player.GetComponent<PlayerStatsHandler>();
                localMovement = player.GetComponent<PlayerMovement>();

                // Initialize smooth health on first find
                if (localStats != null)
                {
                    smoothHealth = localStats.GetCurrentHealth();
                }

                break;
            }
        }
    }

    // ============================
    // TEAM SCORE
    // ============================

    private void UpdateTeamScoreDisplay()
    {
        TeamScoreManager scoreManager = FindFirstObjectByType<TeamScoreManager>();
        if (scoreManager == null)
            return;

        if (team1ScoreText != null)
            team1ScoreText.text = $"Team1: {scoreManager.Team1Score}";

        if (team2ScoreText != null)
            team2ScoreText.text = $"Team2: {scoreManager.Team2Score}";

        UpdateBuffIndicators(scoreManager);
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

    // ============================
    // PLAYER COINS
    // ============================

    public void UpdatePlayerCoinDisplay()
    {
        if (localPlayer == null)
            FindLocalPlayer();

        if (playerCoinText != null)
            playerCoinText.text = localPlayer != null ? $"Coins: {localPlayer.CoinCount}" : "Coins: 0";

        if (playerCoinValueText != null)
            playerCoinValueText.text = localPlayer != null ? $"Value: {localPlayer.TotalCoinValue}" : "Value: 0";
    }

    // ============================
    // PLAYER HEALTH
    // ============================

    private void UpdatePlayerHealthDisplay(bool instant)
    {
        if (localStats == null)
            FindLocalPlayer();

        if (localStats == null)
            return;

        float current = localStats.GetCurrentHealth();
        float max = localStats.GetMaxHealth();

        // Smooth health animation
        if (instant || Mathf.Abs(smoothHealth - current) > 20f)
        {
            smoothHealth = current; // Snap for large changes
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

    // ============================
    // DASH COOLDOWN
    // ============================

    private void UpdateDashDisplay()
    {
        if (localMovement == null)
            return;

        float percent = localMovement.GetDashCooldownPercent();
        float remaining = localMovement.GetDashCooldownRemaining();
        bool ready = localMovement.CanDash();

        // Update radial fill
        if (dashCooldownRadial != null)
        {
            dashCooldownRadial.fillAmount = percent;

            // Optional: Change color based on readiness
            // dashCooldownRadial.color = ready ? Color.yellow : Color.white;
        }

        // Update text
        if (dashCooldownText != null)
        {
            if (ready)
            {
                dashCooldownText.text = "READY";
                dashCooldownText.color = Color.yellow;
            }
            else
            {
                dashCooldownText.text = remaining.ToString("0.0") + "s";
                dashCooldownText.color = Color.white;
            }
        }
    }

    // ============================
    // LEGACY SUPPORT (for backwards compatibility)
    // ============================

    public void SetTrackedPlayer(NetworkedPlayerInventory player)
    {
        localPlayer = player;
        localStats = player.GetComponent<PlayerStatsHandler>();
        localMovement = player.GetComponent<PlayerMovement>();
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