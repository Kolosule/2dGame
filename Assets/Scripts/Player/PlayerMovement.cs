using System.Collections;
using UnityEngine;

/// <summary>
/// Handles all player movement including walking, jumping, dashing, and physics.
/// CLEAN VERSION - UI management removed, only exposes data through public methods
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private Transform groundCheck;

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

    // ============================
    // PUBLIC API FOR UI
    // ============================

    /// <summary>
    /// Returns dash cooldown as a percentage (0 = just used, 1 = ready)
    /// </summary>
    public float GetDashCooldownPercent()
    {
        if (canDash) return 1f;
        return Mathf.Clamp01(dashCooldownTimer / stats.dashCooldown);
    }

    /// <summary>
    /// Returns remaining cooldown time in seconds
    /// </summary>
    public float GetDashCooldownRemaining()
    {
        return Mathf.Max(0f, stats.dashCooldown - dashCooldownTimer);
    }

    /// <summary>
    /// Returns whether dash is ready to use
    /// </summary>
    public bool CanDash()
    {
        return canDash;
    }

    /// <summary>
    /// Returns whether player is currently dashing
    /// </summary>
    public bool IsDashing()
    {
        return isDashing;
    }

    // ============================
    // UNITY LIFECYCLE
    // ============================

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        remainingAirJumps = stats.maxAirJumps;
    }

    void Update()
    {
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

    void FixedUpdate()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        anim.SetBool("Grounded", grounded);
    }

    // ============================
    // INPUT HANDLING
    // ============================

    /// <summary>
    /// Called by PlayerController to handle movement input
    /// </summary>
    public void HandleInput()
    {
        float xAxis = Input.GetAxisRaw("Horizontal");

        // Flip sprite to face movement direction
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

    // ============================
    // JUMP LOGIC
    // ============================

    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
        anim.SetTrigger("Jump");

        // Consume buffered input and coyote time after jumping
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

    private void UpdateJumpVariables()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Reset air jumps when grounded
        if (grounded)
        {
            remainingAirJumps = stats.maxAirJumps;
        }

        // Update coyote time (grace period after leaving ground)
        if (grounded)
        {
            coyoteCounter = coyoteTimeFrames;
        }
        else if (coyoteCounter > 0)
        {
            coyoteCounter--;
        }

        // Update jump buffer (remember jump input briefly)
        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter--;
        }

        // Execute jump if conditions are met
        bool canGroundJump = grounded || coyoteCounter > 0;
        bool canAirJump = !grounded && remainingAirJumps > 0;

        if (jumpBufferCounter > 0 && (canGroundJump || canAirJump))
        {
            Jump();

            // Consume air jump if used
            if (!grounded && coyoteCounter <= 0)
            {
                remainingAirJumps--;
            }
        }
    }

    // ============================
    // DASH LOGIC
    // ============================

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
    /// Dash cooldown - UI reads state via public methods
    /// </summary>
    private IEnumerator DashCooldown()
    {
        dashCooldownTimer = 0f;

        // Count up from 0 to dashCooldown duration
        while (dashCooldownTimer < stats.dashCooldown)
        {
            dashCooldownTimer += Time.deltaTime;
            yield return null;
        }

        // Cooldown complete
        canDash = true;
        dashCooldownTimer = stats.dashCooldown;
    }

    // ============================
    // DEBUG
    // ============================

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}