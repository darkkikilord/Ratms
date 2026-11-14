using System.Collections.Generic;
using Ratms.Networking;
using Ratms.World;
using UnityEngine;

/// <summary>
///  Cámara del juego, estilo Worms (0.20: "cámara inteligente" + cámara libre).
///
///  QUÉ MIRA, por prioridad:
///   1. CÁMARA LIBRE (si el jugador la ha movido): se queda donde la dejes.
///   2. IMPACTO: tras una explosión se para en el cráter y se aleja lo justo
///      para que se vean las ratas de alrededor.
///   3. PROYECTIL en vuelo: lo sigue mirando un poco POR DELANTE (hacia donde
///      va) y se aleja cuanto más rápido vuela.
///   4. RATA VOLANDO: después del impacto, si alguna rata ha salido despedida,
///      la sigue hasta que aterriza (como en Worms).
///   5. Por defecto, la rata del turno (su CUERPO, no el pivote, que está bajo
///      el suelo) y, si estás apuntando, se adelanta un poco hacia el tiro.
///  Además nunca se sale del mapa.
///
///  CÁMARA LIBRE (para apuntar el ataque aéreo lejos o buscar ratas):
///   · Clic DERECHO y arrastrar → mueve la cámara.
///   · Con un arma de clic (aéreo, napalm, teletransporte...) acercar el ratón
///     al borde de la pantalla la desplaza.
///   · V → vista general de todo el mapa (otra vez V, vuelve).
///   · C → vuelve a tu rata (también al moverte, al disparar o al cambiar el turno).
///  Las ratas que quedan fuera de pantalla se marcan con flechitas en el borde
///  (<see cref="OffscreenRatMarkers"/>).
///
///  Acceso global via <see cref="Instance"/>: desde una granada o un misil se
///  llama a <c>CameraFollow.Instance.Shake(...)</c> y listo.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("Seguimiento normal")]
    [Tooltip("Lo que sigue la cámara (la rata del turno). Lo pone el NetworkTurnManager.")]
    public Transform target;
    public Vector3 offset = new(0, 0, -10);
    [Tooltip("(Antiguo) Suavizado: fracción del camino por frame a 60 FPS. Ahora se usa como tiempo de " +
             "suavizado ≈ 0.05 / posSmooth segundos.")]
    public float posSmooth = .15f;

    [Header("Zoom")]
    [Tooltip("Zoom elegido por el jugador (rueda). La cámara lo amplía sola cuando hace falta.")]
    public float targetZoom = 5f;
    public float zoomSpeed = 5f;
    [Tooltip("Permite acercar/alejar la cámara con la rueda del ratón.")]
    public bool scrollZoom = true;
    [Tooltip("Zoom mínimo (más cerca) y máximo (más lejos) con la rueda.")]
    public float minZoom = 4f;
    public float maxZoom = 14f;
    [Tooltip("Zoom máximo en cámara libre / vista general.")]
    public float maxFreeZoom = 24f;
    [Tooltip("Cuánto cambia el zoom por cada 'clic' de la rueda.")]
    public float scrollStep = 0.8f;

    [Header("Cámara inteligente")]
    [Tooltip("Cuánto se adelanta la cámara hacia donde apuntas (unidades).")]
    public float aimLookAhead = 1.6f;
    [Tooltip("Cuánto mira por delante del proyectil (segundos de vuelo).")]
    public float projectileLead = 0.35f;
    [Tooltip("Zoom extra máximo mientras sigue un proyectil rápido.")]
    public float projectileExtraZoom = 2.5f;
    [Tooltip("Velocidad (u/s) a partir de la cual una rata se considera 'volando' y la cámara la sigue tras un impacto.")]
    public float flyingSpeed = 3.5f;

    [Header("Cámara libre")]
    [Tooltip("Velocidad del desplazamiento por los bordes (unidades/s a zoom 6).")]
    public float edgeScrollSpeed = 16f;
    [Tooltip("Ancho de la franja del borde que desplaza la cámara (fracción de la pantalla).")]
    public float edgeFraction = 0.025f;

    private Camera cam;
    // Posición "limpia" de la cámara (sin temblor). El temblor se suma
    // encima solo al pintar, así no se acumula frame a frame.
    private Vector3 basePosition;
    private Vector3 velocity;          // para SmoothDamp
    private bool hasBasePosition;

    // ---------- Estado dinámico ----------
    private Transform originalTarget;   // al que vuelve tras seguir un proyectil
    private Transform overrideTarget;   // proyectil que seguimos
    private float     overrideUntil;
    private Vector3   lastOverridePos;
    private Vector3   overrideVel;
    private Vector3   focusPosition;    // pausa de impacto
    private float     focusUntil;
    private float     focusZoom;
    private float     shakeAmp;
    private float     shakeUntil;

    // Ratas volando tras un impacto
    private float watchFlyersUntil;
    private NetworkPlayerMovement flyer;
    private float flyerCalmSince;
    private readonly Dictionary<NetworkPlayerMovement, Vector3> lastRatPos = new();
    private readonly Dictionary<NetworkPlayerMovement, float> ratSpeed = new();

    // Cámara libre
    /// <summary>¿El jugador está moviendo la cámara a mano?</summary>
    public bool FreeMode { get; private set; }
    /// <summary>¿Vista general de todo el mapa?</summary>
    public bool Overview { get; private set; }
    private Vector3 freePos;
    private float freeZoom;
    private bool rightDown;
    private Vector3 dragStartMouse;
    private Vector3 dragStartCam;
    private bool dragging;

    private DestructibleTerrain terrain;

    // ==============================================================
    private void Awake()
    {
        cam = Camera.main;
        Instance = this;
        if (GetComponent<OffscreenRatMarkers>() == null) gameObject.AddComponent<OffscreenRatMarkers>();
        if (GetComponent<ControlHints>() == null) gameObject.AddComponent<ControlHints>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==============================================================
    // API PÚBLICA
    // ==============================================================

    /// <summary>Asigna el target "permanente" (la rata del turno). Sale de la cámara libre.</summary>
    public void SetTarget(Transform t)
    {
        target = t;
        originalTarget = t;
        ExitFree();
    }

    /// <summary>
    ///  Sigue a <paramref name="t"/> (un proyectil) <paramref name="seconds"/>
    ///  segundos como máximo; si se destruye, vuelve sola.
    /// </summary>
    public void FollowTransform(Transform t, float seconds = 8f)
    {
        if (t == null) return;
        if (target != null && originalTarget == null) originalTarget = target;
        overrideTarget = t;
        overrideUntil = Time.time + seconds;
        lastOverridePos = t.position;
        overrideVel = Vector3.zero;
        ExitFree();
    }

    /// <summary>
    ///  Se queda mirando <paramref name="at"/> durante <paramref name="pauseSeconds"/>
    ///  (el momento dramático del impacto) y después vigila si alguna rata ha
    ///  salido volando antes de volver al target.
    /// </summary>
    public void FocusOnImpact(Vector3 at, float pauseSeconds = 1f)
    {
        overrideTarget = null;
        focusPosition = at;
        focusUntil = Time.time + pauseSeconds;
        focusZoom = ZoomToFitAround(at, 5.5f);
        watchFlyersUntil = focusUntil + 4f;
        flyer = null;
        ExitFree();
    }

    /// <summary>Temblor de <paramref name="seconds"/> s con amplitud en unidades de mundo.</summary>
    public void Shake(float amplitude, float seconds)
    {
        if (PlayerPrefs.GetInt("opt_shake", 1) == 0) return;   // desactivado en Opciones
        shakeAmp = Mathf.Max(shakeAmp, amplitude);
        shakeUntil = Mathf.Max(shakeUntil, Time.time + seconds);
    }

    public void SetZoom(float z) => targetZoom = z;

    /// <summary>Vuelve a seguir a la rata (sale de la cámara libre y de la vista general).</summary>
    public void ExitFree()
    {
        FreeMode = false;
        Overview = false;
        dragging = false;
    }

    // ==============================================================
    // LOOP
    // ==============================================================
    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (terrain == null) terrain = FindFirstObjectByType<DestructibleTerrain>();

        if (!hasBasePosition) { basePosition = transform.position; hasBasePosition = true; }

        TrackRatSpeeds();
        HandleFreeCameraInput();
        HandleWheel();

        // 1) A dónde y con qué zoom queremos mirar.
        float zoom = targetZoom;
        float smooth = Mathf.Clamp(0.05f / Mathf.Max(0.01f, posSmooth), 0.08f, 0.6f);
        Vector3 desired = ComputeDesired(ref zoom, ref smooth);

        // 2) Nunca fuera del mapa.
        desired = ClampToMap(desired, zoom);

        // 3) Mover suave (SmoothDamp: arranca y frena sin tirones; en saltos
        //    largos va más rápido que un Lerp fijo).
        basePosition = Vector3.SmoothDamp(basePosition, desired, ref velocity, smooth, Mathf.Infinity, Time.unscaledDeltaTime);

        // 4) Temblor encima, solo al pintar.
        Vector3 shakeOffset = Vector3.zero;
        if (Time.time < shakeUntil && shakeAmp > 0f)
            shakeOffset = (Vector3)(Random.insideUnitCircle * shakeAmp);
        else
            shakeAmp = 0f;
        transform.position = basePosition + shakeOffset;

        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, zoom, 1f - Mathf.Exp(-zoomSpeed * Time.unscaledDeltaTime));
    }

    private Vector3 ComputeDesired(ref float zoom, ref float smooth)
    {
        // --- 1. Cámara libre ---
        if (FreeMode)
        {
            zoom = freeZoom;
            smooth = dragging ? 0.02f : 0.12f;
            return freePos;
        }

        // --- 2. Pausa de impacto ---
        if (Time.time < focusUntil)
        {
            zoom = Mathf.Max(targetZoom, focusZoom);
            smooth = 0.18f;
            return focusPosition + offset;
        }

        // --- 3. Proyectil en vuelo ---
        if (overrideTarget != null && Time.time < overrideUntil)
        {
            Vector3 p = overrideTarget.position;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector3 v = (p - lastOverridePos) / dt;
            lastOverridePos = p;
            overrideVel = Vector3.Lerp(overrideVel, v, 1f - Mathf.Exp(-8f * dt));
            Vector3 lead = Vector3.ClampMagnitude(overrideVel * projectileLead, 4f);
            float speed = overrideVel.magnitude;
            zoom = targetZoom + Mathf.Clamp01((speed - 4f) / 16f) * projectileExtraZoom;
            smooth = 0.1f;
            return p + lead + offset;
        }
        overrideTarget = null;

        // --- 4. Rata volando tras el impacto ---
        if (Time.time < watchFlyersUntil)
        {
            if (flyer == null || !flyer.IsSpawned)
            {
                flyer = FastestRat();
                flyerCalmSince = Time.time;
            }
            if (flyer != null)
            {
                float s = ratSpeed.TryGetValue(flyer, out var sp) ? sp : 0f;
                if (s > flyingSpeed * 0.5f) flyerCalmSince = Time.time;
                if (Time.time - flyerCalmSince < 0.6f)
                {
                    zoom = Mathf.Max(targetZoom, targetZoom + 1f);
                    smooth = 0.14f;
                    return BodyOf(flyer.transform) + offset;
                }
                watchFlyersUntil = 0f;   // ya aterrizó: volvemos a la rata del turno
            }
        }

        // --- 5. Rata del turno (+ adelanto al apuntar) ---
        Transform t = target != null ? target : originalTarget;
        if (t == null) return transform.position;
        Vector3 pos = BodyOf(t) + offset;
        var mv = t.GetComponent<NetworkPlayerMovement>();
        if (mv != null && mv.IsOwner && mv.CanControlNow)
        {
            var menu = mv.GetComponentInChildren<WeaponsMenuUI>();
            var w = menu != null ? menu.SelectedWeapon : WeaponsMenuUI.WeaponId.None;
            if (w != WeaponsMenuUI.WeaponId.None && !WeaponsMenuUI.ClickTargeted(w) && w != WeaponsMenuUI.WeaponId.Bat)
            {
                float a = mv.AimDegrees * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a) * (mv.FacingRight ? 1f : -1f), Mathf.Sin(a), 0f);
                pos += dir * aimLookAhead;
            }
        }
        return pos;
    }

    // ==============================================================
    // CÁMARA LIBRE
    // ==============================================================
    private void HandleFreeCameraInput()
    {
        if (Ratms.UI.PauseMenu.IsOpen) return;

        // C → volver a la rata
        if (Input.GetKeyDown(KeyCode.C) && (FreeMode || Overview)) { ExitFree(); return; }

        // V → vista general (y otra vez V, volver)
        if (Input.GetKeyDown(KeyCode.V)) { ToggleOverview(); if (!FreeMode) return; }

        HandleDragAndEdges();
    }

    /// <summary>Vista general de todo el mapa (o, si ya lo está, vuelve a la rata).</summary>
    public void ToggleOverview()
    {
        if (Overview) { ExitFree(); return; }
        {
            EnterFree();
            Overview = true;
            if (terrain != null)
            {
                Vector2 size = terrain.WorldSize;
                Vector3 c = terrain.transform.position + new Vector3(size.x * 0.5f, size.y * 0.45f, 0f);
                freePos = new Vector3(c.x, c.y, 0f) + offset;
                float aspect = Mathf.Max(0.5f, cam.aspect);
                freeZoom = Mathf.Clamp(Mathf.Max(size.y * 0.55f, (size.x + 4f) * 0.5f / aspect), minZoom, maxFreeZoom);
            }
        }
    }

    private void HandleDragAndEdges()
    {
        // Clic derecho + arrastrar
        if (Input.GetMouseButtonDown(1))
        {
            rightDown = true;
            dragStartMouse = Input.mousePosition;
            dragStartCam = FreeMode ? freePos : basePosition;
        }
        if (rightDown && Input.GetMouseButton(1))
        {
            Vector3 d = Input.mousePosition - dragStartMouse;
            if (!dragging && d.sqrMagnitude > 36f)
            {
                if (!FreeMode) { EnterFree(); dragStartCam = basePosition; }
                dragging = true;
            }
            if (dragging)
            {
                float unitsPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                freePos = dragStartCam - new Vector3(d.x, d.y, 0f) * unitsPerPixel;
                Overview = false;
            }
        }
        if (Input.GetMouseButtonUp(1)) { rightDown = false; dragging = false; }

        // Borde de la pantalla, solo apuntando con un arma de clic (aéreo, teletransporte...)
        if (AimingWithMouse() && Application.isFocused && !dragging)
        {
            Vector3 m = Input.mousePosition;
            float ex = Screen.width * edgeFraction, ey = Screen.height * edgeFraction;
            Vector2 dir = Vector2.zero;
            if (m.x >= 0 && m.x < ex) dir.x = -1f; else if (m.x > Screen.width - ex && m.x <= Screen.width) dir.x = 1f;
            if (m.y >= 0 && m.y < ey) dir.y = -1f; else if (m.y > Screen.height - ey && m.y <= Screen.height) dir.y = 1f;
            if (dir != Vector2.zero)
            {
                if (!FreeMode) EnterFree();
                freePos += (Vector3)(dir.normalized * edgeScrollSpeed * (cam.orthographicSize / 6f) * Time.unscaledDeltaTime);
                Overview = false;
            }
        }

        // Si la rata del turno (mía) se mueve o dispara, la cámara vuelve a ella.
        if (FreeMode && !dragging && MyRatIsActing()) ExitFree();
        if (FreeMode) freePos = ClampToMap(freePos, freeZoom);
    }

    private void EnterFree()
    {
        if (!FreeMode) { freePos = basePosition; freeZoom = cam != null ? cam.orthographicSize : targetZoom; }
        FreeMode = true;
    }

    private void HandleWheel()
    {
        if (!scrollZoom || Ratms.UI.PauseMenu.IsOpen) return;
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (FreeMode)
        {
            freeZoom = Mathf.Clamp(freeZoom - wheel * scrollStep, minZoom, maxFreeZoom);
            Overview = false;
        }
        else targetZoom = Mathf.Clamp(targetZoom - wheel * scrollStep, minZoom, maxZoom);
    }

    /// <summary>¿La rata del turno es mía y tengo un arma que se apunta con el ratón?</summary>
    private bool AimingWithMouse()
    {
        var mv = target != null ? target.GetComponent<NetworkPlayerMovement>() : null;
        if (mv == null || !mv.IsOwner || !mv.CanControlNow) return false;
        var menu = mv.GetComponentInChildren<WeaponsMenuUI>();
        return menu != null && WeaponsMenuUI.ClickTargeted(menu.SelectedWeapon);
    }

    private bool MyRatIsActing()
    {
        var mv = target != null ? target.GetComponent<NetworkPlayerMovement>() : null;
        if (mv == null || !mv.IsOwner || !mv.CanControlNow) return false;
        return Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Space);
    }

    // ==============================================================
    // AYUDAS
    // ==============================================================
    /// <summary>El CUERPO de una rata (su pivote está ~0.8 u bajo el suelo).</summary>
    private static Vector3 BodyOf(Transform t)
    {
        var h = t.GetComponent<NetworkHealth>();
        return h != null ? (Vector3)h.BodyCenter : t.position;
    }

    /// <summary>Velocidad de cada rata medida por posición (en los demás PCs el cuerpo es cinemático).</summary>
    private void TrackRatSpeeds()
    {
        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
        {
            if (m == null || !m.IsSpawned) continue;
            Vector3 p = m.transform.position;
            if (lastRatPos.TryGetValue(m, out var prev))
            {
                float s = (p - prev).magnitude / dt;
                ratSpeed[m] = Mathf.Lerp(ratSpeed.TryGetValue(m, out var o) ? o : 0f, s, 1f - Mathf.Exp(-10f * dt));
            }
            lastRatPos[m] = p;
        }
    }

    private NetworkPlayerMovement FastestRat()
    {
        NetworkPlayerMovement best = null;
        float bestSpeed = flyingSpeed;
        foreach (var kv in ratSpeed)
        {
            if (kv.Key == null || !kv.Key.IsSpawned) continue;
            var h = kv.Key.GetComponent<NetworkHealth>();
            if (h != null && h.IsDead.Value) continue;
            if (kv.Value > bestSpeed) { bestSpeed = kv.Value; best = kv.Key; }
        }
        return best;
    }

    /// <summary>Zoom para ver bien un impacto y las ratas que haya a <paramref name="radius"/> de él.</summary>
    private float ZoomToFitAround(Vector3 at, float radius)
    {
        float need = targetZoom;
        float aspect = cam != null ? Mathf.Max(0.5f, cam.aspect) : 1.7f;
        foreach (var kv in lastRatPos)
        {
            if (kv.Key == null) continue;
            Vector3 d = BodyOf(kv.Key.transform) - at;
            if (d.magnitude > radius) continue;
            need = Mathf.Max(need, Mathf.Abs(d.y) + 1.5f, (Mathf.Abs(d.x) + 1.5f) / aspect);
        }
        return Mathf.Min(need, maxZoom);
    }

    /// <summary>Que la vista no enseñe (casi) nada fuera del mapa.</summary>
    private Vector3 ClampToMap(Vector3 p, float zoom)
    {
        if (terrain == null || cam == null) return p;
        Vector2 size = terrain.WorldSize;
        Vector3 o = terrain.transform.position;
        float minX = o.x - 4f, maxX = o.x + size.x + 4f;
        float minY = o.y - 2f, maxY = o.y + size.y + 14f;   // por arriba dejamos cielo para tiros bombeados
        float halfH = zoom, halfW = zoom * cam.aspect;
        if (maxX - minX > halfW * 2f) p.x = Mathf.Clamp(p.x, minX + halfW, maxX - halfW);
        else p.x = (minX + maxX) * 0.5f;
        if (maxY - minY > halfH * 2f) p.y = Mathf.Clamp(p.y, minY + halfH, maxY - halfH);
        else p.y = (minY + maxY) * 0.5f;
        return p;
    }
}
