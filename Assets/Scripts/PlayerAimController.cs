using UnityEngine;

// Handles per-player target selection during the aim phase.
public class PlayerAimController : MonoBehaviour
{
    [Header("Aim Settings")]
    [Tooltip("How quickly this player's target marker moves across the arena.")]
    [SerializeField] private float crosshairMoveSpeed = 8f;
    [Tooltip("Starting offset from the player when a new aim phase begins.")]
    [SerializeField] private Vector3 initialOffset = new Vector3(0f, 0f, 4f);
    [Tooltip("Height of the target marker above the arena floor.")]
    [SerializeField] private float markerHeight = 0.15f;
    [Tooltip("Fallback arena height used when no arena bounds collider is assigned.")]
    [SerializeField] private float groundPlaneHeight = 0f;
    [Tooltip("Optional arena bounds used to clamp target selection.")]
    [SerializeField] private Collider arenaBounds;
    [Tooltip("Optional prefab used for the target marker.")]
    [SerializeField] private GameObject crosshairPrefab;
    [Tooltip("Fallback scale used when the controller creates a primitive marker.")]
    [SerializeField] private Vector3 fallbackMarkerScale = new Vector3(0.55f, 0.12f, 0.55f);

    private PlayerSlot playerSlot;
    private PlayerController playerController;
    private GameObject crosshairInstance;
    private Renderer[] crosshairRenderers;
    private bool aimActive;
    private bool hasConfirmed;
    private Vector3 currentTargetPoint;

    public Vector3 CurrentTargetPoint => currentTargetPoint;
    public bool HasConfirmed => hasConfirmed;

    public void Initialize(
        PlayerSlot slot,
        PlayerController controller,
        Collider bounds,
        GameObject sharedCrosshairPrefab)
    {
        playerSlot = slot;
        playerController = controller;
        arenaBounds = bounds != null ? bounds : arenaBounds;
        crosshairPrefab = sharedCrosshairPrefab != null ? sharedCrosshairPrefab : crosshairPrefab;

        EnsureMarker();
        ResetForNextRound();
        SetAimActive(false);
    }

    private void Update()
    {
        if (!aimActive || hasConfirmed || playerSlot == null || !playerSlot.IsAlive)
            return;

        Vector2 aimInput = playerSlot.ReadAimInput();

        if (aimInput.sqrMagnitude > 0.001f)
        {
            currentTargetPoint +=
                new Vector3(aimInput.x, 0f, aimInput.y) *
                crosshairMoveSpeed *
                Time.deltaTime;

            currentTargetPoint = ClampToArena(currentTargetPoint);
            UpdateMarkerTransform();
        }

        if (playerSlot.WasConfirmPressedThisFrame())
            ConfirmCurrentTarget(false);
    }

    public void ResetForNextRound()
    {
        hasConfirmed = false;
        playerSlot?.ClearConfirmedTarget();

        if (playerController != null)
        {
            Vector3 forward = playerController.transform.forward;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Vector3 offset =
                playerController.transform.right * initialOffset.x +
                Vector3.up * initialOffset.y +
                forward.normalized * initialOffset.z;

            currentTargetPoint = playerController.transform.position + offset;
        }
        else
        {
            currentTargetPoint = transform.position + initialOffset;
        }

        currentTargetPoint = ClampToArena(currentTargetPoint);
        UpdateMarkerAppearance();
        UpdateMarkerTransform();

        if (playerController != null && playerSlot != null && playerSlot.IsAlive)
            playerController.SetLine2("Aiming...");
    }

    public void SetAimActive(bool active)
    {
        aimActive = active && playerSlot != null && playerSlot.IsAlive;
        EnsureMarker();
        SetMarkerVisible(aimActive);
    }

    public void AutoConfirmIfNeeded()
    {
        if (!hasConfirmed)
            ConfirmCurrentTarget(true);
    }

    private void ConfirmCurrentTarget(bool wasAutoConfirmed)
    {
        hasConfirmed = true;
        playerSlot?.ConfirmTarget(currentTargetPoint);
        UpdateMarkerAppearance();

        if (playerController != null)
            playerController.SetLine2(wasAutoConfirmed ? "Auto-Locked" : "Locked");

        if (playerSlot != null)
        {
            Debug.Log(
                $"[PlayerAimController] {playerSlot.DisplayName} confirmed target " +
                $"{currentTargetPoint} {(wasAutoConfirmed ? "(auto)" : string.Empty)}");
        }
    }

    private void EnsureMarker()
    {
        if (crosshairInstance != null)
            return;

        if (crosshairPrefab != null)
        {
            crosshairInstance = Instantiate(crosshairPrefab);
        }
        else
        {
            crosshairInstance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crosshairInstance.name = "GeneratedCrosshair";
            crosshairInstance.transform.localScale = fallbackMarkerScale;

            Collider primitiveCollider = crosshairInstance.GetComponent<Collider>();
            if (primitiveCollider != null)
                Destroy(primitiveCollider);
        }

        crosshairInstance.name = $"Crosshair_{(playerSlot != null ? playerSlot.DisplayName : name)}";
        crosshairRenderers = crosshairInstance.GetComponentsInChildren<Renderer>(true);
        UpdateMarkerAppearance();
        UpdateMarkerTransform();
    }

    private void UpdateMarkerTransform()
    {
        if (crosshairInstance == null)
            return;

        crosshairInstance.transform.position = currentTargetPoint;
    }

    private void UpdateMarkerAppearance()
    {
        if (crosshairRenderers == null)
            return;

        Color playerColor = GetPlayerColor();
        Color markerColor = hasConfirmed ? Color.Lerp(playerColor, Color.white, 0.45f) : playerColor;

        foreach (Renderer markerRenderer in crosshairRenderers)
        {
            if (markerRenderer == null)
                continue;

            markerRenderer.material.color = markerColor;
        }
    }

    private void SetMarkerVisible(bool visible)
    {
        if (crosshairRenderers == null)
            return;

        foreach (Renderer markerRenderer in crosshairRenderers)
        {
            if (markerRenderer != null)
                markerRenderer.enabled = visible;
        }
    }

    private Vector3 ClampToArena(Vector3 point)
    {
        if (arenaBounds != null)
        {
            Bounds bounds = arenaBounds.bounds;
            point.x = Mathf.Clamp(point.x, bounds.min.x, bounds.max.x);
            point.z = Mathf.Clamp(point.z, bounds.min.z, bounds.max.z);
            point.y = bounds.max.y + markerHeight;
            return point;
        }

        point.y = groundPlaneHeight + markerHeight;
        return point;
    }

    private Color GetPlayerColor()
    {
        if (playerSlot == null || playerSlot.PlayerId <= 0)
            return Color.white;

        float hue = ((playerSlot.PlayerId - 1) % 8) / 8f;
        return Color.HSVToRGB(hue, 0.8f, 1f);
    }
}
