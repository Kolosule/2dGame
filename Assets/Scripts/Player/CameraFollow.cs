using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public float FollowSpeed = 20f;
    public Transform Target;   // Player transform

    private Camera cam;

    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10);
    [SerializeField] private Vector2 deadzoneSize = new Vector2(1f, 1f);
    [SerializeField] private float zoomOutSize = 10f;
    [SerializeField] private float lookAheadDistance = 2f;
    [SerializeField] private float lookAheadSpeed = 5f;

    private Vector3 currentLookAhead = Vector3.zero;
    private Vector3 targetLookAhead = Vector3.zero;
    private Transform camTransform;

    public float shakeDuration = 0f;
    public float shakeAmount = 0.1f;
    public float decreaseFactor = 1.0f;

    Vector3 originalPos;

    void Awake()
    {
        cam = GetComponent<Camera>();
        Cursor.visible = false;

        if (camTransform == null)
        {
            camTransform = GetComponent<Transform>();
        }
    }

    void OnEnable()
    {
        originalPos = camTransform.localPosition;
        cam.orthographicSize = zoomOutSize;
    }

    private void Update()
    {
        // Check if we have a target
        if (Target == null)
        {
            Debug.LogWarning("Camera has no target to follow!");
            return;
        }

        Vector3 baseTarget = Target.position + offset;
        baseTarget.z = -10f; // Keep camera in front

        float horizontalInput = Input.GetAxisRaw("Horizontal");

        targetLookAhead = new Vector3(horizontalInput * lookAheadDistance, 0, 0);
        currentLookAhead = Vector3.Lerp(currentLookAhead, targetLookAhead, lookAheadSpeed * Time.deltaTime);

        Vector3 targetPosition = baseTarget + currentLookAhead;

        Vector3 delta = targetPosition - transform.position;
        if (Mathf.Abs(delta.x) > deadzoneSize.x || Mathf.Abs(delta.y) > deadzoneSize.y)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, FollowSpeed * Time.deltaTime);
        }
    }

    public void ShakeCamera()
    {
        originalPos = camTransform.localPosition;
        shakeDuration = 0.2f;
    }

    /// <summary>
    /// Set the camera target (called when player spawns)
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            Debug.LogError("⚠️ Trying to set camera target to NULL!");
            return;
        }

        Target = newTarget;

        // Immediately snap to target position
        if (Target != null)
        {
            Vector3 targetPos = Target.position + offset;
            targetPos.z = -10f;
            transform.position = targetPos;
        }

        Debug.Log($"✓ Camera target set to: {newTarget.name} at position {newTarget.position}");
    }

    // Draw debug info in Scene view
    private void OnDrawGizmos()
    {
        if (Target != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, Target.position);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Target.position + offset, new Vector3(deadzoneSize.x * 2, deadzoneSize.y * 2, 1));
        }
    }
}