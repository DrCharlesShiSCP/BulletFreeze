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
    [Tooltip("Horizontal elimination radius around the impact point.")]
    [SerializeField] private float strikeRadius = 2.5f;
    [Tooltip("Short pause after impacts resolve before the next round starts.")]
    [SerializeField] private float postImpactDelay = 0.35f;

    [Header("Debug")]
    [Tooltip("Logs impacts and eliminated players during the shoot phase.")]
    [SerializeField] private bool debugLogs = true;

    private readonly Dictionary<PlayerSlot, HashSet<PlayerSlot>> queuedEliminations =
        new Dictionary<PlayerSlot, HashSet<PlayerSlot>>();
    private readonly Dictionary<PlayerSlot, Vector3> queuedImpactPoints =
        new Dictionary<PlayerSlot, Vector3>();

    private PlayerManager playerManager;

    public float ProjectileSpeed => projectileSpeed;
    public float StrikeRadius => strikeRadius;

    public void Initialize(PlayerManager manager)
    {
        playerManager = manager;
    }

    public IEnumerator ResolveStrikes(IReadOnlyList<PlayerSlot> shooters)
    {
        if (playerManager == null)
            playerManager = PlayerManager.Instance;

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

        while (remainingProjectiles > 0)
            yield return null;

        ApplyQueuedEliminations();

        if (postImpactDelay > 0f)
            yield return new WaitForSeconds(postImpactDelay);
    }

    private void LaunchProjectile(
        PlayerSlot shooter,
        IReadOnlyList<PlayerSlot> participants,
        Action onComplete)
    {
        Vector3 origin = shooter.Controller.ProjectileSpawnPosition;
        Vector3 target = shooter.ConfirmedTargetPoint;

        if (projectilePrefab != null)
        {
            GameObject projectileInstance = Instantiate(projectilePrefab, origin, Quaternion.identity);
            StrikeProjectile projectile = projectileInstance.GetComponent<StrikeProjectile>();

            if (projectile != null)
            {
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

            Destroy(projectileInstance);
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
        if (impactEffectPrefab != null)
            Instantiate(impactEffectPrefab, impactPoint, Quaternion.identity);

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
}
