using UnityEngine;
using Fusion;

/// <summary>
/// Local-player camera juice: triggers a directional camera kick when a dash starts. Lives on
/// the Player prefab; only the local input-authority instance drives the (single) gameplay
/// camera — mirrors PlayerCameraRespawnHandler's gating so a remote player's events never move
/// your camera.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerCameraFeelHandler : MonoBehaviour
{
    [Header("💥 Dash Kick")]
    [Tooltip("Strength of the camera kick when a dash starts (world units).")]
    [SerializeField] private float dashKickMagnitude = 0.5f;
    [Tooltip("How long the dash kick decays over.")]
    [SerializeField] private float dashKickDuration = 0.12f;

    private NetworkObject netObj;
    private PlayerMovement movement;
    private PlayerCamera playerCamera;
    private bool wasDashing;

    private void Awake()
    {
        if (DedicatedServerPresentation.IsHeadless)
        {
            enabled = false;
            return;
        }

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
}
