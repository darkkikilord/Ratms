using UnityEngine;

/// <summary>
///  Cámara del juego con extras tipo Worms:
///    • Seguimiento suave del target (la rata Owner, normalmente).
///    • Shake (temblor) temporal — ideal para explosiones.
///    • Follow externo: cualquier script puede pedirle que siga otro Transform
///      (por ejemplo, un proyectil recién lanzado).
///    • FocusOnImpact: se queda unos segundos mirando un punto (el cráter)
///      y luego vuelve solo al target original. Muy "Worms": sirve para
///      ver bien si has pegado o no.
///
///  Acceso global via <see cref="Instance"/> — es un singleton por escena.
///  No hace falta pasar referencias: desde una granada o un misil se llama
///  a <c>CameraFollow.Instance.Shake(...)</c> y listo.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Seguimiento normal")]
    public Transform target;
    public Vector3 offset = new(0, 0, -10);
    public float posSmooth = .15f;

    [Header("Zoom")]
    public float targetZoom = 5f;
    public float zoomSpeed = 5f;

    private Camera cam;

    // ---------- Estado dinámico (shake, focus, follow externo) ----------
    private Transform originalTarget;   // al que vuelve tras seguir un proyectil
    private Transform overrideTarget;   // si no es null, seguimos esto en vez de target
    private float     overrideUntil;    // Time.time hasta cuándo seguimos el override
    private Vector3   focusPosition;    // si focusUntil > Time.time, quedamos fijos aquí
    private float     focusUntil;       // Time.time hasta cuándo quedamos fijos
    private float     shakeAmp;         // amplitud del temblor actual
    private float     shakeUntil;       // Time.time hasta cuándo tiembla

    // ==============================================================
    private void Awake()
    {
        cam = Camera.main;
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==============================================================
    // API PÚBLICA
    // ==============================================================

    /// <summary>Asigna el target "permanente" (normalmente la rata del Owner).</summary>
    public void SetTarget(Transform t)
    {
        target = t;
        originalTarget = t;
    }

    /// <summary>
    ///  Pide a la cámara que siga a <paramref name="t"/> durante
    ///  <paramref name="seconds"/> segundos como máximo (luego vuelve al target
    ///  original). Si el transform se destruye (ej. un misil tras explotar),
    ///  la cámara vuelve sola al target.
    /// </summary>
    public void FollowTransform(Transform t, float seconds = 8f)
    {
        if (t == null) return;
        if (target != null && originalTarget == null) originalTarget = target;
        overrideTarget = t;
        overrideUntil = Time.time + seconds;
    }

    /// <summary>
    ///  Deja de seguir al override y se queda mirando <paramref name="at"/>
    ///  durante <paramref name="pauseSeconds"/>. Luego vuelve al target original.
    ///  Úsalo justo al explotar un proyectil para el momento dramático.
    /// </summary>
    public void FocusOnImpact(Vector3 at, float pauseSeconds = 1f)
    {
        overrideTarget = null;
        focusPosition = at;
        focusUntil = Time.time + pauseSeconds;
    }

    /// <summary>
    ///  Hace temblar la cámara <paramref name="seconds"/> segundos con amplitud
    ///  <paramref name="amplitude"/> (en unidades de mundo). Un valor típico
    ///  para una explosión: amplitude 0.15–0.35, seconds 0.25–0.5.
    /// </summary>
    public void Shake(float amplitude, float seconds)
    {
        shakeAmp = Mathf.Max(shakeAmp, amplitude); // si ya tiembla, no lo bajamos
        shakeUntil = Mathf.Max(shakeUntil, Time.time + seconds);
    }

    public void SetZoom(float z) => targetZoom = z;

    // ==============================================================
    // LOOP
    // ==============================================================
    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // 1) Decidir el target efectivo este frame.
        Vector3 desired = ComputeDesiredPosition();

        // 2) Mover la cámara suave hacia el target.
        transform.position = Vector3.Lerp(transform.position, desired, posSmooth);

        // 3) Aplicar shake encima (suma un desplazamiento aleatorio).
        if (Time.time < shakeUntil && shakeAmp > 0f)
        {
            Vector2 offsetShake = Random.insideUnitCircle * shakeAmp;
            transform.position += (Vector3)offsetShake;
        }
        else
        {
            shakeAmp = 0f;
        }

        // 4) Zoom suave.
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, Time.deltaTime * zoomSpeed);
    }

    /// <summary>
    ///  Escoge qué posición queremos como centro de la cámara este frame:
    ///  1º si estamos en focus (pausa de impacto) → ese punto fijo.
    ///  2º si estamos siguiendo un proyectil → su posición.
    ///  3º por defecto → el target original (la rata del Owner).
    /// </summary>
    private Vector3 ComputeDesiredPosition()
    {
        // Focus de impacto — máxima prioridad
        if (Time.time < focusUntil)
        {
            return focusPosition + offset;
        }

        // Proyectil activo
        if (overrideTarget != null && Time.time < overrideUntil)
        {
            return overrideTarget.position + offset;
        }

        // El override ha caducado o el transform se destruyó → limpiamos
        overrideTarget = null;

        // Target normal (o el último conocido si lo perdemos)
        Transform t = target != null ? target : originalTarget;
        if (t == null) return transform.position; // no hay nada, nos quedamos quietos
        return t.position + offset;
    }
}
