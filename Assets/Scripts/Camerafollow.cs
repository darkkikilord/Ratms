using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new(0, 0, -10);
    public float posSmooth = .15f;

    [Header("Zoom")]
    public float targetZoom = 5f;
    public float zoomSpeed = 5f;
    private Camera cam;

    private void Awake() => cam = Camera.main;

    private void LateUpdate()
    {
        if (target == null || cam == null) return;

        transform.position = Vector3.Lerp(transform.position,
                                          target.position + offset,
                                          posSmooth);

        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize,
                                          targetZoom,
                                          Time.deltaTime * zoomSpeed);
    }

    public void SetZoom(float z) => targetZoom = z;
}
