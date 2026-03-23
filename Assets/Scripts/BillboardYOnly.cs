using UnityEngine;

public class BillboardYOnly : MonoBehaviour
{
    Transform cam;

    void LateUpdate()
    {
        if (cam == null)
            cam = Camera.main.transform;

        Vector3 lookPos = cam.position - transform.position;
        lookPos.y = 0f;

        if (lookPos.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(-lookPos);
    }
}