using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public float FollowSpeed = 20f;
    public Transform Target;   // 👈 Player transform

    private Camera cam;

    [SerializeField] private Vector3 offset;
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
            camTransform = GetComponent(typeof(Transform)) as Transform;
        }
    }

    void OnEnable()
    {
        originalPos = camTransform.localPosition;
        cam.orthographicSize = zoomOutSize;
    }

    private void Update()
    {
        // 👇 Prevent errors if Target has been destroyed
        if (Target == null) return;

        Vector3 baseTarget = Target.position + offset;
        baseTarget.z = -300f;

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

    // 👇 New method to update camera target after respawn
    public void SetTarget(Transform newTarget)
    {
        Target = newTarget;
    }
}