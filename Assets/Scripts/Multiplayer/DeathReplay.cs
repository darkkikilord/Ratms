using System.Collections;
using System.Collections.Generic;
using Ratms.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.30 — REPETICIÓN cuando revienta una rata (como la de Worms).
    ///
    ///  Cómo funciona (todo LOCAL, en cada PC):
    ///   1. Grabamos SIEMPRE los últimos ~3,7 s de la pantalla: 30 fotos por
    ///      segundo en un anillo de RenderTextures. Es lo que se vio de verdad:
    ///      terreno, explosión, vísceras...
    ///      0.31: la resolución ya no es 640 px fijos (se veía borroso a pantalla
    ///      completa): es la de la pantalla si cabe en la memoria de la gráfica
    ///      (<see cref="PickWidth"/>), y al reproducir se FUNDE cada foto con la
    ///      siguiente, así la cámara lenta no va a saltos.
    ///   2. Al morir una rata (<see cref="NoteDeath"/>) seguimos grabando 1,4 s
    ///      más para pillar la explosión entera y luego CONGELAMOS el anillo.
    ///   3. Cuando el turno acaba (transición) se reproduce: 1,6 s a velocidad
    ///      normal y, desde un poco antes de la muerte, a CÁMARA LENTA (×0,4),
    ///      con franjas de cine y el rótulo "REPETICIÓN".
    ///   El servidor alarga la transición del turno lo que dura la repetición
    ///   (<see cref="Length"/>) para que nadie se pierda el turno siguiente.
    ///   Cualquier tecla o clic la salta; si empieza TU turno, se corta sola.
    ///
    ///  Se apaga en Opciones ("Repetición a cámara lenta...", <c>opt_slowmo</c>)
    ///  y en las pruebas automáticas (salvo <c>-autoTestSlowmo</c>).
    ///
    ///  0.34:
    ///   • Graba SOLO LA CÁMARA DEL JUEGO (una segunda pasada de render a una
    ///     textura, <c>SubmitRenderRequest</c> de URP): antes era una foto de la
    ///     pantalla y salían el reloj, las barras... de aquel momento.
    ///     Si la gráfica no lo admite, vuelve a la foto de pantalla.
    ///   • Rótulo de la jugada: "¡X revienta a Y!" (<see cref="NoteDeath"/>).
    ///   • También hay repetición de los GOLPAZOS (≥ <see cref="BigHit"/> sin matar).
    ///   • Tecla G durante la repetición: se guarda como GIF animado en
    ///     Documentos\Ratms Repeticiones (<see cref="GifWriter"/>, en otro hilo).
    /// </summary>
    public class DeathReplay : MonoBehaviour
    {
        public const float Before = 2.0f;       // segundos antes de la muerte
        public const float After = 1.4f;        // segundos después
        public const float SlowFrom = 0.4f;     // empieza la cámara lenta este rato antes
        public const float SlowSpeed = 0.4f;
        /// <summary>Lo que dura la repetición entera (en segundos reales).</summary>
        public const float Length = (Before - SlowFrom) + (SlowFrom + After) / SlowSpeed + 0.4f;

        /// <summary>Daño a partir del cual un golpe que NO mata también tiene repetición.</summary>
        public const int BigHit = 50;

        private const int Fps = 30;
        private const int Frames = (int)((Before + After + 0.3f) * Fps);
        /// <summary>Ancho mínimo y máximo de la grabación (px).</summary>
        private const int MinWidth = 800, MaxWidth = 1920;

        private static DeathReplay instance;
        public static bool Playing => instance != null && instance.state == State.Playing;
        /// <summary>Para las pruebas: ¿hay una repetición grabada esperando?</summary>
        public static bool HasPending => instance != null && (instance.state == State.Waiting || instance.state == State.Ready);

        private enum State { Recording, Waiting, Ready, Playing }
        private State state;

        private RenderTexture[] ring;
        private float[] times;
        /// <summary>Dónde estaba la cámara en cada foto (x, y, zoom): solo se funden fotos casi iguales.</summary>
        private Vector3[] camAt;
        private int head, count;
        private RenderTexture grab;
        /// <summary>Destino del render de la cámara (con profundidad); luego se copia al anillo.</summary>
        private RenderTexture camTarget;
        /// <summary>¿La foto está boca abajo? (las fotos de pantalla en Direct3D sí; el render de cámara no).</summary>
        private bool[] flipped;
        private bool cameraPath = true;
        private float nextCapture;
        private string caption;
        private static string pendingCaption;

        // Guardar GIF
        private bool saving;
        private string toast;
        private float toastUntil;
        /// <summary>Para las pruebas: ruta del último GIF guardado (o null).</summary>
        public static string LastGifPath { get; private set; }
        public static bool SavingGif => instance != null && instance.saving;

        private float deathAt, readyAt, vt, lastReal;
        private bool boomPlayed;
        private bool flip;

        private static float holdUntil;
        /// <summary>No graba durante un momento (la prueba automática hace sus propias capturas).</summary>
        public static void HoldRecording(float seconds) => holdUntil = Time.unscaledTime + seconds;

        public static void Ensure()
        {
            if (instance != null || !SlowMotion.Enabled) return;
            instance = new GameObject("Repeticion").AddComponent<DeathReplay>();
        }

        /// <summary>(Todos los PCs) Una rata acaba de morir: guardar la jugada.</summary>
        public static void NoteDeath(Vector2 at) => NoteMoment(at, pendingCaption);

        /// <summary>
        ///  (Todos los PCs) Momento digno de repetición en <paramref name="at"/>
        ///  con un rótulo opcional. Si ya hay una jugada guardada, gana la primera.
        /// </summary>
        public static void NoteMoment(Vector2 at, string text)
        {
            pendingCaption = null;
            if (!SlowMotion.Enabled) return;
            Ensure();
            if (instance == null || instance.state != State.Recording || instance.count < Fps) return;
            instance.state = State.Waiting;
            instance.deathAt = Time.unscaledTime;
            instance.caption = text;
        }

        /// <summary>El rótulo de la PRÓXIMA muerte (lo pone NetworkHealth justo antes de la cámara lenta).</summary>
        public static void SetNextCaption(string text) => pendingCaption = text;

        private void Awake()
        {
            ring = new RenderTexture[Frames];
            times = new float[Frames];
            camAt = new Vector3[Frames];
            flipped = new bool[Frames];
            float aspect = Mathf.Max(1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            int width = PickWidth(aspect);
            int h = Mathf.RoundToInt(width / aspect);
            Debug.Log($"[Repetición] Grabando a {width}×{h}, {Fps} fps ({Frames} fotos, ~{Frames * (long)width * h * 4 / (1024 * 1024)} MB de vídeo)");
            for (int i = 0; i < Frames; i++)
            {
                ring[i] = new RenderTexture(width, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                ring[i].Create();
            }
            camTarget = new RenderTexture(width, h, 24, RenderTextureFormat.ARGB32);
            camTarget.Create();
            // En Direct3D la foto de la pantalla sale boca abajo.
            flip = SystemInfo.graphicsUVStartsAtTop;
            StartCoroutine(Record());
        }

        /// <summary>
        ///  Ancho de la grabación: el de la pantalla (nítido), salvo que el anillo
        ///  entero no quepa en ~15 % de la memoria de la gráfica (tope 512 MB):
        ///  entonces el mayor que quepa. Un PC con gráfica de 8 GB a 1080p graba
        ///  a ~1570 px; uno de 2 GB, a ~1200 px. Nunca baja de 800.
        /// </summary>
        private static int PickWidth(float aspect)
        {
            long vram = Mathf.Max(1024, SystemInfo.graphicsMemorySize);   // MB (0 si no se sabe)
            double budget = Mathf.Clamp(vram * 0.15f, 96f, 512f) * 1024.0 * 1024.0;
            // bytes = Frames × w × (w / aspect) × 4  →  w = √(bytes × aspect / (Frames × 4))
            int fit = (int)System.Math.Sqrt(budget * aspect / (Frames * 4.0));
            int w = Mathf.Min(Screen.width, MaxWidth, fit);
            return Mathf.Max(MinWidth, w & ~1);
        }

        private void OnDestroy()
        {
            if (ring != null) foreach (var r in ring) if (r != null) r.Release();
            if (grab != null) grab.Release();
            if (camTarget != null) camTarget.Release();
            if (instance == this) instance = null;
        }

        private IEnumerator Record()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (cameraPath) continue;   // se graba en LateUpdate (render de la cámara)
                if (state != State.Recording && state != State.Waiting) continue;
                if (Time.unscaledTime < nextCapture || Time.unscaledTime < holdUntil) continue;
                var tm = NetworkTurnManager.Instance;
                if (tm == null || !tm.IsSpawned) continue;
                nextCapture = Time.unscaledTime + 1f / Fps;
                flipped[head] = flip;
                if (grab == null || grab.width != Screen.width || grab.height != Screen.height)
                {
                    if (grab != null) grab.Release();
                    grab = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);
                Graphics.Blit(grab, ring[head]);
                times[head] = Time.unscaledTime;
                var cam = Camera.main;
                camAt[head] = cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, cam.orthographicSize) : Vector3.zero;
                head = (head + 1) % Frames;
                count = Mathf.Min(count + 1, Frames);
            }
        }

        /// <summary>
        ///  0.34: graba la CÁMARA DEL JUEGO a una textura (sin el HUD, que se
        ///  dibuja después encima de la pantalla).
        /// </summary>
        private void LateUpdate()
        {
            if (!cameraPath) return;
            if (state != State.Recording && state != State.Waiting) return;
            if (Time.unscaledTime < nextCapture || Time.unscaledTime < holdUntil) return;
            var tm = NetworkTurnManager.Instance;
            if (tm == null || !tm.IsSpawned) return;
            var cam = Camera.main;
            if (cam == null) return;
            nextCapture = Time.unscaledTime + 1f / Fps;
            if (!RenderCamera(cam))
            {
                cameraPath = false;   // a la antigua: foto de la pantalla
                Debug.Log("[Repetición] La cámara no admite render a textura: grabo la pantalla.");
                return;
            }
            Graphics.Blit(camTarget, ring[head]);
            flipped[head] = false;
            times[head] = Time.unscaledTime;
            camAt[head] = new Vector3(cam.transform.position.x, cam.transform.position.y, cam.orthographicSize);
            head = (head + 1) % Frames;
            count = Mathf.Min(count + 1, Frames);
        }

        /// <summary>0 = aún no se sabe, 1 = StandardRequest, 2 = SingleCameraRequest, 3 = Camera.Render.</summary>
        private int renderWay;

        /// <summary>
        ///  Pinta la cámara en <see cref="camTarget"/>. Prueba las formas que tiene
        ///  URP (según versión y renderer, unas valen y otras no) y se queda con
        ///  la primera que funcione.
        /// </summary>
        private bool RenderCamera(Camera cam)
        {
            if (renderWay == 0 || renderWay == 1)
            {
                var std = new RenderPipeline.StandardRequest { destination = camTarget };
                if (RenderPipeline.SupportsRenderRequest(cam, std))
                {
                    RenderPipeline.SubmitRenderRequest(cam, std);
                    if (renderWay == 0) Debug.Log("[Repetición] Grabo la cámara (StandardRequest).");
                    renderWay = 1;
                    return true;
                }
            }
            if (renderWay == 0 || renderWay == 2)
            {
                var single = new UniversalRenderPipeline.SingleCameraRequest { destination = camTarget };
                if (RenderPipeline.SupportsRenderRequest(cam, single))
                {
                    RenderPipeline.SubmitRenderRequest(cam, single);
                    if (renderWay == 0) Debug.Log("[Repetición] Grabo la cámara (SingleCameraRequest).");
                    renderWay = 2;
                    return true;
                }
            }
            if (renderWay == 0 || renderWay == 3)
            {
                var prev = cam.targetTexture;
                try
                {
                    cam.targetTexture = camTarget;
                    cam.Render();
                }
                catch (System.Exception e)
                {
                    cam.targetTexture = prev;
                    Debug.Log($"[Repetición] Camera.Render falla: {e.Message}");
                    return false;
                }
                cam.targetTexture = prev;
                if (renderWay == 0) Debug.Log("[Repetición] Grabo la cámara (Camera.Render).");
                renderWay = 3;
                return true;
            }
            return false;
        }

        private void Update()
        {
            var tm = NetworkTurnManager.Instance;
            switch (state)
            {
                case State.Waiting:
                    if (Time.unscaledTime >= deathAt + After) { state = State.Ready; readyAt = Time.unscaledTime; }
                    break;
                case State.Ready:
                    // Se pone al acabar el turno (o al acabar la partida); si el
                    // turno sigue mucho rato, se descarta para no estorbar.
                    if (tm != null && (tm.InTransition.Value || tm.GameEnded.Value)) Begin();
                    else if (Time.unscaledTime - readyAt > 15f || tm == null) state = State.Recording;
                    break;
                case State.Playing:
                {
                    float dt = Time.unscaledTime - lastReal;
                    lastReal = Time.unscaledTime;
                    bool slow = vt >= deathAt - SlowFrom;
                    vt += dt * (slow ? SlowSpeed : 1f);
                    if (!boomPlayed && vt >= deathAt) { boomPlayed = true; Sfx.PlayPitched(Sfx.Id.BigExplosion, 0.8f, 0.55f); }
                    bool myTurn = tm != null && !tm.InTransition.Value && tm.CurrentRat != null && tm.CurrentRat.IsOwner;
                    if (Input.GetKeyDown(KeyCode.G) && !saving) StartCoroutine(SaveGif());
                    bool skip = Input.anyKeyDown && !Input.GetKeyDown(KeyCode.G) && vt > deathAt - Before + 0.3f;
                    if (vt >= deathAt + After || myTurn || skip || tm == null) End();
                    break;
                }
            }
        }

        private void Begin()
        {
            state = State.Playing;
            vt = deathAt - Before;
            lastReal = Time.unscaledTime;
            boomPlayed = false;
        }

        private void End()
        {
            state = State.Recording;
            count = 0;   // lo grabado ya no vale (empieza otra jugada)
        }

        /// <summary>
        ///  Foto grabada más cercana (por detrás) al instante t, la SIGUIENTE y
        ///  cuánto falta para llegar a ella (0-1): se dibujan las dos fundidas.
        /// </summary>
        private RenderTexture FrameAt(float t, out RenderTexture next, out float blend) => FrameAt(t, out next, out blend, out _);

        private RenderTexture FrameAt(float t, out RenderTexture next, out float blend, out int index)
        {
            RenderTexture best = null, first = null;
            float bestT = float.MinValue, firstT = float.MaxValue;
            next = null; blend = 0f; index = -1;
            float nextT = float.MaxValue;
            int bestK = -1, nextK = -1, firstK = -1;
            for (int i = 0; i < count; i++)
            {
                int k = (head - 1 - i + Frames) % Frames;
                float ft = times[k];
                if (ft <= t && ft > bestT) { bestT = ft; best = ring[k]; bestK = k; }
                if (ft > t && ft < nextT) { nextT = ft; next = ring[k]; nextK = k; }
                if (ft < firstT) { firstT = ft; first = ring[k]; firstK = k; }
            }
            if (best == null) { next = null; index = firstK; return first; }
            index = bestK;
            // Si hay un hueco grande (p. ej. HoldRecording de las pruebas), no fundir.
            // Y si la CÁMARA se movió mucho entre las dos (un corte, un zoom brusco),
            // fundirlas dejaría una imagen fantasma: entonces se pone la foto tal cual.
            bool similar = false;
            if (next != null && bestK >= 0 && nextK >= 0)
            {
                Vector3 a = camAt[bestK], b = camAt[nextK];
                float z = Mathf.Max(0.5f, a.z);
                similar = Vector2.Distance(a, b) < z * 0.02f && Mathf.Abs(a.z - b.z) < z * 0.02f;
            }
            if (next != null && similar && nextT - bestT < 3f / Fps) blend = Mathf.Clamp01((t - bestT) / (nextT - bestT));
            else next = null;
            return best;
        }

        private void OnGUI()
        {
            if (state != State.Playing) { DrawToast(); return; }
            GUI.depth = -50;   // por encima de todo (también del cartel de victoria)
            var tex = FrameAt(vt, out var nextTex, out float blend, out int texIdx);
            if (tex == null || Event.current.type != EventType.Repaint) return;
            bool flipFrame = texIdx >= 0 && flipped[texIdx];
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            var c = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = c;
            var uv = flipFrame ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(full, tex, uv);
            if (nextTex != null && blend > 0.02f)
            {
                GUI.color = new Color(1f, 1f, 1f, blend);
                GUI.DrawTextureWithTexCoords(full, nextTex, uv);
                GUI.color = c;
            }

            bool slow = vt >= deathAt - SlowFrom;
            // Tono sepia suave: se nota que es una repetición
            GUI.color = new Color(0.45f, 0.3f, 0.12f, 0.16f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);

            TrenchGUI.Begin();
            float vw = TrenchGUI.VW, vh = TrenchGUI.VH, bar = 76f;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, vw, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, vh - bar, vw, bar), Texture2D.whiteTexture);
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            GUI.color = blink ? new Color(0.9f, 0.12f, 0.1f) : new Color(0.4f, 0.05f, 0.05f);
            GUI.DrawTexture(new Rect(34f, 26f, 22f, 22f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.9f, 0.6f);
            TrenchGUI.Label(new Rect(70f, 16f, 600f, 44f), slow ? "REPETICIÓN  ·  CÁMARA LENTA" : "REPETICIÓN");
            // 0.34: rótulo de la jugada ("¡X revienta a Y!")
            if (!string.IsNullOrEmpty(caption))
            {
                GUI.color = new Color(1f, 0.85f, 0.4f);
                var al = TrenchGUI.LabelStyle.alignment;
                TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
                TrenchGUI.Label(new Rect(0f, vh - bar + 16f, vw, 44f), caption);
                TrenchGUI.LabelStyle.alignment = al;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            TrenchGUI.Small(new Rect(vw - 470f, vh - bar + 24f, 440f, 30f), saving ? "Guardando GIF…" : "G: guardar GIF  ·  otra tecla: saltar");
            GUI.color = c;
            TrenchGUI.End();
            DrawToast();
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil) return;
            GUI.depth = -50;
            TrenchGUI.Begin();
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            var r = new Rect(TrenchGUI.VW * 0.5f - 520f, 96f, 1040f, 44f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.92f, 0.6f);
            TrenchGUI.Small(new Rect(r.x + 16f, r.y + 7f, r.width - 32f, 30f), toast);
            GUI.color = c;
            TrenchGUI.End();
        }

        // ------------------------------------------------------------
        //  0.34 — GUARDAR COMO GIF
        // ------------------------------------------------------------
        /// <summary>Para las pruebas: guarda la repetición que se está viendo.</summary>
        public static void SaveGifForTest() { if (instance != null && instance.state == State.Playing && !instance.saving) instance.StartCoroutine(instance.SaveGif()); }

        /// <summary>
        ///  Guarda la repetición tal cual se ve (normal y luego cámara lenta) en un
        ///  GIF de 480 px a 20 fps. Pasos: (1) por cada foto del anillo que haga
        ///  falta, copia pequeña en la gráfica y lectura asíncrona (no congela el
        ///  juego); (2) en OTRO HILO, el GIF se codifica y se escribe a disco.
        /// </summary>
        private IEnumerator SaveGif()
        {
            saving = true;
            const int outW = 480;
            const float outStep = 0.05f;   // 20 fps
            float aspect = ring[0].width / (float)ring[0].height;
            int outH = Mathf.RoundToInt(outW / aspect) & ~1;

            // Línea de tiempo igual que al reproducir: normal y luego ×0,4.
            var seq = new List<int>();
            float t = deathAt - Before;
            while (t < deathAt + After)
            {
                FrameAt(t, out _, out _, out int k);
                if (k >= 0) seq.Add(k);
                t += outStep * (t >= deathAt - SlowFrom ? SlowSpeed : 1f);
            }
            var unique = new List<int>(new HashSet<int>(seq));
            var data = new Dictionary<int, byte[]>();
            var temps = new List<RenderTexture>();
            int pending = 0;
            foreach (int k in unique)
            {
                var small = RenderTexture.GetTemporary(outW, outH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(ring[k], small);
                temps.Add(small);
                bool rowsTopFirst = flipped[k];   // foto de pantalla en D3D: ya viene con la fila de arriba primero
                int key = k;
                pending++;
                UnityEngine.Rendering.AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, req =>
                {
                    pending--;
                    if (req.hasError) return;
                    var src = req.GetData<byte>();
                    var bytes = new byte[outW * outH * 4];
                    int row = outW * 4;
                    for (int y = 0; y < outH; y++)
                    {
                        int sy = rowsTopFirst ? y : outH - 1 - y;
                        Unity.Collections.NativeArray<byte>.Copy(src, sy * row, bytes, y * row, row);
                    }
                    data[key] = bytes;
                });
            }
            float waitUntil = Time.unscaledTime + 5f;
            while (pending > 0 && Time.unscaledTime < waitUntil) yield return null;
            foreach (var r in temps) RenderTexture.ReleaseTemporary(r);

            string dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "Ratms Repeticiones");
            string path = System.IO.Path.Combine(dir, $"ratms_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.gif");
            var frames = new List<byte[]>();
            foreach (int k in seq) if (data.TryGetValue(k, out var b)) frames.Add(b);
            string error = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    using var gif = new GifWriter(path, outW, outH);
                    foreach (var f in frames) gif.AddFrame(f, 5);
                }
                catch (System.Exception e) { error = e.Message; }
            });
            while (!task.IsCompleted) yield return null;
            saving = false;
            if (error == null && frames.Count > 0)
            {
                LastGifPath = path;
                Career.OnGifSaved();   // 0.35
                toast = Loc.F("Repetición guardada: {0}", path);
                Debug.Log($"[Repetición] GIF guardado ({frames.Count} fotogramas): {path}");
            }
            else
            {
                toast = Loc.T("No se pudo guardar la repetición.");
                Debug.LogWarning($"[Repetición] No se pudo guardar el GIF: {error ?? "sin fotogramas"}");
            }
            toastUntil = Time.unscaledTime + 6f;
        }
    }
}
