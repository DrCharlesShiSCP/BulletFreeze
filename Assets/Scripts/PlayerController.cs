using TMPro;
using UnityEngine;

// Shared movement and presentation controller for a spawned player avatar.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Horizontal movement speed while the active phase allows movement.")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("How quickly the avatar rotates toward its move direction.")]
    [SerializeField] private float rotationLerpSpeed = 12f;
    [Tooltip("Gravity applied while the character controller is active.")]
    [SerializeField] private float gravity = -25f;
    [Tooltip("Small downward force to keep the controller grounded.")]
    [SerializeField] private float groundedVelocity = -2f;

    [Header("References")]
    [Tooltip("Optional animator using the package's IsGameplay / IsGrounded / IsRunning parameters.")]
    [SerializeField] private Animator animator;
    [Tooltip("Optional root used to collect renderers when none are assigned manually.")]
    [SerializeField] private Transform visualsRoot;
    [Tooltip("Optional point used when spawning projectiles.")]
    [SerializeField] private Transform projectileSpawnPoint;
    [Tooltip("Optional shared camera used for camera-relative movement.")]
    [SerializeField] private Camera movementCamera;
    [Tooltip("Optional manually assigned renderers to hide during countdown or elimination.")]
    [SerializeField] private Renderer[] renderersToToggle;
    [Tooltip("Optional floating text for the player name line.")]
    [SerializeField] private TMP_Text line1;
    [Tooltip("Optional floating text for the player status line.")]
    [SerializeField] private TMP_Text line2;

    [HideInInspector] public PlayerSlot playerSlot;

    private CharacterController characterController;
    private Transform cameraTransform;
    private Vector3 velocity;
    private bool movementAllowed;
    private bool phaseVisible = true;
    private bool isEliminated;

    public Vector3 ProjectileSpawnPosition =>
        projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.position + Vector3.up * 1.25f;

    public float MoveSpeed => moveSpeed;
    public bool IsEliminated => isEliminated;

    protected virtual void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (line1 == null)
        {
            Transform line1Transform = transform.Find("CharacterName");
            if (line1Transform != null)
                line1 = line1Transform.GetComponent<TMP_Text>();
        }

        if (line2 == null)
        {
            Transform line2Transform = transform.Find("PlayerReadyStatus");
            if (line2Transform != null)
                line2 = line2Transform.GetComponent<TMP_Text>();
        }

        if (renderersToToggle == null || renderersToToggle.Length == 0)
        {
            Transform renderRoot = visualsRoot != null ? visualsRoot : transform;
            renderersToToggle = renderRoot.GetComponentsInChildren<Renderer>(true);
        }

        RefreshCameraReference();
    }

    protected virtual void Update()
    {
        RefreshCameraReference();
        UpdateAnimatorState();

        if (isEliminated || playerSlot == null || !playerSlot.IsAlive)
            return;

        if (!movementAllowed || !characterController.enabled)
        {
            if (animator != null)
                animator.SetBool("IsRunning", false);

            ApplyGravity();
            return;
        }

        HandleMovement();
        ApplyGravity();
    }

    public virtual void Initialize(PlayerSlot slot, Camera sharedCamera)
    {
        playerSlot = slot;
        movementCamera = sharedCamera != null ? sharedCamera : movementCamera;
        RefreshCameraReference();

        SetLine1(slot.DisplayName);
        SetLine2(slot.InputLabel);
        ApplyVisibilityState();
    }

    public void ResetToSpawn(Vector3 position, Quaternion rotation)
    {
        velocity = Vector3.zero;
        movementAllowed = false;
        isEliminated = false;
        phaseVisible = true;

        if (characterController != null)
            characterController.enabled = false;

        transform.SetPositionAndRotation(position, rotation);

        if (characterController != null)
            characterController.enabled = true;

        ApplyVisibilityState();
        UpdateAnimatorState();
    }

    public void SetMovementAllowed(bool allowed)
    {
        movementAllowed = allowed && !isEliminated;

        if (!movementAllowed && animator != null)
            animator.SetBool("IsRunning", false);
    }

    public void SetPhaseVisible(bool visible)
    {
        phaseVisible = visible;
        ApplyVisibilityState();
    }

    public void SetEliminated(bool eliminated)
    {
        isEliminated = eliminated;
        movementAllowed = movementAllowed && !eliminated;

        if (characterController != null && characterController.enabled == eliminated)
            characterController.enabled = !eliminated;

        if (eliminated)
            velocity = Vector3.zero;

        ApplyVisibilityState();
        UpdateAnimatorState();
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

    private void HandleMovement()
    {
        if (cameraTransform == null)
            return;

        Vector2 moveInput = playerSlot.ReadMoveInput();

        if (moveInput.sqrMagnitude < 0.001f)
        {
            if (animator != null)
                animator.SetBool("IsRunning", false);
            return;
        }

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = cameraTransform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 worldMove = forward * moveInput.y + right * moveInput.x;
        characterController.Move(worldMove * moveSpeed * Time.deltaTime);

        if (worldMove.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(worldMove, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationLerpSpeed * Time.deltaTime);
        }

        if (animator != null)
            animator.SetBool("IsRunning", true);
    }

    private void ApplyGravity()
    {
        if (characterController == null || !characterController.enabled)
            return;

        if (characterController.isGrounded && velocity.y < 0f)
            velocity.y = groundedVelocity;

        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void RefreshCameraReference()
    {
        if (movementCamera == null && Camera.main != null)
            movementCamera = Camera.main;

        cameraTransform = movementCamera != null ? movementCamera.transform : null;
    }

    private void ApplyVisibilityState()
    {
        bool visible = phaseVisible && !isEliminated;

        if (renderersToToggle == null)
            return;

        foreach (Renderer rendererToToggle in renderersToToggle)
        {
            if (rendererToToggle != null)
                rendererToToggle.enabled = visible;
        }
    }

    private void UpdateAnimatorState()
    {
        if (animator == null)
            return;

        animator.SetBool(
            "IsGameplay",
            GamePhaseManager.Instance != null &&
            GamePhaseManager.Instance.CurrentPhase != GamePhaseType.None &&
            GamePhaseManager.Instance.CurrentPhase != GamePhaseType.MatchOver);
        animator.SetBool(
            "IsGrounded",
            characterController != null &&
            characterController.enabled &&
            characterController.isGrounded);

        if (isEliminated || !movementAllowed)
            animator.SetBool("IsRunning", false);
    }
}
