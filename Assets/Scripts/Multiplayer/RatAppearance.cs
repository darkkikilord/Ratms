using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ASPECTO DE LA RATA — dibujo y animación pixel-art generados por código.
    ///
    ///  Cada rata se construye POR PIEZAS a partir de su <see cref="RatLook"/>
    ///  (anatomía, pelaje, cola, brazo, pata, orejas, ojo, casco, boca,
    ///  marcas). Todas siguen el mismo patrón visual:
    ///   · Escala de píxel del terreno (32 px por unidad), contorno oscuro de
    ///     1 px y luz desde arriba-izquierda (tonos claro/medio/oscuro).
    ///   · El EQUIPO se reconoce por la BUFANDA y el detalle del casco, en
    ///     el color del equipo.
    ///   · Animaciones: respirar, parpadear, andar (4 fotogramas), en el aire
    ///     y mueca de dolor al recibir un golpe.
    ///
    ///  NetworkPlayerMovement lo añade solo al aparecer la rata. El estado
    ///  (quieta/andando/en el aire) se deduce del MOVIMIENTO del transform,
    ///  así funciona igual en el dueño y en los demás clientes.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class RatAppearance : MonoBehaviour
    {
        private static readonly Color32[] TeamColors =
        {
            new Color32(214, 58, 58, 255),    // rojo
            new Color32(60, 118, 226, 255),   // azul
            new Color32(66, 176, 86, 255),    // verde
            new Color32(232, 196, 58, 255),   // amarillo
        };
        private static readonly Color32 DummyColor = new Color32(120, 128, 96, 255);   // caqui (diana)

        private const int S = 32;              // lienzo 32×32
        private const float PPU = 32f;         // = terreno
        private const float PivotX = 14.5f;    // centro del cuerpo
        private const float PivotY = 1f;       // fila de los pies (debajo queda el contorno)

        private enum Frame { Idle0, Idle1, Blink, Walk0, Walk1, Walk2, Walk3, Air, Hurt, Count }
        private static readonly Dictionary<(uint, int, int), Sprite[]> cache = new();

        private NetworkPlayerMovement movement;
        private SpriteRenderer originalBody;
        private SpriteRenderer sr;
        private Collider2D col;
        private BoxCollider2D box;
        private Sprite[] frames;

        private Vector3 lastPos;
        private float vx, vy, airTime, animTime, nextBlink, blinkUntil;

        /// <summary>Color del equipo (bufanda y casco).</summary>
        public Color32 Team { get; private set; }

        /// <summary>Aspecto de esta rata.</summary>
        public RatLook Look { get; private set; }

        private float flashUntil;
        private NetworkHealth health;
        private int wound;

        // SALPICADURAS: sangre de OTRAS ratas (reventadas o heridas al lado).
        // 0..3, se queda toda la partida (en la trinchera no hay lavanderia).
        private int splat;
        private float dripUntil, nextSplatDrip;

        // MUTILACIONES (NetworkHealth.Injuries): patas/brazos arrancados,
        // ojo vendado, prótesis. Cambian el dibujo (y sin patas, se arrastra).
        private byte inj;
        private float nextCrawlDrip;

        private void Redraw() => frames = GetFrames(Look.Pack(), Team, wound, splat, inj);

        /// <summary>Cuánta sangre ajena lleva encima (0-3).</summary>
        public int Splat => splat;

        /// <summary>
        ///  Mancha a esta rata con sangre ajena (<paramref name="levels"/>
        ///  niveles más, hasta 3) y la deja goteando unos segundos.
        ///  Solo visual y local: cada cliente lo calcula igual a partir del
        ///  mismo golpe/muerte, así que no gasta red.
        /// </summary>
        public void Splatter(int levels)
        {
            if (!Fx.GoreOn || levels <= 0 || frames == null) return;
            int s = Mathf.Clamp(splat + levels, 0, 3);
            dripUntil = Time.time + 2.5f + levels;
            if (s == splat) return;
            splat = s;
            Redraw();
        }

        /// <summary>
        ///  Salpica a todas las ratas vivas a menos de <paramref name="radius"/>
        ///  de <paramref name="at"/> (menos <paramref name="except"/>): las
        ///  más cercanas se llevan más sangre y les vuelan gotas encima.
        /// </summary>
        public static void SplatterAround(Vector2 at, float radius, GameObject except, int maxLevels = 2)
        {
            if (!Fx.GoreOn) return;
            foreach (var ra in FindObjectsByType<RatAppearance>(FindObjectsSortMode.None))
            {
                if (ra == null || ra.gameObject == except || ra.sr == null || !ra.isActiveAndEnabled) continue;
                Vector2 c = ra.sr.bounds.center;
                float d = Vector2.Distance(at, c);
                if (d > radius) continue;
                int lv = d < radius * 0.45f ? maxLevels : Mathf.Max(1, maxLevels - 1);
                ra.Splatter(lv);
                Fx.BloodToward(at, c, lv * 7);
            }
        }

        /// <summary>Al recibir un golpe: parpadeo rojo, mueca y "aplastón" un instante.</summary>
        public void HitFlash() => flashUntil = Time.time + 0.32f;

        /// <summary>Color de un equipo (0-3).</summary>
        public static Color32 ColorForTeam(int teamIndex) => TeamColors[Mathf.Abs(teamIndex) % TeamColors.Length];

        public void Init(NetworkPlayerMovement m, int teamIndex, bool isDummy, uint lookCode)
        {
            movement = m;
            col = m.GetComponent<Collider2D>();
            box = m.GetComponent<BoxCollider2D>();
            originalBody = m.GetComponent<SpriteRenderer>();

            var team = isDummy ? DummyColor : ColorForTeam(teamIndex);
            Team = team;
            if (lookCode == 0) lookCode = RatLook.Random(new System.Random((int)m.NetworkObjectId * 7919)).Pack();
            Look = RatLook.Unpack(lookCode);
            frames = GetFrames(lookCode, team);

            var go = new GameObject("RatBody");
            go.transform.SetParent(transform, worldPositionStays: false);
            sr = go.AddComponent<SpriteRenderer>();
            if (originalBody != null)
            {
                sr.sortingLayerID = originalBody.sortingLayerID;
                sr.sortingOrder = originalBody.sortingOrder;
            }
            lastPos = transform.position;
            nextBlink = Time.time + Random.Range(2f, 4f);
            animTime = Random.Range(0f, 2f);   // que no respiren todas a la vez
        }

        /// <summary>Los 4 fotogramas de andar de un aspecto (desfile del menú principal).</summary>
        public static Sprite[] WalkFrames(uint lookCode, Color32 team)
        {
            var f = GetFrames(lookCode, team);
            return new[] { f[(int)Frame.Walk0], f[(int)Frame.Walk1], f[(int)Frame.Walk2], f[(int)Frame.Walk3] };
        }

        /// <summary>Muestra con LESIONES (galería de la prueba). <paramref name="walkFrame"/> 0-3 = andando, -1 = quieta.</summary>
        public static Sprite PreviewInjured(uint lookCode, Color32 team, byte injuries, int walkFrame = -1)
        {
            var f = GetFrames(lookCode, team, 0, 0, injuries);
            return f[walkFrame >= 0 ? (int)Frame.Walk0 + (walkFrame % 4) : (int)Frame.Idle0];
        }

        /// <summary>Sprite de muestra (quieta) de un aspecto: para menús y pruebas.</summary>
        public static Sprite Preview(uint lookCode, Color32 team) => GetFrames(lookCode, team)[(int)Frame.Idle0];

        private void LateUpdate()
        {
            if (sr == null || movement == null) return;
            if (originalBody != null && originalBody.enabled) originalBody.enabled = false;

            // --- Velocidad a partir del movimiento real (sirve en todos los clientes) ---
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 p = transform.position;
            vx = Mathf.Lerp(vx, (p.x - lastPos.x) / dt, 0.35f);
            vy = Mathf.Lerp(vy, (p.y - lastPos.y) / dt, 0.35f);
            lastPos = p;
            airTime = Mathf.Abs(vy) > 0.9f ? airTime + dt : 0f;

            float flash = Mathf.Clamp01((flashUntil - Time.time) / 0.32f);

            // --- Fotograma ---
            Frame f;
            if (flash > 0f) f = Frame.Hurt;
            else if (airTime > 0.08f) f = Frame.Air;
            else if (Mathf.Abs(vx) > 0.3f)
            {
                // La pata de palo cojea: paso más lento.
                bool crawl = health != null && health.Crawling;
                float speed = crawl ? 0.7f : Look.pegLeg || (health != null && health.LegsLost > 0) ? 0.8f : 1f;
                animTime += dt * Mathf.Lerp(7f, 12f, Mathf.Clamp01(Mathf.Abs(vx) / 2f)) * speed;
                f = Frame.Walk0 + ((int)animTime % 4);
            }
            else
            {
                animTime += dt * 1.6f;
                if (Time.time > nextBlink) { blinkUntil = Time.time + 0.12f; nextBlink = Time.time + Random.Range(2.2f, 4.5f); }
                f = Time.time < blinkUntil ? Frame.Blink : (((int)animTime % 2) == 0 ? Frame.Idle0 : Frame.Idle1);
            }
            // Heridas: el dibujo se ensucia de sangre según la vida que le queda.
            if (health == null) health = movement.GetComponent<NetworkHealth>();
            if (health != null && health.MaxHealth > 0)
            {
                float hp = health.CurrentHealth.Value / (float)health.MaxHealth;
                int w = hp > 0.66f ? 0 : hp > 0.33f ? 1 : 2;
                if (w != wound) { wound = w; Redraw(); }
                if (health.Injuries.Value != inj) { inj = health.Injuries.Value; Redraw(); }
                // A rastras: deja un reguero de sangre al avanzar.
                if (health.Crawling && Mathf.Abs(vx) > 0.3f && Time.time > nextCrawlDrip)
                {
                    nextCrawlDrip = Time.time + Random.Range(0.08f, 0.18f);
                    Fx.Drip((Vector2)sr.bounds.center + new Vector2(-Mathf.Sign(vx) * 0.15f, -0.08f));
                }
            }
            // Recién salpicada: le chorrea la sangre ajena un rato.
            if (Time.time < dripUntil && Time.time > nextSplatDrip)
            {
                nextSplatDrip = Time.time + Random.Range(0.12f, 0.35f);
                Fx.Drip((Vector2)sr.bounds.center + new Vector2(Random.Range(-0.2f, 0.2f), -0.2f));
            }
            sr.sprite = frames[(int)f];
            sr.flipX = !movement.FacingRight;

            // --- Golpe: parpadeo rojo/blanco ---
            sr.color = flash > 0f && ((int)(Time.time * 28f) % 2 == 0) ? new Color(1f, 0.3f, 0.3f, 1f) : Color.white;

            // --- Posición: los PIES del dibujo sobre el fondo del collider ---
            if (col != null && col.enabled)
            {
                // Con BoxCollider2D calculamos los pies desde el TRANSFORM (no
                // desde col.bounds): los bounds solo se actualizan en el paso de
                // física, y en las ratas rivales (las mueve la red) iban un paso
                // por detrás → el dibujo se separaba del arma.
                Bounds b = col.bounds;
                float bottom;
                if (box != null)
                {
                    Vector2 localBottom = box.offset - new Vector2(0f, box.size.y * 0.5f);
                    Vector3 wb = transform.TransformPoint(localBottom);
                    Vector3 wc = transform.TransformPoint(box.offset);
                    b.center = new Vector3(wc.x, b.center.y, b.center.z);
                    bottom = wb.y - box.edgeRadius;
                }
                else bottom = b.min.y;
                // Cojera de la pata de palo: un pelín arriba y abajo al andar.
                bool lame = Look.pegLeg || (health != null && health.LegsLost == 1);
                float limp = lame && f >= Frame.Walk0 && f <= Frame.Walk3 && ((int)f % 2 == 1) ? 1f / 32f : 0f;
                sr.transform.position = new Vector3(b.center.x, bottom + limp, transform.position.z);
            }
            Vector3 ls = transform.lossyScale;
            float squash = flash * flash;
            sr.transform.localScale = new Vector3((1f + 0.22f * squash) / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                                                  (1f - 0.18f * squash) / Mathf.Max(0.0001f, Mathf.Abs(ls.y)), 1f);
        }

        private void OnDisable()
        {
            if (originalBody != null && sr != null) originalBody.enabled = false;
        }

        // ============================================================
        //  ANATOMÍAS
        // ============================================================
        /// <summary>Medidas de cada tipo de cuerpo (en píxeles del lienzo, pies en y≈1).</summary>
        private struct Anat
        {
            public float bx, by, brx, bry;          // cuerpo
            public float bdx, bdy, belRx, belRy;    // barriga (relativa al cuerpo)
            public float hx, hy, hrx, hry;          // cabeza
            public float snoutLen, snoutRy;         // hocico
            public int legH, backLegX, frontLegX;   // patas
            public float armX, armY, armR;          // brazo de delante
            public float tailX, tailY, tailLen;     // cola
            public bool hump, naked;
        }

        private static Anat AnatomyOf(RatLook.BodyType b) => b switch
        {
            RatLook.BodyType.Flaca => new Anat
            {
                bx = 14f, by = 11f, brx = 3.6f, bry = 6.2f, bdx = 1.5f, bdy = -1f, belRx = 1.7f, belRy = 4.2f,
                hx = 17.6f, hy = 20.2f, hrx = 4.3f, hry = 3.4f, snoutLen = 4.9f, snoutRy = 1.8f,
                legH = 5, backLegX = 11, frontLegX = 15, armX = 17.8f, armY = 12.8f, armR = 1.8f,
                tailX = 11f, tailY = 6f, tailLen = 1.1f,
            },
            RatLook.BodyType.Jorobada => new Anat
            {
                bx = 13f, by = 8.6f, brx = 6.1f, bry = 5.4f, bdx = 3f, bdy = -1.6f, belRx = 2.6f, belRy = 3.2f,
                hx = 19.8f, hy = 13.6f, hrx = 4.7f, hry = 3.9f, snoutLen = 4.1f, snoutRy = 2.0f,
                legH = 3, backLegX = 9, frontLegX = 15, armX = 19.6f, armY = 8.4f, armR = 2.1f,
                tailX = 8f, tailY = 4f, tailLen = 1f, hump = true,
            },
            RatLook.BodyType.Musculosa => new Anat
            {
                bx = 14.6f, by = 10.2f, brx = 6.5f, bry = 7.1f, bdx = 2.6f, bdy = 0.4f, belRx = 3.4f, belRy = 4.6f,
                hx = 18.6f, hy = 19.8f, hrx = 4.1f, hry = 3.5f, snoutLen = 3.4f, snoutRy = 2.0f,
                legH = 3, backLegX = 10, frontLegX = 16, armX = 20.6f, armY = 11f, armR = 2.9f,
                tailX = 9f, tailY = 4.5f, tailLen = 0.9f,
            },
            RatLook.BodyType.Topo => new Anat
            {
                bx = 13.4f, by = 6.8f, brx = 7.4f, bry = 4.6f, bdx = 1f, bdy = -1.7f, belRx = 5.2f, belRy = 2.1f,
                hx = 21f, hy = 9.4f, hrx = 4.4f, hry = 3.7f, snoutLen = 2.6f, snoutRy = 2.4f,
                legH = 2, backLegX = 9, frontLegX = 17, armX = 19.6f, armY = 5.6f, armR = 1.7f,
                tailX = 6.4f, tailY = 5f, tailLen = 0.45f, naked = true,
            },
            _ => new Anat   // GORDA (la de siempre)
            {
                bx = 13.8f, by = 9.3f, brx = 5.8f, bry = 6.8f, bdx = 2.4f, bdy = -1.1f, belRx = 2.8f, belRy = 4.4f,
                hx = 17f, hy = 18.3f, hrx = 5.4f, hry = 4.4f, snoutLen = 3.8f, snoutRy = 2.3f,
                legH = 3, backLegX = 10, frontLegX = 15, armX = 19f, armY = 10.5f, armR = 2.2f,
                tailX = 9.5f, tailY = 4f, tailLen = 1f,
            },
        };

        private struct Palette { public Color32 fur, furL, furD, belly, bellyD, pink, pinkD; }

        private static Palette PaletteOf(RatLook look)
        {
            if (look.body == RatLook.BodyType.Topo)   // rata topo desnuda: piel rosa arrugada
                return new Palette
                {
                    fur = new Color32(204, 146, 136, 255), furL = new Color32(226, 176, 164, 255), furD = new Color32(156, 102, 98, 255),
                    belly = new Color32(222, 172, 160, 255), bellyD = new Color32(184, 132, 124, 255),
                    pink = new Color32(214, 120, 120, 255), pinkD = new Color32(166, 86, 88, 255),
                };
            var p = new Palette { pink = new Color32(190, 124, 116, 255), pinkD = new Color32(146, 88, 84, 255) };
            switch (look.fur)
            {
                case RatLook.FurType.Gris:
                    p.fur = new Color32(112, 112, 116, 255); p.furL = new Color32(144, 144, 148, 255); p.furD = new Color32(74, 74, 80, 255);
                    p.belly = new Color32(164, 158, 150, 255); p.bellyD = new Color32(128, 124, 118, 255); break;
                case RatLook.FurType.Negra:
                    p.fur = new Color32(60, 54, 58, 255); p.furL = new Color32(88, 82, 86, 255); p.furD = new Color32(38, 33, 37, 255);
                    p.belly = new Color32(104, 96, 94, 255); p.bellyD = new Color32(78, 72, 72, 255); break;
                case RatLook.FurType.Pelirroja:
                    p.fur = new Color32(152, 102, 58, 255); p.furL = new Color32(186, 134, 82, 255); p.furD = new Color32(106, 68, 38, 255);
                    p.belly = new Color32(204, 172, 128, 255); p.bellyD = new Color32(164, 136, 98, 255); break;
                case RatLook.FurType.Albina:
                    p.fur = new Color32(214, 206, 196, 255); p.furL = new Color32(238, 234, 226, 255); p.furD = new Color32(168, 158, 150, 255);
                    p.belly = new Color32(234, 226, 216, 255); p.bellyD = new Color32(200, 190, 180, 255);
                    p.pink = new Color32(230, 140, 146, 255); p.pinkD = new Color32(186, 100, 110, 255); break;
                default:   // Parda y Sarnosa
                    p.fur = new Color32(112, 100, 88, 255); p.furL = new Color32(142, 128, 110, 255); p.furD = new Color32(76, 66, 58, 255);
                    p.belly = new Color32(158, 142, 118, 255); p.bellyD = new Color32(126, 112, 92, 255); break;
            }
            return p;
        }

        // ============================================================
        //  DIBUJO
        // ============================================================
        private static Sprite[] GetFrames(uint lookCode, Color32 team, int wound = 0, int splat = 0, byte inj = 0)
        {
            var key = (lookCode, (team.r << 16) | (team.g << 8) | team.b, wound + splat * 4 + inj * 16);
            if (cache.TryGetValue(key, out var cached)) return cached;
            var look = RatLook.Unpack(lookCode);
            var result = new Sprite[(int)Frame.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Draw((Frame)i, look, team, wound, splat, inj);
            cache[key] = result;
            return result;
        }

        // Índices de "material" del dibujo (luego se convierten a color con luz)
        private const byte Empty = 0, Fur = 1, Belly = 2, Pink = 3, Eye = 4, Pupil = 5, Helmet = 6, Whisker = 7,
                           Band = 8, Teeth = 9, Scar = 10, Mange = 11, Bandage = 12, Blood = 13, Cig = 14, Ember = 15,
                           Brow = 16, Claw = 17, Wood = 18, Brass = 19, Leather = 20, Rubber = 21, Glass = 22, Gold = 23,
                           Wrinkle = 24, Cigar = 25, Patch = 26, BandD = 27;

        private static Sprite Draw(Frame frame, RatLook look, Color32 team, int wound = 0, int splat = 0, byte inj = 0)
        {
            // --- Mutilaciones (ver NetworkHealth.Injuries) ---
            int legsLost = inj & 3, armsLost = (inj >> 2) & 3;
            bool headHurt = (inj & 16) != 0, peg = (inj & 32) != 0, hook = (inj & 64) != 0;
            // La pata de delante es la primera que se pierde; si ya era de
            // palo "de nacimiento", la que se pierde es la de atrás.
            bool frontLegGone = legsLost >= 1;
            bool backLegGone = legsLost >= 2 || (legsLost == 1 && look.pegLeg);
            bool crawl = legsLost >= 2 && !peg;
            bool frontPeg = look.pegLeg || (frontLegGone && peg);
            bool frontArmGone = armsLost >= 1 || look.oneArm;
            bool backArmGone = armsLost >= 2 || (armsLost == 1 && look.oneArm);
            bool hookShown = hook && armsLost >= 1;

            var mat = new byte[S * S];
            var shade = new sbyte[S * S];     // -1 sombra, 0 medio, +1 luz
            var a = AnatomyOf(look.body);
            var rng = new System.Random(look.variant * 31 + 7);

            // --- Pose ---
            int bob = 0, headBob = 0, frontFoot = 0, backFoot = 0, footLift = 0;
            float tailUp = 0f;
            bool blink = frame == Frame.Blink, air = frame == Frame.Air, hurt = frame == Frame.Hurt;
            switch (frame)
            {
                case Frame.Idle1: headBob = -1; break;
                case Frame.Walk0: frontFoot = 2; backFoot = -2; break;
                case Frame.Walk1: bob = 1; break;
                case Frame.Walk2: frontFoot = -2; backFoot = 2; break;
                case Frame.Walk3: bob = 1; break;
                case Frame.Air: footLift = 2; tailUp = 1f; break;
                case Frame.Hurt: headBob = -1; tailUp = 0.6f; break;
            }
            float tailSway = frame >= Frame.Walk0 && frame <= Frame.Walk3 ? ((int)frame % 2 == 0 ? 1f : -1f) : 0f;
            float hx = a.hx, hy = a.hy + bob + headBob;

            // --- Cola ---
            float tailLen = look.tail switch
            {
                RatLook.TailType.Larga => 1.35f, RatLook.TailType.Munon => 0.28f, RatLook.TailType.SinCola => 0f, _ => 1f,
            } * a.tailLen;
            if (tailLen > 0f)
            {
                float thick = a.naked ? 0.5f : 0.9f;
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    float x = a.tailX - 9f * tailLen * t;
                    float y = a.tailY + bob + (4f + 5f * tailUp) * tailLen * t * t + tailSway * Mathf.Sin(t * 3f) +
                              (t > 0.55f && tailLen > 0.6f ? 1.2f : 0f);
                    Dot(mat, shade, x, y, t < 0.3f ? thick : 0.55f, ((int)(t * 14) % 3 == 0) ? Scar : Pink);
                }
                if (look.tail == RatLook.TailType.Munon)   // punta vendada con sangre
                {
                    float tex2 = a.tailX - 9f * tailLen, tey = a.tailY + bob + (4f + 5f * tailUp) * tailLen;
                    Dot(mat, shade, tex2, tey, 1.1f, Bandage);
                    Set(mat, shade, Mathf.RoundToInt(tex2) - 1, Mathf.RoundToInt(tey), Blood, 0);
                }
            }
            else Dot(mat, shade, a.tailX - 0.5f, a.tailY + bob, 0.8f, Scar);   // sin cola: cicatriz

            // --- Pata de atrás ---
            if (backLegGone) LegStump(mat, shade, a.backLegX, a.legH, footLift);
            else
            {
                Rect(mat, shade, a.backLegX + backFoot / 2, 2 + footLift, 2, a.legH, Fur, -1);
                Ellipse(mat, shade, a.backLegX + 1.5f + backFoot, 1.6f + footLift, 2.6f, 1.2f, Pink);
                Set(mat, shade, a.backLegX + 4 + backFoot, 1 + footLift, Claw, 0);
            }

            // --- Cuerpo ---
            if (a.hump) Ellipse(mat, shade, a.bx - 1.8f, a.by + 3.2f + bob, 4.2f, 3.6f, Fur);
            Ellipse(mat, shade, a.bx, a.by + bob, a.brx, a.bry, Fur);
            Ellipse(mat, shade, a.bx + a.bdx, a.by + a.bdy + bob, a.belRx, a.belRy, Belly);
            if (look.body == RatLook.BodyType.Musculosa)   // pectorales marcados
            {
                for (int k = -2; k <= 2; k++) Set(mat, shade, Mathf.RoundToInt(a.bx + a.bdx + k * 0.5f), Mathf.RoundToInt(a.by + bob + 1.2f), Fur, -1);
                Set(mat, shade, Mathf.RoundToInt(a.bx + a.bdx), Mathf.RoundToInt(a.by + bob - 0.5f), Fur, -1);
            }
            if (a.naked)   // arrugas de rata topo
                for (int k = -2; k <= 2; k++)
                {
                    int wx = Mathf.RoundToInt(a.bx + k * 2.6f);
                    for (int dy = -2; dy <= 2; dy++)
                        if ((dy + k) % 2 == 0) Set(mat, shade, wx + (dy > 0 ? 1 : 0), Mathf.RoundToInt(a.by + bob) + dy, Wrinkle, 0, onlyIf: Fur);
                }

            // Sarna (calvas) y sangre seca: según la "variante"
            int mangeSpots = look.fur == RatLook.FurType.Sarnosa ? 7 : 2;
            if (!a.naked)
                for (int k = 0; k < mangeSpots; k++)
                {
                    int mx = Mathf.RoundToInt(a.bx - a.brx * 0.7f + (float)rng.NextDouble() * a.brx * 1.2f);
                    int my = Mathf.RoundToInt(a.by + bob - a.bry * 0.4f + (float)rng.NextDouble() * a.bry * 1.1f);
                    Set(mat, shade, mx, my, Mange, 0, onlyIf: Fur);
                    if (look.fur == RatLook.FurType.Sarnosa) Set(mat, shade, mx + 1, my, Mange, 0, onlyIf: Fur);
                }
            for (int k = 0; k < 2; k++)
            {
                int mx = Mathf.RoundToInt(a.bx - 2f + (float)rng.NextDouble() * 4f);
                int my = Mathf.RoundToInt(a.by + bob - a.bry * 0.5f + (float)rng.NextDouble() * 2f);
                Set(mat, shade, mx, my, Blood, 0, onlyIf: Fur);
            }
            if (look.mark == RatLook.MarkType.Venda)   // venda alrededor de la barriga
                for (int x = Mathf.RoundToInt(a.bx - a.brx); x <= Mathf.RoundToInt(a.bx + a.brx); x++)
                {
                    Set(mat, shade, x, Mathf.RoundToInt(a.by + bob - 1), Bandage, 0, onlyIfSolid: true);
                    Set(mat, shade, x, Mathf.RoundToInt(a.by + bob - 2), (x % 4 == 0) ? Blood : Bandage, 0, onlyIfSolid: true);
                }

            // --- Pata de delante (o pata de palo) ---
            if (frontLegGone && !frontPeg) LegStump(mat, shade, a.frontLegX, a.legH, footLift);
            else if (frontPeg)
            {
                int px = a.frontLegX + frontFoot / 2;
                Rect(mat, shade, px, 3 + footLift, 2, Mathf.Max(1, a.legH - 1), Fur, 0);   // muslo
                for (int y = 1 + footLift; y <= 2 + footLift; y++) Set(mat, shade, px + 1, y, Wood, 0);
                Set(mat, shade, px, 3 + footLift, Leather, 0);                             // correa
            }
            else
            {
                Rect(mat, shade, a.frontLegX + frontFoot / 2, 2 + footLift, 2, a.legH, Fur, 0);
                Ellipse(mat, shade, a.frontLegX + 1.5f + frontFoot, 1.6f + footLift, 2.6f, 1.2f, Pink);
                Set(mat, shade, a.frontLegX + 4 + frontFoot, 1 + footLift, Claw, 0);
            }

            // --- Brazo de delante (o muñón vendado) ---
            float ay = a.armY + bob + (air ? 2.5f : 0f) + (hurt ? 1.5f : 0f);
            float armX = a.armX;
            if (crawl && !air)
            {
                // A RASTRAS: el brazo va por el suelo tirando del cuerpo
                // (alterna delante/detrás al "andar").
                ay = a.legH + 1.4f;
                armX += frontFoot * 0.9f + 1f;
            }
            // Hombro de atrás vendado si también ha perdido ese brazo
            if (backArmGone)
            {
                Dot(mat, shade, armX - 3.4f, ay + 1.2f, 1.1f, Bandage);
                Set(mat, shade, Mathf.RoundToInt(armX - 3.4f), Mathf.RoundToInt(ay), Blood, 0);
            }
            if (frontArmGone)
            {
                // Muñón: hombro con venda empapada y gotas de sangre colgando
                Ellipse(mat, shade, armX - 1.2f, ay + 0.4f, 1.8f, 1.4f, Bandage);
                if (armsLost >= 1 && !hookShown)   // herida FRESCA: venda empapada que gotea
                {
                    Dot(mat, shade, armX - 0.6f, ay + 0.2f, 1.0f, Blood);
                    for (int k = 1; k <= 3; k++) Set(mat, shade, Mathf.RoundToInt(armX - 0.6f), Mathf.RoundToInt(ay) - 1 - k, Blood, (sbyte)(k > 1 ? -1 : 0), onlyEmpty: k > 1);
                }
                Set(mat, shade, Mathf.RoundToInt(armX - 0.2f), Mathf.RoundToInt(ay), Blood, 0);
                Set(mat, shade, Mathf.RoundToInt(armX - 0.2f), Mathf.RoundToInt(ay) - 1, Blood, 0);
                Set(mat, shade, Mathf.RoundToInt(armX - 1.2f), Mathf.RoundToInt(ay) - 1, Blood, 0);
                if (hookShown)
                {
                    // GARFIO de metal saliendo del muñón
                    int hx0 = Mathf.RoundToInt(armX + 0.6f), hy0 = Mathf.RoundToInt(ay);
                    Rect(mat, shade, hx0, hy0 - 1, 2, 2, Brass, 0);                       // abrazadera
                    Set(mat, shade, hx0 + 2, hy0, Whisker, 0); Set(mat, shade, hx0 + 3, hy0, Whisker, 0);
                    Set(mat, shade, hx0 + 4, hy0 - 1, Whisker, 0); Set(mat, shade, hx0 + 4, hy0 - 2, Whisker, 0);
                    Set(mat, shade, hx0 + 4, hy0 - 3, Whisker, -1); Set(mat, shade, hx0 + 3, hy0 - 4, Whisker, -1);
                    Set(mat, shade, hx0 + 2, hy0 - 3, Whisker, 0);
                }
                else
                    Set(mat, shade, Mathf.RoundToInt(armX - 0.2f), Mathf.RoundToInt(ay) - 3, Blood, 0, onlyEmpty: true);
            }
            else
            {
                Ellipse(mat, shade, armX, ay, a.armR, a.armR * 0.7f, Fur);
                Dot(mat, shade, armX + a.armR, ay, a.armR * 0.5f, Pink);
            }

            // --- Bufanda del equipo (se reconoce el equipo en todas las ratas) ---
            {
                float nx = hx - a.hrx * 0.55f, ny = hy - a.hry * 0.8f;
                if (a.naked || look.body == RatLook.BodyType.Jorobada)
                    Ellipse(mat, shade, nx - 0.5f, ny + 0.6f, 1.6f, 2.4f, Band);
                else
                    Ellipse(mat, shade, nx, ny, 2.8f, 1.3f, Band);
                // Punta que cuelga por detrás, ondeando al andar
                int scx = Mathf.RoundToInt(nx - 2.5f), scy = Mathf.RoundToInt(ny - 0.5f);
                Set(mat, shade, scx, scy, BandD, 0); Set(mat, shade, scx - 1, scy - 1 + (tailSway > 0 ? 1 : 0), BandD, 0);
                Set(mat, shade, scx - 2, scy - 1 + (air ? 1 : 0), Band, 0);
            }

            // --- Oreja de atrás ---
            if (look.ear != RatLook.EarType.Arrancada && !a.naked)
            {
                float er = a.hrx * 0.57f * (look.ear == RatLook.EarType.Grande ? 1.4f : 1f);
                float earX = hx - a.hrx * 0.87f, earY = hy + a.hry * 0.9f;
                Ellipse(mat, shade, earX, earY, er, er, Fur);
                Ellipse(mat, shade, earX + 0.1f, earY - 0.1f, er * 0.52f, er * 0.52f, Pink);
                if (look.ear == RatLook.EarType.Rota)
                {
                    Set(mat, shade, Mathf.RoundToInt(earX + er * 0.5f), Mathf.RoundToInt(earY + er * 0.8f), Empty, 0);
                    Set(mat, shade, Mathf.RoundToInt(earX + er * 0.2f), Mathf.RoundToInt(earY + er * 0.9f), Empty, 0);
                    Set(mat, shade, Mathf.RoundToInt(earX + er * 0.5f), Mathf.RoundToInt(earY + er * 0.5f), Empty, 0);
                }
            }
            else if (!a.naked)   // oreja arrancada: muñón con costra
                Dot(mat, shade, hx - a.hrx * 0.8f, hy + a.hry * 0.75f, 1f, Scar);

            // --- Cabeza y hocico ---
            float snx = hx + a.hrx * 0.93f;
            Ellipse(mat, shade, hx, hy, a.hrx, a.hry, Fur);
            Ellipse(mat, shade, snx, hy - 1.2f, a.snoutLen, a.snoutRy, Fur);
            Ellipse(mat, shade, snx + 0.4f, hy - 1.2f - a.snoutRy * 0.55f, a.snoutLen * 0.63f, 1.0f, Belly);   // mentón
            float noseX = snx + a.snoutLen - 0.2f;
            Dot(mat, shade, noseX, hy - 0.8f, a.naked ? 1.3f : 1.1f, Pink);
            if (hurt)   // boca abierta gritando
            {
                int my = Mathf.RoundToInt(hy - 2.2f);
                Set(mat, shade, Mathf.RoundToInt(snx + 1), my, Brow, 0); Set(mat, shade, Mathf.RoundToInt(snx + 2), my, Brow, 0);
                Set(mat, shade, Mathf.RoundToInt(snx + 2), my - 1, Blood, 0);
            }
            if (a.naked)   // arrugas en la cara
            {
                Set(mat, shade, Mathf.RoundToInt(hx - 1), Mathf.RoundToInt(hy + 1), Wrinkle, 0);
                Set(mat, shade, Mathf.RoundToInt(hx - 2), Mathf.RoundToInt(hy), Wrinkle, 0);
                Set(mat, shade, Mathf.RoundToInt(hx - 1), Mathf.RoundToInt(hy - 2), Wrinkle, 0);
            }

            // --- Dientes ---
            int ty = Mathf.RoundToInt(hy - 1.2f - a.snoutRy - 0.1f);
            int tx = Mathf.RoundToInt(snx + 1);
            if (a.naked)
            {
                // Rata topo: paletas ENORMES que salen del hocico
                int fx = Mathf.RoundToInt(noseX) - 1;
                for (int k = 0; k < 4; k++) { Set(mat, shade, fx, ty + 1 - k, Teeth, 0); Set(mat, shade, fx + 1, ty + 1 - k, Teeth, 0); }
                if (look.goldTooth) { Set(mat, shade, fx + 1, ty - 1, Gold, 0); Set(mat, shade, fx + 1, ty - 2, Gold, 0); }
            }
            else if (look.mouth == RatLook.MouthType.Mellada)
            {
                Set(mat, shade, tx, ty, Teeth, 0); Set(mat, shade, tx, ty - 1, Teeth, 0);
                Set(mat, shade, tx + 2, ty, look.goldTooth ? Gold : Teeth, 0);
            }
            else
            {
                Set(mat, shade, tx, ty, Teeth, 0); Set(mat, shade, tx + 1, ty, look.goldTooth ? Gold : Teeth, 0); Set(mat, shade, tx, ty - 1, Teeth, 0);
            }

            // --- Marcas en la cara ---
            int sy = Mathf.RoundToInt(hy - 1f);
            int cheekX = Mathf.RoundToInt(hx - 1);
            if (look.mark == RatLook.MarkType.Cicatriz)
            {
                Set(mat, shade, cheekX, sy + 1, Scar, 0); Set(mat, shade, cheekX + 1, sy, Scar, 0); Set(mat, shade, cheekX + 2, sy - 1, Scar, 0);
            }
            else if (look.mark == RatLook.MarkType.Puntos)   // costura de Frankenstein
            {
                for (int k = 0; k < 4; k++)
                {
                    Set(mat, shade, cheekX - 1 + k, sy + 2 - (k / 2), Scar, 0);
                    if (k % 2 == 0) { Set(mat, shade, cheekX - 1 + k, sy + 3 - (k / 2), Brow, 0); Set(mat, shade, cheekX - 1 + k, sy + 1 - (k / 2), Brow, 0); }
                }
            }

            // --- Boca: cigarro / puro ---
            bool masked = look.hat == RatLook.HatType.MascaraGas;
            if (!masked && !a.naked && !hurt)
            {
                int cy = Mathf.RoundToInt(hy - 1.2f - a.snoutRy * 0.6f);
                int cx = Mathf.RoundToInt(noseX - 1);
                if (look.mouth == RatLook.MouthType.Cigarro)
                {
                    for (int k = 0; k < 4; k++) Set(mat, shade, cx + k, cy - (k >= 2 ? 1 : 0), Cig, 0);
                    Set(mat, shade, cx + 4, cy - 1, Ember, 0);
                }
                else if (look.mouth == RatLook.MouthType.Puro)
                {
                    for (int k = 0; k < 4; k++) { Set(mat, shade, cx + k, cy, Cigar, 0); Set(mat, shade, cx + k, cy - 1, Cigar, -1); }
                    Set(mat, shade, cx + 4, cy, Ember, 0); Set(mat, shade, cx + 4, cy - 1, Ember, 0);
                    Set(mat, shade, cx + 1, cy, Band, 0);   // anilla del puro (color de equipo)
                }
            }

            // --- Ojo ---
            int ey = Mathf.RoundToInt(hy - 0.2f + (a.naked ? 0.8f : 0f));
            int exx = Mathf.RoundToInt(hx + 2f - (a.naked ? 0.5f : 0f));
            if (masked)
            {
                // MÁSCARA DE GAS: goma sobre el hocico, ojo de cristal y filtro colgando
                Ellipse(mat, shade, snx + 0.6f, hy - 1.4f, a.snoutLen * 0.85f, a.snoutRy + 0.4f, Rubber);
                Dot(mat, shade, exx + 0.4f, ey + 0.2f, 1.6f, Brass);
                Dot(mat, shade, exx + 0.4f, ey + 0.2f, 1.0f, Glass);
                if (hurt) Set(mat, shade, exx, ey, Blood, 0);
                int fx = Mathf.RoundToInt(snx + 1), fy = Mathf.RoundToInt(hy - 1.2f - a.snoutRy - 1.5f);
                Rect(mat, shade, fx, fy - 2, 3, 3, Leather, 0);
                Set(mat, shade, fx + 1, fy - 3, Brass, 0);
            }
            else if (a.naked)
            {
                Set(mat, shade, exx, ey, blink || hurt ? Wrinkle : Brow, 0);   // ojillos diminutos
            }
            else if (look.eye == RatLook.EyeType.Parche)
            {
                Set(mat, shade, exx, ey, Patch, 0); Set(mat, shade, exx + 1, ey, Patch, 0);
                Set(mat, shade, exx, ey + 1, Patch, 0); Set(mat, shade, exx + 1, ey + 1, Patch, 0);
                for (int k = 1; k <= 4; k++) Set(mat, shade, exx - k, ey + 1 + (k > 2 ? 1 : 0), Patch, 0);   // cinta
            }
            else
            {
                Set(mat, shade, exx - 1, ey + 2, Brow, 0); Set(mat, shade, exx, ey + 2, Brow, 0); Set(mat, shade, exx + 1, ey + 1, Brow, 0);
                if (blink || hurt)
                {
                    Set(mat, shade, exx, ey, Brow, 0); Set(mat, shade, exx + 1, ey, Brow, 0);
                    if (hurt) Set(mat, shade, exx + 1, ey + 1, Brow, 0);
                }
                else if (look.eye == RatLook.EyeType.Loco)   // ojo saltón de loco
                {
                    Dot(mat, shade, exx + 0.5f, ey + 0.5f, 1.3f, Eye);
                    Set(mat, shade, exx + 1, ey + 1, Pupil, 0);
                }
                else
                {
                    Set(mat, shade, exx, ey, Eye, 0); Set(mat, shade, exx + 1, ey, Pupil, 0);
                    Set(mat, shade, exx, ey + 1, Eye, 0); Set(mat, shade, exx - 1, ey, Blood, 0);
                    if (look.eye == RatLook.EyeType.Monoculo)
                    {
                        Set(mat, shade, exx + 2, ey, Gold, 0); Set(mat, shade, exx + 2, ey + 1, Gold, 0);
                        Set(mat, shade, exx - 1, ey + 1, Gold, 0); Set(mat, shade, exx, ey - 1, Gold, 0); Set(mat, shade, exx + 1, ey - 1, Gold, 0);
                        for (int k = 1; k <= 3; k++) Set(mat, shade, exx - 1, ey - k - 1, Gold, 0, onlyEmpty: true);   // cadenita
                    }
                }
            }

            // --- Casco / gorro ---
            DrawHat(mat, shade, look, a, hx, hy);

            // --- Bigotes ---
            if (!masked)
                for (int k = 0; k < (a.naked ? 2 : 4); k++)
                {
                    int wx = Mathf.RoundToInt(noseX - 1.6f);
                    Set(mat, shade, wx + k, Mathf.RoundToInt(hy + 0.2f + k * 0.45f), Whisker, 0, onlyEmpty: true);
                    Set(mat, shade, wx + 1 + k, Mathf.RoundToInt(hy - 1.0f - k * 0.2f), Whisker, 0, onlyEmpty: true);
                }

            // --- TUERTA: venda ensangrentada cruzando la cabeza sobre el ojo ---
            if (headHurt)
            {
                int x0 = Mathf.RoundToInt(hx - a.hrx - 0.5f);
                for (int x = x0; x <= exx + 1; x++)
                {
                    Set(mat, shade, x, ey + 1, Bandage, 0, onlyIfSolid: true);
                    Set(mat, shade, x, ey, Bandage, (sbyte)(x % 3 == 0 ? -1 : 0), onlyIfSolid: true);
                }
                Set(mat, shade, exx, ey, Blood, 0); Set(mat, shade, exx + 1, ey, Blood, 0); Set(mat, shade, exx, ey + 1, Blood, -1);
                Set(mat, shade, exx, ey - 1, Blood, 0, onlyIfSolid: true); Set(mat, shade, exx, ey - 2, Blood, -1, onlyIfSolid: true);
                Set(mat, shade, x0 - 1, ey, Bandage, 0); Set(mat, shade, x0 - 2, ey - 1, Bandage, 0);   // nudo que cuelga
            }

            // --- HERIDAS: cuanto menos vida, más sangre en el pelaje ---
            if (wound > 0)
            {
                var wr = new System.Random(look.variant * 13 + 5);
                int spots = wound == 1 ? 6 : 15;
                for (int k = 0, tries = 0; k < spots && tries < 400; tries++)
                {
                    int x = wr.Next(S), y = wr.Next(3, S);
                    byte m0 = mat[y * S + x];
                    if (m0 != Fur && m0 != Belly) continue;
                    Set(mat, shade, x, y, Blood, (sbyte)(wr.Next(2) == 0 ? 0 : -1));
                    if (wound == 2)
                    {
                        // churretes que caen
                        for (int dy = 1; dy <= 1 + wr.Next(3); dy++) Set(mat, shade, x, y - dy, Blood, -1, onlyIf: Fur);
                        if (wr.Next(3) == 0) Set(mat, shade, x + 1, y, Blood, 0, onlyIf: Fur);
                    }
                    k++;
                }
            }

            // --- SALPICADURAS de sangre ajena: manchas por TODO (casco,
            //     bufanda, cara...), más grandes y con churretes al subir de nivel.
            if (splat > 0)
            {
                var sp = new System.Random(look.variant * 29 + 3);
                int blobs = splat == 1 ? 7 : splat == 2 ? 15 : 26;
                for (int k = 0, tries = 0; k < blobs && tries < 600; tries++)
                {
                    int x = sp.Next(S), y = sp.Next(2, S);
                    byte m0 = mat[y * S + x];
                    if (m0 == Empty || m0 == Eye || m0 == Pupil || m0 == Blood) continue;
                    sbyte sh = (sbyte)(sp.Next(3) == 0 ? -1 : 0);
                    Set(mat, shade, x, y, Blood, sh);
                    // gotón de 2-4 px
                    int extra = sp.Next(1 + splat);
                    for (int e = 0; e < extra; e++)
                        Set(mat, shade, x + sp.Next(-1, 2), y + sp.Next(-1, 2), Blood, sh, onlyIfSolid: true);
                    // churrete que cae
                    if (splat >= 2 && sp.Next(2) == 0)
                        for (int dy = 1; dy <= 1 + sp.Next(splat + 1); dy++) Set(mat, shade, x, y - dy, Blood, -1, onlyIfSolid: true);
                    k++;
                }
            }

            // --- A RASTRAS: sin patas, la panza va pegada al suelo ---
            if (crawl)
            {
                int drop = Mathf.Max(1, a.legH);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        int src = (y + drop) * S + x, dst = y * S + x;
                        bool inside = y + drop < S;
                        mat[dst] = inside ? mat[src] : Empty;
                        shade[dst] = inside ? shade[src] : (sbyte)0;
                    }
            }

            return ToSprite(mat, shade, look, team);
        }

        /// <summary>Muñón de pata: un trocito de muslo con venda empapada.</summary>
        private static void LegStump(byte[] mat, sbyte[] shade, int x, int legH, int footLift)
        {
            int top = 2 + footLift + legH;
            Rect(mat, shade, x, top - 2, 2, 2, Fur, 0);
            Set(mat, shade, x, top - 3, Bandage, 0); Set(mat, shade, x + 1, top - 3, Bandage, 0);
            Set(mat, shade, x, top - 4, Blood, 0); Set(mat, shade, x + 1, top - 4, Blood, -1);
            Set(mat, shade, x + 1, top - 5, Blood, -1, onlyEmpty: true);
        }

        private static void DrawHat(byte[] mat, sbyte[] shade, RatLook look, Anat a, float hx, float hy)
        {
            float domeX = hx - 0.4f, domeY = hy + a.hry * 0.55f;
            float rx = Mathf.Max(a.hrx, 4.6f);
            switch (look.hat)
            {
                case RatLook.HatType.Adrian:   // francés: más alto, con cresta
                    Dome(mat, shade, domeX, domeY, rx * 0.95f, 3.6f, Helmet);
                    for (float x = domeX - rx * 0.6f; x <= domeX + rx * 0.5f; x += 1f)
                    {
                        float dx = (x - domeX) / (rx * 0.95f);
                        int top = Mathf.RoundToInt(domeY + 3.6f * Mathf.Sqrt(Mathf.Max(0f, 1f - dx * dx)));
                        Set(mat, shade, Mathf.RoundToInt(x), top, Helmet, 1);
                    }
                    for (int x = Mathf.RoundToInt(domeX - rx - 1); x <= Mathf.RoundToInt(domeX + rx + 2); x++) Set(mat, shade, x, Mathf.RoundToInt(domeY), Helmet, -1);
                    for (int x = Mathf.RoundToInt(domeX - rx * 0.6f); x <= Mathf.RoundToInt(domeX + rx * 0.6f); x++) Set(mat, shade, x, Mathf.RoundToInt(domeY) + 1, Band, 0);
                    break;
                case RatLook.HatType.Pickelhaube:   // alemán de cuero con pincho (ridículo)
                    Dome(mat, shade, domeX, domeY, rx * 0.9f, 3.2f, Leather);
                    {
                        int sx = Mathf.RoundToInt(domeX), top = Mathf.RoundToInt(domeY + 3.2f);
                        for (int k = 1; k <= 4; k++) Set(mat, shade, sx, top + k, Brass, k == 4 ? (sbyte)1 : (sbyte)0);
                        Set(mat, shade, sx - 1, top + 1, Brass, 0); Set(mat, shade, sx + 1, top + 1, Brass, 0);
                        Set(mat, shade, Mathf.RoundToInt(domeX + rx * 0.6f), Mathf.RoundToInt(domeY + 1.5f), Brass, 0);   // placa
                    }
                    for (int x = Mathf.RoundToInt(domeX + rx * 0.3f); x <= Mathf.RoundToInt(domeX + rx + 2); x++) Set(mat, shade, x, Mathf.RoundToInt(domeY), Leather, -1);   // visera
                    for (int x = Mathf.RoundToInt(domeX - rx * 0.8f); x <= Mathf.RoundToInt(domeX + rx * 0.4f); x++) Set(mat, shade, x, Mathf.RoundToInt(domeY) + 1, Band, 0);
                    break;
                case RatLook.HatType.Gorra:   // gorra de plato, con la parte de arriba del color del equipo
                    {
                        int by = Mathf.RoundToInt(domeY);
                        for (int x = Mathf.RoundToInt(domeX - rx * 0.8f); x <= Mathf.RoundToInt(domeX + rx * 0.7f); x++)
                        {
                            Set(mat, shade, x, by, Helmet, -1); Set(mat, shade, x, by + 1, Helmet, 0);
                        }
                        for (int x = Mathf.RoundToInt(domeX - rx * 1.05f); x <= Mathf.RoundToInt(domeX + rx * 0.9f); x++)
                        {
                            Set(mat, shade, x, by + 2, Band, 0); Set(mat, shade, x, by + 3, x < domeX ? Band : BandD, 0);
                        }
                        for (int x = Mathf.RoundToInt(domeX + rx * 0.7f); x <= Mathf.RoundToInt(domeX + rx + 2); x++) Set(mat, shade, x, by - 1, Leather, 0);   // visera
                        Set(mat, shade, Mathf.RoundToInt(domeX + rx * 0.5f), by + 1, Brass, 0);   // insignia
                    }
                    break;
                case RatLook.HatType.Panuelo:   // pañuelo atado del color del equipo
                    Dome(mat, shade, domeX, domeY - 0.3f, rx * 0.95f, 2.6f, Band);
                    {
                        int kx = Mathf.RoundToInt(domeX - rx * 0.95f), ky = Mathf.RoundToInt(domeY);
                        Set(mat, shade, kx - 1, ky, BandD, 0); Set(mat, shade, kx - 2, ky - 1, BandD, 0); Set(mat, shade, kx - 2, ky + 1, Band, 0);
                        Set(mat, shade, kx - 3, ky - 2, Band, 0);
                        for (int k = 0; k < 3; k++) Set(mat, shade, Mathf.RoundToInt(domeX - 1 + k * 2), ky + 1, BandD, 0);   // lunares
                    }
                    break;
                default:   // BRODIE inglés (y el que lleva la máscara de gas): ala muy ancha
                    Dome(mat, shade, domeX + 0.2f, domeY, rx, 3.0f, Helmet);
                    {
                        int brimY = Mathf.RoundToInt(domeY);
                        int half = Mathf.RoundToInt(rx * 1.6f);
                        for (int x = Mathf.RoundToInt(hx) - half; x <= Mathf.RoundToInt(hx) + half - 1; x++) Set(mat, shade, x, brimY, Helmet, -1);
                        for (int x = Mathf.RoundToInt(hx - rx * 0.9f); x <= Mathf.RoundToInt(hx + rx * 0.8f); x++) Set(mat, shade, x, brimY + 1, Band, 0);
                        Set(mat, shade, Mathf.RoundToInt(hx - 1), brimY + 2, Empty, 0);   // abolladura
                    }
                    break;
            }
        }

        /// <summary>Media elipse (casco) con luz arriba-izquierda.</summary>
        private static void Dome(byte[] mat, sbyte[] shade, float cx, float cy, float rx, float ry, byte m)
        {
            for (int y = Mathf.FloorToInt(cy); y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dy >= 0f && dx * dx + dy * dy <= 1f)
                        Set(mat, shade, x, y, m, (sbyte)(dx < -0.25f && dy > 0.4f ? 1 : dx > 0.4f ? -1 : 0));
                }
        }

        // ============================================================
        //  A COLOR
        // ============================================================
        private static Sprite ToSprite(byte[] mat, sbyte[] shade, RatLook look, Color32 team)
        {
            var p = PaletteOf(look);
            bool albino = look.fur == RatLook.FurType.Albina;
            Color32 eye = new Color32(226, 214, 160, 255), pupil = albino ? new Color32(220, 30, 40, 255) : new Color32(150, 18, 18, 255);
            Color32 whisker = new Color32(176, 166, 150, 190);
            Color32 helmet, helmetL, helmetD;
            if (look.hat == RatLook.HatType.Adrian) { helmet = new Color32(98, 110, 130, 255); helmetL = new Color32(130, 142, 162, 255); helmetD = new Color32(66, 76, 94, 255); }
            else if (look.hat == RatLook.HatType.Gorra) { helmet = new Color32(104, 92, 64, 255); helmetL = new Color32(134, 120, 86, 255); helmetD = new Color32(72, 62, 42, 255); }
            else { helmet = new Color32(96, 96, 62, 255); helmetL = new Color32(126, 126, 84, 255); helmetD = new Color32(66, 66, 44, 255); }
            Color32 teeth = new Color32(226, 200, 96, 255), scar = new Color32(112, 42, 40, 255), mange = new Color32(176, 126, 116, 255);
            Color32 bandage = new Color32(204, 194, 164, 255), blood = new Color32(150, 14, 14, 255);
            Color32 cig = new Color32(230, 226, 214, 255), ember = new Color32(255, 128, 40, 255);
            Color32 brow = new Color32(36, 26, 24, 255), claw = new Color32(214, 204, 180, 255);
            Color32 wood = new Color32(128, 88, 50, 255), brass = new Color32(206, 166, 64, 255), leather = new Color32(52, 40, 34, 255);
            Color32 rubber = new Color32(92, 104, 80, 255), rubberL = new Color32(124, 136, 106, 255), glass = new Color32(170, 220, 214, 255);
            Color32 gold = new Color32(246, 204, 70, 255), wrinkle = new Color32(150, 96, 92, 255), cigar = new Color32(96, 60, 34, 255), cigarD = new Color32(70, 42, 24, 255);
            Color32 patch = new Color32(24, 20, 20, 255);
            Color32 band = team, bandD = Darken(team, 0.35f);
            Color32 outline = new Color32(20, 15, 16, 255);

            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                sbyte s = shade[i];
                px[i] = mat[i] switch
                {
                    Fur => s > 0 ? p.furL : s < 0 ? p.furD : p.fur,
                    Belly => s < 0 ? p.bellyD : p.belly,
                    Pink => s < 0 ? p.pinkD : p.pink,
                    Eye => eye,
                    Pupil => pupil,
                    Helmet => s > 0 ? helmetL : s < 0 ? helmetD : helmet,
                    Whisker => whisker,
                    Band => s < 0 ? bandD : band,
                    BandD => bandD,
                    Teeth => teeth,
                    Scar => scar,
                    Mange => mange,
                    Bandage => bandage,
                    Blood => blood,
                    Cig => cig,
                    Ember => ember,
                    Brow => brow,
                    Claw => claw,
                    Wood => wood,
                    Brass => s > 0 ? Lighten(brass, 0.3f) : brass,
                    Leather => s > 0 ? Lighten(leather, 0.25f) : leather,
                    Rubber => s > 0 ? rubberL : rubber,
                    Glass => glass,
                    Gold => gold,
                    Wrinkle => wrinkle,
                    Cigar => s < 0 ? cigarD : cigar,
                    Patch => patch,
                    _ => new Color32(0, 0, 0, 0),
                };
            }
            // Contorno: todo píxel vacío que toca a uno relleno (no bigote)
            var outlined = (Color32[])px.Clone();
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    if (mat[i] != Empty) continue;
                    if (Solid(mat, x - 1, y) || Solid(mat, x + 1, y) || Solid(mat, x, y - 1) || Solid(mat, x, y + 1))
                        outlined[i] = outline;
                }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(outlined);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(PivotX / S, PivotY / S), PPU, 0, SpriteMeshType.FullRect);
        }

        private static bool Solid(byte[] mat, int x, int y) =>
            x >= 0 && y >= 0 && x < S && y < S && mat[y * S + x] != Empty && mat[y * S + x] != Whisker;

        private static void Set(byte[] mat, sbyte[] shade, int x, int y, byte m, sbyte s,
                                bool onlyEmpty = false, byte onlyIf = 255, bool onlyIfSolid = false)
        {
            if (x < 0 || y < 0 || x >= S || y >= S) return;
            int i = y * S + x;
            if (onlyEmpty && mat[i] != Empty) return;
            if (onlyIf != 255 && mat[i] != onlyIf) return;
            if (onlyIfSolid && mat[i] == Empty) return;
            mat[i] = m;
            shade[i] = s;
        }

        /// <summary>Elipse rellena con sombreado por luz desde arriba-izquierda.</summary>
        private static void Ellipse(byte[] mat, sbyte[] shade, float cx, float cy, float rx, float ry, byte m)
        {
            int x0 = Mathf.FloorToInt(cx - rx - 1), x1 = Mathf.CeilToInt(cx + rx + 1);
            int y0 = Mathf.FloorToInt(cy - ry - 1), y1 = Mathf.CeilToInt(cy + ry + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                    float d = nx * nx + ny * ny;
                    if (d > 1f) continue;
                    float light = -nx * 0.6f + ny * 0.8f;
                    sbyte s = (sbyte)(light > 0.45f && d > 0.25f ? 1 : light < -0.35f && d > 0.35f ? -1 : 0);
                    Set(mat, shade, x, y, m, s);
                }
        }

        private static void Rect(byte[] mat, sbyte[] shade, int x, int y, int w, int h, byte m, sbyte s)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Set(mat, shade, xx, yy, m, s);
        }

        private static void Dot(byte[] mat, sbyte[] shade, float cx, float cy, float r, byte m) =>
            Ellipse(mat, shade, cx, cy, Mathf.Max(0.5f, r), Mathf.Max(0.5f, r), m);

        private static Color32 Lighten(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255, c.r + (255 - c.r) * k), (byte)Mathf.Min(255, c.g + (255 - c.g) * k), (byte)Mathf.Min(255, c.b + (255 - c.b) * k), c.a);
        private static Color32 Darken(Color32 c, float k) =>
            new Color32((byte)(c.r * (1 - k)), (byte)(c.g * (1 - k)), (byte)(c.b * (1 - k)), c.a);
    }
}
