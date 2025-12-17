using System.Collections;
using UnityEngine;
using UnityEngine.UI; // for UI Image

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Jump Settings")]
    [SerializeField] private int coyoteTimeFrames = 6;   // ~0.1s at 60fps
    [SerializeField] private int jumpBufferFrames = 6;   // ~0.1s at 60fps
    [SerializeField] private float jumpCutMultiplier = 0.5f; // short hop multiplier

    [Header("UI")]
    [SerializeField] private Image dashCooldownBar; // drag your UI Image here

    private Rigidbody2D rb;
    private Animator anim;
    private bool canDash = true;
    private bool isDashing = false;

    private int remainingAirJumps;
    private int coyoteCounter;
    private int jumpBufferCounter;

    private float originalGravity;
    private float originalDrag;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        remainingAirJumps = stats.maxAirJumps;

        if (dashCooldownBar != null)
            dashCooldownBar.fillAmount = 1f; // full = dash ready

    }

    void Update()
    {
        HandleInput();
        UpdateJumpVariables();
        HandleVariableJumpHeight();

        // Cancel dash instantly when Shift is released
        if (isDashing && Input.GetKeyUp(KeyCode.LeftShift))
        {
            Debug.Log("Dash canceled instantly by releasing Shift");
            StopAllCoroutines(); // stop dash coroutine
            EndDash();
            StartCoroutine(DashCooldown()); // ensure cooldown still runs
        }
    }

    public void HandleInput()
    {
        float xAxis = Input.GetAxisRaw("Horizontal");
        float yAxis = Input.GetAxisRaw("Vertical");

        // Flip sprite
        if (xAxis != 0) transform.localScale = new Vector2(Mathf.Sign(xAxis), 1);

        // Only apply normal movement if not dashing
        if (!isDashing)
        {
            rb.linearVelocity = new Vector2(xAxis * stats.walkSpeed, rb.linearVelocity.y);
            anim.SetBool("Walking", xAxis != 0);
        }

        // Buffer jump input
        if (Input.GetButtonDown("Jump"))
        {
            jumpBufferCounter = jumpBufferFrames;

            // Cancel dash if currently dashing
            if (isDashing)
            {
                StopAllCoroutines();
                EndDash();
                Jump();
                StartCoroutine(DashCooldown());
            }
        }

        // Dash input
        FlagCarrierMarker carrierMarker = GetComponent<FlagCarrierMarker>();
        bool isCarryingFlag = carrierMarker != null && carrierMarker.IsCarryingFlag();

        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash && !isCarryingFlag)

        {
            StartCoroutine(Dash(xAxis, yAxis));
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
        anim.SetTrigger("Jump");

        // Consume buffered input and coyote time after a jump
        jumpBufferCounter = 0;
        coyoteCounter = 0;
    }

    private void HandleVariableJumpHeight()
    {
        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }
    }

    // ========== FIX #1: HORIZONTAL-ONLY DASH ==========
    private IEnumerator Dash(float xAxis, float yAxis)
    {
        // Prevent dash if carrying flag
        FlagCarrierMarker carrierMarker = GetComponent<FlagCarrierMarker>();
        if (carrierMarker != null && carrierMarker.IsCarryingFlag())
        {
            Debug.Log("Cannot dash while carrying flag!");
            yield break; // Exit coroutine early
        }


        canDash = false;
        isDashing = true;
        anim.SetTrigger("Dashing");

        originalGravity = rb.gravityScale;
        originalDrag = rb.linearDamping;

        rb.gravityScale = 0f;
        rb.linearDamping = 0f;

        // FORCE HORIZONTAL DASH ONLY - completely ignore yAxis
        Vector2 dashDir = new Vector2(transform.localScale.x, 0); // Default to facing direction

        // If player is pressing left/right, use that direction
        if (xAxis != 0)
        {
            dashDir = new Vector2(xAxis, 0);
        }
        // Note: yAxis is completely ignored - no vertical dashing possible

        rb.linearVelocity = dashDir * stats.dashSpeed;

        yield return new WaitForSeconds(stats.dashTime);

        EndDash();
        StartCoroutine(DashCooldown());
    }

    private void EndDash()
    {
        rb.gravityScale = originalGravity;
        rb.linearDamping = originalDrag;
        isDashing = false;

        Debug.Log("EndDash triggered, gravity restored to: " + rb.gravityScale);
    }

    private IEnumerator DashCooldown()
    {
        if (dashCooldownBar != null)
        {
            dashCooldownBar.gameObject.SetActive(true);
            dashCooldownBar.fillAmount = 1f; // start full
        }

        float elapsed = 0f;
        while (elapsed < stats.dashCooldown)
        {
            elapsed += Time.deltaTime;

            if (dashCooldownBar != null)
            {
                dashCooldownBar.fillAmount = 1f - (elapsed / stats.dashCooldown);
            }

            yield return null;
        }

        canDash = true;

        if (dashCooldownBar != null)
        {
            dashCooldownBar.fillAmount = 1f; // show full = ready
            dashCooldownBar.gameObject.SetActive(false);
        }

        Debug.Log("Dash ready!");
    }

    private void UpdateJumpVariables()
    {
        bool isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (isGrounded)
        {
            coyoteCounter = coyoteTimeFrames;
            remainingAirJumps = stats.maxAirJumps;
        }
        else
        {
            coyoteCounter--;
        }

        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter--;

            if (coyoteCounter > 0)
            {
                Jump();
            }
            else if (remainingAirJumps > 0)
            {
                remainingAirJumps--;
                Jump();
            }
        }
    }
}