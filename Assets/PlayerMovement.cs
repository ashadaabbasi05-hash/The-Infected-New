using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float maxSpeed = 5f;
    public float acceleration = 30f;
    public float deceleration = 40f;
    public float friction = 5f;
    public float sprintMultiplier = 1.5f;
    public float dashSpeed = 14f;
    public float dashDuration = 0.12f;
    public float dashCooldown = 0.7f;
    public Joystick joystick;
    public float inputSmoothing = 15f;

    Rigidbody2D rb;
    PlayerIdentity identity;

    Vector2 input;
    Vector2 smoothedInput;
    bool sprint;

    bool isDashing;
    float dashTimer;
    float dashCooldownTimer;
    Vector2 dashDir;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        identity = GetComponent<PlayerIdentity>();
    }

    bool IsMovementBlocked()
    {
        return identity != null && identity.isFrozen;
    }

    void Update()
    {
        if (IsMovementBlocked())
        {
            input = Vector2.zero;
            smoothedInput = Vector2.zero;
            sprint = false;
            return;
        }

        ReadInput();
        HandleDashTimers();
        if ((Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump")) && dashCooldownTimer <= 0f && input.sqrMagnitude > 0.01f)
        {
            StartDash(input.normalized);
        }
    }

    void FixedUpdate()
    {
        if (IsMovementBlocked())
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            isDashing = false;
            return;
        }

        if (isDashing)
        {
            rb.linearVelocity = dashDir * dashSpeed;
            return;
        }

        float targetSpeed = maxSpeed * (sprint ? sprintMultiplier : 1f);
        Vector2 targetVel = input * targetSpeed;

        if (input.sqrMagnitude > 0.0001f)
        {
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, targetVel, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero, deceleration * Time.fixedDeltaTime);
            if (rb.linearVelocity.magnitude < 0.01f) rb.linearVelocity = Vector2.zero;
        }

        if (input.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, Mathf.Clamp01(friction * Time.fixedDeltaTime));
        }
    }

    void ReadInput()
    {
        float h = 0f;
        float v = 0f;

        if (joystick != null)
        {
            h = joystick.Horizontal;
            v = joystick.Vertical;
        }
        else
        {
            h = GetKeyboardHorizontal();
            v = GetKeyboardVertical();
        }

        Vector2 targetInput = new Vector2(h, v);

        if (targetInput.sqrMagnitude > 1f)
        {
            targetInput.Normalize();
        }

        smoothedInput = Vector2.MoveTowards(smoothedInput, targetInput, inputSmoothing * Time.deltaTime);
        input = smoothedInput;

        sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetButton("Fire3");
    }

    float GetKeyboardHorizontal()
    {
        float horizontal = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;

        return horizontal;
    }

    float GetKeyboardVertical()
    {
        float vertical = 0f;

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;

        return vertical;
    }

    void StartDash(Vector2 direction)
    {
        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        dashDir = direction.normalized;
    }

    void HandleDashTimers()
    {
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
        if (!isDashing) return;
        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            isDashing = false;
        }
    }
}