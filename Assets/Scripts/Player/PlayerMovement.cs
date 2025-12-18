using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles all player movement including walking, jumping, dashing, and physics.
/// Cleaned up version with better organization and comments.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Image dashCooldownBar; // Optional UI element for dash cooldown

    [Header("Ground Detection")]
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Jump Settings")]
    [SerializeField] private int coyoteTimeFrames = 6;   // Frames of grace time after leaving platform (~0.1s at 60fps)
    [SerializeField] private int jumpBufferFrames = 6;   // Frames to remember jump input (~0.1s at 60fps)
    [SerializeField] private float jumpCutMultiplier = 0.5f; // Multiplier for releasing jump early (short hop)

    // Component references
    private Rigidbody2D rb;
    private Animator anim;

    // Dash state
    private bool canDash = true;
    private bool isDashing = false;
    private float originalGravity;
    private float originalDrag;

    // Jump state
    private int remainingAirJumps;
    private int coyoteCounter;
    private int jumpBufferCounter;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        remainingAirJumps = stats.maxAirJumps;

        if (dashCooldownBar != null)
            dashCooldownBar.fillAmount = 1f; // Full = dash ready
    }

    void Update()
    {
        HandleInput();
        UpdateJumpVariables();
        HandleVariableJumpHeight();

        // Allow canceling dash by releasing Shift
        if (isDashing && Input.GetKeyUp(KeyCode.LeftShift))
        {
            StopAllCoroutines();
            EndDash();
            StartCoroutine(DashCooldown());
        }
    }

    public void HandleInput()
    {
        float xAxis = Input.GetAxisRaw("Horizontal");

        // Flip sprite to face movement direction
        if (xAxis != 0)
        {
            transform.localScale = new Vector2(Mathf.Sign(xAxis), 1);
        }

        // Apply movement (unless dashing)
        if (!isDashing)
        {
            rb.linearVelocity = new Vector2(xAxis * stats.walkSpeed, rb.linearVelocity.y);
            anim.SetBool("Walking", xAxis != 0);
        }

        // Buffer jump input for responsive controls
        if (Input.GetButtonDown("Jump"))
        {
            jumpBufferCounter = jumpBufferFrames;

            // Cancel dash and jump immediately if dashing
            if (isDashing)
            {
                StopAllCoroutines();
                EndDash();
                Jump();
                StartCoroutine(DashCooldown());
            }
        }

        // Handle dash input
        FlagCarrierMarker carrierMarker = GetComponent<FlagCarrierMarker>();
        bool isCarryingFlag = carrierMarker != null && carrierMarker.IsCarryingFlag();

        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash && !isCarryingFlag)
        {
            StartCoroutine(Dash(xAxis));
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
        anim.SetTrigger("Jump");

        // Consume buffered input and coyote time after jumping
        jumpBufferCounter = 0;
        coyoteCounter = 0;
    }

    /// <summary>
    /// Allows variable jump height by cutting velocity when jump button is released.
    /// </summary>
    private void HandleVariableJumpHeight()
    {
        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }
    }

    /// <summary>
    /// Horizontal-only dash with instant start and smooth physics.
    /// </summary>
    private IEnumerator Dash(float xAxis)
    {
        // Double-check flag carrier status
        FlagCarrierMarker carrierMarker = GetComponent<FlagCarrierMarker>();
        if (carrierMarker != null && carrierMarker.IsCarryingFlag())
        {
            yield break;
        }

        canDash = false;
        isDashing = true;
        anim.SetTrigger("Dashing");

        // Store original physics values
        originalGravity = rb.gravityScale;
        originalDrag = rb.linearDamping;

        // Disable gravity and drag during dash
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;

        // Calculate dash direction (horizontal only)
        Vector2 dashDir = new Vector2(transform.localScale.x, 0); // Default to facing direction
        if (xAxis != 0)
        {
            dashDir = new Vector2(xAxis, 0); // Use input if pressing a direction
        }

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
    }

    private IEnumerator DashCooldown()
    {
        if (dashCooldownBar != null)
        {
            dashCooldownBar.gameObject.SetActive(true);
            dashCooldownBar.fillAmount = 1f;
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
            dashCooldownBar.fillAmount = 1f;
            dashCooldownBar.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Updates coyote time, jump buffer, and handles jump logic.
    /// </summary>
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

        // Process buffered jump input
        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter--;

            // Ground jump (with coyote time)
            if (coyoteCounter > 0)
            {
                Jump();
            }
            // Air jump
            else if (remainingAirJumps > 0)
            {
                remainingAirJumps--;
                Jump();
            }
        }
    }
}