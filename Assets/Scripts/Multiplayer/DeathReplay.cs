using System.Collections;
using Ratms.UI;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.30 — REPETICIÓN cuando revienta una rata (como la de Worms).
    ///
    ///  Cómo funciona (todo LOCAL, en cada PC):
    ///   1. Grabamos SIEMPRE los últimos ~3,6 s de la pantalla: 25 fotos por
    ///      segundo a 640 px de ancho en un anillo de RenderTextures (≈ 80 MB
    ///      de vídeo). Es lo que se vio de verdad: terreno, explosión, vísceras...
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

        private const int Fps = 25;
        private const int Frames = (int)((Before + After + 0.3f) * Fps);
        private const int Width = 640;

        private static DeathReplay instance;
        public static bool Playing => instance != null && instance.state == State.Playing;
        /// <summary>Para las pruebas: ¿hay una repetición grabada esperando?</summary>
        public static bool HasPending => instance != null && (instance.state == State.Waiting || instance.state == State.Ready);

        private enum State { Recording, Waiting, Ready, Playing }
        private State state;

        private RenderTexture[] ring;
        private float[] times;
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
            float aspect = Mathf.Max(1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            int h = Mathf.RoundToInt(Width / aspect);
            for (int i = 0; i < Frames; i++)
            {
                ring[i] = new RenderTexture(Width, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
                ring[i].Create();
            }
            // En Direct3D la foto de la pantalla sale boca abajo.
            flip = SystemInfo.graphicsUVStartsAtTop;
            StartCoroutine(Record());
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

        /// <summary>Foto grabada más cercana (por detrás) al instante t.</summary>
        private RenderTexture FrameAt(float t)
        {
            RenderTexture best = null;
            float bestT = float.MinValue, firstT = float.MaxValue;
            RenderTexture first = null;
            for (int i = 0; i < count; i++)
            {
                int k = (head - 1 - i + Frames) % Frames;
                float ft = times[k];
                if (ft <= t && ft > bestT) { bestT = ft; best = ring[k]; }
                if (ft < firstT) { firstT = ft; first = ring[k]; }
            }
            return best != null ? best : first;
        }

        private void OnGUI()
        {
            if (state != State.Playing) return;
            GUI.depth = -50;   // por encima de todo (también del cartel de victoria)
            var tex = FrameAt(vt);
            if (tex == null || Event.current.type != EventType.Repaint) return;
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            var c = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(full, tex, flip ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f));

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
