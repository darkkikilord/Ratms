using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class TrajectoryLine : MonoBehaviour
{
    public Transform firePoint;

    [Header("Longitud de la curva")]
    public int resolution = 20;   // ? antes 30
    public float timeStep = 0.05f; // ? antes 0.1f

    private LineRenderer lr;
    private float gravity;
    private bool visible;
    private float cachedForce;
    private Vector2 cachedDir;

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        gravity = Mathf.Abs(Physics2D.gravity.y);
        Hide();
    }

    private void Update()
    {
        if (!visible) return;
        DrawTrajectory(cachedForce, cachedDir);
    }

    // ========== API ==========
    public void Show(float force, Vector2 dir)
    {
        visible = true;
        cachedForce = force;
        cachedDir = dir;
        lr.enabled = true;
    }

    public void Hide()
    {
        visible = false;
        lr.enabled = false;
        lr.positionCount = 0;
    }

    // ========== Dibujo ==========
    private void DrawTrajectory(float force, Vector2 dir)
    {
        if (firePoint == null) return;

        lr.positionCount = resolution;
        Vector2 startPos = firePoint.position;
        Vector2 startVel = dir.normalized * force;

        for (int i = 0; i < resolution; i++)
        {
            float t = i * timeStep;
            float dx = startVel.x * t;
            float dy = startVel.y * t - 0.5f * gravity * t * t;
            lr.SetPosition(i, startPos + new Vector2(dx, dy));
        }
    }
}
