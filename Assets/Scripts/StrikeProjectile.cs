using System;
using UnityEngine;

// Simple projectile mover used by ProjectileStrikeSystem during the shoot phase.
public class StrikeProjectile : MonoBehaviour
{
    [Header("Visual Motion")]
    [Tooltip("Rotate the projectile to face its travel direction.")]
    [SerializeField] private bool faceVelocity = true;
    [Tooltip("Fallback spin speed when faceVelocity is disabled.")]
    [SerializeField] private float spinSpeed = 360f;
    [Tooltip("Delay before destroying the projectile after impact.")]
    [SerializeField] private float destroyDelay = 0.05f;

    private Vector3 targetPoint;
    private float travelSpeed;
    private Action impactCallback;
    private bool isActive;

    public void Launch(
        Vector3 startPoint,
        Vector3 destination,
        float speed,
        Action onImpact)
    {
        transform.position = startPoint;
        targetPoint = destination;
        travelSpeed = Mathf.Max(0.1f, speed);
        impactCallback = onImpact;
        isActive = true;
    }

    private void Update()
    {
        if (!isActive)
            return;

        Vector3 toTarget = targetPoint - transform.position;
        float step = travelSpeed * Time.deltaTime;

        if (toTarget.sqrMagnitude <= step * step)
        {
            transform.position = targetPoint;
            isActive = false;
            impactCallback?.Invoke();
            Destroy(gameObject, destroyDelay);
            return;
        }

        Vector3 direction = toTarget.normalized;
        transform.position += direction * step;

        if (faceVelocity && direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
        else
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }
    }
}
