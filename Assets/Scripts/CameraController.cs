using UnityEngine;
using System.Collections.Generic;

public class PartyCameraController : MonoBehaviour
{
    public Camera targetCamera;

    public float minDistance = 6f;
    public float zoomSpeed = 6f;
    public float followSmoothSpeed = 8f;

    public Vector3 cameraOffset = new Vector3(0, 8, 10);

    public float lookHeightOffset = 1.5f;

    void LateUpdate()
    {
        if (PlayerManager.Instance == null || targetCamera == null)
            return;

        List<PlayerSlot> players = PlayerManager.Instance.GetAlivePlayers();

        if (players.Count == 0)
            return;

        // Average position
        Vector3 avg = Vector3.zero;

        foreach (var p in players)
            avg += p.Controller.transform.position;

        avg /= players.Count;

        // Max distance between players
        float maxDistance = 0f;

        for (int i = 0; i < players.Count; i++)
        {
            for (int j = i + 1; j < players.Count; j++)
            {
                float d = Vector3.Distance(
                    players[i].Controller.transform.position,
                    players[j].Controller.transform.position);

                maxDistance = Mathf.Max(maxDistance, d);
            }
        }

        float targetZoom = Mathf.Max(minDistance, maxDistance);

        Vector3 desiredPosition =
            avg + cameraOffset.normalized * targetZoom;

        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            desiredPosition,
            Time.deltaTime * followSmoothSpeed);

        Vector3 lookTarget = avg + Vector3.up * lookHeightOffset;
        targetCamera.transform.LookAt(lookTarget);
    }
}
