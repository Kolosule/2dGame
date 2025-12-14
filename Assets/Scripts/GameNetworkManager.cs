using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages network connection and player spawning
/// Attach this to a GameObject in your main menu scene
/// </summary>
public class GameNetworkManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private GameObject menuPanel;

    [Header("Game Settings")]
    [SerializeField] private int maxPlayers = 8;

    private NetworkManager networkManager;

    private void Start()
    {
        networkManager = NetworkManager.Singleton;

        // IMPORTANT: Enable connection approval
        if (networkManager != null)
        {
            networkManager.NetworkConfig.ConnectionApproval = true;
            Debug.Log("✓ Connection approval enabled");
        }

        // Setup button listeners
        if (hostButton != null)
            hostButton.onClick.AddListener(StartHost);

        if (clientButton != null)
            clientButton.onClick.AddListener(StartClient);
    }

    private void StartHost()
    {
        Debug.Log("Starting as Host...");

        // Start as host (server + client)
        bool success = networkManager.StartHost();

        if (success)
        {
            Debug.Log("Host started successfully!");
            HideMenu();

            // Load the game scene
            if (networkManager.SceneManager != null)
            {
                // Replace "GameScene" with your actual game scene name
                networkManager.SceneManager.LoadScene("Gameplay", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        else
        {
            Debug.LogError("Failed to start host");
        }
    }

    private void StartClient()
    {
        Debug.Log("Starting as Client...");

        // Start as client
        bool success = networkManager.StartClient();

        if (success)
        {
            Debug.Log("Client started successfully!");
            HideMenu();
            // Client will automatically load the scene that the host loads
        }
        else
        {
            Debug.LogError("Failed to start client");
        }
    }

    private void HideMenu()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }
    }

    // Shutdown network connection
    public void Disconnect()
    {
        if (networkManager.IsServer)
        {
            networkManager.Shutdown();
        }
        else if (networkManager.IsClient)
        {
            networkManager.Shutdown();
        }

        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}