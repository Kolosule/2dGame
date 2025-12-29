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
    private Animator anim;
    private FlagCarrierMarker flagCarrierMarker; // NEW: Reference to flag carrier component

    // Jump mechanics
    private int remainingAirJumps;
    private int coyoteTimeCounter;
    private int jumpBufferCounter;
    private bool isJumping;
    private bool isJumpCut;

    // Dash mechanics
    private bool isDashing;
    private bool canDash = true;

    // NEW: Stun mechanics
    private bool isStunned = false;
    private float stunEndTime = 0f;

    // ============================
    // UNITY LIFECYCLE
    // ============================

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();
        flagCarrierMarker = GetComponent<FlagCarrierMarker>(); // NEW: Get flag carrier component

        if (anim == null)
        {
            Debug.LogWarning("PlayerMovement: Animator not found in children! Make sure the Sprite child has an Animator component.");
        }

        if (rb == null)
        {
            Debug.LogError("PlayerMovement: Rigidbody2D is missing!");
        }
    }

    void Update()
    {
        HandleInput();
        UpdateJumpVariables();
        HandleVariableJumpHeight();

        // NEW: Update stun status
        if (isStunned && Time.time >= stunEndTime)
        {
            isStunned = false;
            Debug.Log("Player stun ended");
        }
    }

    void FixedUpdate()
    {
        float xAxis = Input.GetAxisRaw("Horizontal");

        // Don't override velocity if dashing
        if (!isDashing)
        {
            // NEW: Don't move if stunned
            if (isStunned)
            {
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            }
            else
            {
                // Normal movement - FIXED: Use walkSpeed
                float moveSpeed = xAxis * stats.walkSpeed;
                rb.linearVelocity = new Vector2(moveSpeed, rb.linearVelocity.y);
            }
        }

        // Flip sprite based on movement direction (allow during dash)
        if (xAxis < 0)
        {
            transform.localScale = new Vector3(-1, 1, 1);
        }
        else if (xAxis > 0)
        {
            transform.localScale = new Vector3(1, 1, 1);
        }

        // Update walking animation
        if (anim != null)
        {
            anim.SetBool("Walking", xAxis != 0 && !isDashing);
        }
    }

    public void HandleInput()
    {
        // NEW: Don't handle input if stunned
        if (isStunned)
        {
            return;
        }

        float xAxis = Input.GetAxisRaw("Horizontal");

        if (anim != null)
        {
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

                if (rb != null)
                {
                    rb.gravityScale = 5f;
                }

                Jump();
                StartCoroutine(DashCooldown());
            }
        }

        // FIXED: Check if carrying flag before allowing dash
        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash && !isDashing)
        {
            // NEW: Check if carrying flag
            if (flagCarrierMarker != null && flagCarrierMarker.IsCarryingFlag())
            {
                Debug.Log("Cannot dash while carrying flag!");
                return; // Block dash if carrying flag
            }

            StartCoroutine(Dash());
        }

        // NEW: Cancel dash early when releasing shift
        if (Input.GetKeyUp(KeyCode.LeftShift) && isDashing)
        {
            StopAllCoroutines();
            EndDash();

            if (rb != null)
            {
                rb.gravityScale = 5f; // Restore gravity
            }

            StartCoroutine(DashCooldown());
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

            // NEW: Clear stun when grounded
            if (isStunned && Time.time >= stunEndTime)
            {
                isStunned = false;
            }
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
            // NEW: Don't allow jump if stunned
            if (!isStunned)
            {
                Jump();
                jumpBufferCounter = 0;
            }
        }
    }

    private void Jump()
    {
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        if (grounded || coyoteTimeCounter > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            coyoteTimeCounter = 0;
        }
        else if (remainingAirJumps > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            remainingAirJumps--;
        }

        isJumping = true;
        isJumpCut = false;

        if (anim != null)
        {
            anim.SetTrigger("Jump");
        }
    }

    private void HandleVariableJumpHeight()
    {
        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0 && !isJumpCut)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
            isJumpCut = true;
        }

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

        float dashDirection = Mathf.Sign(transform.localScale.x);
        rb.linearVelocity = new Vector2(dashDirection * stats.dashSpeed, 0);

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

            if (dashCooldownBar != null)
            {
                dashCooldownBar.fillAmount = elapsed / stats.dashCooldown;
            }

            yield return null;
        }

        canDash = true;
    }

    // ============================
    // NEW: STUN MECHANICS
    // ============================

    /// <summary>
    /// Stuns the player, preventing dash and jump until grounded
    /// </summary>
    public void ApplyStun(float duration)
    {
        isStunned = true;
        stunEndTime = Time.time + duration;

        // Cancel dash if currently dashing
        if (isDashing)
        {
            StopAllCoroutines();
            EndDash();
            rb.gravityScale = 5f;
            StartCoroutine(DashCooldown());
        }

        Debug.Log($"Player stunned for {duration} seconds");
    }

    /// <summary>
    /// Check if player is currently stunned
    /// </summary>
    public bool IsStunned()
    {
        return isStunned;
    }

    // ============================
    // PUBLIC METHODS
    // ============================

    public bool IsDashing()
    {
        return isDashing;
    }

    public float GetDashCooldownPercent()
    {
        if (canDash) return 1f;
        return Mathf.Clamp01((stats.dashCooldown - GetDashCooldownRemaining()) / stats.dashCooldown);
    }

    public float GetDashCooldownRemaining()
    {
        return canDash ? 0f : stats.dashCooldown;
    }

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