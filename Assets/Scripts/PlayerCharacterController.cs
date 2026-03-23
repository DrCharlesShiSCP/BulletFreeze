using UnityEngine;
using TMPro;

// Gameplay and lobby controller for player avatar
[RequireComponent(typeof(CharacterController))]
public class PlayerCharacterController : MonoBehaviour
{
    public PlayerSlot playerSlot;
    public Animator animator;

    public float moveSpeed = 4f;
    public float jumpForce = 6f;
    public float gravity = -20f;

    private CharacterController characterController;
    private Vector3 velocity;

    private bool isJumping = false;
    private bool isPunching = false;

    private TMP_Text line1;
    private TMP_Text line2;

    private Transform camTransform;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        if (Camera.main != null)
            camTransform = Camera.main.transform;

        Transform nameObj = transform.Find("CharacterName");
        Transform statusObj = transform.Find("PlayerReadyStatus");

        if (nameObj != null)
            line1 = nameObj.GetComponent<TMP_Text>();

        if (statusObj != null)
            line2 = statusObj.GetComponent<TMP_Text>();
    }

    private void Update()
    {
        if (playerSlot == null || playerSlot.gamepad == null)
            return;

        if (!GameManager.Instance.gameStarted)
        {
            animator.SetBool("IsGameplay", false);
            return;
        }

        animator.SetBool("IsGameplay", true);
        animator.SetBool("IsGrounded", characterController.isGrounded);

        HandleMovement();
        HandleActions();
        ApplyGravity();
    }

    private void HandleMovement()
    {
        if (isPunching)
            return;

        if (camTransform == null)
            return;

        Vector2 moveInput =
            playerSlot.gamepad.leftStick.ReadValue();

        if (moveInput.sqrMagnitude < 0.01f)
        {
            animator.SetBool("IsRunning", false);
            return;
        }

        Vector3 camForward = camTransform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = camTransform.right;
        camRight.y = 0f;
        camRight.Normalize();

        Vector3 move =
            camForward * moveInput.y +
            camRight * moveInput.x;

        characterController.Move(move * moveSpeed * Time.deltaTime);

        if (move != Vector3.zero)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(move, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                10f * Time.deltaTime);
        }

        animator.SetBool("IsRunning", true);
    }

    private void HandleActions()
    {
        var pad = playerSlot.gamepad;

        if (pad == null)
            return;

        if (pad.buttonSouth.wasPressedThisFrame && !isJumping)
        {
            Jump();
        }
    }

    private void Jump()
    {
        isJumping = true;
        animator.SetTrigger("Jump");

        velocity.y = jumpForce;
    }

    private void Punch()
    {
        isPunching = true;
        animator.SetTrigger("Punch");

        animator.SetBool("IsRunning", false);
    }

    private void ApplyGravity()
    {
        if (characterController.isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
            isJumping = false;
            isPunching = false;
        }

        velocity.y += gravity * Time.deltaTime;

        characterController.Move(velocity * Time.deltaTime);
    }

    public void SetLine1(string text)
    {
        if (line1 != null)
            line1.text = text;
    }

    public void SetLine2(string text)
    {
        if (line2 != null)
            line2.text = text;
    }
}