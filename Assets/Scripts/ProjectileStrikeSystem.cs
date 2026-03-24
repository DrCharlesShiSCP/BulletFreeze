using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Spawns projectiles for each confirmed player and resolves splash damage.
public class ProjectileStrikeSystem : MonoBehaviour
{
    [Header("Projectile Settings")]
    [Tooltip("Projectile prefab used during the shoot phase. Add StrikeProjectile to the prefab for travel visuals.")]
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("Optional VFX prefab spawned at the impact point.")]
    [SerializeField] private GameObject impactEffectPrefab;
    [Tooltip("How quickly spawned projectiles travel to their selected target.")]
    [SerializeField] private float projectileSpeed = 14f;
    [Tooltip("World-space Y position where airstrike projectiles spawn before dropping downward.")]
    [SerializeField] private float airstrikeSpawnY = 20f;
    [Tooltip("Horizontal elimination radius around the impact point.")]
    [SerializeField] private float strikeRadius = 2.5f;
    [Tooltip("Short pause after impacts resolve before the next round starts.")]
    [SerializeField] private float postImpactDelay = 0.35f;
    [Tooltip("Failsafe timeout in seconds to prevent the shoot phase from hanging forever.")]
    [SerializeField] private float shootPhaseTimeout = 5f;

    [Header("Impact Readability")]
    [Tooltip("Shows a short-lived ground marker that matches the elimination radius.")]
    [SerializeField] private bool showImpactRadiusIndicator = true;
    [Tooltip("How long the impact radius marker stays visible.")]
    [SerializeField] private float impactRadiusIndicatorDuration = 0.7f;
    [Tooltip("Thickness of the temporary ground marker.")]
    [SerializeField] private float impactRadiusIndicatorHeight = 0.08f;
    [Tooltip("Tint used for the temporary impact radius marker.")]
    [SerializeField] private Color impactRadiusIndicatorColor = new Color(1f, 0.4f, 0.15f, 0.5f);
    [Header("Prop Blast")]
    [Tooltip("Objects with this tag will be blown away if they are inside the blast radius.")]
    [SerializeField] private string propsTag = "props";
    [Tooltip("Impulse applied to props caught in the blast.")]
    [SerializeField] private float propBlastForce = 14f;
    [Tooltip("Extra upward lift applied to blasted props.")]
    [SerializeField] private float propBlastUpwardForce = 2.5f;
    [Tooltip("Torque added to props so they tumble after the blast.")]
    [SerializeField] private float propBlastTorque = 10f;

    [Header("Debug")]
    [Tooltip("Logs impacts and eliminated players during the shoot phase.")]
    [SerializeField] private bool debugLogs = true;

    private readonly Dictionary<PlayerSlot, HashSet<PlayerSlot>> queuedEliminations =
        new Dictionary<PlayerSlot, HashSet<PlayerSlot>>();
    private readonly Dictionary<PlayerSlot, Vector3> queuedImpactPoints =
        new Dictionary<PlayerSlot, Vector3>();
    private readonly HashSet<Rigidbody> blastedPropBodies = new HashSet<Rigidbody>();

    private PlayerManager playerManager;

    public float ProjectileSpeed => projectileSpeed;
    public float StrikeRadius => strikeRadius;

    public void Initialize(PlayerManager manager)
    {
        playerManager = manager;
        WarnAboutMissingReferences();
    }

    public IEnumerator ResolveStrikes(IReadOnlyList<PlayerSlot> shooters)
    {
        if (playerManager == null)
            playerManager = PlayerManager.Instance;

        WarnAboutMissingReferences();

        queuedEliminations.Clear();
        queuedImpactPoints.Clear();

        if (shooters == null || shooters.Count == 0)
            yield break;

        List<PlayerSlot> firingPlayers = new List<PlayerSlot>();

        foreach (PlayerSlot shooter in shooters)
        {
            if (shooter == null ||
                !shooter.IsAlive ||
                !shooter.HasConfirmedTarget ||
                shooter.Controller == null)
            {
                continue;
            }

            firingPlayers.Add(shooter);
        }

        if (firingPlayers.Count == 0)
            yield break;

        List<PlayerSlot> participants =
            playerManager != null ? playerManager.GetAlivePlayers() : new List<PlayerSlot>(firingPlayers);

        int remainingProjectiles = firingPlayers.Count;

        foreach (PlayerSlot shooter in firingPlayers)
        {
            LaunchProjectile(
                shooter,
                participants,
                () => remainingProjectiles--);
        }

        float timeoutAt = Time.time + Mathf.Max(1f, shootPhaseTimeout);

        while (remainingProjectiles > 0)
        {
            if (Time.time > timeoutAt)
            {
                Debug.LogWarning(
                    $"[ProjectileStrikeSystem] Shoot phase timed out with " +
                    $"{remainingProjectiles} projectile(s) still pending. Continuing round.");
                break;
            }

            yield return null;
        }

        ApplyQueuedEliminations();

        if (postImpactDelay > 0f)
            yield return new WaitForSeconds(postImpactDelay);
    }

    private void LaunchProjectile(
        PlayerSlot shooter,
        IReadOnlyList<PlayerSlot> participants,
        Action onComplete)
    {
        Vector3 target = shooter.ConfirmedTargetPoint;
        Vector3 origin = new Vector3(target.x, airstrikeSpawnY, target.z);

        if (debugLogs)
        {
            Debug.Log(
                $"[ProjectileStrikeSystem] Spawning projectile for {shooter.DisplayName} " +
                $"at {origin} -> {target}");
        }

        if (projectilePrefab != null)
        {
            GameObject projectileInstance = Instantiate(projectilePrefab, origin, Quaternion.identity);
            if (!projectileInstance.activeSelf)
            {
                Debug.LogWarning(
                    $"[ProjectileStrikeSystem] Projectile prefab '{projectilePrefab.name}' spawned inactive. " +
                    "Forcing it active so shoot phase can complete.");
                projectileInstance.SetActive(true);
            }

            StrikeProjectile projectile = projectileInstance.GetComponent<StrikeProjectile>();

            if (projectile != null)
            {
                if (!projectile.enabled)
                {
                    Debug.LogWarning(
                        $"[ProjectileStrikeSystem] StrikeProjectile on '{projectileInstance.name}' was disabled. " +
                        "Forcing it enabled.");
                    projectile.enabled = true;
                }

                projectile.Launch(
                    origin,
                    target,
                    projectileSpeed,
                    () =>
                    {
                        HandleImpact(shooter, target, participants);
                        onComplete?.Invoke();
                    });

                return;
            }

            Debug.LogWarning(
                $"[ProjectileStrikeSystem] Projectile prefab '{projectilePrefab.name}' is missing " +
                $"{nameof(StrikeProjectile)}. Using fallback strike timing instead.");

            Destroy(projectileInstance);
        }
        else
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] Projectile prefab reference is missing. " +
                "Using fallback strike timing with no visible projectile.");
        }

        StartCoroutine(FallbackStrikeRoutine(shooter, origin, target, participants, onComplete));
    }

    private IEnumerator FallbackStrikeRoutine(
        PlayerSlot shooter,
        Vector3 origin,
        Vector3 target,
        IReadOnlyList<PlayerSlot> participants,
        Action onComplete)
    {
        float travelTime = Mathf.Max(
            0.05f,
            Vector3.Distance(origin, target) / Mathf.Max(0.1f, projectileSpeed));

        float elapsed = 0f;

        while (elapsed < travelTime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        HandleImpact(shooter, target, participants);
        onComplete?.Invoke();
    }

    private void HandleImpact(
        PlayerSlot shooter,
        Vector3 impactPoint,
        IReadOnlyList<PlayerSlot> participants)
    {
        GamePhaseManager.Instance?.PlayExplosionSoundAt(impactPoint);

        if (impactEffectPrefab != null)
        {
            GameObject impactFxInstance = Instantiate(impactEffectPrefab, impactPoint, Quaternion.identity);
            if (!impactFxInstance.activeSelf)
            {
                Debug.LogWarning(
                    $"[ProjectileStrikeSystem] Impact effect prefab '{impactEffectPrefab.name}' spawned inactive. " +
                    "Forcing it active so the explosion is visible.");
                impactFxInstance.SetActive(true);
            }
        }
        else
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] Impact effect prefab reference is missing. No explosion FX will be shown.");
        }

        if (showImpactRadiusIndicator)
            StartCoroutine(ShowImpactRadiusIndicator(impactPoint));

        BlastNearbyProps(impactPoint);

        bool hitAnyPlayer = false;

        foreach (PlayerSlot participant in participants)
        {
            if (participant == null || participant.Controller == null)
                continue;

            float distance = HorizontalDistance(
                participant.Controller.transform.position,
                impactPoint);

            if (distance > strikeRadius)
                continue;

            if (!queuedEliminations.TryGetValue(participant, out HashSet<PlayerSlot> hitters))
            {
                hitters = new HashSet<PlayerSlot>();
                queuedEliminations.Add(participant, hitters);
            }

            hitters.Add(shooter);
            queuedImpactPoints[participant] = impactPoint;
            hitAnyPlayer = true;
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[ProjectileStrikeSystem] {shooter.DisplayName} impact at {impactPoint}. " +
                $"{(hitAnyPlayer ? "Players were caught in the radius." : "No players were hit.")}");
        }
    }

    private void ApplyQueuedEliminations()
    {
        if (playerManager == null)
            return;

        foreach (KeyValuePair<PlayerSlot, HashSet<PlayerSlot>> elimination in queuedEliminations)
        {
            PlayerSlot victim = elimination.Key;
            if (victim == null || !victim.IsAlive)
                continue;

            StringBuilder sourceBuilder = new StringBuilder();
            bool first = true;

            foreach (PlayerSlot hitter in elimination.Value)
            {
                if (!first)
                    sourceBuilder.Append(", ");

                sourceBuilder.Append(hitter != null ? hitter.DisplayName : "Unknown");
                first = false;
            }

            playerManager.EliminatePlayer(
                victim,
                sourceBuilder.ToString(),
                queuedImpactPoints.TryGetValue(victim, out Vector3 impactPoint)
                    ? impactPoint
                    : Vector3.zero);
        }
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector2 a2 = new Vector2(a.x, a.z);
        Vector2 b2 = new Vector2(b.x, b.z);
        return Vector2.Distance(a2, b2);
    }

    private void BlastNearbyProps(Vector3 impactPoint)
    {
        if (string.IsNullOrWhiteSpace(propsTag))
            return;

        blastedPropBodies.Clear();

        Collider[] hitColliders = Physics.OverlapSphere(
            impactPoint,
            strikeRadius,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        foreach (Collider hitCollider in hitColliders)
        {
            if (hitCollider == null)
                continue;

            GameObject taggedObject = FindTaggedPropObject(hitCollider.transform);
            if (taggedObject == null)
                continue;

            Rigidbody propBody = taggedObject.GetComponent<Rigidbody>();
            if (propBody == null)
                propBody = taggedObject.AddComponent<Rigidbody>();

            if (!blastedPropBodies.Add(propBody))
                continue;

            propBody.isKinematic = false;
            propBody.useGravity = true;
            propBody.interpolation = RigidbodyInterpolation.Interpolate;

            Vector3 blastDirection = taggedObject.transform.position - impactPoint;
            blastDirection.y = 0f;

            if (blastDirection.sqrMagnitude < 0.001f)
                blastDirection = UnityEngine.Random.insideUnitSphere;

            blastDirection.Normalize();
            blastDirection.y += propBlastUpwardForce;

            propBody.AddForce(blastDirection.normalized * propBlastForce, ForceMode.Impulse);
            propBody.AddTorque(UnityEngine.Random.onUnitSphere * propBlastTorque, ForceMode.Impulse);

            if (debugLogs)
            {
                Debug.Log(
                    $"[ProjectileStrikeSystem] Blasted prop '{taggedObject.name}' at {taggedObject.transform.position}.");
            }
        }
    }

    private GameObject FindTaggedPropObject(Transform startTransform)
    {
        Transform current = startTransform;

        while (current != null)
        {
            if (string.Equals(current.tag, propsTag, StringComparison.Ordinal))
                return current.gameObject;

            current = current.parent;
        }

        return null;
    }

    private void WarnAboutMissingReferences()
    {
        if (playerManager == null)
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] PlayerManager reference is missing. " +
                "Projectile resolution will try to recover via singleton lookup.");
        }

        if (projectilePrefab == null)
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] Projectile prefab reference is missing. " +
                "Fallback strike timing will be used without visible projectiles.");
        }

        if (impactEffectPrefab == null)
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] Impact effect prefab reference is missing. " +
                "Impacts will resolve without explosion visuals.");
        }
    }

    private IEnumerator ShowImpactRadiusIndicator(Vector3 impactPoint)
    {
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        indicator.name = "ImpactRadiusIndicator";
        indicator.transform.position = new Vector3(
            impactPoint.x,
            impactPoint.y + (impactRadiusIndicatorHeight * 0.5f),
            impactPoint.z);
        indicator.transform.localScale = new Vector3(
            strikeRadius * 2f,
            impactRadiusIndicatorHeight * 0.5f,
            strikeRadius * 2f);

        Collider indicatorCollider = indicator.GetComponent<Collider>();
        if (indicatorCollider != null)
            Destroy(indicatorCollider);

        Renderer indicatorRenderer = indicator.GetComponent<Renderer>();
        Material indicatorMaterial = CreateImpactIndicatorMaterial();

        if (indicatorRenderer != null && indicatorMaterial != null)
            indicatorRenderer.sharedMaterial = indicatorMaterial;

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, impactRadiusIndicatorDuration);
        Color startColor = impactRadiusIndicatorColor;
        Vector3 startScale = indicator.transform.localScale;
        Vector3 endScale = new Vector3(startScale.x * 1.15f, startScale.y, startScale.z * 1.15f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);

            if (indicator != null)
                indicator.transform.localScale = Vector3.Lerp(startScale, endScale, normalizedTime);

            if (indicatorMaterial != null)
            {
                Color fadedColor = startColor;
                fadedColor.a = Mathf.Lerp(startColor.a, 0f, normalizedTime);
                SetMaterialColor(indicatorMaterial, fadedColor);
            }

            yield return null;
        }

        if (indicatorMaterial != null)
            Destroy(indicatorMaterial);

        if (indicator != null)
            Destroy(indicator);
    }

    private Material CreateImpactIndicatorMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning(
                "[ProjectileStrikeSystem] Could not find a compatible shader for the impact radius indicator.");
            return null;
        }

        Material material = new Material(shader);
        SetMaterialColor(material, impactRadiusIndicatorColor);

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
