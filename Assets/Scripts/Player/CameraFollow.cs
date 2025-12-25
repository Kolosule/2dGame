using UnityEngine;
using System.Collections;

/// <summary>
/// Camera follow that handles scene transitions and continuously searches for player
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

        // Start searching for player when enabled
        if (autoFindPlayer && searchCoroutine == null)
        {
            searchCoroutine = StartCoroutine(SearchForPlayer());
        }
    }

    void OnDisable()
    {
        // Stop searching when disabled
        if (searchCoroutine != null)
        {
            StopCoroutine(searchCoroutine);
            searchCoroutine = null;
        }
    }

    /// <summary>
    /// Continuously search for the local player until found
    /// </summary>
    private IEnumerator SearchForPlayer()
    {
        Debug.Log("CameraFollow: Started searching for player...");

        while (true)
        {
            // If we already have a target, check if it's still valid
            if (Target != null)
            {
                yield return new WaitForSeconds(searchInterval);
                continue;
            }

            // Search for player by tag
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                // Check if this is the LOCAL player (has enabled PlayerController)
                var controller = player.GetComponent<PlayerController>();
                if (controller != null && controller.enabled)
                {
                    SetTarget(player.transform);
                    Debug.Log($"✓ CameraFollow: Found local player via tag: {player.name}");
                    yield break; // Stop searching
                }
            }

            // Alternative: Search for NetworkPlayerWrapper with IsOwner
            NetworkPlayerWrapper[] players = FindObjectsByType<NetworkPlayerWrapper>(FindObjectsSortMode.None);
            foreach (var playerWrapper in players)
            {
                if (playerWrapper.HasInputAuthority)
                {
                    SetTarget(playerWrapper.transform);
                    Debug.Log($"✓ CameraFollow: Found local player via NetworkPlayerWrapper: {playerWrapper.name}");
                    yield break; // Stop searching
                }
            }

            // Wait before searching again
            yield return new WaitForSeconds(searchInterval);
        }
    }

    private void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        UpdateCameraPosition();
        UpdateCameraShake();
    }

    private void UpdateCameraPosition()
    {
        Vector3 baseTarget = Target.position + offset;
        baseTarget.z = -10f;

        float horizontalInput = Input.GetAxisRaw("Horizontal");
        targetLookAhead = new Vector3(horizontalInput * lookAheadDistance, 0, 0);
        currentLookAhead = Vector3.Lerp(currentLookAhead, targetLookAhead, lookAheadSpeed * Time.deltaTime);

        Vector3 targetPosition = baseTarget + currentLookAhead;
        Vector3 delta = targetPosition - transform.position;

        // Snap if far away
        if (delta.magnitude > 50f)
        {
            transform.position = targetPosition;
        }
        // Smooth follow if outside deadzone
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

    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            Debug.LogError("⚠️ CameraFollow: Trying to set camera target to NULL!");
            return;
        }

        Target = newTarget;

        Vector3 snapPosition = Target.position + offset;
        snapPosition.z = -10f;
        transform.position = snapPosition;
        currentLookAhead = Vector3.zero;

        Debug.Log($"✓ CameraFollow: Target locked to {newTarget.name} at {snapPosition}");

        // Stop searching since we found our target
        if (searchCoroutine != null)
        {
            StopCoroutine(searchCoroutine);
            searchCoroutine = null;
        }
    }
}