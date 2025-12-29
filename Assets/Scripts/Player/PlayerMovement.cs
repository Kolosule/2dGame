using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles all player movement including walking, jumping, dashing, and physics.
/// FIXED VERSION - Corrected DashCooldown coroutine
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
    private float dashCooldownTimer = 0f;

    // Jump state
    private int remainingAirJumps;
    private int coyoteCounter;
    private int jumpBufferCounter;

    // Public getters for UI
    public float GetDashCooldownPercent()
    {
        if (canDash) return 1f;
        return Mathf.Clamp01(dashCooldownTimer / stats.dashCooldown);
    }

    public float GetDashCooldownRemaining()
    {
        return Mathf.Max(0f, stats.dashCooldown - dashCooldownTimer);
    }

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

        // Flip sprite to face movement direction (FIXED - preserves original scale)
        if (xAxis != 0)
        {
            Vector3 currentScale = transform.localScale;
            currentScale.x = Mathf.Abs(currentScale.x) * Mathf.Sign(xAxis);
            transform.localScale = currentScale;
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

    /// <summary>
    /// FIXED - Dash cooldown coroutine with proper UI updates
    /// Removed duplicate logic and simplified the cooldown tracking
    /// </summary>
    private IEnumerator DashCooldown()
    {
        // Show cooldown bar (dash just used)
        if (dashCooldownBar != null)
        {
            dashCooldownBar.gameObject.SetActive(true);
            dashCooldownBar.fillAmount = 0f; // Start empty since dash was just used
        }

        dashCooldownTimer = 0f;

        // Count up from 0 to dashCooldown duration
        while (dashCooldownTimer < stats.dashCooldown)
        {
            dashCooldownTimer += Time.deltaTime;

            // Update UI - fill gradually as cooldown progresses
            if (dashCooldownBar != null)
            {
                // fillAmount goes from 0 (just dashed) to 1 (ready again)
                dashCooldownBar.fillAmount = dashCooldownTimer / stats.dashCooldown;
            }

            yield return null;
        }

        // Cooldown complete
        canDash = true;
        dashCooldownTimer = stats.dashCooldown;

        // Hide cooldown bar (dash is ready)
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

        // Reset air jumps when grounded
        if (isGrounded)
        {
            remainingAirJumps = stats.maxAirJumps;
        }

        // Update coyote time counter
        if (isGrounded)
        {
            coyoteCounter = coyoteTimeFrames;
        }
        else
        {
            coyoteCounter--;
        }

        // Decrement jump buffer counter
        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter--;
        }

        // Execute jump if conditions are met
        bool hasJumpInput = jumpBufferCounter > 0;
        bool canCoyoteJump = coyoteCounter > 0;
        bool canAirJump = remainingAirJumps > 0;

        if (hasJumpInput && (canCoyoteJump || canAirJump))
        {
            Jump();

            // Consume air jump if not coyote jumping
            if (!canCoyoteJump)
            {
                remainingAirJumps--;
            }
        }
    }

    /// <summary>
    /// Visualize ground check position in editor
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}