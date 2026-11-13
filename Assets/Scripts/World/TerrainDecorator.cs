using System.Collections.Generic;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  DECORADOR DEL TERRENO — pinta detalles sobre el mapa ya generado para
    ///  que tenga más vida y "sentido": que se lea como un sitio, no como una
    ///  masa marrón.
    ///
    ///  Dos tipos de detalle:
    ///   · DECORACIÓN de superficie (hierbajos, flores, setas, piedrecitas,
    ///     latas, raíces y goteos colgando de los techos de las cuevas).
    ///     Se pinta con alfa <see cref="DestructibleTerrain.DecorAlpha"/>:
    ///     se ve, pero la física la ignora (la rata la atraviesa) y las
    ///     explosiones la borran.
    ///   · OBJETOS ENTERRADOS (huesos, quesos, raíces, tuberías, ladrillos).
    ///     Son terreno SÓLIDO normal: solo cambian de color los píxeles de
    ///     tierra que ya había, así que no añaden ni quitan colisión.
    ///
    ///  Escala: la rata mide ~8 px de terreno de alto (PPU 32), así que todo
    ///  es pequeñito y pixel-art.
    ///
    ///  DETERMINISTA: usa solo la semilla del mapa → todos los jugadores ven
    ///  exactamente las mismas flores en el mismo sitio.
    /// </summary>
    public static class TerrainDecorator
    {
        public struct Settings
        {
            public BiomeType biome;
            public Color32 grass, grassOutline, dirtDark;
            public int grassBandPx;
            public float density;          // multiplicador global (1 = normal)
        }

        private static readonly Color32 Air = new Color32(0, 0, 0, 0);

        public static void Decorate(Color32[] px, int w, int h, int[] heightLine, int seed, Settings s)
        {
            var rng = new System.Random(seed ^ 0x5EED);
            float d = Mathf.Max(0f, s.density);
            if (d <= 0f) return;

            PaintBuried(px, w, h, heightLine, rng, s, d);
            PaintSurfaces(px, w, h, rng, s, d);
        }

        // ============================================================
        //  SUPERFICIES (decoración sin física)
        // ============================================================
        private static void PaintSurfaces(Color32[] px, int w, int h, System.Random rng, Settings s, float d)
        {
            bool sewer = s.biome == BiomeType.Sewer;

            // Recogemos primero las superficies (si pintásemos sobre la marcha,
            // una flor recién pintada parecería "suelo" para la columna de al lado).
            var floors = new List<Vector2Int>(w * 2);
            var ceilings = new List<Vector2Int>(w);
            for (int x = 1; x < w - 1; x++)
            {
                for (int y = h - 3; y >= 2; y--)
                {
                    bool here = Solid(px, w, x, y);
                    if (here && !Solid(px, w, x, y + 1)) floors.Add(new Vector2Int(x, y));
                    else if (here && !Solid(px, w, x, y - 1) && !Solid(px, w, x, y - 2)) ceilings.Add(new Vector2Int(x, y));
                }
            }

            foreach (var f in floors)
            {
                int x = f.x, y = f.y;
                // Solo en suelo razonablemente plano (no en paredes)
                bool flat = (Solid(px, w, x - 1, y) || Solid(px, w, x - 1, y - 1)) &&
                            (Solid(px, w, x + 1, y) || Solid(px, w, x + 1, y - 1)) &&
                            !Solid(px, w, x - 1, y + 2) && !Solid(px, w, x + 1, y + 2);
                if (!flat) continue;
                bool openSky = OpenToSky(px, w, h, x, y, 60);
                double r = rng.NextDouble();

                if (!sewer)
                {
                    if (openSky)
                    {
                        if (r < 0.010 * d) Mushroom(px, w, h, x, y, rng, new Color32(200, 40, 40, 255));
                        else if (r < 0.035 * d) Flower(px, w, h, x, y, rng);
                        else if (r < 0.045 * d) Pebble(px, w, h, x, y, rng, new Color32(125, 122, 118, 255));
                        else if (r < 0.62 * d) GrassBlade(px, w, h, x, y, rng, s);
                    }
                    else
                    {
                        if (r < 0.035 * d) Mushroom(px, w, h, x, y, rng, rng.NextDouble() < 0.5
                                                       ? new Color32(150, 90, 60, 255) : new Color32(140, 90, 170, 255));
                        else if (r < 0.06 * d) Pebble(px, w, h, x, y, rng, new Color32(105, 100, 98, 255));
                    }
                }
                else
                {
                    if (r < 0.010 * d) Can(px, w, h, x, y, rng);
                    else if (r < 0.018 * d) Bottle(px, w, h, x, y, rng);
                    else if (r < 0.04 * d) Puddle(px, w, h, x, y, rng);
                    else if (openSky && r < 0.35 * d) Moss(px, w, h, x, y, rng);
                }
            }

            foreach (var c in ceilings)
            {
                double r = rng.NextDouble();
                if (!sewer && r < 0.06 * d) Hanging(px, w, h, c.x, c.y, rng, new Color32(95, 65, 40, 255), 3, 10);
                else if (sewer && r < 0.07 * d) Hanging(px, w, h, c.x, c.y, rng, new Color32(110, 190, 60, 255), 2, 7);
            }
        }

        private static void GrassBlade(Color32[] px, int w, int h, int x, int y, System.Random rng, Settings s)
        {
            // Briznas cortas casi siempre, alguna más larga.
            int len = 1 + (int)(rng.NextDouble() * rng.NextDouble() * 5.5);
            float tone = (float)rng.NextDouble();
            Color32 c = Lerp(Darken(s.grass, 0.15f), s.grassOutline, tone * 0.7f);
            for (int i = 1; i <= len; i++) Decor(px, w, h, x, y + i, i == len ? Lerp(c, s.grassOutline, 0.4f) : c);
        }

        private static void Flower(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            Color32[] petals =
            {
                new Color32(230, 70, 70, 255), new Color32(250, 215, 70, 255),
                new Color32(245, 245, 245, 255), new Color32(170, 110, 225, 255), new Color32(90, 160, 240, 255),
            };
            var petal = petals[rng.Next(petals.Length)];
            int stem = 3 + rng.Next(3);
            var stemC = new Color32(45, 110, 45, 255);
            for (int i = 1; i <= stem; i++) Decor(px, w, h, x, y + i, stemC);
            int top = y + stem + 1;
            Decor(px, w, h, x, top, new Color32(250, 220, 90, 255));   // centro
            Decor(px, w, h, x - 1, top, petal);
            Decor(px, w, h, x + 1, top, petal);
            Decor(px, w, h, x, top + 1, petal);
            Decor(px, w, h, x, top - 1, petal);
        }

        private static void Mushroom(Color32[] px, int w, int h, int x, int y, System.Random rng, Color32 cap)
        {
            var stemC = new Color32(232, 220, 196, 255);
            int stem = 2 + rng.Next(2);
            for (int i = 1; i <= stem; i++) Decor(px, w, h, x, y + i, stemC);
            int cy = y + stem + 1;
            for (int dx = -2; dx <= 2; dx++) Decor(px, w, h, x + dx, cy, dx == -2 || dx == 2 ? Darken(cap, 0.2f) : cap);
            for (int dx = -1; dx <= 1; dx++) Decor(px, w, h, x + dx, cy + 1, cap);
            Decor(px, w, h, x - 1, cy, new Color32(245, 240, 235, 255));   // motitas
            Decor(px, w, h, x + 1, cy + 1, new Color32(245, 240, 235, 255));
        }

        private static void Pebble(Color32[] px, int w, int h, int x, int y, System.Random rng, Color32 c)
        {
            int half = 1 + rng.Next(2);
            for (int dx = -half; dx <= half; dx++) Decor(px, w, h, x + dx, y + 1, Darken(c, 0.1f));
            for (int dx = -half + 1; dx <= half - 1; dx++) Decor(px, w, h, x + dx, y + 2, Lighten(c, 0.15f));
        }

        private static void Moss(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            var c = rng.NextDouble() < 0.5 ? new Color32(70, 95, 50, 255) : new Color32(90, 115, 55, 255);
            Decor(px, w, h, x, y + 1, c);
            if (rng.NextDouble() < 0.4) Decor(px, w, h, x, y + 2, c);
        }

        private static void Can(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            var body = rng.NextDouble() < 0.5 ? new Color32(190, 45, 45, 255) : new Color32(60, 120, 190, 255);
            var rim = new Color32(200, 200, 205, 255);
            for (int dy = 1; dy <= 4; dy++)
                for (int dx = 0; dx <= 2; dx++)
                    Decor(px, w, h, x + dx - 1, y + dy, dy == 1 || dy == 4 ? rim : body);
        }

        private static void Bottle(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            var glass = new Color32(70, 150, 90, 255);
            var shine = new Color32(150, 210, 165, 255);
            for (int dy = 1; dy <= 4; dy++) { Decor(px, w, h, x, y + dy, glass); Decor(px, w, h, x + 1, y + dy, dy == 2 ? shine : glass); }
            Decor(px, w, h, x, y + 5, glass);
            Decor(px, w, h, x, y + 6, new Color32(120, 80, 50, 255));   // corcho
        }

        private static void Puddle(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            int len = 3 + rng.Next(5);
            var c = new Color32(120, 200, 70, 255);
            for (int dx = 0; dx < len; dx++)
                if (Solid(px, w, x + dx, y)) Decor(px, w, h, x + dx, y + 1, dx == 0 || dx == len - 1 ? Darken(c, 0.2f) : c);
        }

        private static void Hanging(Color32[] px, int w, int h, int x, int y, System.Random rng, Color32 c, int minLen, int maxLen)
        {
            int len = minLen + rng.Next(maxLen - minLen + 1);
            int cx = x;
            for (int i = 1; i <= len; i++)
            {
                if (i > 2 && rng.NextDouble() < 0.2) cx += rng.Next(3) - 1;   // pequeño zigzag
                Decor(px, w, h, cx, y - i, i == len ? Lighten(c, 0.15f) : c);
            }
        }

        // ============================================================
        //  ENTERRADOS (solo recolorean tierra sólida)
        // ============================================================
        private static void PaintBuried(Color32[] px, int w, int h, int[] heightLine, System.Random rng, Settings s, float d)
        {
            bool sewer = s.biome == BiomeType.Sewer;
            float area = w * (float)h;

            // Raíces que bajan desde la hierba (pradera)
            if (!sewer)
            {
                var rootC = new Color32(92, 62, 38, 255);
                for (int x = 2; x < w - 2; x++)
                {
                    if (rng.NextDouble() > 0.035 * d) continue;
                    int y = heightLine[x] - s.grassBandPx + 1;
                    int len = 6 + rng.Next(18);
                    int cx = x;
                    for (int i = 0; i < len; i++)
                    {
                        if (i > 2 && rng.NextDouble() < 0.3) cx += rng.Next(3) - 1;
                        Buried(px, w, h, cx, y - i, rootC);
                        if (i < len / 3) Buried(px, w, h, cx + 1, y - i, rootC);   // más gruesa arriba
                    }
                }
            }

            // Huesos (en los dos biomas)
            int bones = Mathf.RoundToInt(area / 70000f * d);
            for (int i = 0; i < bones; i++)
            {
                if (!RandomBuriedSpot(px, w, h, heightLine, rng, s.grassBandPx + 12, out int bx, out int by)) continue;
                Bone(px, w, h, bx, by, rng);
            }

            // Quesos (¡es un juego de ratas!) — pocos y repartidos
            int cheeses = Mathf.Max(1, Mathf.RoundToInt(area / 350000f * d));
            for (int i = 0; i < cheeses; i++)
            {
                if (!RandomBuriedSpot(px, w, h, heightLine, rng, s.grassBandPx + 20, out int cx, out int cy)) continue;
                Cheese(px, w, h, cx, cy, rng);
            }

            if (sewer)
            {
                int pipes = Mathf.RoundToInt(area / 200000f * d);
                for (int i = 0; i < pipes; i++)
                {
                    if (!RandomBuriedSpot(px, w, h, heightLine, rng, s.grassBandPx + 15, out int x, out int y)) continue;
                    Pipe(px, w, h, x, y, 40 + rng.Next(110), rng);
                }
                int walls = Mathf.RoundToInt(area / 260000f * d);
                for (int i = 0; i < walls; i++)
                {
                    if (!RandomBuriedSpot(px, w, h, heightLine, rng, s.grassBandPx + 10, out int x, out int y)) continue;
                    Bricks(px, w, h, x, y, 28 + rng.Next(40), 14 + rng.Next(18), rng);
                }
            }
        }

        private static bool RandomBuriedSpot(Color32[] px, int w, int h, int[] heightLine, System.Random rng, int minDepth,
                                             out int x, out int y)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                x = 20 + rng.Next(Mathf.Max(1, w - 40));
                int top = heightLine[x] - minDepth;
                if (top < 20) continue;
                y = 15 + rng.Next(Mathf.Max(1, top - 15));
                if (Solid(px, w, x, y)) return true;
            }
            x = y = 0;
            return false;
        }

        private static void Bone(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            var bone = new Color32(228, 222, 204, 255);
            var shade = new Color32(186, 178, 156, 255);
            int len = 9 + rng.Next(7);
            float ang = (float)(rng.NextDouble() * Mathf.PI);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            for (int i = -len / 2; i <= len / 2; i++)
            {
                int bx = x + Mathf.RoundToInt(ca * i), by = y + Mathf.RoundToInt(sa * i);
                Buried(px, w, h, bx, by, bone);
                Buried(px, w, h, bx + Mathf.RoundToInt(-sa), by + Mathf.RoundToInt(ca), shade);
            }
            // Cabezas del hueso: dos bolitas a cada extremo
            for (int e = -1; e <= 1; e += 2)
            {
                int ex = x + Mathf.RoundToInt(ca * len / 2 * e), ey = y + Mathf.RoundToInt(sa * len / 2 * e);
                int nx = Mathf.RoundToInt(-sa * 1.5f), ny = Mathf.RoundToInt(ca * 1.5f);
                Blob(px, w, h, ex + nx, ey + ny, 1, bone);
                Blob(px, w, h, ex - nx, ey - ny, 1, bone);
            }
        }

        private static void Cheese(Color32[] px, int w, int h, int x, int y, System.Random rng)
        {
            var cheese = new Color32(248, 205, 70, 255);
            var rind = new Color32(214, 158, 42, 255);
            var hole = new Color32(204, 150, 40, 255);
            int baseW = 12 + rng.Next(6), tall = 8 + rng.Next(3);
            bool flip = rng.NextDouble() < 0.5;
            for (int dy = 0; dy < tall; dy++)
            {
                int rowW = Mathf.RoundToInt(baseW * (1f - dy / (float)tall));   // cuña
                for (int dx = 0; dx < rowW; dx++)
                {
                    int cx = flip ? x - dx : x + dx;
                    bool edge = dy == 0 || dx == rowW - 1 || dx == 0;
                    Buried(px, w, h, cx, y + dy, edge ? rind : cheese);
                }
            }
            // Agujeros del queso
            for (int k = 0; k < 3; k++)
            {
                int hx = x + (flip ? -1 : 1) * (2 + rng.Next(Mathf.Max(1, baseW / 2)));
                int hy = y + 1 + rng.Next(Mathf.Max(1, tall / 2));
                Buried(px, w, h, hx, hy, hole);
                if (rng.NextDouble() < 0.5) Buried(px, w, h, hx + 1, hy, hole);
            }
        }

        private static void Pipe(Color32[] px, int w, int h, int x, int y, int len, System.Random rng)
        {
            var outline = new Color32(50, 54, 60, 255);
            var light = new Color32(172, 178, 184, 255);
            var body = new Color32(132, 137, 144, 255);
            var dark = new Color32(92, 97, 104, 255);
            var rust = new Color32(140, 82, 42, 255);
            for (int dx = 0; dx < len; dx++)
            {
                bool flange = dx % 24 < 2;
                int top = flange ? 4 : 3, bottom = flange ? -4 : -3;
                for (int dy = bottom; dy <= top; dy++)
                {
                    Color32 c = dy == top || dy == bottom ? outline : dy >= top - 1 ? light : dy <= bottom + 1 ? dark : body;
                    if (c.Equals(body) && rng.NextDouble() < 0.05) c = rust;
                    Buried(px, w, h, x + dx, y + dy, c);
                }
            }
        }

        private static void Bricks(Color32[] px, int w, int h, int x, int y, int bw, int bh, System.Random rng)
        {
            var mortar = new Color32(92, 86, 80, 255);
            for (int dy = 0; dy < bh; dy++)
            {
                int row = dy / 5;
                for (int dx = 0; dx < bw; dx++)
                {
                    bool m = dy % 5 == 0 || (dx + (row % 2) * 4) % 9 == 0;
                    Color32 c = m ? mortar : Darken(new Color32(156, 74, 56, 255), (float)(((dx / 9 + row * 7) * 37 % 10) / 50.0));
                    Buried(px, w, h, x + dx, y + dy, c);
                }
            }
        }

        private static void Blob(Color32[] px, int w, int h, int x, int y, int r, Color32 c)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r + 1) Buried(px, w, h, x + dx, y + dy, c);
        }

        // ============================================================
        //  Utilidades
        // ============================================================
        private static bool Solid(Color32[] px, int w, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w) return false;
            int i = y * w + x;
            return i < px.Length && DestructibleTerrain.IsSolidPixel(px[i]);
        }

        private static bool OpenToSky(Color32[] px, int w, int h, int x, int y, int checkPx)
        {
            for (int k = 2; k <= checkPx && y + k < h; k++)
                if (Solid(px, w, x, y + k)) return false;
            return true;
        }

        /// <summary>Píxel de decoración: solo sobre AIRE, con alfa especial (sin física).</summary>
        private static void Decor(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (px[i].a != 0) return;
            c.a = DestructibleTerrain.DecorAlpha;
            px[i] = c;
        }

        /// <summary>Recolorea un píxel de tierra SÓLIDA (no crea ni quita terreno).</summary>
        private static void Buried(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (!DestructibleTerrain.IsSolidPixel(px[i])) return;
            c.a = 255;
            px[i] = c;
        }

        private static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));
        private static Color32 Darken(Color32 c, float k) => new Color32((byte)(c.r * (1 - k)), (byte)(c.g * (1 - k)), (byte)(c.b * (1 - k)), c.a);
        private static Color32 Lighten(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255, c.r + 255 * k), (byte)Mathf.Min(255, c.g + 255 * k), (byte)Mathf.Min(255, c.b + 255 * k), c.a);
    }
}
