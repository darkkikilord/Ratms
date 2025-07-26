using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Zoom")]
    public float targetZoom = 5f;           // Zoom deseado
    public float zoomSpeed = 5f;            // Qué tan rápido se ajusta el zoom
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
        if (cam != null)
        {
            cam.orthographicSize = targetZoom;
        }
    }

    void LateUpdate()
    {
        if (target == null || cam == null) return;

        // Movimiento suave
        Vector3 desiredPosition = target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;

        // Zoom suave hacia el valor deseado
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, Time.deltaTime * zoomSpeed);
    }

    // Permitir cambiar el zoom desde otros scripts
    public void SetZoom(float newZoom)
    {
        targetZoom = newZoom;
    }
}
