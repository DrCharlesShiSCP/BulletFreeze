using TMPro;
using UnityEngine;
using System.Collections.Generic;

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
    [Header("Elimination")]
    [Tooltip("When enabled, child rigidbodies and colliders are switched into ragdoll mode on elimination.")]
    [SerializeField] private bool enableRagdollOnElimination = true;
    [Tooltip("Impulse applied away from the impact point when the ragdoll activates.")]
    [SerializeField] private float ragdollImpactForce = 8f;
    [Tooltip("Extra upward force added to the ragdoll so deaths feel less flat.")]
    [SerializeField] private float ragdollUpwardForce = 2f;

    [HideInInspector] public PlayerSlot playerSlot;

    private CharacterController characterController;
    private Transform cameraTransform;
    private Vector3 velocity;
    private bool movementAllowed;
    private bool phaseVisible = true;
    private bool isEliminated;
    private bool ragdollActive;
    private Rigidbody[] ragdollBodies;
    private Collider[] ragdollColliders;
    private readonly List<TransformPose> ragdollStartPoses = new List<TransformPose>();
    private Rigidbody rootRigidbody;

    public Vector3 ProjectileSpawnPosition =>
        projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.position + Vector3.up * 1.25f;

    public Camera SharedCameraRef => movementCamera != null ? movementCamera : Camera.main;
    public float MoveSpeed => moveSpeed;
    public bool IsEliminated => isEliminated;

    protected virtual void Awake()
    {
        characterController = GetComponent<CharacterController>();
        rootRigidbody = GetComponent<Rigidbody>();

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
        CacheRagdollParts();
        SetRagdollState(false, Vector3.zero);
        WarnAboutMissingReferences();
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
        SetRagdollState(false, Vector3.zero);

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

    public void SetEliminated(bool eliminated, Vector3 impactPoint)
    {
        if (isEliminated == eliminated)
        {
            ApplyVisibilityState();
            UpdateAnimatorState();
            return;
        }

        isEliminated = eliminated;
        movementAllowed = movementAllowed && !eliminated;

        if (eliminated)
        {
            velocity = Vector3.zero;
            SetRagdollState(true, impactPoint);
        }
        else
        {
            SetRagdollState(false, impactPoint);

            if (characterController != null)
                characterController.enabled = true;
        }

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
        bool visible = ragdollActive || (!isEliminated && phaseVisible);

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

    private void WarnAboutMissingReferences()
    {
        if (characterController == null)
        {
            Debug.LogWarning(
                $"[PlayerController] Missing CharacterController on '{name}'.");
        }

        if (movementCamera == null && Camera.main == null)
        {
            Debug.LogWarning(
                $"[PlayerController] No movement camera assigned for '{name}', and no MainCamera was found.");
        }

        if (enableRagdollOnElimination)
        {
            if (ragdollBodies == null || ragdollBodies.Length == 0)
            {
                Debug.LogWarning(
                    $"[PlayerController] Ragdoll is enabled on '{name}', but no child Rigidbodies were found. " +
                    "Animated bones alone are not enough. Add ragdoll physics to the character prefab.");
            }
            else if (ragdollColliders == null || ragdollColliders.Length == 0)
            {
                Debug.LogWarning(
                    $"[PlayerController] Ragdoll is enabled on '{name}', but no child Colliders were found. " +
                    "Add colliders to the ragdoll bones so they can collide with the world.");
            }
        }
    }

    private void CacheRagdollParts()
    {
        List<Rigidbody> foundBodies = new List<Rigidbody>(GetComponentsInChildren<Rigidbody>(true));
        if (rootRigidbody != null)
            foundBodies.Remove(rootRigidbody);

        ragdollBodies = foundBodies.ToArray();

        List<Collider> foundColliders = new List<Collider>(GetComponentsInChildren<Collider>(true));
        if (characterController != null)
            foundColliders.Remove(characterController);

        ragdollColliders = foundColliders.ToArray();
        ragdollStartPoses.Clear();

        foreach (Rigidbody body in ragdollBodies)
        {
            if (body == null)
                continue;

            ragdollStartPoses.Add(new TransformPose(body.transform));
        }
    }

    private void SetRagdollState(bool enabled, Vector3 impactPoint)
    {
        ragdollActive = enabled && enableRagdollOnElimination && ragdollBodies != null && ragdollBodies.Length > 0;

        if (animator != null)
            animator.enabled = !ragdollActive;

        if (characterController != null)
            characterController.enabled = !enabled;

        if (rootRigidbody != null)
        {
            rootRigidbody.isKinematic = true;
            rootRigidbody.linearVelocity = Vector3.zero;
            rootRigidbody.angularVelocity = Vector3.zero;
        }

        if (ragdollBodies == null || ragdollColliders == null)
            return;

        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body == null)
                continue;

            body.isKinematic = !ragdollActive;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (!ragdollActive && i < ragdollStartPoses.Count)
                ragdollStartPoses[i].ApplyTo(body.transform);
        }

        foreach (Collider ragdollCollider in ragdollColliders)
        {
            if (ragdollCollider != null)
                ragdollCollider.enabled = ragdollActive;
        }

        if (ragdollActive)
            ApplyRagdollImpactForce(impactPoint);
    }

    private void ApplyRagdollImpactForce(Vector3 impactPoint)
    {
        Vector3 origin = impactPoint;
        bool hasImpactPoint = impactPoint.sqrMagnitude > 0.001f;

        foreach (Rigidbody body in ragdollBodies)
        {
            if (body == null)
                continue;

            Vector3 direction =
                hasImpactPoint
                    ? (body.worldCenterOfMass - origin).normalized
                    : transform.forward;

            if (direction.sqrMagnitude < 0.001f)
                direction = transform.forward.sqrMagnitude > 0.001f ? transform.forward : Vector3.up;

            direction += Vector3.up * ragdollUpwardForce;
            body.AddForce(direction.normalized * ragdollImpactForce, ForceMode.Impulse);
        }
    }

    private readonly struct TransformPose
    {
        private readonly Transform target;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;

        public TransformPose(Transform transform)
        {
            target = transform;
            localPosition = transform.localPosition;
            localRotation = transform.localRotation;
        }

        public void ApplyTo(Transform transform)
        {
            if (transform == null || transform != target)
                return;

            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }
    }
}
