using UnityEngine;
using System.Collections;

/// <summary>
/// FIXED VERSION - Only follows the LOCAL player, not all players
/// Key changes:
/// - Only searches for players with HasInputAuthority = true
/// - Validates target player before following
/// - Prevents other players from hijacking the camera
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Follow Settings")]
    [SerializeField] private float followSpeed = 20f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10);
    [SerializeField] private Vector2 deadzoneSize = new Vector2(1f, 1f);
    [SerializeField] private float zoomOutSize = 10f;

    [Header("Look Ahead")]
    [SerializeField] private float lookAheadDistance = 2f;
    [SerializeField] private float lookAheadSpeed = 5f;

    [Header("Camera Shake")]
    [SerializeField] private float shakeDuration = 0f;
    [SerializeField] private float shakeAmount = 0.1f;
    [SerializeField] private float decreaseFactor = 1.0f;

    [Header("Auto-Find Player")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private float searchInterval = 0.5f;

    public Transform Target;

    private Camera cam;
    private Transform camTransform;
    private Vector3 currentLookAhead = Vector3.zero;
    private Vector3 targetLookAhead = Vector3.zero;
    private Vector3 originalPos;
    private Coroutine searchCoroutine;
    private PlayerController lockedPlayer; // local player we follow
    private Rigidbody2D targetRigidbody; // followed player's body (drives look-ahead)

    void Awake()
    {
        cam = GetComponent<Camera>();
        camTransform = transform;
        Cursor.visible = false;

        Debug.Log("CameraFollow: Awake");
    }

    void OnEnable()
    {
        originalPos = camTransform.localPosition;
        cam.orthographicSize = zoomOutSize;

        if (autoFindPlayer && searchCoroutine == null)
        {
            searchCoroutine = StartCoroutine(SearchForPlayer());
        }
    }

    void OnDisable()
    {
        if (searchCoroutine != null)
        {
            StopCoroutine(searchCoroutine);
            searchCoroutine = null;
        }
    }

    /// <summary>
    /// ⭐ FIXED VERSION - Only finds the LOCAL player
    /// </summary>
    private IEnumerator SearchForPlayer()
    {
        Debug.Log("CameraFollow: Started searching for LOCAL player...");

        while (true)
        {
            // If we already have a valid target, check if it's still the local player
            if (Target != null && lockedPlayer != null)
            {
                // Verify it's still our player
                if (lockedPlayer.HasInputAuthority)
                {
                    yield return new WaitForSeconds(searchInterval);
                    continue;
                }
                else
                {
                    // Player lost input authority (shouldn't happen, but handle it)
                    Debug.LogWarning("CameraFollow: Player lost input authority! Searching again...");
                    Target = null;
                    lockedPlayer = null;
                }
            }

            // Search for the LOCAL player only
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

            foreach (var player in players)
            {
                // ⭐ CRITICAL CHECK: Only follow players with InputAuthority
                if (player.HasInputAuthority)
                {
                    SetTarget(player.transform);
                    lockedPlayer = player;
                    Debug.Log($"✓ CameraFollow: Found and locked to LOCAL player: {player.name}");
                    yield break; // Stop searching
                }
            }

            // Wait before searching again
            yield return new WaitForSeconds(searchInterval);
        }
    }

    private void LateUpdate()
    {
        if (Target == null) return;

        // ⭐ SAFETY CHECK: Verify we're still following our player
        if (lockedPlayer != null && !lockedPlayer.HasInputAuthority)
        {
            Debug.LogWarning("CameraFollow: Target player lost input authority!");
            Target = null;
            lockedPlayer = null;

            // Restart search
            if (searchCoroutine == null)
            {
                searchCoroutine = StartCoroutine(SearchForPlayer());
            }
            return;
        }

        UpdateCameraPosition();
        UpdateCameraShake();
    }

    private void UpdateCameraPosition()
    {
        Vector3 baseTarget = Target.position + offset;
        baseTarget.z = -10f;

        // Look-ahead follows the player's actual motion, not a raw global input axis.
        // This keeps the camera tied to the LOCAL player only (no ghost input from the
        // shared Input devices) and works for any input source feeding the sim.
        float vx = targetRigidbody != null ? targetRigidbody.linearVelocity.x : 0f;
        float horizontalInput = Mathf.Abs(vx) > 0.1f ? Mathf.Sign(vx) : 0f;
        targetLookAhead = new Vector3(horizontalInput * lookAheadDistance, 0, 0);
        currentLookAhead = Vector3.Lerp(currentLookAhead, targetLookAhead, lookAheadSpeed * Time.deltaTime);

        Vector3 targetPosition = baseTarget + currentLookAhead;
        Vector3 delta = targetPosition - transform.position;

        if (delta.magnitude > 50f)
        {
            transform.position = targetPosition;
        }
        else if (Mathf.Abs(delta.x) > deadzoneSize.x || Mathf.Abs(delta.y) > deadzoneSize.y)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);
        }
    }

    private void UpdateCameraShake()
    {
        if (shakeDuration > 0)
        {
            camTransform.localPosition = originalPos + Random.insideUnitSphere * shakeAmount;
            shakeDuration -= Time.deltaTime * decreaseFactor;
        }
        else
        {
            shakeDuration = 0f;
            camTransform.localPosition = originalPos;
        }
    }

    public void ShakeCamera()
    {
        originalPos = camTransform.localPosition;
        shakeDuration = 0.2f;
    }

    /// <summary>
    /// Sets the camera target - validates that it's the local player
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            Debug.LogError("⚠️ CameraFollow: Trying to set camera target to NULL!");
            return;
        }

        // ⭐ VALIDATION: Ensure this is the local player
        PlayerController playerController = newTarget.GetComponent<PlayerController>();
        if (playerController != null && !playerController.HasInputAuthority)
        {
            Debug.LogWarning($"⚠️ CameraFollow: Attempted to set target to NON-LOCAL player {newTarget.name}! Ignoring.");
            return;
        }

        Target = newTarget;
        lockedPlayer = playerController;
        targetRigidbody = newTarget.GetComponent<Rigidbody2D>();

        Vector3 snapPosition = Target.position + offset;
        snapPosition.z = -10f;
        transform.position = snapPosition;
        currentLookAhead = Vector3.zero;

        Debug.Log($"✓ CameraFollow: Target locked to {newTarget.name} at {snapPosition}");

        if (searchCoroutine != null)
        {
            StopCoroutine(searchCoroutine);
            searchCoroutine = null;
        }
    }
}