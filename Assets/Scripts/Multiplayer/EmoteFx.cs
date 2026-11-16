using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.37 — PARTÍCULAS DE LOS EMOTES (locales, en cada PC): estrellas,
    ///  chispas, notas musicales, lágrimas, vapor de enfado, corazones y la nube
    ///  verde de la pedorreta. Sprites pixel-art de 8×8 hechos por código (mismo
    ///  estilo que <see cref="Fx"/>) que salen de la rata, flotan y se apagan.
    ///  Los mismos dibujos sirven de iconos en el menú de emotes (<see cref="Icon"/>).
    /// </summary>
    public class EmoteFx : MonoBehaviour
    {
        public enum Kind { Stars, Sparks, Notes, Tears, Steam, Hearts, Gas }

        private struct P { public SpriteRenderer sr; public Vector2 vel; public float born, life, spin, grav, size; }
        private static EmoteFx instance;
        private readonly List<P> ps = new();
        private static readonly Dictionary<Kind, Sprite> sprites = new();
        private static readonly Dictionary<Kind, Texture2D> icons = new();

        // Dibujos 8×8 (fila de arriba primero). '.' vacío, el resto colores.
        private static readonly Dictionary<Kind, string[]> Art = new()
        {
            [Kind.Stars] = new[] { "...Y....", "...Y....", "..YYY...", "YYYWYYY.", ".YYYYY..", "..YYY...", ".YY.YY..", ".Y...Y.." },
            [Kind.Sparks] = new[] { "...W....", "...Y....", ".W.Y.W..", "..YYY...", "WYYWYYW.", "..YYY...", ".W.Y.W..", "...W...." },
            [Kind.Notes] = new[] { "...YYYY.", "...Y..Y.", "...Y..Y.", "...Y..Y.", ".YYY.YY.", "YWYY.YY.", "YYY.YWY.", ".Y..YY.." },
            [Kind.Tears] = new[] { "...B....", "...B....", "..BBB...", "..BWB...", ".BBWBB..", ".BBBBB..", ".BBBBB..", "..BBB..." },
            [Kind.Steam] = new[] { "..GGG...", ".GWWWG..", "GWWWWWG.", "GWWWWWWG", "GWWWWWWG", ".GWWWWG.", "..GGGG..", "........" },
            [Kind.Hearts] = new[] { ".RR.RR..", "RPRRRRR.", "RPRRRRR.", "RRRRRRR.", ".RRRRR..", "..RRR...", "...R....", "........" },
            [Kind.Gas] = new[] { "..OOO...", ".OVVVO..", "OVVVVVO.", "OVVOVVVO", "OVVVVVVO", ".OVVVVO.", "..OOOO..", "........" },
        };

        private static Color32 Col(char c) => c switch
        {
            'Y' => new Color32(255, 214, 64, 255),
            'W' => new Color32(255, 255, 240, 255),
            'K' => new Color32(40, 30, 30, 255),
            'B' => new Color32(90, 170, 255, 255),
            'G' => new Color32(150, 150, 150, 255),
            'R' => new Color32(220, 40, 70, 255),
            'P' => new Color32(255, 150, 180, 255),
            'O' => new Color32(70, 120, 40, 255),
            'V' => new Color32(140, 200, 70, 255),
            _ => new Color32(0, 0, 0, 0),
        };

        private static Texture2D Tex(Kind k)
        {
            if (icons.TryGetValue(k, out var t) && t != null) return t;
            var rows = Art[k];
            t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    t.SetPixel(x, 7 - y, Col(rows[y][x]));
            t.Apply();
            icons[k] = t;
            return t;
        }

        /// <summary>Icono para el menú de emotes.</summary>
        public static Texture2D Icon(Kind k) => Art.ContainsKey(k) ? Tex(k) : null;

        private static Sprite Spr(Kind k)
        {
            if (sprites.TryGetValue(k, out var s) && s != null) return s;
            s = Sprite.Create(Tex(k), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 32f);
            sprites[k] = s;
            return s;
        }

        /// <summary>Lanza el efecto de un emote sobre una rata (en este PC).</summary>
        public static void Play(NetworkPlayerMovement rat, Kind k)
        {
            if (rat == null) return;
            if (instance == null) instance = new GameObject("EmotesFx").AddComponent<EmoteFx>();
            instance.StartCoroutine(instance.Emit(rat, k));
        }

        private IEnumerator Emit(NetworkPlayerMovement rat, Kind k)
        {
            var body = rat.GetComponentInChildren<SpriteRenderer>();
            int layer = body != null ? body.sortingLayerID : 0, order = body != null ? body.sortingOrder + 20 : 50;
            int n = k switch { Kind.Hearts => 7, Kind.Notes => 8, Kind.Tears => 10, Kind.Steam => 8, Kind.Gas => 9, _ => 12 };
            float span = k == Kind.Stars || k == Kind.Sparks ? 0.25f : 1.5f;
            for (int i = 0; i < n; i++)
            {
                if (rat == null) yield break;
                var hp = rat.GetComponent<NetworkHealth>();
                Vector2 c = hp != null ? hp.BodyCenter : (Vector2)rat.transform.position;
                bool right = rat.FacingRight;
                Vector2 at, vel;
                float grav = 0f, life = 1.3f, size = 1f, spin = 0f;
                switch (k)
                {
                    case Kind.Stars:
                    case Kind.Sparks:
                    {
                        float a = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                        at = c + Vector2.up * 0.3f;
                        vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(2.5f, 4.5f);
                        grav = 6f; life = 0.9f; spin = Random.Range(-400f, 400f); size = Random.Range(0.8f, 1.3f);
                        break;
                    }
                    case Kind.Notes:
                        at = c + new Vector2(Random.Range(-0.4f, 0.4f), 0.5f);
                        vel = new Vector2(Mathf.Sin(i * 1.7f) * 0.8f, 1.4f);
                        life = 1.4f; spin = Mathf.Sin(i) * 40f;
                        break;
                    case Kind.Tears:
                        at = c + new Vector2(right ? 0.22f : -0.22f, 0.3f) + Vector2.right * Random.Range(-0.05f, 0.05f);
                        vel = new Vector2((right ? 1f : -1f) * Random.Range(0.6f, 1.6f), Random.Range(0.8f, 2f));
                        grav = 9f; life = 0.8f; size = 0.7f;
                        break;
                    case Kind.Steam:
                        at = c + new Vector2(Random.Range(-0.15f, 0.15f), 0.55f);
                        vel = new Vector2((i % 2 == 0 ? -1f : 1f) * Random.Range(0.4f, 1.2f), Random.Range(1.5f, 2.5f));
                        life = 0.9f; size = Random.Range(0.7f, 1.2f);
                        break;
                    case Kind.Hearts:
                        at = c + new Vector2(Random.Range(-0.3f, 0.3f), 0.5f);
                        vel = new Vector2(Mathf.Sin(i * 2.1f) * 0.6f, Random.Range(1f, 1.6f));
                        life = 1.6f; size = Random.Range(0.8f, 1.3f);
                        break;
                    default: // Gas
                        at = c + new Vector2(right ? -0.35f : 0.35f, -0.2f);
                        vel = new Vector2((right ? -1f : 1f) * Random.Range(0.5f, 1.5f), Random.Range(0.1f, 0.8f));
                        life = 1.6f; size = Random.Range(1f, 1.8f);
                        break;
                }
                var go = new GameObject("emote");
                go.transform.position = new Vector3(at.x, at.y, -3f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Spr(k);
                sr.sortingLayerID = layer;
                sr.sortingOrder = order;
                size *= 1.7f;   // que se vean bien también con la cámara alejada
                go.transform.localScale = Vector3.one * size;
                ps.Add(new P { sr = sr, vel = vel, born = Time.time, life = life, grav = grav, spin = spin, size = size });
                yield return new WaitForSeconds(span / n);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = ps.Count - 1; i >= 0; i--)
            {
                var p = ps[i];
                if (p.sr == null) { ps.RemoveAt(i); continue; }
                float t = (Time.time - p.born) / p.life;
                if (t >= 1f) { Destroy(p.sr.gameObject); ps.RemoveAt(i); continue; }
                p.vel += Vector2.down * p.grav * dt;
                var tr = p.sr.transform;
                tr.position += (Vector3)(p.vel * dt);
                tr.Rotate(0f, 0f, p.spin * dt);
                // "pop" al nacer y se apaga al final
                float pop = t < 0.15f ? Mathf.Lerp(0.3f, 1.15f, t / 0.15f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.15f) * 4f));
                tr.localScale = Vector3.one * p.size * pop;
                var c = p.sr.color; c.a = Mathf.Clamp01((1f - t) * 3f); p.sr.color = c;
                ps[i] = p;
            }
        }
    }
}
