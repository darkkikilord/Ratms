using System.Collections;
using System.Collections.Generic;
using Ratms.UI;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>0.27: clima de la partida (se elige en las reglas; viaja por red con el bioma).</summary>
    public enum Weather : byte { Despejado, Lluvia, Niebla, Tormenta, Nieve }

    /// <summary>
    ///  0.30 — EL CLIMA CAMBIA EL JUEGO (no solo se ve). Todo sale de
    ///  <see cref="WeatherFx.Current"/>, que es igual en todos los PCs:
    ///   • Lluvia: el fuego dura la mitad y el gas se disuelve antes.
    ///   • Nieve: el suelo RESBALA (cuesta arrancar y frenar) y las caídas duelen menos.
    ///   • Niebla: no ves la vida ni el nombre de las ratas enemigas lejanas.
    ///   • Tormenta: el viento sopla siempre fuerte, el fuego dura menos y caen rayos.
    /// </summary>
    public static class WeatherRules
    {
        public static Weather Now => WeatherFx.Current;
        public static float FireLife => Now switch { Weather.Lluvia => 0.5f, Weather.Tormenta => 0.6f, Weather.Nieve => 0.8f, _ => 1f };
        public static float GasLife => Now switch { Weather.Lluvia => 0.6f, Weather.Tormenta => 0.75f, _ => 1f };
        public static float FallDamage => Now == Weather.Nieve ? 0.6f : 1f;
        public static bool Slippery => Now == Weather.Nieve;
        public const float FogSight = 10f;

        /// <summary>Texto del aviso al empezar la partida (español; se traduce al mostrarlo).</summary>
        public static string Describe(Weather w) => w switch
        {
            Weather.Lluvia => "LLUVIA\nEl fuego y el gas duran menos",
            Weather.Niebla => "NIEBLA\nNo ves la vida de los enemigos lejanos",
            Weather.Tormenta => "TORMENTA\nViento fuerte y ¡cuidado con los rayos!",
            Weather.Nieve => "NIEVE\nEl suelo resbala y las caídas duelen menos",
            _ => null,
        };

        private static int fogFrame = -1;
        private static int fogTeam = -1;
        private static readonly List<Vector2> fogEyes = new();

        /// <summary>
        ///  ¿Esconde la niebla la vida/nombre de esta rata? Solo a las enemigas
        ///  que estén a más de <see cref="FogSight"/> u de TODAS las ratas del
        ///  equipo que mira (el tuyo online; el del turno en partida local).
        /// </summary>
        public static bool FogHides(NetworkPlayerMovement m)
        {
            if (Now != Weather.Niebla || m == null) return false;
            if (fogFrame != Time.frameCount)
            {
                fogFrame = Time.frameCount;
                fogEyes.Clear();
                fogTeam = -1;
                var tm = NetworkTurnManager.Instance;
                var cur = tm != null ? tm.CurrentRat : null;
                var nm = NetworkManager.Singleton;
                bool local = nm != null && nm.IsServer && nm.ConnectedClientsIds.Count <= 1;
                if (cur != null && (local || cur.IsOwner)) fogTeam = cur.TeamIndex.Value;
                var all = Object.FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None);
                if (fogTeam < 0)
                    foreach (var r in all) if (r.IsOwner && !r.IsTestDummy) { fogTeam = r.TeamIndex.Value; break; }
                foreach (var r in all)
                {
                    if (r.TeamIndex.Value != fogTeam || r.IsTestDummy) continue;
                    var h = r.GetComponent<NetworkHealth>();
                    if (h != null && !h.IsDead.Value) fogEyes.Add(h.BodyCenter);
                }
            }
            if (fogTeam < 0 || m.TeamIndex.Value == fogTeam) return false;
            var mh = m.GetComponent<NetworkHealth>();
            Vector2 p = mh != null ? mh.BodyCenter : (Vector2)m.transform.position;
            foreach (var e in fogEyes) if ((e - p).sqrMagnitude < FogSight * FogSight) return false;
            return true;
        }
    }

    // =====================================================================
    //  CÁMARA LENTA
    // =====================================================================
    /// <summary>
    ///  0.27 — CÁMARA LENTA al reventar una rata: el juego va a cámara lenta
    ///  un momento, la cámara se acerca y salen franjas de cine. Pasa a la vez
    ///  en todos los PCs (lo dispara la muerte, que llega a todos). Se puede
    ///  quitar en Opciones; en las pruebas automáticas está apagada.
    /// </summary>
    public class SlowMotion : MonoBehaviour
    {
        private static SlowMotion instance;
        private static float lastAt = -99f;
        private float until, bars;
        private bool active;

        public static bool Enabled => PlayerPrefs.GetInt("opt_slowmo", 1) == 1 && !IsAutoTest;
        private static bool? autoTest;
        private static bool IsAutoTest => autoTest ??= System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ratmsAutoTest") >= 0
                                                        && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autoTestSlowmo") < 0;

        public static void Trigger(Vector2 at)
        {
            if (!Enabled || Time.unscaledTime - lastAt < 5f) return;
            lastAt = Time.unscaledTime;
            if (instance == null) instance = new GameObject("CamaraLenta").AddComponent<SlowMotion>();
            instance.StartCoroutine(instance.Run(at));
        }

        /// <summary>
        ///  0.30: ya NO se ralentiza el juego en directo (con Time.timeScale el
        ///  anfitrión iba lento y descuadraba los turnos). Ahora la cámara se
        ///  acerca a la explosión y la CÁMARA LENTA se ve en la REPETICIÓN
        ///  (<see cref="DeathReplay"/>) al acabar el turno.
        /// </summary>
        private IEnumerator Run(Vector2 at)
        {
            CameraFollow.Instance?.FocusOnImpact(at, 1.4f);
            DeathReplay.NoteDeath(at);
            yield break;
        }

        private void OnDestroy() { if (active) Time.timeScale = 1f; }

        private void Update()
        {
            bars = Mathf.MoveTowards(bars, active ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            // Si alguien pausa o cambia de escena a mitad, que no se quede lento
            if (active && Time.unscaledTime > until + 2f) { Time.timeScale = 1f; active = false; }
        }

        private void OnGUI()
        {
            if (bars <= 0.01f) return;
            TrenchGUI.Begin();
            float h = 70f * bars;
            var c = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, TrenchGUI.VW, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, TrenchGUI.VH - h, TrenchGUI.VW, h), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.4f, bars);
            TrenchGUI.Label(new Rect(30f, TrenchGUI.VH - h + 16f, 600f, 40f), "¡CÁMARA LENTA!");
            GUI.color = c;
            TrenchGUI.End();
        }
    }

    // =====================================================================
    //  BOCADILLOS (lo que dicen las ratas)
    // =====================================================================
    /// <summary>0.27 — bocadillos de cómic encima de las ratas (burlas, frases al empezar el turno).</summary>
    public class SpeechBubbles : MonoBehaviour
    {
        private struct Bubble { public Transform who; public string text; public float until, born; }
        private static SpeechBubbles instance;
        private readonly List<Bubble> bubbles = new();
        private GUIStyle style;
        private static Texture2D paper;

        public static void Say(Transform who, string text, float seconds = 2.6f)
        {
            if (who == null || string.IsNullOrEmpty(text)) return;
            if (instance == null) instance = new GameObject("Bocadillos").AddComponent<SpeechBubbles>();
            instance.bubbles.RemoveAll(b => b.who == who);
            instance.bubbles.Add(new Bubble { who = who, text = text, until = Time.unscaledTime + seconds, born = Time.unscaledTime });
        }

        private void OnGUI()
        {
            if (bubbles.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            float k = Mathf.Max(0.6f, Screen.height / 1080f);
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, font = TrenchGUI.Typewriter };
                style.normal.textColor = new Color(0.12f, 0.08f, 0.05f);
            }
            if (paper == null) { paper = new Texture2D(1, 1); paper.SetPixel(0, 0, Color.white); paper.Apply(); }
            style.fontSize = Mathf.RoundToInt(20 * k);
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                if (b.who == null || !b.who.gameObject.activeInHierarchy || Time.unscaledTime > b.until) { bubbles.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(b.who.position + Vector3.up * 1.35f);
                if (sp.z < 0f) continue;
                float pop = Mathf.Clamp01((Time.unscaledTime - b.born) * 8f);
                float fade = Mathf.Clamp01((b.until - Time.unscaledTime) * 3f);
                var size = style.CalcSize(new GUIContent(b.text));
                float w = Mathf.Min(size.x + 26f * k, 360f * k);
                float h = style.CalcHeight(new GUIContent(b.text), w - 20f * k) + 16f * k;
                w *= Mathf.Lerp(0.6f, 1f, pop); h *= Mathf.Lerp(0.6f, 1f, pop);
                var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h - 14f * k, w, h);
                var c = GUI.color;
                GUI.color = new Color(0.1f, 0.07f, 0.05f, fade);   // borde
                GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), paper);
                GUI.DrawTexture(new Rect(sp.x - 7f * k, r.yMax, 14f * k, 9f * k), paper);
                GUI.color = new Color(1f, 0.97f, 0.88f, fade);   // papel
                GUI.DrawTexture(r, paper);
                GUI.DrawTexture(new Rect(sp.x - 4f * k, r.yMax, 8f * k, 6f * k), paper);
                GUI.color = new Color(1f, 1f, 1f, fade);
                GUI.Label(new Rect(r.x + 10f * k, r.y, r.width - 20f * k, r.height), b.text, style);
                GUI.color = c;
            }
        }
    }

    // =====================================================================
    //  BURLAS (tecla P) y frases
    // =====================================================================
    /// <summary>0.27 — P = provocar: tu rata del turno suelta una burla con su voz.</summary>
    public class TauntInput : MonoBehaviour
    {
        private float next;

        private void Update()
        {
            if (!Controls.Down(Controls.Act.Taunt) || Time.unscaledTime < next) return;
            if (GUIUtility.keyboardControl != 0 || PauseMenu.IsOpen) return;
            var tm = NetworkTurnManager.Instance;
            var nm = NetworkManager.Singleton;
            if (tm == null || nm == null || !tm.IsSpawned) return;
            var rat = tm.CurrentRat;
            if (rat == null || !rat.IsOwner) return;
            next = Time.unscaledTime + 2f;
            tm.TauntServerRpc(rat.NetworkObjectId);
            Career.OnTaunt();   // 0.35
        }
    }

    public static class RatLines
    {
        public static readonly string[] Taunts =
        {
            "¡Ven aquí si tienes bigotes!", "¡Tu madre era un hámster!", "¡Huele a queso rancio!", "¡Te voy a dejar sin cola!",
            "¡Ni con mira láser me das!", "¡Cobarde de alcantarilla!", "¡Soy la reina de la cloaca!", "¡A llorar al desagüe!",
            "¡Ratonzuelo de laboratorio!", "¡Esta trinchera es mía!", "¡Ñiec, ñiec, ñiec!", "¡Apuntas como un topo!",
        };
        public static readonly string[] TurnStart =
        {
            "¡A por ellos!", "¡Por el queso!", "Esto va a doler...", "¡Al ataque!", "¿Quién quiere guerra?",
            "Huele a victoria", "Ni un paso atrás", "¡Que tiemble la cloaca!", "Mamá, ¡mírame!", "Vamos allá...",
        };
        public static string Get(int code)
        {
            var arr = code >= 100 ? TurnStart : Taunts;
            return arr[(code % 100) % arr.Length];
        }
    }

    // =====================================================================
    //  LÁPIDAS con nombre
    // =====================================================================
    /// <summary>0.27 — nombre de la rata sobre su lápida (se ve de cerca).</summary>
    public class GravestoneName : MonoBehaviour
    {
        public string ratName;
        private static GUIStyle style;

        private void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null || string.IsNullOrEmpty(ratName) || cam.orthographicSize > 9f) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 0.62f);
            if (sp.z < 0f || sp.x < -100 || sp.x > Screen.width + 100) return;
            float k = Mathf.Max(0.6f, Screen.height / 1080f);
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, font = TrenchGUI.Typewriter };
            style.fontSize = Mathf.RoundToInt(14 * k);
            var r = new Rect(sp.x - 100f, Screen.height - sp.y - 24f * k, 200f, 22f * k);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), "† " + ratName, style);
            style.normal.textColor = new Color(0.9f, 0.88f, 0.82f);
            GUI.Label(r, "† " + ratName, style);
        }
    }

    /// <summary>0.27 — rayo en zigzag desde el cielo hasta el suelo (se apaga en un momento).</summary>
    public class LightningBolt : MonoBehaviour
    {
        private LineRenderer lr;
        private float life = 0.45f;

        public static void Spawn(Vector2 at)
        {
            var go = new GameObject("Rayo");
            var b = go.AddComponent<LightningBolt>();
            b.lr = go.AddComponent<LineRenderer>();
            b.lr.material = new Material(Shader.Find("Sprites/Default"));
            b.lr.widthMultiplier = 0.16f;
            b.lr.sortingOrder = 70;
            const int n = 14;
            b.lr.positionCount = n;
            float top = at.y + 18f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float x = at.x + (i == 0 || i == n - 1 ? 0f : Random.Range(-0.7f, 0.7f));
                b.lr.SetPosition(i, new Vector3(x, Mathf.Lerp(top, at.y, t), 0f));
            }
        }

        private void Update()
        {
            life -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(life / 0.45f);
            bool on = (int)(life * 30f) % 3 != 0;   // parpadea
            lr.startColor = lr.endColor = new Color(0.85f, 0.92f, 1f, on ? a : a * 0.3f);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    // =====================================================================
    //  CLIMA
    // =====================================================================
    /// <summary>
    ///  0.27 — lo que se VE y se OYE del clima (en cada PC): lluvia, niebla,
    ///  tormenta (lluvia fuerte, cielo oscuro y relámpagos) o nieve. Los rayos
    ///  que caen de verdad los decide el servidor (NetworkTurnManager.Spectacle).
    /// </summary>
    public class WeatherFx : MonoBehaviour
    {
        private struct Drop { public Transform t; public Vector2 vel; public float sway; }
        private static WeatherFx instance;
        private readonly List<Drop> drops = new();
        private Weather built = (Weather)255;
        private Sprite rainSprite, snowSprite;
        private AudioSource rainLoop;
        private float flash;
        private static Texture2D fogTex;

        public static Weather Current = Weather.Despejado;

        public static void Ensure()
        {
            if (instance == null) instance = new GameObject("Clima").AddComponent<WeatherFx>();
        }

        public static void Flash() { if (instance != null) instance.flash = 1f; }

        private void Update()
        {
            if (built != Current) Rebuild();
            var cam = Camera.main;
            if (cam == null || drops.Count == 0) { flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 3f); return; }
            float hh = cam.orthographicSize, hw = hh * cam.aspect;
            Vector2 c = cam.transform.position;
            float wind = NetworkTurnManager.WindAcceleration;
            bool snow = Current == Weather.Nieve;
            foreach (var d in drops)
            {
                Vector3 p = d.t.position;
                Vector2 v = d.vel + new Vector2(wind * (snow ? 0.6f : 0.9f), 0f);
                if (snow) v.x += Mathf.Sin(Time.time * 1.7f + d.sway) * 0.6f;
                p += (Vector3)(v * Time.deltaTime);
                // Envolver alrededor de la cámara (las gotas nunca se acaban)
                if (p.y < c.y - hh - 1f) { p.y += hh * 2f + 2f; p.x = c.x + Random.Range(-hw - 2f, hw + 2f); }
                if (p.x < c.x - hw - 2f) p.x += hw * 2f + 4f;
                if (p.x > c.x + hw + 2f) p.x -= hw * 2f + 4f;
                if (p.y > c.y + hh + 3f) p.y -= hh * 2f + 4f;
                d.t.position = p;
                float s = Mathf.Max(0.6f, hh / 6f);
                d.t.localScale = new Vector3(s, s, 1f);
                if (!snow) d.t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + 90f);
            }
            flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 3f);
            if (Current == Weather.Tormenta && Random.value < Time.deltaTime * 0.05f) { flash = 0.6f; Sfx.Play(Sfx.Id.Thunder, 0.35f, 0.2f); }   // relámpago lejano (solo visual)
        }

        private void Rebuild()
        {
            built = Current;
            foreach (var d in drops) if (d.t != null) Destroy(d.t.gameObject);
            drops.Clear();
            if (rainLoop != null) { Destroy(rainLoop); rainLoop = null; }
            int n = Current switch { Weather.Lluvia => 220, Weather.Tormenta => 380, Weather.Nieve => 160, _ => 0 };
            if (n == 0) return;
            bool snow = Current == Weather.Nieve;
            var sprite = snow ? (snowSprite ??= Dot(2, 2, new Color32(240, 244, 255, 230))) : (rainSprite ??= Dot(1, 7, new Color32(170, 200, 235, 150)));
            var cam = Camera.main;
            Vector2 c = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
            float hh = cam != null ? cam.orthographicSize : 8f, hw = hh * 1.8f;
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("gota");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = 60;
                go.transform.position = new Vector3(c.x + Random.Range(-hw, hw), c.y + Random.Range(-hh, hh), 0f);
                var vel = snow ? new Vector2(0f, -Random.Range(0.9f, 1.8f)) : new Vector2(0f, -Random.Range(14f, 19f));
                drops.Add(new Drop { t = go.transform, vel = vel, sway = Random.value * 10f });
            }
            if (!snow && !Application.isBatchMode)
            {
                rainLoop = gameObject.AddComponent<AudioSource>();
                rainLoop.clip = Sfx.Clip(Sfx.Id.Rain);
                rainLoop.loop = true;
                rainLoop.volume = (Current == Weather.Tormenta ? 0.5f : 0.3f) * PlayerPrefs.GetFloat("opt_master", 0.8f) * PlayerPrefs.GetFloat("opt_sfx", 0.8f);
                rainLoop.Play();
            }
        }

        private static Sprite Dot(int w, int h, Color32 c)
        {
            var t = new Texture2D(w, h) { filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
        }

        private void OnGUI()
        {
            if (Current == Weather.Despejado && flash <= 0f) return;
            GUI.depth = 50;   // 0.30.1: POR DETRÁS de menús, arsenal y marcadores (antes los tapaba)
            var c = GUI.color;
            if (Current == Weather.Niebla)
            {
                if (fogTex == null) fogTex = FogTexture();
                GUI.color = new Color(0.8f, 0.83f, 0.86f, 0.55f);   // más suave
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), fogTex);
            }
            else if (Current == Weather.Tormenta)
            {
                GUI.color = new Color(0.05f, 0.06f, 0.12f, 0.28f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            if (flash > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, flash * 0.55f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            GUI.color = c;
        }

        /// <summary>Niebla: transparente en el centro y espesa hacia los bordes.</summary>
        private static Texture2D FogTexture()
        {
            const int S = 64;
            var t = new Texture2D(S, S) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S * 0.5f) / (S * 0.5f), dy = (y - S * 0.5f) / (S * 0.5f);
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                    float a = Mathf.Clamp01((d - 0.5f) / 0.6f);   // centro despejado más grande
                    a = a * a * (3f - 2f * a) * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }
    }
}
