using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Handles per-player target selection during the aim phase.
public class PlayerAimController : MonoBehaviour
{
    [Header("Aim Settings")]
    [Tooltip("How quickly this player's target marker moves across the arena when using a controller.")]
    [SerializeField] private float crosshairMoveSpeed = 8f;
    [Tooltip("Starting offset from the player when a new aim phase begins.")]
    [SerializeField] private Vector3 initialOffset = new Vector3(0f, 0f, 4f);
    [Tooltip("Height of the target marker above the arena floor.")]
    [SerializeField] private float markerHeight = 0.15f;
    [Tooltip("Fallback arena height used when no arena bounds collider is assigned.")]
    [SerializeField] private float groundPlaneHeight = 0f;
    [Tooltip("Arena bound objects used to clamp target selection. Assign one parent object or multiple individual collider objects.")]
    [SerializeField] private Transform[] arenaBoundTargets;
    [Tooltip("Optional prefab used for the target marker.")]
    [SerializeField] private GameObject crosshairPrefab;
    [Tooltip("Fallback scale used when the controller creates a primitive marker.")]
    [SerializeField] private Vector3 fallbackMarkerScale = new Vector3(0.55f, 0.12f, 0.55f);

    [Header("Debug")]
    [Tooltip("Random offset radius used when a fake debug player picks an aim point.")]
    [SerializeField] private float debugTargetScatterRadius = 2.5f;

    private PlayerSlot playerSlot;
    private PlayerController playerController;
    private GameObject crosshairInstance;
    private Renderer[] crosshairRenderers;
    private readonly List<Collider> arenaBoundColliders = new List<Collider>();
    private MaterialPropertyBlock materialPropertyBlock;
    private Bounds cachedArenaBounds;
    private bool hasCachedArenaBounds;
    private bool aimActive;
    private bool hasConfirmed;
    private Vector3 currentTargetPoint;
    private float resolvedGroundHeight;

    public Vector3 CurrentTargetPoint => currentTargetPoint;
    public bool HasConfirmed => hasConfirmed;

    private void Awake()
    {
        materialPropertyBlock = new MaterialPropertyBlock();
    }

    public void Initialize(
        PlayerSlot slot,
        PlayerController controller,
        Transform[] boundsTargets,
        GameObject sharedCrosshairPrefab)
    {
        playerSlot = slot;
        playerController = controller;
        arenaBoundTargets = boundsTargets != null && boundsTargets.Length > 0
            ? boundsTargets
            : arenaBoundTargets;
        crosshairPrefab = sharedCrosshairPrefab != null ? sharedCrosshairPrefab : crosshairPrefab;
        RebuildArenaBoundsCache();
        SyncGroundPlaneHeightFromController();

        EnsureMarker();
        ResetForNextRound();
        SetAimActive(false);
    }

    private void Update()
    {
        if (!aimActive || playerSlot == null || !playerSlot.IsAlive)
            return;

        if (playerSlot.IsDebugPlayer)
            return;

        if (playerSlot.UsesKeyboard)
        {
            if (TryGetMouseAimPoint(out Vector3 mouseTargetPoint))
            {
                currentTargetPoint = ClampToArena(mouseTargetPoint);
                UpdateMarkerTransform();
            }

            return;
        }

        Vector2 aimInput = playerSlot.ReadAimInput();

        if (aimInput.sqrMagnitude <= 0.001f)
            return;

        currentTargetPoint +=
            new Vector3(aimInput.x, 0f, aimInput.y) *
            crosshairMoveSpeed *
            Time.deltaTime;

        currentTargetPoint = ClampToArena(currentTargetPoint);
        UpdateMarkerTransform();
    }

    public void ResetForNextRound()
    {
        hasConfirmed = false;
        playerSlot?.ClearConfirmedTarget();
        SyncGroundPlaneHeightFromController();

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

        if (playerSlot != null && playerSlot.IsDebugPlayer)
            currentTargetPoint = GenerateDebugTargetPoint();

        currentTargetPoint = ClampToArena(currentTargetPoint);
        UpdateMarkerAppearance();
        UpdateMarkerTransform();

        if (playerController != null && playerSlot != null && playerSlot.IsAlive)
            playerController.SetLine2(playerSlot.IsDebugPlayer ? "Debug Aim" : "Aiming...");
    }

    public void SetAimActive(bool active)
    {
        aimActive = active && playerSlot != null && playerSlot.IsAlive;
        EnsureMarker();
        SetMarkerVisible(aimActive);
    }

    public void FinalizeCurrentTarget()
    {
        if (hasConfirmed)
            return;

        hasConfirmed = true;
        playerSlot?.ConfirmTarget(currentTargetPoint);
        UpdateMarkerAppearance();

        if (playerController != null)
            playerController.SetLine2("Locked");

        if (playerSlot != null)
        {
            Debug.Log(
                $"[PlayerAimController] {playerSlot.DisplayName} locked target at {currentTargetPoint}.");
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

            if (materialPropertyBlock == null)
                materialPropertyBlock = new MaterialPropertyBlock();

            markerRenderer.GetPropertyBlock(materialPropertyBlock);

            if (markerRenderer.sharedMaterial != null)
            {
                if (markerRenderer.sharedMaterial.HasProperty("_BaseColor"))
                    materialPropertyBlock.SetColor("_BaseColor", markerColor);

                if (markerRenderer.sharedMaterial.HasProperty("_Color"))
                    materialPropertyBlock.SetColor("_Color", markerColor);
            }

            markerRenderer.SetPropertyBlock(materialPropertyBlock);
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
        if (hasCachedArenaBounds)
        {
            point.x = Mathf.Clamp(point.x, cachedArenaBounds.min.x, cachedArenaBounds.max.x);
            point.z = Mathf.Clamp(point.z, cachedArenaBounds.min.z, cachedArenaBounds.max.z);
            point.y = resolvedGroundHeight + markerHeight;
            return point;
        }

        point.y = resolvedGroundHeight + markerHeight;
        return point;
    }

    private bool TryGetMouseAimPoint(out Vector3 targetPoint)
    {
        targetPoint = currentTargetPoint;

        if (Mouse.current == null)
            return false;

        Camera aimCamera = playerController != null ? playerController.SharedCameraRef : Camera.main;
        if (aimCamera == null)
            return false;

        Ray aimRay = aimCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, resolvedGroundHeight, 0f));

        if (groundPlane.Raycast(aimRay, out float enter))
        {
            targetPoint = aimRay.GetPoint(enter);
            return true;
        }

        return false;
    }

    private Vector3 GenerateDebugTargetPoint()
    {
        if (playerController == null)
            return ClampToArena(transform.position + initialOffset);

        Vector3 targetPoint = playerController.transform.position + playerController.transform.forward * initialOffset.z;

        if (PlayerManager.Instance != null)
        {
            List<PlayerSlot> alivePlayers = PlayerManager.Instance.GetAlivePlayers();
            List<PlayerSlot> possibleTargets = new List<PlayerSlot>();

            foreach (PlayerSlot alivePlayer in alivePlayers)
            {
                if (alivePlayer == null || alivePlayer.Controller == null)
                    continue;

                if (alivePlayers.Count > 1 && alivePlayer == playerSlot)
                    continue;

                possibleTargets.Add(alivePlayer);
            }

            if (possibleTargets.Count > 0)
            {
                PlayerSlot targetPlayer = possibleTargets[Random.Range(0, possibleTargets.Count)];
                targetPoint = targetPlayer.Controller.transform.position;
            }
        }

        Vector2 scatter = Random.insideUnitCircle * debugTargetScatterRadius;
        targetPoint += new Vector3(scatter.x, 0f, scatter.y);
        return ClampToArena(targetPoint);
    }

    private void RebuildArenaBoundsCache()
    {
        arenaBoundColliders.Clear();
        hasCachedArenaBounds = false;

        if (arenaBoundTargets == null || arenaBoundTargets.Length == 0)
            return;

        foreach (Transform boundsTarget in arenaBoundTargets)
        {
            if (boundsTarget == null)
                continue;

            Collider[] colliders = boundsTarget.GetComponentsInChildren<Collider>(true);
            foreach (Collider foundCollider in colliders)
            {
                if (foundCollider != null && !arenaBoundColliders.Contains(foundCollider))
                    arenaBoundColliders.Add(foundCollider);
            }
        }

        foreach (Collider arenaCollider in arenaBoundColliders)
        {
            if (!hasCachedArenaBounds)
            {
                cachedArenaBounds = arenaCollider.bounds;
                hasCachedArenaBounds = true;
            }
            else
            {
                cachedArenaBounds.Encapsulate(arenaCollider.bounds);
            }
        }

        if (hasCachedArenaBounds &&
            playerController != null &&
            Mathf.Abs(cachedArenaBounds.min.y - playerController.transform.position.y) > 2f)
        {
            Debug.LogWarning(
                $"[PlayerAimController] Arena bound collider Y range ({cachedArenaBounds.min.y:F2} to " +
                $"{cachedArenaBounds.max.y:F2}) does not match player ground height " +
                $"({playerController.transform.position.y:F2}) for " +
                $"{(playerSlot != null ? playerSlot.DisplayName : name)}. " +
                "Using player height for ground aiming and collider bounds only for X/Z clamping.");
        }
    }

    private Color GetPlayerColor()
    {
        if (playerSlot == null || playerSlot.PlayerId <= 0)
            return Color.white;

        float hue = ((playerSlot.PlayerId - 1) % 8) / 8f;
        return Color.HSVToRGB(hue, 0.8f, 1f);
    }

    private void SyncGroundPlaneHeightFromController()
    {
        resolvedGroundHeight = groundPlaneHeight;

        if (playerController != null)
            resolvedGroundHeight = playerController.transform.position.y;
    }
}
