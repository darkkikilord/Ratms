using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ASPECTO DE LA RATA — dibujo y animación pixel-art generados por código.
    ///
    ///  Sustituye al sprite antiguo (archivos .ase) por una rata más cuidada:
    ///   · Misma escala de píxel que el terreno (32 px por unidad) → todo el
    ///     juego se ve coherente.
    ///   · Contorno oscuro de 1 px para que se lea bien sobre cualquier fondo.
    ///   · Luz desde arriba-izquierda (tonos claro/medio/oscuro).
    ///   · El COLOR DE EQUIPO va solo en el CASCO (antes se teñía la rata
    ///     entera y quedaba de un marrón apagado). El color sale del número
    ///     de jugador, así lo ven igual TODOS los clientes (el tinte antiguo
    ///     solo lo aplicaba el servidor).
    ///   · Animaciones: respirar, parpadear, andar (4 fotogramas), en el aire.
    ///
    ///  NetworkPlayerMovement lo añade solo al aparecer la rata (no hay que
    ///  tocar el prefab). Para volver a la rata antigua: desmarca
    ///  "useProceduralRat" en NetworkPlayerMovement.
    ///
    ///  El estado (quieta/andando/en el aire) se deduce del MOVIMIENTO del
    ///  propio transform, así funciona igual en el dueño y en los demás
    ///  clientes (que no simulan física).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class RatAppearance : MonoBehaviour
    {
        // Paleta de cascos por jugador (OwnerClientId % n).
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

        private enum Frame { Idle0, Idle1, Blink, Walk0, Walk1, Walk2, Walk3, Air, Count }
        private static readonly Dictionary<Color32, Sprite[]> cache = new();

        private NetworkPlayerMovement movement;
        private SpriteRenderer originalBody;
        private SpriteRenderer sr;
        private Collider2D col;
        private BoxCollider2D box;
        private Sprite[] frames;

        private Vector3 lastPos;
        private float vx, vy, airTime, animTime, nextBlink, blinkUntil;

        public void Init(NetworkPlayerMovement m, ulong ownerId, bool isDummy)
        {
            movement = m;
            col = m.GetComponent<Collider2D>();
            box = m.GetComponent<BoxCollider2D>();
            originalBody = m.GetComponent<SpriteRenderer>();

            var team = isDummy ? DummyColor : TeamColors[(int)(ownerId % (ulong)TeamColors.Length)];
            frames = GetFrames(team);

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
        }

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

            // --- Fotograma ---
            Frame f;
            if (airTime > 0.08f) f = Frame.Air;
            else if (Mathf.Abs(vx) > 0.3f)
            {
                animTime += dt * Mathf.Lerp(7f, 12f, Mathf.Clamp01(Mathf.Abs(vx) / 2f));
                f = Frame.Walk0 + ((int)animTime % 4);
            }
            else
            {
                animTime += dt * 1.6f;
                if (Time.time > nextBlink) { blinkUntil = Time.time + 0.12f; nextBlink = Time.time + Random.Range(2.2f, 4.5f); }
                f = Time.time < blinkUntil ? Frame.Blink : (((int)animTime % 2) == 0 ? Frame.Idle0 : Frame.Idle1);
            }
            sr.sprite = frames[(int)f];
            sr.flipX = !movement.FacingRight;

            // --- Posición: los PIES del dibujo sobre el fondo del collider ---
            if (col != null && col.enabled)
            {
                Bounds b = col.bounds;
                float bottom = b.min.y - (box != null ? box.edgeRadius : 0f);
                sr.transform.position = new Vector3(b.center.x, bottom, transform.position.z);
            }
            // El hijo no debe heredar escalas raras del prefab.
            Vector3 ls = transform.lossyScale;
            sr.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)), 1f);
        }

        private void OnDisable()
        {
            if (originalBody != null && sr != null) originalBody.enabled = false;
        }

        // ============================================================
        //  DIBUJO
        // ============================================================
        private static Sprite[] GetFrames(Color32 team)
        {
            if (cache.TryGetValue(team, out var cached)) return cached;
            var result = new Sprite[(int)Frame.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Draw((Frame)i, team);
            cache[team] = result;
            return result;
        }

        // Índices de "material" del dibujo (luego se convierten a color con luz)
        private const byte Empty = 0, Fur = 1, Belly = 2, Pink = 3, Eye = 4, Glint = 5, Helmet = 6, Whisker = 7, Strap = 8;

        private static Sprite Draw(Frame frame, Color32 team)
        {
            var mat = new byte[S * S];
            var shade = new sbyte[S * S];     // -1 sombra, 0 medio, +1 luz

            // Parámetros de pose
            int bob = 0, headBob = 0, frontFoot = 0, backFoot = 0, footLift = 0;
            float tailUp = 0f;
            bool blink = frame == Frame.Blink, air = frame == Frame.Air;
            switch (frame)
            {
                case Frame.Idle1: headBob = -1; break;
                case Frame.Walk0: frontFoot = 2; backFoot = -2; break;
                case Frame.Walk1: bob = 1; break;
                case Frame.Walk2: frontFoot = -2; backFoot = 2; break;
                case Frame.Walk3: bob = 1; break;
                case Frame.Air: footLift = 2; tailUp = 1f; break;
            }
            float tailSway = frame >= Frame.Walk0 && frame <= Frame.Walk3 ? ((int)frame % 2 == 0 ? 1f : -1f) : 0f;

            // Capas de atrás a delante
            // Cola: curva desde el trasero hacia la izquierda y arriba
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                float x = 9.5f - 8.5f * t;
                float y = 4f + bob + (5f + 4f * tailUp) * t * t + tailSway * Mathf.Sin(t * 3f);
                Dot(mat, shade, x, y, t < 0.45f ? 1.0f : 0.6f, Pink);
            }
            // Pata y pie de atrás
            Rect(mat, shade, 10 + backFoot / 2, 2 + footLift, 2, 3, Fur, -1);
            Ellipse(mat, shade, 11.5f + backFoot, 1.6f + footLift, 2.6f, 1.2f, Pink);
            // Cuerpo y barriga
            Ellipse(mat, shade, 14f, 9.5f + bob, 5.6f, 7f, Fur);
            Ellipse(mat, shade, 16.2f, 8.5f + bob, 3f, 4.8f, Belly);
            // Pata y pie de delante
            Rect(mat, shade, 15 + frontFoot / 2, 2 + footLift, 2, 3, Fur, 0);
            Ellipse(mat, shade, 16.5f + frontFoot, 1.6f + footLift, 2.6f, 1.2f, Pink);
            // Brazo (sujeta las armas a la altura del pecho)
            Ellipse(mat, shade, 19f, (air ? 13f : 10.5f) + bob, 2.2f, 1.5f, Fur);
            Dot(mat, shade, 21.2f, (air ? 13f : 10.5f) + bob, 1.1f, Pink);
            // Oreja (detrás de la cabeza)
            float hy = 18.5f + bob + headBob;
            Ellipse(mat, shade, 12.5f, hy + 4.2f, 3.1f, 3.1f, Fur);
            Ellipse(mat, shade, 12.6f, hy + 4.1f, 1.7f, 1.7f, Pink);
            // Cabeza y hocico
            Ellipse(mat, shade, 17f, hy, 5.6f, 4.6f, Fur);
            Ellipse(mat, shade, 21.8f, hy - 1f, 3.4f, 2.5f, Fur);
            Ellipse(mat, shade, 22.3f, hy - 2.2f, 2.2f, 1.1f, Belly);   // mentón
            Dot(mat, shade, 25f, hy - 0.6f, 1.2f, Pink);                // nariz
            // Casco (cúpula del color del equipo + borde + correa)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f - 16.6f) / 6.2f, dy = (y + 0.5f - (hy + 2.2f)) / 3.8f;
                    if (dy >= 0f && dx * dx + dy * dy <= 1f) { Set(mat, shade, x, y, Helmet, (sbyte)(dx < -0.2f && dy > 0.45f ? 1 : dx > 0.45f ? -1 : 0)); }
                }
            for (int x = 10; x <= 23; x++) Set(mat, shade, x, Mathf.RoundToInt(hy + 2.2f), Helmet, -1);   // ala del casco
            // Ojo
            int ey = Mathf.RoundToInt(hy - 0.2f);
            if (blink) { Set(mat, shade, 19, ey, Eye, 0); Set(mat, shade, 20, ey, Eye, 0); }
            else
            {
                Set(mat, shade, 19, ey, Eye, 0); Set(mat, shade, 20, ey, Eye, 0);
                Set(mat, shade, 19, ey + 1, Eye, 0); Set(mat, shade, 20, ey + 1, Glint, 0);
            }
            // Bigotes
            for (int k = 0; k < 4; k++)
            {
                Set(mat, shade, 24 + k, Mathf.RoundToInt(hy - 0.5f + k * 0.35f), Whisker, 0, onlyEmpty: true);
                Set(mat, shade, 24 + k, Mathf.RoundToInt(hy - 1.6f - k * 0.3f), Whisker, 0, onlyEmpty: true);
            }

            // --- A color, con contorno ---
            Color32 fur = new Color32(140, 140, 152, 255), furL = new Color32(178, 178, 190, 255), furD = new Color32(100, 100, 116, 255);
            Color32 belly = new Color32(218, 208, 206, 255), bellyD = new Color32(186, 174, 176, 255);
            Color32 pink = new Color32(236, 150, 166, 255), pinkD = new Color32(198, 108, 128, 255);
            Color32 eye = new Color32(22, 18, 26, 255), glint = new Color32(250, 250, 255, 255);
            Color32 whisker = new Color32(210, 210, 220, 200);
            Color32 outline = new Color32(34, 28, 40, 255);
            Color32 hL = Lighten(team, 0.25f), hD = Darken(team, 0.3f), strap = new Color32(60, 46, 34, 255);

            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                sbyte s = shade[i];
                px[i] = mat[i] switch
                {
                    Fur => s > 0 ? furL : s < 0 ? furD : fur,
                    Belly => s < 0 ? bellyD : belly,
                    Pink => s < 0 ? pinkD : pink,
                    Eye => eye,
                    Glint => glint,
                    Helmet => s > 0 ? hL : s < 0 ? hD : team,
                    Whisker => whisker,
                    Strap => strap,
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

        private static void Set(byte[] mat, sbyte[] shade, int x, int y, byte m, sbyte s, bool onlyEmpty = false)
        {
            if (x < 0 || y < 0 || x >= S || y >= S) return;
            int i = y * S + x;
            if (onlyEmpty && mat[i] != Empty) return;
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
                    float light = -nx * 0.6f + ny * 0.8f;          // luz desde arriba-izquierda
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
