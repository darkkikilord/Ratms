using System;
using System.Collections.Generic;
using Ratms.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ratms.Networking
{
    /// <summary>
    ///  EFECTOS VISUALES por código (cero assets, cero ParticleSystem que
    ///  configurar): explosiones, humo, tierra, chispas, agua, lápidas y GORE.
    ///
    ///  Son 100% LOCALES (cada cliente los pinta al recibir el ClientRpc del
    ///  arma), así no gastan red. Motor propio de partículas con pool de
    ///  SpriteRenderers que sabe:
    ///   • alargar las gotas en la dirección en la que vuelan (regueros),
    ///   • girar trozos, REBOTAR en el terreno (por píxeles) y quedarse
    ///     tirados en el suelo un rato (y volver a caer si les vuelan el suelo),
    ///   • MANCHAR la tierra (sangre) y dejar estela (sangre o humo),
    ///   • color en 3 fases (fuego blanco → naranja → humo),
    ///   • dejarse llevar por el viento (humo, ascuas).
    ///
    ///  Uso: Fx.Explosion(punto, radio); Fx.Blood(punto, dir, daño);
    ///       Fx.Gibs(punto, colorEquipo); Fx.TrailPuff(punto); Fx.Splash(punto);
    ///       Fx.Sparks(punto, n, color); Fx.MuzzleFlash(boca, dir);
    ///       Fx.Drip(punto); Fx.Gravestone(punto).
    /// </summary>
    public class Fx : MonoBehaviour
    {
        private struct P
        {
            public SpriteRenderer sr;
            public Vector2 pos, vel;
            public float age, life, gravity, drag, s0, s1;
            public Color c0, c1, cm;
            public float midT;          // > 0 → color en 3 fases (c0 → cm en midT → c1)
            public bool fadeLate;       // color fijo; se desvanece en el último 25 %
            public float rot, spin;     // grados, grados/s
            public float stretch;       // > 0 → se alarga según la velocidad (gotas, chispas)
            public bool stains;         // mancha la tierra al tocarla
            public int stainPx;
            public bool collides;       // choca con la tierra (si no rebota ni se pega, muere)
            public int bounces;
            public float restitution;
            public bool sticks, resting;
            public float restGravity;
            public float trailEvery, trailT;
            public byte trailKind;      // 1 = sangre, 2 = humo
            public float wind;          // cuánto le afecta el viento
        }

        private const int MaxParticles = 1400;
        private const int SortingOrder = 200;
        private const int GibOrder = 60;

        /// <summary>
        ///  ¿Gore activado? (PlayerPrefs "opt_gore", 1 por defecto). Con 0 no
        ///  hay sangre ni vísceras: las ratas sueltan "pelusa" gris.
        /// </summary>
        public static bool GoreOn => PlayerPrefs.GetInt("opt_gore", 1) == 1;

        private static Fx instance;
        private readonly P[] parts = new P[MaxParticles];
        private int count;
        private readonly Stack<SpriteRenderer> pool = new();
        private readonly List<(float at, Action act)> delayed = new();

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
        //  GORE
        // ============================================================
        /// <summary>
        ///  Salpicón de sangre: regueros de gotas que salen en
        ///  <paramref name="dir"/> (y algo hacia arriba), goterones, bruma
        ///  roja y, si el golpe es fuerte, un chorro "arterial" que dura un
        ///  instante. Todo lo que cae en tierra la deja MANCHADA para el resto
        ///  de la partida. <paramref name="amount"/> ≈ daño recibido.
        /// </summary>
        public static void Blood(Vector2 at, Vector2 dir, float amount)
        {
            var fx = I;
            Vector2 baseDir = ((dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up) + Vector2.up * 0.5f).normalized;

            // Regueros finos y rápidos
            int n = Mathf.Clamp(Mathf.RoundToInt(amount * 0.5f), 6, 45);
            for (int i = 0; i < n; i++)
            {
                Vector2 v = (baseDir + Random.insideUnitCircle * 0.75f) * Random.Range(2f, 6.5f);
                fx.BloodDrop(at + Random.insideUnitCircle * 0.08f, v, Random.Range(1.2f, 2.4f) / 32f);
            }
            // Goterones (más lentos y gordos: manchan más)
            for (int i = 0; i < n / 5 + 1; i++)
            {
                Vector2 v = (baseDir + Random.insideUnitCircle * 0.5f) * Random.Range(1.2f, 3.5f);
                fx.BloodDrop(at, v, Random.Range(3f, 4.2f) / 32f, stainPx: 2);
            }
            // Bruma roja
            for (int i = 0; i < 3; i++)
            {
                ref var m = ref fx.New(at + Random.insideUnitCircle * 0.12f, baseDir * Random.Range(0.3f, 0.8f), Random.Range(0.35f, 0.6f), Cloud(i));
                m.drag = 3f; m.s0 = 0.18f; m.s1 = Random.Range(0.5f, 0.75f);
                m.c0 = GoreOn ? new Color(0.62f, 0.04f, 0.05f, 0.55f) : new Color(0.6f, 0.58f, 0.55f, 0.45f);
                m.c1 = new Color(m.c0.r * 0.6f, 0f, 0f, 0f);
                m.spin = Random.Range(-90f, 90f);
            }
            // Chorro arterial: un pequeño surtidor que dura un momento
            if (amount >= 20f && GoreOn)
            {
                float dur = Mathf.Lerp(0.2f, 0.5f, Mathf.InverseLerp(20f, 60f, amount));
                fx.Repeat(dur, 0.025f, () =>
                {
                    Vector2 v = (baseDir + Vector2.up * 0.3f + Random.insideUnitCircle * 0.35f).normalized * Random.Range(4f, 6f);
                    fx.BloodDrop(at, v, Random.Range(1.5f, 2.5f) / 32f);
                });
            }
        }

        /// <summary>Gota suelta (rata malherida que va sangrando).</summary>
        public static void Drip(Vector2 at)
        {
            if (!GoreOn) return;
            I.BloodDrop(at + new Vector2(Random.Range(-0.12f, 0.12f), 0f), new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0f, 0.5f)),
                        Random.Range(1.5f, 2.5f) / 32f);
        }

        /// <summary>
        ///  Muerte en tierra: la rata REVIENTA. Estallido de sangre en todas
        ///  direcciones, bruma, y TROZOS (carne, tripas, huesos, un ojo, dientes,
        ///  la cola, pelo... y el CASCO con la franja de su equipo) que giran,
        ///  rebotan dejando reguero y se quedan tirados por el suelo. Debajo
        ///  queda un charco.
        /// </summary>
        public static void Gibs(Vector2 at, Color32? team = null)
        {
            var fx = I;
            bool gore = GoreOn;

            // Fogonazo rojo y bruma espesa
            ref var f = ref fx.New(at, Vector2.zero, 0.14f, soft);
            f.s0 = 0.6f; f.s1 = 1.8f;
            f.c0 = gore ? new Color(1f, 0.55f, 0.5f, 0.85f) : new Color(1f, 1f, 1f, 0.7f); f.c1 = new Color(0.8f, 0.1f, 0.1f, 0f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                ref var m = ref fx.New(at + d * 0.2f, d * 1.2f + Vector2.up * 0.3f, Random.Range(0.6f, 1.1f), Cloud(i));
                m.drag = 2.5f; m.gravity = 0.4f; m.s0 = 0.3f; m.s1 = Random.Range(0.9f, 1.4f);
                m.c0 = gore ? new Color(0.55f, 0.03f, 0.04f, 0.6f) : new Color(0.62f, 0.6f, 0.58f, 0.5f);
                m.c1 = new Color(m.c0.r * 0.5f, m.c0.g * 0.5f, m.c0.b * 0.5f, 0f);
                m.spin = Random.Range(-120f, 120f);
            }

            // Estallido radial + salpicón hacia arriba
            Blood(at, Vector2.up, 70f);
            for (int i = 0; i < 34; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                fx.BloodDrop(at + d * 0.1f, (d + Vector2.up * 0.35f) * Random.Range(4f, 9f), Random.Range(1.5f, 3f) / 32f);
            }

            // TROZOS
            if (gore)
            {
                Gib(at, Meat(0), 3); Gib(at, Meat(1), 3);
                Gib(at, Gut(0), 1, bloodTrail: true); Gib(at, Gut(1), 1, bloodTrail: true);
                Gib(at, Bone(), 3, stain: false, bloodTrail: false);
                Gib(at, Eye(), 1, stain: false);
                Gib(at, Tooth(), 3, stain: false, bloodTrail: false, small: true);
                Gib(at, Tail(), 1);
            }
            Gib(at, FurTuft(), gore ? 4 : 10, stain: false, bloodTrail: false, small: true);
            // El casco sale volando girando y se queda en el campo de batalla
            {
                Vector2 v = new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(7f, 9.5f));
                ref var h = ref fx.New(at + Vector2.up * 0.2f, v, Random.Range(22f, 30f), Helmet(team ?? new Color32(120, 128, 96, 255)), GibOrder);
                h.gravity = 11f; h.drag = 0.15f; h.spin = Random.Range(500f, 900f) * (Random.value < 0.5f ? -1f : 1f);
                h.collides = true; h.bounces = 3; h.restitution = 0.45f; h.sticks = true; h.fadeLate = true;
                h.rot = Random.Range(0f, 360f);
            }

            // Charco debajo (cuando ya ha caído la primera sangre)
            if (gore) fx.Later(0.12f, () => Puddle(at, 0.45f));
        }

        private static void Gib(Vector2 at, Sprite sprite, int copies, bool stain = true, bool bloodTrail = true, bool small = false)
        {
            var fx = I;
            for (int i = 0; i < copies; i++)
            {
                Vector2 v = (Random.insideUnitCircle + Vector2.up * 0.85f).normalized * Random.Range(small ? 3f : 3.5f, small ? 8f : 9f);
                ref var p = ref fx.New(at + Random.insideUnitCircle * 0.12f, v, Random.Range(9f, 15f), sprite, GibOrder);
                p.gravity = 11f; p.drag = 0.2f;
                p.rot = Random.Range(0f, 360f); p.spin = Random.Range(-900f, 900f);
                p.collides = true; p.bounces = Random.Range(1, 3); p.restitution = small ? 0.45f : 0.3f; p.sticks = true;
                p.stains = stain && GoreOn; p.stainPx = 2;
                p.fadeLate = true;
                if (bloodTrail && GoreOn) { p.trailEvery = 0.05f; p.trailKind = 1; }
            }
        }

        /// <summary>Charco de sangre sobre el suelo que hay debajo de <paramref name="at"/>.</summary>
        private static void Puddle(Vector2 at, float halfWidth)
        {
            const float step = 1f / 32f;
            for (float x = -halfWidth; x <= halfWidth; x += step * 2f)
            {
                // Busca la superficie debajo (hasta 3 u)
                Vector2 p = new Vector2(at.x + x, at.y + 0.3f);
                bool found = false;
                for (int k = 0; k < 110; k++, p.y -= step)
                    if (DestructibleTerrain.IsSolidAtWorld(p)) { found = true; break; }
                if (!found) continue;
                float edge = 1f - Mathf.Abs(x) / halfWidth;                 // más gordo en el centro
                int r = edge > 0.55f ? 2 : 1;
                if (Random.value < 0.15f + edge) DestructibleTerrain.StainAtWorld(p, BloodColor32(dark: true), r, 0.85f);
            }
        }

        private static Color BloodColor() => GoreOn
            ? (Random.value < 0.3f
                ? new Color(Random.Range(0.32f, 0.42f), 0f, Random.Range(0.01f, 0.04f), 1f)          // oscura (venosa)
                : new Color(Random.Range(0.55f, 0.8f), Random.Range(0f, 0.05f), Random.Range(0f, 0.05f), 1f))
            : new Color(0.55f, 0.52f, 0.5f, 1f);   // sin gore: pelusa gris

        private static Color32 BloodColor32(bool dark = false)
        {
            Color c = dark && GoreOn ? new Color(Random.Range(0.3f, 0.45f), 0f, 0.02f, 1f) : BloodColor();
            return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
        }

        private void BloodDrop(Vector2 at, Vector2 vel, float size, int stainPx = 1)
        {
            ref var p = ref New(at, vel, Random.Range(1.2f, 2f), square);
            p.gravity = 11f; p.drag = 0.25f; p.s0 = p.s1 = size;
            p.c0 = BloodColor(); p.fadeLate = true;
            p.stretch = 0.2f;
            p.collides = true; p.stains = GoreOn; p.stainPx = size > 2.8f / 32f ? Mathf.Max(stainPx, 2) : stainPx;
        }

        // ============================================================
        //  EXPLOSIONES
        // ============================================================
        /// <summary>
        ///  Explosión completa. <paramref name="radius"/> = radio del arma (u).
        ///  Fogonazo, onda expansiva, bolas de fuego que se vuelven humo negro,
        ///  columna de humo que se lleva el viento, piedras del color del suelo
        ///  que rebotan, cascotes ardiendo con estela, ascuas y chispas.
        /// </summary>
        public static void Explosion(Vector2 at, float radius)
        {
            var fx = I;
            float r = Mathf.Max(0.5f, radius);
            float k = Mathf.Sqrt(r / 2.5f);
            Color32 dirt = DestructibleTerrain.TrySampleColorAround(at, r * 0.95f, out var c) ? c : new Color32(120, 80, 48, 255);

            // Fogonazo (núcleo blanco + halo)
            ref var flash = ref fx.New(at, Vector2.zero, 0.12f, soft);
            flash.s0 = r * 1.4f; flash.s1 = r * 2.6f;
            flash.c0 = new Color(1f, 1f, 0.9f, 1f); flash.c1 = new Color(1f, 0.7f, 0.3f, 0f);
            ref var halo = ref fx.New(at, Vector2.zero, 0.3f, soft);
            halo.s0 = r * 2.2f; halo.s1 = r * 3.4f;
            halo.c0 = new Color(1f, 0.6f, 0.2f, 0.45f); halo.c1 = new Color(1f, 0.3f, 0.1f, 0f);

            // Onda expansiva
            ref var ring = ref fx.New(at, Vector2.zero, 0.3f, Ring());
            ring.s0 = r * 0.6f; ring.s1 = r * 3.3f;
            ring.c0 = new Color(1f, 0.96f, 0.85f, 0.75f); ring.c1 = new Color(1f, 0.85f, 0.6f, 0f);

            // Bolas de fuego: blanco → naranja → humo oscuro
            for (int i = 0; i < 16; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                ref var p = ref fx.New(at + d * r * 0.3f, d * Random.Range(1.2f, 3.2f) * (r / 2.5f), Random.Range(0.55f, 0.95f), Cloud(i));
                p.gravity = -2.2f; p.drag = 3f; p.s0 = r * 0.3f; p.s1 = r * Random.Range(0.7f, 1.0f);
                p.c0 = new Color(1f, 0.97f, 0.75f, 1f); p.cm = new Color(1f, Random.Range(0.35f, 0.55f), 0.08f, 0.95f);
                p.c1 = new Color(0.14f, 0.12f, 0.11f, 0f); p.midT = 0.3f;
                p.spin = Random.Range(-200f, 200f); p.rot = Random.Range(0f, 360f);
            }
            // Humo: aparece, sube en columna y se lo lleva el viento
            for (int i = 0; i < 12; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                float g = Random.Range(0.13f, 0.3f);
                ref var p = ref fx.New(at + d * r * 0.5f, d * 0.5f + Vector2.up * Random.Range(0.5f, 1.3f) * k, Random.Range(1.8f, 3.2f), Cloud(i + 3));
                p.gravity = -0.35f; p.drag = 1.1f; p.wind = 0.25f;
                p.s0 = r * 0.45f; p.s1 = r * Random.Range(1.1f, 1.7f);
                p.c0 = new Color(g, g, g, 0f); p.cm = new Color(g, g * 0.95f, g * 0.9f, 0.62f); p.c1 = new Color(g * 1.6f, g * 1.6f, g * 1.6f, 0f);
                p.midT = 0.15f;
                p.spin = Random.Range(-40f, 40f); p.rot = Random.Range(0f, 360f);
            }
            // Piedras del color del terreno (rebotan y se quedan un momento)
            for (int i = 0; i < 18; i++)
            {
                Vector2 d = (Random.insideUnitCircle + Vector2.up * 0.7f).normalized;
                ref var p = ref fx.New(at + d * r * 0.45f, d * Random.Range(3f, 8f) * k, Random.Range(1.6f, 2.8f), Rock(i), GibOrder);
                p.gravity = 11f; p.drag = 0.3f;
                p.c0 = Shade(dirt, Random.Range(0.75f, 1.1f)); p.fadeLate = true;
                p.rot = Random.Range(0f, 360f); p.spin = Random.Range(-600f, 600f);
                p.collides = true; p.bounces = 1; p.restitution = 0.3f; p.sticks = true;
            }
            // Tierra fina
            for (int i = 0; i < 16; i++)
            {
                Vector2 d = (Random.insideUnitCircle + Vector2.up * 0.6f).normalized;
                float size = Random.Range(1.5f, 3f) / 32f;
                ref var p = ref fx.New(at + d * r * 0.4f, d * Random.Range(3f, 7.5f) * k, Random.Range(0.9f, 1.6f), square);
                p.gravity = 9.8f; p.drag = 0.4f; p.s0 = p.s1 = size;
                p.c0 = Shade(dirt, Random.Range(0.6f, 1.15f)); p.fadeLate = true;
                p.collides = true;
            }
            // Cascotes ardiendo con estela de humo
            for (int i = 0; i < 6; i++)
            {
                Vector2 d = (Random.insideUnitCircle * 0.8f + Vector2.up).normalized;
                ref var p = ref fx.New(at, d * Random.Range(5f, 9f) * k, Random.Range(1f, 1.6f), square);
                p.gravity = 9f; p.drag = 0.3f; p.s0 = 3f / 32f; p.s1 = 1.5f / 32f;
                p.c0 = new Color(1f, 0.85f, 0.4f, 1f); p.c1 = new Color(0.7f, 0.15f, 0.05f, 0.6f);
                p.collides = true;
                p.trailEvery = 0.04f; p.trailKind = 2;
            }
            // Ascuas que flotan y se lleva el viento
            for (int i = 0; i < 12; i++)
            {
                ref var p = ref fx.New(at + Random.insideUnitCircle * r * 0.6f, Random.insideUnitCircle * 1.5f + Vector2.up * 1.2f,
                                       Random.Range(1.2f, 2.4f), square);
                p.gravity = -0.6f; p.drag = 1.5f; p.wind = 0.6f; p.s0 = p.s1 = Random.Range(1f, 1.6f) / 32f;
                p.c0 = new Color(1f, 0.75f, 0.25f, 1f); p.cm = new Color(1f, 0.35f, 0.05f, 1f); p.c1 = new Color(0.4f, 0.05f, 0f, 0f); p.midT = 0.5f;
            }
            // Chispas
            Sparks(at, 16, new Color(1f, 0.92f, 0.5f, 1f), 5f, 11f);
        }

        /// <summary>Bocanada de humo para la estela de un proyectil.</summary>
        public static void TrailPuff(Vector2 at)
        {
            float g = Random.Range(0.62f, 0.8f);
            ref var p = ref I.New(at + Random.insideUnitCircle * 0.03f, Random.insideUnitCircle * 0.15f + Vector2.up * 0.2f,
                                  Random.Range(0.5f, 0.8f), Cloud(Random.Range(0, 3)));
            p.drag = 1f; p.wind = 0.2f; p.s0 = 0.08f; p.s1 = Random.Range(0.26f, 0.36f);
            p.c0 = new Color(1f, 0.85f, 0.5f, 0.7f); p.cm = new Color(g, g, g, 0.55f); p.c1 = new Color(g, g, g, 0f); p.midT = 0.12f;
            p.rot = Random.Range(0f, 360f); p.spin = Random.Range(-60f, 60f);
        }

        /// <summary>Chispas pequeñas y rápidas que dejan trazo (golpes, mecha, explosión).</summary>
        public static void Sparks(Vector2 at, int count, Color color, float minSpeed = 2f, float maxSpeed = 5f)
        {
            var fx = I;
            for (int i = 0; i < count; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                ref var p = ref fx.New(at, d * Random.Range(minSpeed, maxSpeed), Random.Range(0.18f, 0.42f), square);
                p.gravity = 6f; p.drag = 1.5f; p.s0 = p.s1 = Random.Range(1f, 1.8f) / 32f;
                p.c0 = color; p.c1 = new Color(color.r, 0.4f, 0.1f, 0f);
                p.stretch = 0.35f;
            }
        }

        /// <summary>Golpe seco (bate): destello blanco + anillo pequeño.</summary>
        public static void Impact(Vector2 at, float size = 1f)
        {
            var fx = I;
            ref var f = ref fx.New(at, Vector2.zero, 0.1f, soft);
            f.s0 = 0.5f * size; f.s1 = 0.9f * size; f.c0 = new Color(1f, 1f, 0.9f, 0.95f); f.c1 = new Color(1f, 0.8f, 0.4f, 0f);
            ref var r = ref fx.New(at, Vector2.zero, 0.22f, Ring());
            r.s0 = 0.3f * size; r.s1 = 1.4f * size; r.c0 = new Color(1f, 1f, 0.9f, 0.8f); r.c1 = new Color(1f, 0.9f, 0.6f, 0f);
        }

        /// <summary>Fogonazo en la boca de un arma de fuego (cono de llama + humo).</summary>
        public static void MuzzleFlash(Vector2 at, Vector2 dir)
        {
            var fx = I;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            ref var f = ref fx.New(at + dir * 0.12f, Vector2.zero, 0.07f, soft);
            f.s0 = 0.55f; f.s1 = 0.3f; f.c0 = new Color(1f, 0.95f, 0.7f, 1f); f.c1 = new Color(1f, 0.6f, 0.2f, 0f);
            for (int i = 0; i < 7; i++)
            {
                Vector2 v = (dir + Random.insideUnitCircle * 0.3f) * Random.Range(4f, 9f);
                ref var p = ref fx.New(at, v, Random.Range(0.06f, 0.12f), soft);
                p.drag = 12f; p.s0 = Random.Range(0.12f, 0.2f); p.s1 = 0.05f;
                p.c0 = new Color(1f, 0.9f, 0.5f, 1f); p.c1 = new Color(1f, 0.35f, 0.05f, 0f);
            }
            for (int i = 0; i < 4; i++)
            {
                float g = Random.Range(0.55f, 0.75f);
                ref var p = ref fx.New(at + dir * 0.2f, dir * Random.Range(0.6f, 1.6f) + Vector2.up * 0.3f, Random.Range(0.5f, 0.9f), Cloud(i));
                p.drag = 2.5f; p.wind = 0.3f; p.s0 = 0.12f; p.s1 = Random.Range(0.4f, 0.6f);
                p.c0 = new Color(g, g, g, 0.5f); p.c1 = new Color(g, g, g, 0f); p.spin = Random.Range(-60f, 60f);
            }
        }

        /// <summary>Polvo al golpear tierra (perdigones, bate contra el suelo).</summary>
        public static void Dust(Vector2 at, Vector2 normal, int count = 6)
        {
            var fx = I;
            Color32 dirt = DestructibleTerrain.TrySampleColorAround(at, 0.3f, out var c) ? c : new Color32(120, 80, 48, 255);
            for (int i = 0; i < count; i++)
            {
                Vector2 v = (normal + Random.insideUnitCircle * 0.8f) * Random.Range(1.5f, 4f);
                ref var p = ref fx.New(at, v, Random.Range(0.5f, 0.9f), square);
                p.gravity = 9.8f; p.drag = 0.6f; p.s0 = p.s1 = Random.Range(1f, 2f) / 32f;
                p.c0 = Shade(dirt, Random.Range(0.7f, 1.1f)); p.fadeLate = true; p.collides = true;
            }
            ref var puff = ref fx.New(at, normal * 0.4f, 0.5f, Cloud(0));
            puff.drag = 3f; puff.s0 = 0.1f; puff.s1 = 0.45f;
            Color dc = Shade(dirt, 1.2f); puff.c0 = new Color(dc.r, dc.g, dc.b, 0.5f); puff.c1 = new Color(dc.r, dc.g, dc.b, 0f);
        }

        /// <summary>Salpicadura de agua (rata al agua).</summary>
        public static void Splash(Vector2 at)
        {
            var fx = I;
            for (int i = 0; i < 30; i++)
            {
                Vector2 v = new Vector2(Random.Range(-2.2f, 2.2f), Random.Range(3f, 7.5f));
                ref var p = ref fx.New(at, v, Random.Range(0.6f, 1.1f), square);
                p.gravity = 12f; p.drag = 0.2f; p.s0 = p.s1 = Random.Range(1.5f, 3.5f) / 32f;
                p.c0 = Random.value < 0.4f ? new Color(0.92f, 0.97f, 1f, 1f) : new Color(0.35f, 0.7f, 0.95f, 1f);
                p.fadeLate = true; p.stretch = 0.15f;
            }
            ref var f = ref fx.New(at, Vector2.zero, 0.5f, soft);
            f.s0 = 0.3f; f.s1 = 1.4f; f.c0 = new Color(0.9f, 0.97f, 1f, 0.7f); f.c1 = new Color(0.6f, 0.85f, 1f, 0f);
            ref var ring = ref fx.New(at, Vector2.zero, 0.6f, Ring());
            ring.s0 = 0.3f; ring.s1 = 2.2f; ring.c0 = new Color(0.85f, 0.95f, 1f, 0.7f); ring.c1 = new Color(0.6f, 0.85f, 1f, 0f);
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
        //  Motor
        // ============================================================
        private ref P New(Vector2 pos, Vector2 vel, float life, Sprite sprite, int order = SortingOrder)
        {
            if (soft == null) BuildSprites();
            if (count >= MaxParticles) Kill(0);   // tope: sacrifica la más vieja (aprox.)
            var sr = pool.Count > 0 ? pool.Pop() : NewRenderer();
            sr.sprite = sprite != null ? sprite : soft;
            sr.sortingOrder = order;
            sr.color = Color.white;
            sr.transform.localScale = Vector3.zero;   // invisible hasta su primer LateUpdate
            sr.gameObject.SetActive(true);
            ref var p = ref parts[count++];
            p = new P { sr = sr, pos = pos, vel = vel, life = life, s0 = 1f, s1 = 1f, c0 = Color.white, c1 = Color.white };
            return ref p;
        }

        private void Kill(int i)
        {
            var sr = parts[i].sr;
            if (sr != null) { sr.gameObject.SetActive(false); pool.Push(sr); }
            parts[i] = parts[--count];
            parts[count] = default;
        }

        private SpriteRenderer NewRenderer()
        {
            var go = new GameObject("p");
            go.transform.SetParent(transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        private void Later(float seconds, Action a) => delayed.Add((Time.time + seconds, a));

        private void Repeat(float duration, float every, Action a)
        {
            for (float t = 0f; t < duration; t += every) Later(t, a);
        }

        private void LateUpdate()
        {
            for (int i = delayed.Count - 1; i >= 0; i--)
            {
                if (Time.time < delayed[i].at) continue;
                var a = delayed[i].act;
                delayed.RemoveAt(i);
                a();
            }

            float dt = Time.deltaTime;
            float wind = NetworkTurnManager.WindAcceleration;
            for (int i = count - 1; i >= 0; i--)
            {
                ref var p = ref parts[i];
                if (p.sr == null) { Kill(i); continue; }
                p.age += dt;
                if (p.age >= p.life) { Kill(i); continue; }

                if (p.resting)
                {
                    // ¿Le han volado el suelo de debajo? → vuelve a caer
                    if (DestructibleTerrain.IsSolidAtWorld(p.pos + Vector2.down * 0.07f)) { Apply(ref p); continue; }
                    p.resting = false; p.gravity = p.restGravity; p.collides = true;
                }

                p.vel.y -= p.gravity * dt;
                if (p.wind != 0f) p.vel.x += wind * p.wind * dt;
                p.vel *= 1f / (1f + p.drag * dt);
                Vector2 prev = p.pos;
                p.pos += p.vel * dt;
                p.rot += p.spin * dt;

                if (p.collides && p.age > 0.03f && DestructibleTerrain.IsSolidAtWorld(p.pos))
                {
                    if (p.stains)
                        DestructibleTerrain.StainAtWorld(p.pos, new Color32((byte)(p.c0.r * 255), (byte)(p.c0.g * 255), (byte)(p.c0.b * 255), 255),
                                                         p.stainPx, 0.85f);
                    if (p.bounces > 0)
                    {
                        Vector2 nrm = SurfaceNormal(p.pos);
                        p.pos = prev;
                        float vn = Vector2.Dot(p.vel, nrm);
                        Vector2 vt = p.vel - vn * nrm;
                        p.vel = vt * 0.6f - vn * p.restitution * nrm;
                        p.spin *= -0.5f;
                        p.bounces--;
                        if (p.sticks && p.vel.magnitude < 1f) Rest(ref p);
                    }
                    else if (p.sticks) { p.pos = prev; Rest(ref p); }
                    else { Kill(i); continue; }
                }

                if (p.trailEvery > 0f && !p.resting)
                {
                    p.trailT -= dt;
                    if (p.trailT <= 0f && p.vel.sqrMagnitude > 1f)
                    {
                        p.trailT = p.trailEvery;
                        Vector2 at = p.pos;   // copia: New() puede mover la memoria del struct
                        if (p.trailKind == 1) BloodDrop(at, p.vel * 0.15f + Random.insideUnitCircle * 0.3f, Random.Range(1f, 2f) / 32f);
                        else TrailSmoke(at);
                        p = ref parts[i];
                    }
                }
                Apply(ref p);
            }
        }

        private void TrailSmoke(Vector2 at)
        {
            float g = Random.Range(0.25f, 0.4f);
            ref var s = ref New(at, Vector2.up * 0.3f, Random.Range(0.4f, 0.7f), Cloud(Random.Range(0, 3)));
            s.drag = 1f; s.wind = 0.2f; s.s0 = 0.06f; s.s1 = 0.22f;
            s.c0 = new Color(g, g, g, 0.5f); s.c1 = new Color(g, g, g, 0f);
        }

        private static void Rest(ref P p)
        {
            p.resting = true; p.restGravity = p.gravity; p.gravity = 0f;
            p.vel = Vector2.zero; p.spin = 0f;
            // Que la vida restante dé para verlo tirado, pero sin eternizarse
            p.trailEvery = 0f;
        }

        /// <summary>Normal aproximada de la tierra en un punto (mirando qué vecinos están vacíos).</summary>
        private static Vector2 SurfaceNormal(Vector2 at)
        {
            const float e = 0.09f;
            Vector2 n = Vector2.zero;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (!DestructibleTerrain.IsSolidAtWorld(at + d * e)) n += d;
            }
            return n.sqrMagnitude < 0.0001f ? Vector2.up : n.normalized;
        }

        private static void Apply(ref P p)
        {
            float t = p.age / p.life;
            float s = Mathf.Lerp(p.s0, p.s1, t);
            Color c;
            if (p.fadeLate) { c = p.c0; c.a *= Mathf.Clamp01((1f - t) / 0.25f); }
            else if (p.midT > 0f) c = t < p.midT ? Color.Lerp(p.c0, p.cm, t / p.midT) : Color.Lerp(p.cm, p.c1, (t - p.midT) / (1f - p.midT));
            else c = Color.Lerp(p.c0, p.c1, t);
            p.sr.color = c;

            var tr = p.sr.transform;
            tr.position = new Vector3(p.pos.x, p.pos.y, 0f);
            if (p.stretch > 0f && !p.resting)
            {
                float speed = p.vel.magnitude;
                float ang = speed > 0.01f ? Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg : 0f;
                tr.rotation = Quaternion.Euler(0f, 0f, ang);
                tr.localScale = new Vector3(s * (1f + Mathf.Min(speed * p.stretch, 3.5f)), s, 1f);
            }
            else
            {
                tr.rotation = Quaternion.Euler(0f, 0f, p.rot);
                tr.localScale = new Vector3(s, s, 1f);
            }
        }

        private static Color Shade(Color32 c, float k) => new Color(c.r / 255f * k, c.g / 255f * k, c.b / 255f * k, 1f);

        // ============================================================
        //  Sprites (generados una vez)
        // ============================================================
        private static Sprite soft, square, ring;
        private static Sprite[] clouds, rocks;
        private static readonly Dictionary<string, Sprite> pixelArt = new();
        private static readonly Dictionary<Color32, Sprite> helmets = new();

        private static Sprite Cloud(int i) { if (clouds == null) BuildSprites(); return clouds[Mathf.Abs(i) % clouds.Length]; }
        private static Sprite Rock(int i) { if (rocks == null) BuildSprites(); return rocks[Mathf.Abs(i) % rocks.Length]; }
        private static Sprite Ring() { if (ring == null) BuildSprites(); return ring; }

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

            // Anillo (onda expansiva)
            const int R = 64;
            var rt = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - R / 2f) * (x + 0.5f - R / 2f) + (y + 0.5f - R / 2f) * (y + 0.5f - R / 2f)) / (R / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.12f);
                    rt.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            rt.Apply();
            ring = Sprite.Create(rt, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);

            // Nubes "abullonadas" (humo, fuego): unión de varios círculos suaves
            var rng = new System.Random(77);
            clouds = new Sprite[4];
            for (int v = 0; v < clouds.Length; v++)
            {
                var ct = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var blobs = new List<Vector3>();
                blobs.Add(new Vector3(16f, 15f, 8.5f));
                for (int b = 0; b < 5; b++)
                {
                    float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float dist = 4f + (float)rng.NextDouble() * 3.5f;
                    blobs.Add(new Vector3(16f + Mathf.Cos(ang) * dist, 15f + Mathf.Sin(ang) * dist, 4.5f + (float)rng.NextDouble() * 3f));
                }
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float a = 0f;
                        foreach (var bl in blobs)
                        {
                            float d = Mathf.Sqrt((x + 0.5f - bl.x) * (x + 0.5f - bl.x) + (y + 0.5f - bl.y) * (y + 0.5f - bl.y)) / bl.z;
                            a = Mathf.Max(a, Mathf.Clamp01((1f - d) * 2.2f));
                        }
                        float light = 0.82f + 0.18f * (y / (float)N);   // más claro arriba
                        ct.SetPixel(x, y, new Color(light, light, light, a * a * (3f - 2f * a)));
                    }
                ct.Apply();
                clouds[v] = Sprite.Create(ct, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            }

            // Piedras (en gris: se tiñen con el color del suelo)
            rocks = new[]
            {
                Pix("rockA", 'C', ".LL.", "LMMD", "MMDD", ".DD."),
                Pix("rockB", 'C', "LM.", "MMD", ".DD"),
                Pix("rockC", 'C', "LLM.", "MMMD"),
                Pix("rockD", 'C', ".LM.", "LMMD", "MMDD", "MDD.", ".D.."),
            };
        }

        private static Sprite Meat(int i) => i == 0
            ? Pix("meatA", 'G', ".oo..", "orRo.", "orrRo", ".orro", "..oo.")
            : Pix("meatB", 'G', ".ooo.", "oRrro", "orrro", ".oo..");
        private static Sprite Gut(int i) => i == 0
            ? Pix("gutA", 'G', ".oooooo.", "opPpPpPo", ".oooooo.")
            : Pix("gutB", 'G', ".ooo...", "opPpo..", ".oopPo.", "...opPo", "....oo.");
        private static Sprite Bone() => Pix("bone", 'G', "bB..bB", ".bbbB.", "bB..bB");
        private static Sprite Eye() => Pix("eye", 'G', ".ooo.", "owwwo", "owkno", "owwwo", ".oro.", "..r..");
        private static Sprite Tooth() => Pix("tooth", 'G', "tt", "tT");
        private static Sprite Tail() => Pix("tail", 'G', "rP........", ".PPppp....", "....pppPp.", "........pp");
        private static Sprite FurTuft() => Pix("fur", 'G', ".fF", "fFf", "Ff.");

        private static Sprite Helmet(Color32 team)
        {
            if (helmets.TryGetValue(team, out var s)) return s;
            s = Pix("helmet" + team.r + "_" + team.g + "_" + team.b, 'G',
                    "...ooooo...",
                    "..ohHHhho..",
                    ".ohHhhhhgo.",
                    ".oxxxxxxxo.",
                    "ogggggggggo",
                    ".ooooooooo.");
            // Repinta la franja con el color del equipo
            var tex = s.texture;
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                if (px[i].r == 255 && px[i].g == 0 && px[i].b == 255) px[i] = team;
            tex.SetPixels32(px);
            tex.Apply();
            helmets[team] = s;
            return s;
        }

        /// <summary>
        ///  Sprite pixel-art desde filas de caracteres (fila 0 = arriba), a 32 px/u
        ///  como el terreno. 'C' = paleta de grises para teñir; 'G' = paleta gore.
        /// </summary>
        private static Sprite Pix(string key, char palette, params string[] rows)
        {
            if (pixelArt.TryGetValue(key, out var cached)) return cached;
            int h = rows.Length, w = 0;
            foreach (var row in rows) w = Mathf.Max(w, row.Length);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = x < rows[y].Length ? rows[y][x] : '.';
                    px[(h - 1 - y) * w + x] = PaletteColor(ch);
                }
            tex.SetPixels32(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
            pixelArt[key] = s;
            return s;
        }

        private static Color32 PaletteColor(char ch) => ch switch
        {
            // grises para teñir
            'L' => new Color32(255, 255, 255, 255),
            'M' => new Color32(212, 212, 212, 255),
            'D' => new Color32(150, 150, 150, 255),
            // gore
            'o' => new Color32(46, 8, 12, 255),       // contorno
            'r' => new Color32(150, 18, 24, 255),     // carne
            'R' => new Color32(200, 66, 66, 255),     // carne clara
            'p' => new Color32(212, 118, 118, 255),   // tripa
            'P' => new Color32(158, 64, 76, 255),     // tripa oscura
            'b' => new Color32(234, 226, 202, 255),   // hueso
            'B' => new Color32(184, 172, 150, 255),
            'w' => new Color32(232, 226, 210, 255),   // ojo
            'k' => new Color32(160, 16, 16, 255),
            'n' => new Color32(20, 14, 14, 255),
            't' => new Color32(228, 202, 98, 255),    // diente amarillo
            'T' => new Color32(176, 148, 64, 255),
            'f' => new Color32(118, 104, 90, 255),    // pelo
            'F' => new Color32(78, 68, 58, 255),
            'h' => new Color32(96, 96, 62, 255),      // casco
            'H' => new Color32(130, 130, 88, 255),
            'g' => new Color32(64, 64, 42, 255),
            'x' => new Color32(255, 0, 255, 255),     // franja de equipo (se repinta)
            _ => new Color32(0, 0, 0, 0),
        };

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
            // Salpicaduras de sangre en la losa (estética de trinchera)
            if (GoreOn)
            {
                var bl = new Color32(120, 14, 16, 255);
                o[3 * W + 3] = bl; o[2 * W + 4] = bl; o[4 * W + 8] = bl; o[1 * W + 8] = bl; o[1 * W + 7] = bl;
            }
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
