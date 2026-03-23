using UnityEngine;

public class BillboardYOnly : MonoBehaviour
{
    Transform cam;

    void LateUpdate()
    {
        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;

        if (cam == null)
            return;

        Vector3 lookPos = cam.position - transform.position;
        lookPos.y = 0f;

        if (lookPos.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(-lookPos);
    }
}
