using UnityEngine;
using Fusion;

/// <summary>
/// Local-player camera juice: triggers a directional camera kick when a dash starts, and relays
/// the predicted melee hit-stop to the camera. Lives on the Player prefab; only the local
/// input-authority instance drives the (single) gameplay camera — mirrors
/// PlayerCameraRespawnHandler's gating so a remote player's events never move your camera.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerCameraFeelHandler : MonoBehaviour
{
    [Header("💥 Dash Kick")]
    [Tooltip("Strength of the camera kick when a dash starts (world units).")]
    [SerializeField] private float dashKickMagnitude = 0.5f;
    [Tooltip("How long the dash kick decays over.")]
    [SerializeField] private float dashKickDuration = 0.12f;

    [Header("🛑 Melee Hit-Stop")]
    [Tooltip("How long the camera holds (freezes follow) when your melee lands.")]
    [SerializeField] private float hitStopDuration = 0.07f;
    [Tooltip("Small upward punch added on a landed hit.")]
    [SerializeField] private float hitStopPunch = 0.15f;

    private NetworkObject netObj;
    private PlayerMovement movement;
    private PlayerCamera playerCamera;
    private bool wasDashing;

    private void Awake()
    {
        netObj = GetComponent<NetworkObject>();
        movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // Only the local player's handler may drive the single gameplay camera.
        if (netObj == null || !netObj.HasInputAuthority) return;

        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<PlayerCamera>();
            if (playerCamera == null) return;
        }

        bool dashing = movement != null && movement.IsDashing();
        if (dashing && !wasDashing)
        {
            // Kick in the dash travel direction (player faces dash direction via localScale.x sign).
            float dir = transform.localScale.x >= 0f ? 1f : -1f;
            playerCamera.AddImpulse(new Vector2(dir, 0f), dashKickMagnitude, dashKickDuration);
        }
        wasDashing = dashing;
    }

    /// <summary>Called by PlayerCombat when a local melee swing is predicted to connect.</summary>
    public void TriggerHitStop()
    {
        if (netObj == null || !netObj.HasInputAuthority) return;
        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<PlayerCamera>();
            if (playerCamera == null) return;
        }
        playerCamera.Hold(hitStopDuration);
        playerCamera.AddImpulse(Vector2.up, hitStopPunch, hitStopDuration);
    }
}
