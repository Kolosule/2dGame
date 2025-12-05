using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    [Header("Dash Settings")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashTime = 0.2f;
    [SerializeField] private float dashCooldown = 1f;
    [SerializeField] private LayerMask wallLayer;
    [SerializeField] private float dashDistance = 3f;

    private Rigidbody2D rb;
    private float lastDashTime;
    private bool isDashing;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 👈 prevents tunneling
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift) && Time.time > lastDashTime + dashCooldown)
        {
            Vector2 dashDirection = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
            if (dashDirection == Vector2.zero) dashDirection = transform.right; // default forward

            StartCoroutine(PerformDash(dashDirection));
        }
    }

    private System.Collections.IEnumerator PerformDash(Vector2 dashDirection)
    {
        isDashing = true;
        lastDashTime = Time.time;

        // 👇 Raycast safety check
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dashDirection, dashDistance, wallLayer);
        float actualDistance = hit.collider != null ? hit.distance : dashDistance;

        float dashEndTime = Time.time + dashTime;
        while (Time.time < dashEndTime)
        {
            rb.linearVelocity = dashDirection * dashSpeed;
            yield return null;
        }

        rb.linearVelocity = Vector2.zero;

        // 👇 If wall detected, stop at wall
        if (hit.collider != null)
        {
            rb.position = hit.point - dashDirection * 0.1f; // small offset so we don’t overlap wall
        }

        isDashing = false;
    }
}