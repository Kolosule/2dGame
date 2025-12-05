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
        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash)
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

    private IEnumerator Dash(float xAxis, float yAxis)
    {
        // Block pure vertical input
        if (xAxis == 0 && yAxis != 0)
        {
            EndDash();
            yield break;
        }

        canDash = false;
        isDashing = true;
        anim.SetTrigger("Dashing");

        originalGravity = rb.gravityScale;
        originalDrag = rb.linearDamping;

        rb.gravityScale = 0f;
        rb.linearDamping = 0f;

        Vector2 dashDir = new Vector2(transform.localScale.x, 0);
        if (xAxis != 0 || yAxis != 0)
        {
            dashDir = new Vector2(xAxis, yAxis).normalized;
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
                dashCooldownBar.fillAmount = 1f - (elapsed / stats.dashCooldown); // shrink
            yield return null;
        }

        canDash = true;

        if (dashCooldownBar != null)
        {
            dashCooldownBar.fillAmount = 1f; // instantly refill when ready
            dashCooldownBar.gameObject.SetActive(false); // hide if you prefer
        }
    }

    private bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    private void UpdateJumpVariables()
    {
        bool grounded = IsGrounded();

        if (grounded)
        {
            remainingAirJumps = stats.maxAirJumps;
            coyoteCounter = coyoteTimeFrames;

            if (jumpBufferCounter > 0)
            {
                Jump();
            }
        }
        else
        {
            if (coyoteCounter > 0) coyoteCounter--;
        }

        if (jumpBufferCounter > 0)
        {
            if (!grounded && coyoteCounter > 0)
            {
                Jump();
            }
            else if (!grounded && coyoteCounter <= 0 && remainingAirJumps > 0)
            {
                remainingAirJumps--;
                Jump();
            }
        }

        if (jumpBufferCounter > 0) jumpBufferCounter--;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}