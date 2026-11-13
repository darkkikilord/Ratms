using System.Collections.Generic;
using Ratms.World;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  EFECTOS VISUALES por código (cero assets, cero ParticleSystem que
    ///  configurar): explosiones con fogonazo, fuego, humo, tierra volando y
    ///  chispas; estela de humo; salpicadura de agua; chispas; lápidas.
    ///
    ///  Son 100% LOCALES (cada cliente los pinta al recibir el ClientRpc del
    ///  arma), así no gastan red. Usa un pool de SpriteRenderers.
    ///
    ///  Uso: Fx.Explosion(punto, radio); Fx.TrailPuff(punto); Fx.Splash(punto);
    ///       Fx.Sparks(punto, n); Fx.Gravestone(punto).
    /// </summary>
    public class Fx : MonoBehaviour
    {
        private struct P
        {
            public SpriteRenderer sr;
            public Vector2 pos, vel;
            public float age, life, gravity, drag, s0, s1;
            public Color c0, c1;
        }

        private const int SortingOrder = 200;
        private static Fx instance;
        private readonly List<P> live = new();
        private readonly Stack<SpriteRenderer> pool = new();
        private static Sprite soft, square;

        private static Fx I
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[Fx]");
                    instance = go.AddComponent<Fx>();
                }
                return instance;
            }
        }

        // ============================================================
        //  API
        // ============================================================
        /// <summary>Explosión completa. <paramref name="radius"/> = radio del arma (u).</summary>
        public static void Explosion(Vector2 at, float radius)
        {
            var fx = I;
            float r = Mathf.Max(0.5f, radius);
            Color32 dirt = DestructibleTerrain.TrySampleColorAround(at, r * 0.95f, out var c) ? c : new Color32(120, 80, 48, 255);

            // Fogonazo
            fx.Emit(at, Vector2.zero, 0.16f, 0f, 0f, r * 1.6f, r * 2.3f,
                    new Color(1f, 0.97f, 0.75f, 0.95f), new Color(1f, 0.6f, 0.2f, 0f), soft);
            // Bolas de fuego
            for (int i = 0; i < 12; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                fx.Emit(at + d * r * 0.3f, d * Random.Range(1.2f, 3f) * (r / 2.5f), Random.Range(0.35f, 0.6f), -2f, 3f,
                        r * 0.35f, r * Random.Range(0.6f, 0.9f),
                        new Color(1f, Random.Range(0.75f, 0.95f), 0.3f, 1f), new Color(0.75f, 0.15f, 0.05f, 0f), soft);
            }
            // Humo (sale un poco después y sube)
            for (int i = 0; i < 9; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                float g = Random.Range(0.25f, 0.45f);
                fx.Emit(at + d * r * 0.5f, d * 0.6f + Vector2.up * Random.Range(0.4f, 0.9f), Random.Range(1.3f, 2.2f), -0.3f, 1.2f,
                        r * 0.4f, r * Random.Range(1.0f, 1.5f),
                        new Color(g, g, g, 0.55f), new Color(g * 1.3f, g * 1.3f, g * 1.3f, 0f), soft);
            }
            // Tierra volando (del color del terreno de alrededor)
            for (int i = 0; i < 22; i++)
            {
                Vector2 d = (Random.insideUnitCircle + Vector2.up * 0.6f).normalized;
                Color dc = Shade(dirt, Random.Range(0.7f, 1.15f));
                float size = Random.Range(2f, 4.5f) / 32f;
                fx.Emit(at + d * r * 0.4f, d * Random.Range(3f, 7.5f) * Mathf.Sqrt(r / 2.5f), Random.Range(1.0f, 1.8f), 9.8f, 0.4f,
                        size, size, dc, new Color(dc.r, dc.g, dc.b, 0f), square, fadeLate: true);
            }
            // Chispas
            Sparks(at, 12, new Color(1f, 0.9f, 0.4f, 1f), 5f, 9f);
        }

        /// <summary>Bocanada de humo para la estela de un proyectil.</summary>
        public static void TrailPuff(Vector2 at)
        {
            float g = Random.Range(0.7f, 0.85f);
            I.Emit(at + Random.insideUnitCircle * 0.03f, Random.insideUnitCircle * 0.15f + Vector2.up * 0.2f, Random.Range(0.45f, 0.7f), 0f, 1f,
                   0.07f, Random.Range(0.22f, 0.3f), new Color(g, g, g, 0.55f), new Color(g, g, g, 0f), soft);
        }

        /// <summary>Chispas pequeñas y rápidas (golpes, mecha, explosión).</summary>
        public static void Sparks(Vector2 at, int count, Color color, float minSpeed = 2f, float maxSpeed = 5f)
        {
            var fx = I;
            for (int i = 0; i < count; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                float size = Random.Range(1f, 2f) / 32f;
                fx.Emit(at, d * Random.Range(minSpeed, maxSpeed), Random.Range(0.18f, 0.4f), 6f, 1.5f,
                        size, size, color, new Color(color.r, 0.4f, 0.1f, 0f), square);
            }
        }

        /// <summary>Salpicadura de agua (rata al agua).</summary>
        public static void Splash(Vector2 at)
        {
            var fx = I;
            for (int i = 0; i < 26; i++)
            {
                Vector2 v = new Vector2(Random.Range(-2f, 2f), Random.Range(3f, 7f));
                float size = Random.Range(2f, 4f) / 32f;
                Color c = Random.value < 0.4f ? new Color(0.92f, 0.97f, 1f, 1f) : new Color(0.35f, 0.7f, 0.95f, 1f);
                fx.Emit(at, v, Random.Range(0.6f, 1.1f), 12f, 0.2f, size, size, c, new Color(c.r, c.g, c.b, 0f), square, fadeLate: true);
            }
            fx.Emit(at, Vector2.zero, 0.5f, 0f, 0f, 0.3f, 1.4f, new Color(0.9f, 0.97f, 1f, 0.7f), new Color(0.6f, 0.85f, 1f, 0f), soft);
        }

        /// <summary>Pone una LÁPIDA donde ha muerto una rata (cae hasta el suelo).</summary>
        public static void Gravestone(Vector2 at)
        {
            var go = new GameObject("Lápida");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GravestoneSprite();
            sr.sortingOrder = 40;
            go.AddComponent<GravestoneSettle>();
        }

        // ============================================================
        //  Motor de partículas mínimo
        // ============================================================
        private void Emit(Vector2 pos, Vector2 vel, float life, float gravity, float drag, float s0, float s1,
                          Color c0, Color c1, Sprite sprite, bool fadeLate = false)
        {
            if (live.Count > 900) return;   // tope de seguridad
            if (soft == null) BuildSprites();
            var sr = pool.Count > 0 ? pool.Pop() : NewRenderer();
            sr.sprite = sprite == null ? soft : sprite;
            sr.gameObject.SetActive(true);
            if (fadeLate) c1 = new Color(c1.r, c1.g, c1.b, -1f);   // marca: se desvanece solo al final
            live.Add(new P { sr = sr, pos = pos, vel = vel, life = life, gravity = gravity, drag = drag, s0 = s0, s1 = s1, c0 = c0, c1 = c1 });
            Apply(live[live.Count - 1], 0f);
        }

        private SpriteRenderer NewRenderer()
        {
            var go = new GameObject("p");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortingOrder;
            return sr;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.sr.gameObject.SetActive(false);
                    pool.Push(p.sr);
                    live.RemoveAt(i);
                    continue;
                }
                p.vel.y -= p.gravity * dt;
                p.vel *= 1f / (1f + p.drag * dt);
                p.pos += p.vel * dt;
                live[i] = p;
                Apply(p, p.age / p.life);
            }
        }

        private static void Apply(P p, float t)
        {
            float s = Mathf.Lerp(p.s0, p.s1, t);
            p.sr.transform.position = new Vector3(p.pos.x, p.pos.y, 0f);
            p.sr.transform.localScale = new Vector3(s, s, 1f);
            if (p.c1.a < 0f)   // "fadeLate": color fijo y se desvanece en el último 30%
            {
                var c = p.c0; c.a = p.c0.a * Mathf.Clamp01((1f - t) / 0.3f);
                p.sr.color = c;
            }
            else p.sr.color = Color.Lerp(p.c0, p.c1, t);
        }

        private static Color Shade(Color32 c, float k) => new Color(c.r / 255f * k, c.g / 255f * k, c.b / 255f * k, 1f);

        private static void BuildSprites()
        {
            const int N = 32;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
            t.Apply();
            soft = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);   // 1 u de diámetro

            var q = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            q.SetPixel(0, 0, Color.white);
            q.Apply();
            square = Sprite.Create(q, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);   // 1 u de lado
        }

        private static Sprite gravestone;
        private static Sprite GravestoneSprite()
        {
            if (gravestone != null) return gravestone;
            const int W = 12, H = 15;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color32 clear = new Color32(0, 0, 0, 0), stone = new Color32(150, 150, 158, 255), light = new Color32(186, 186, 194, 255),
                    dark = new Color32(104, 104, 114, 255), outline = new Color32(40, 36, 46, 255), grass = new Color32(80, 150, 70, 255);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Losa con la parte de arriba redondeada
                    bool inside = x >= 1 && x <= W - 2 && y >= 1 && (y < H - 4 || (x - 5.5f) * (x - 5.5f) / 20f + (y - (H - 5f)) * (y - (H - 5f)) / 12f <= 1f);
                    Color32 c = clear;
                    if (inside) c = x <= 2 ? light : x >= W - 3 ? dark : stone;
                    tex.SetPixel(x, y, c);
                }
            // Contorno
            var px = tex.GetPixels32();
            var o = (Color32[])px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (px[y * W + x].a != 0) continue;
                    bool n = (x > 0 && px[y * W + x - 1].a != 0) || (x < W - 1 && px[y * W + x + 1].a != 0) ||
                             (y > 0 && px[(y - 1) * W + x].a != 0) || (y < H - 1 && px[(y + 1) * W + x].a != 0);
                    if (n) o[y * W + x] = outline;
                }
            // Cruz grabada
            for (int y = 5; y <= 10; y++) o[y * W + 6] = dark;
            for (int x = 4; x <= 8; x++) o[9 * W + x] = dark;
            // Matojo de hierba en la base
            o[0 * W + 1] = grass; o[0 * W + 2] = grass; o[0 * W + 9] = grass; o[0 * W + 10] = grass; o[1 * W + 1] = grass;
            tex.SetPixels32(o);
            tex.Apply();
            gravestone = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 32f);
            return gravestone;
        }
    }

    /// <summary>La lápida cae hasta el suelo y, si le vuelan el suelo de debajo, vuelve a caer.</summary>
    public class GravestoneSettle : MonoBehaviour
    {
        private float fallSpeed;

        private void Update()
        {
            Vector2 p = transform.position;
            // ¿Hay suelo justo debajo? (miramos los píxeles del terreno)
            if (DestructibleTerrain.IsSolidAtWorld(p + Vector2.down * 0.02f)) { fallSpeed = 0f; return; }
            fallSpeed = Mathf.Min(fallSpeed + 15f * Time.deltaTime, 12f);
            transform.position = p + Vector2.down * fallSpeed * Time.deltaTime;
            if (p.y < -60f) Destroy(gameObject);   // se ha ido al agua/vacío
        }
    }
}
