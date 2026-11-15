using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.29 — SE ENTIENDE LO QUE PASA. Iconos pixel-art de estado encima de
    ///  cada rata (le falta una pata, un brazo, está tuerta, envenenada...),
    ///  justo debajo de su vida. Se ven de un vistazo sin tener que fijarse en
    ///  el dibujo de la rata.
    /// </summary>
    public class StatusBadges : MonoBehaviour
    {
        private NetworkHealth health;
        private readonly List<SpriteRenderer> icons = new();
        private int lastSig = -1;
        private Transform row;

        private NetworkPlayerMovement fogMove;
        private void Awake() { health = GetComponent<NetworkHealth>(); fogMove = GetComponent<NetworkPlayerMovement>(); }

        private void LateUpdate()
        {
            if (health == null) return;
            bool alive = health.IsSpawned && !health.IsDead.Value && gameObject.activeInHierarchy;
            int sig = !alive ? 0 : (health.Injuries.Value | (health.Poisoned ? 256 : 0));
            if (sig != lastSig) { lastSig = sig; Rebuild(); }
            if (row == null) return;
            bool fog = fogMove != null && WeatherRules.FogHides(fogMove);   // 0.30
            if (fog == row.gameObject.activeSelf && lastSig != 0) row.gameObject.SetActive(!fog);
            row.position = new Vector3(transform.position.x, transform.position.y + 1.32f, -4f);
            var cam = Camera.main;
            float k = cam != null ? Mathf.Clamp(cam.orthographicSize / 6f, 0.8f, 1.8f) : 1f;   // legibles también de lejos
            row.localScale = Vector3.one * k;
        }

        private void Rebuild()
        {
            foreach (var i in icons) if (i != null) Destroy(i.gameObject);
            icons.Clear();
            if (lastSig == 0) { if (row != null) row.gameObject.SetActive(false); return; }
            if (row == null) row = new GameObject("Estado").transform;
            row.gameObject.SetActive(true);
            var list = new List<Sprite>();
            for (int n = 0; n < health.LegsLost; n++) list.Add(health.HasPegLeg && n == 0 ? Badge.PegLeg() : Badge.Leg());
            for (int n = 0; n < health.ArmsLost; n++) list.Add(health.HasHook && n == 0 ? Badge.Hook() : Badge.Arm());
            if (health.HeadHurt) list.Add(Badge.Eye());
            if (health.Poisoned) list.Add(Badge.Poison());
            float w = 0.3f;
            for (int i = 0; i < list.Count; i++)
            {
                var go = new GameObject("icono");
                go.transform.SetParent(row, false);
                go.transform.localPosition = new Vector3((i - (list.Count - 1) * 0.5f) * w, 0f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = list[i];
                sr.sortingOrder = 950;
                icons.Add(sr);
            }
        }

        private void OnDestroy() { if (row != null) Destroy(row.gameObject); }
    }

    /// <summary>
    ///  0.29 — FLECHA DEL TURNO (como en Worms): al empezar un turno, una flecha
    ///  del color del equipo bota encima de la rata que juega; después se queda
    ///  un marcador pequeño mientras dure el turno.
    /// </summary>
    public class TurnMarker : MonoBehaviour
    {
        private SpriteRenderer arrow;
        private ulong lastRat = ulong.MaxValue;
        private float shownAt;

        private void LateUpdate()
        {
            var tm = NetworkTurnManager.Instance;
            var rat = tm != null && tm.IsSpawned && !tm.InTransition.Value ? tm.CurrentRat : null;
            if (rat == null || !rat.gameObject.activeInHierarchy)
            {
                if (arrow != null) arrow.enabled = false;
                lastRat = ulong.MaxValue;
                return;
            }
            if (arrow == null)
            {
                arrow = new GameObject("FlechaTurno").AddComponent<SpriteRenderer>();
                arrow.sprite = Badge.Arrow();
                arrow.sortingOrder = 960;
            }
            if (rat.NetworkObjectId != lastRat)
            {
                lastRat = rat.NetworkObjectId; shownAt = Time.time;
                RatVoice.Say(rat, RatVoice.Kind.Hey, 0.6f);   // 0.33: "¡eh!" al empezar su turno
            }
            float t = Time.time - shownAt;
            bool big = t < 3f;
            float bounce = Mathf.Abs(Mathf.Sin(Time.time * (big ? 7f : 3f))) * (big ? 0.35f : 0.12f);
            var cam = Camera.main;
            float k = cam != null ? Mathf.Clamp(cam.orthographicSize / 6f, 0.8f, 2f) : 1f;
            arrow.enabled = !rat.FiredThisTurn || big;
            arrow.color = (Color)RatAppearance.ColorForTeam(Mathf.Max(0, rat.TeamIndex.Value)) * new Color(1f, 1f, 1f, big ? 1f : 0.7f) + new Color(0.25f, 0.25f, 0.25f, 0f);
            arrow.transform.position = new Vector3(rat.transform.position.x, rat.transform.position.y + 2.25f * Mathf.Max(1f, k * 0.8f) + bounce, -4f);
            arrow.transform.localScale = Vector3.one * (big ? 1.6f : 1f) * k;
        }
    }

    /// <summary>Iconos pixel-art de 7×7 (32 px por unidad, contorno oscuro).</summary>
    public static class Badge
    {
        private static readonly Dictionary<string, Sprite> cache = new();

        public static Sprite Leg() => Make("leg", "..ooo..", ".oPPo..", ".oPPo..", ".oPPo..", ".oPPoo.", ".oPPPPo", "ooooooo", true);
        public static Sprite PegLeg() => Make("peg", "..ooo..", ".oPPo..", ".oPPo..", "..oWo..", "..oWo..", "..oWo..", "..ooo..", false);
        public static Sprite Arm() => Make("arm", "ooo....", "oPPoooo", "oPPPPPo", "ooooPPo", "...oPPo", "...oooo", ".......", true);
        public static Sprite Hook() => Make("hook", ".......", "oooo...", "oPPMMo.", "oooo.Mo", ".....Mo", "...oMo.", "...oo..", false);
        public static Sprite Eye() => Make("eye", ".......", ".ooooo.", "oCCCCCo", "oCCKCCo", "oCCCCCo", ".ooooo.", ".......", true);
        public static Sprite Poison() => Make("poison", "..ooo..", ".oGGGo.", "oGoGoGo", "oGGGGGo", ".oGoGo.", ".oGGGo.", "..ooo..", false);

        public static Sprite Arrow() => Make("arrow",
            "ooooooooo",
            "oWWWWWWWo",
            "oWWWWWWWo",
            "ooWWWWWoo",
            ".oWWWWWo.",
            ".ooWWWoo.",
            "..oWWWo..",
            "..ooWoo..",
            "...oWo...",
            "...ooo...", false);

        private static Sprite Make(string key, params object[] rowsAndCross)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            int n = rowsAndCross.Length - 1;
            bool cross = (bool)rowsAndCross[n];
            int h = n, w = ((string)rowsAndCross[0]).Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                string row = (string)rowsAndCross[y];
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    px[(h - 1 - y) * w + x] = c switch
                    {
                        'o' => new Color32(24, 18, 18, 255),
                        'P' => new Color32(222, 170, 150, 255),   // piel/pata
                        'W' => new Color32(240, 236, 226, 255),   // blanco (la flecha se tiñe del equipo)
                        'M' => new Color32(176, 178, 186, 255),   // metal
                        'C' => new Color32(236, 232, 220, 255),
                        'K' => new Color32(190, 30, 30, 255),
                        'G' => new Color32(120, 210, 70, 255),    // veneno
                        _ => new Color32(0, 0, 0, 0),
                    };
                }
            }
            if (cross)   // raya roja: "lo ha perdido"
                for (int i = 0; i < Mathf.Min(w, h); i++)
                {
                    px[i * w + i] = new Color32(230, 40, 30, 255);   // una raya: se sigue viendo qué pieza es
                }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
            cache[key] = s;
            return s;
        }
    }
}
