using UnityEngine;
using System.Collections;

public class PlayerMovement : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Jump Settings")]
    [SerializeField] private int coyoteTimeFrames = 6;
    [SerializeField] private int jumpBufferFrames = 6;
    [SerializeField] private float jumpCutMultiplier = 0.1f;

    [Header("UI References")]
    [SerializeField] private UnityEngine.UI.Image dashCooldownBar;

    // Component references
    private Rigidbody2D rb;
    private Animator anim; // NOW REFERENCES CHILD OBJECT

    // Jump mechanics
    private int remainingAirJumps;
    private int coyoteTimeCounter;
    private int jumpBufferCounter;
    private bool isJumping;
    private bool isJumpCut;

    // Dash mechanics
    private bool isDashing;
    private bool canDash = true;

    // ============================
    // UNITY LIFECYCLE
    // ============================

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // UPDATED: Get Animator from child object instead of parent
        anim = GetComponentInChildren<Animator>();

        if (anim == null)
        {
            Debug.LogWarning("PlayerMovement: Animator not found in children! Make sure the Sprite child has an Animator component.");
        }

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

            // IMPORTANT: Restore gravity when cancelling dash
            if (rb != null)
            {
                rb.gravityScale = 5f; // Restore default gravity
            }

            EndDash();
            StartCoroutine(DashCooldown());
        }
    }

    void FixedUpdate()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Check if animator exists before setting parameters
        if (anim != null)
        {
            anim.SetBool("Grounded", grounded);
        }
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

            // Check if animator exists before setting parameters
            if (anim != null)
            {
                anim.SetBool("Walking", xAxis != 0);
            }
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

                // IMPORTANT: Restore gravity before jumping
                if (rb != null)
                {
                    rb.gravityScale = 5f; // Restore default gravity
                }

                Jump();
                StartCoroutine(DashCooldown());
            }
        }

        // Handle dash input
        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash && !isDashing)
        {
            StartCoroutine(Dash());
        }
    }

    // ============================
    // JUMP MECHANICS
    // ============================

    private void UpdateJumpVariables()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Coyote time: grace period after leaving ground
        if (grounded)
        {
            coyoteTimeCounter = coyoteTimeFrames;
            remainingAirJumps = stats.maxAirJumps;
        }
        else
        {
            coyoteTimeCounter--;
        }

        // Jump buffer countdown
        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter--;
        }

        // Execute buffered jump if conditions are met
        if (jumpBufferCounter > 0 && (coyoteTimeCounter > 0 || remainingAirJumps > 0))
        {
            Jump();
            jumpBufferCounter = 0;
        }
    }

    private void Jump()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (grounded || coyoteTimeCounter > 0)
        {
            // Ground or coyote time jump
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            coyoteTimeCounter = 0;
        }
        else if (remainingAirJumps > 0)
        {
            // Air jump
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            remainingAirJumps--;
        }

        isJumping = true;
        isJumpCut = false;

        // Check if animator exists before setting parameters
        if (anim != null)
        {
            anim.SetTrigger("Jump");
        }
    }

    private void HandleVariableJumpHeight()
    {
        // Cut jump short if button released early
        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0 && !isJumpCut)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
            isJumpCut = true;
        }

        // Reset jump state when landing
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        if (grounded && isJumping)
        {
            isJumping = false;
        }
    }

    // ============================
    // DASH MECHANICS
    // ============================

    private IEnumerator Dash()
    {
        canDash = false;
        isDashing = true;

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0;

        // Dash in facing direction
        float dashDirection = Mathf.Sign(transform.localScale.x);
        rb.linearVelocity = new Vector2(dashDirection * stats.dashSpeed, 0);

        // Check if animator exists before setting parameters
        if (anim != null)
        {
            anim.SetBool("Dashing", true);
        }

        yield return new WaitForSeconds(stats.dashTime);

        EndDash();
        rb.gravityScale = originalGravity;

        yield return StartCoroutine(DashCooldown());
    }

    private void EndDash()
    {
        isDashing = false;

        // Check if animator exists before setting parameters
        if (anim != null)
        {
            anim.SetBool("Dashing", false);
        }
    }

    private IEnumerator DashCooldown()
    {
        float elapsed = 0;

        while (elapsed < stats.dashCooldown)
        {
            elapsed += Time.deltaTime;

            // Update UI if dash cooldown bar exists
            if (dashCooldownBar != null)
            {
                dashCooldownBar.fillAmount = elapsed / stats.dashCooldown;
            }

            yield return null;
        }

        canDash = true;
    }

    // ============================
    // PUBLIC METHODS
    // ============================

    public bool IsDashing()
    {
        return isDashing;
    }

    /// <summary>
    /// Returns dash cooldown as a percentage (0 = just used, 1 = ready)
    /// </summary>
    public float GetDashCooldownPercent()
    {
        if (canDash) return 1f;
        return Mathf.Clamp01((stats.dashCooldown - GetDashCooldownRemaining()) / stats.dashCooldown);
    }

    /// <summary>
    /// Returns remaining cooldown time in seconds
    /// </summary>
    public float GetDashCooldownRemaining()
    {
        return canDash ? 0f : stats.dashCooldown;
    }

    /// <summary>
    /// Returns whether dash is ready to use
    /// </summary>
    public bool CanDash()
    {
        return canDash;
    }

    // ============================
    // DEBUG
    // ============================

    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}