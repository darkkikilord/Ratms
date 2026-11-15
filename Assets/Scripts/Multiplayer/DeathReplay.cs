using System.Collections;
using Ratms.UI;
using UnityEngine;

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
    /// </summary>
    public class DeathReplay : MonoBehaviour
    {
        public const float Before = 2.0f;       // segundos antes de la muerte
        public const float After = 1.4f;        // segundos después
        public const float SlowFrom = 0.4f;     // empieza la cámara lenta este rato antes
        public const float SlowSpeed = 0.4f;
        /// <summary>Lo que dura la repetición entera (en segundos reales).</summary>
        public const float Length = (Before - SlowFrom) + (SlowFrom + After) / SlowSpeed + 0.4f;

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
        private float nextCapture;

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
        public static void NoteDeath(Vector2 at)
        {
            if (!SlowMotion.Enabled) return;
            Ensure();
            if (instance == null || instance.state != State.Recording || instance.count < Fps) return;
            instance.state = State.Waiting;
            instance.deathAt = Time.unscaledTime;
        }

        private void Awake()
        {
            ring = new RenderTexture[Frames];
            times = new float[Frames];
            camAt = new Vector3[Frames];
            float aspect = Mathf.Max(1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            int width = PickWidth(aspect);
            int h = Mathf.RoundToInt(width / aspect);
            Debug.Log($"[Repetición] Grabando a {width}×{h}, {Fps} fps ({Frames} fotos, ~{Frames * (long)width * h * 4 / (1024 * 1024)} MB de vídeo)");
            for (int i = 0; i < Frames; i++)
            {
                ring[i] = new RenderTexture(width, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                ring[i].Create();
            }
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
            if (instance == this) instance = null;
        }

        private IEnumerator Record()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (state != State.Recording && state != State.Waiting) continue;
                if (Time.unscaledTime < nextCapture || Time.unscaledTime < holdUntil) continue;
                var tm = NetworkTurnManager.Instance;
                if (tm == null || !tm.IsSpawned) continue;
                nextCapture = Time.unscaledTime + 1f / Fps;
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
                    bool skip = Input.anyKeyDown && vt > deathAt - Before + 0.3f;
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
        private RenderTexture FrameAt(float t, out RenderTexture next, out float blend)
        {
            RenderTexture best = null, first = null;
            float bestT = float.MinValue, firstT = float.MaxValue;
            next = null; blend = 0f;
            float nextT = float.MaxValue;
            int bestK = -1, nextK = -1;
            for (int i = 0; i < count; i++)
            {
                int k = (head - 1 - i + Frames) % Frames;
                float ft = times[k];
                if (ft <= t && ft > bestT) { bestT = ft; best = ring[k]; bestK = k; }
                if (ft > t && ft < nextT) { nextT = ft; next = ring[k]; nextK = k; }
                if (ft < firstT) { firstT = ft; first = ring[k]; }
            }
            if (best == null) { next = null; return first; }
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
            if (state != State.Playing) return;
            GUI.depth = -50;   // por encima de todo (también del cartel de victoria)
            var tex = FrameAt(vt, out var nextTex, out float blend);
            if (tex == null || Event.current.type != EventType.Repaint) return;
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            var c = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = c;
            var uv = flip ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
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
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            TrenchGUI.Small(new Rect(vw - 470f, vh - bar + 24f, 440f, 30f), "Cualquier tecla: saltar");
            GUI.color = c;
            TrenchGUI.End();
        }
    }
}
