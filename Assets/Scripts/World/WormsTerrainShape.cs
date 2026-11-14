using System.Collections.Generic;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  FORMA del mapa al estilo Worms (0.18.1). Solo decide qué píxel es tierra
    ///  y cuál aire; los colores los pone <see cref="TerrainGenerator"/>.
    ///
    ///  El generador antiguo era un "perfil de alturas + agujeros": salía siempre
    ///  una cúpula con una colina suave y bolsas de cueva cerradas (las ratas
    ///  aparecían dentro, sin salida). En Worms el terreno es una ISLA con
    ///  personalidad: picos y valles, salientes que hacen de techo, arcos,
    ///  islotes flotando, canales de agua que parten el mapa y túneles largos
    ///  que salen a la superficie. Para conseguirlo:
    ///
    ///   1. Trabajamos en una REJILLA gruesa (1 celda = 4 px): todo es 16 veces
    ///      más barato y las formas salen redondeadas, sin dientes de sierra.
    ///   2. Un CAMPO DE DENSIDAD por celda: positivo = tierra. Mezcla el perfil
    ///      de colinas con ruido 2D cerca de la superficie (así aparecen
    ///      salientes y arcos, que un simple perfil de alturas no puede hacer).
    ///   3. Túneles con ruido "ridged" (|ruido − 0.5| pequeño = línea): salen
    ///      pasillos largos y conectados, no bolsas.
    ///   4. Islas flotantes, canales de agua y bordes que bajan al mar.
    ///   5. Suavizado + limpieza (se quitan migas sueltas y huecos minúsculos).
    ///   6. Se amplía a píxeles interpolando el campo (bordes curvos y finos).
    ///
    ///  Todo depende SOLO de la semilla (System.Random + Mathf.PerlinNoise), así
    ///  que cada PC genera exactamente el mismo mapa.
    /// </summary>
    public static class WormsTerrainShape
    {
        public struct Result
        {
            /// <summary>true = píxel sólido (tierra). Índice y·ancho + x.</summary>
            public bool[] solid;
            /// <summary>Perfil base de colinas (px) por columna: lo que queda por
            /// debajo se ve como "tierra de fondo" en las cuevas y túneles.</summary>
            public int[] backdropLine;
        }

        private const int Cell = 4;

        public static Result Build(int width, int height, int seed)
        {
            var rng = new System.Random(seed ^ 0x77A11);
            float R() => (float)rng.NextDouble();
            float Off() => R() * 10000f;

            int gw = width / Cell, gh = height / Cell;
            float[] field = new float[gw * gh];

            // ---------- 1) PERFIL DE COLINAS ----------
            // Tres octavas: montes grandes, lomas y rugosidad. La base va a
            // ~45-55 % del alto para dejar cielo para disparos bombeados.
            float oA = Off(), oB = Off(), oC = Off();
            float baseH = gh * (0.40f + R() * 0.10f);
            float bigAmp = gh * (0.13f + R() * 0.07f);   // a veces mapas más montañosos
            float[] prof = new float[gw];
            for (int x = 0; x < gw; x++)
            {
                float a = (Mathf.PerlinNoise(x * 0.0085f + oA, 0.31f) - 0.5f) * 2f;
                float b = (Mathf.PerlinNoise(x * 0.028f + oB, 0.77f) - 0.5f) * 2f;
                float c = (Mathf.PerlinNoise(x * 0.09f + oC, 0.53f) - 0.5f) * 2f;
                prof[x] = baseH + a * bigAmp + b * gh * 0.045f + c * gh * 0.012f;
            }

            // Bordes: el terreno baja hasta el agua (isla), con curva irregular.
            float oE = Off();
            int edge = Mathf.RoundToInt(gw * (0.13f + R() * 0.05f));
            for (int x = 0; x < gw; x++)
            {
                int d = Mathf.Min(x, gw - 1 - x);
                if (d >= edge) continue;
                float t = 1f - d / (float)edge;
                t = t * t * (0.8f + Mathf.PerlinNoise(x * 0.05f + oE, 0.2f) * 0.4f);
                prof[x] = Mathf.Lerp(prof[x], -gh * 0.08f, Mathf.Clamp01(t));
            }

            // CANALES: 0-2 cortes que bajan hasta el agua y parten la isla.
            // Así hay que usar cuerda, teletransporte o un buen salto, como en Worms.
            int gaps = R() < 0.3f ? 0 : (R() < 0.65f ? 1 : 2);
            var gapCenters = new List<int>();
            for (int g = 0; g < gaps; g++)
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    int cx = Mathf.RoundToInt(gw * (0.28f + R() * 0.44f));
                    bool ok = true;
                    foreach (int o in gapCenters) if (Mathf.Abs(o - cx) < gw * 0.22f) ok = false;
                    if (!ok) continue;
                    gapCenters.Add(cx);
                    float half = 6f + R() * 6f;          // 12-24 celdas = 1.5-3 u de canal
                    float shoulder = 18f + R() * 14f;    // bajada suave a los lados
                    for (int x = 0; x < gw; x++)
                    {
                        float d = Mathf.Abs(x - cx);
                        if (d > half + shoulder) continue;
                        float k = d <= half ? 1f : 1f - (d - half) / shoulder;
                        k = k * k * (3f - 2f * k);       // smoothstep
                        prof[x] = Mathf.Lerp(prof[x], -gh * 0.1f, k);
                    }
                    break;
                }
            }

            // Suavizado del perfil (2 pasadas de media móvil).
            for (int pass = 0; pass < 2; pass++)
            {
                float[] tmp = new float[gw];
                for (int x = 0; x < gw; x++)
                {
                    float s = 0; int n = 0;
                    for (int i = -2; i <= 2; i++) { int xi = Mathf.Clamp(x + i, 0, gw - 1); s += prof[xi]; n++; }
                    tmp[x] = s / n;
                }
                prof = tmp;
            }

            // ---------- 2) CAMPO DE DENSIDAD ----------
            float oOx = Off(), oOy = Off(), oO2x = Off(), oO2y = Off();
            float oTx = Off(), oTy = Off(), oMx = Off(), oMy = Off();
            float oPx = Off(), oPy = Off();
            float overhang = 0.9f + R() * 0.5f;              // cuánto "salen" los salientes
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++)
                {
                    float depth = prof[x] - y;               // >0 = bajo el perfil
                    float d = depth / 14f;

                    // Ruido 2D que deforma la superficie: salientes, arcos, bahías.
                    // Solo pesa cerca de la superficie (en lo hondo manda la tierra).
                    float n1 = (Mathf.PerlinNoise(x * 0.045f + oOx, y * 0.06f + oOy) - 0.5f) * 2f;
                    float n2 = (Mathf.PerlinNoise(x * 0.11f + oO2x, y * 0.13f + oO2y) - 0.5f) * 2f;
                    float near = Mathf.Clamp01(1f - Mathf.Abs(depth) / 30f);
                    d += (n1 * overhang + n2 * 0.35f) * (0.35f + 0.65f * near);

                    // TÚNELES: líneas del ruido ridged, entre 5 y 70 celdas de
                    // hondo, y solo donde una segunda máscara lo permite (no
                    // queremos un queso gruyer).
                    if (depth > 5f && depth < 75f)
                    {
                        float tn = Mathf.PerlinNoise(x * 0.012f + oTx, y * 0.02f + oTy);
                        float ridge = Mathf.Abs(tn - 0.5f);
                        float mask = Mathf.PerlinNoise(x * 0.02f + oMx, y * 0.02f + oMy);
                        if (mask > 0.47f)
                        {
                            float w = 0.045f * Mathf.Clamp01((mask - 0.47f) * 6f);
                            if (ridge < w) d = Mathf.Min(d, -0.6f * (1f - ridge / w) - 0.05f);
                        }
                        // Alguna cueva grande y redonda (galería), pero pocas.
                        float cave = Mathf.PerlinNoise(x * 0.03f + oPx, y * 0.045f + oPy);
                        if (cave > 0.74f) d = Mathf.Min(d, -(cave - 0.74f) * 10f);
                    }

                    field[y * gw + x] = d;
                }
            }

            // ---------- 3) ISLAS FLOTANTES ----------
            int islands = 1 + rng.Next(3);
            float oIx = Off();
            for (int i = 0; i < islands; i++)
            {
                for (int tries = 0; tries < 10; tries++)
                {
                    int cx = Mathf.RoundToInt(gw * (0.15f + R() * 0.7f));
                    float rx = 12f + R() * 16f;
                    float ry = rx * (0.4f + R() * 0.2f);
                    float floor = Mathf.Max(prof[Mathf.Clamp(cx, 0, gw - 1)], 0f) + ry + 22f;
                    float ceil = gh - ry - 22f;
                    if (floor >= ceil) continue;
                    float cy = floor + R() * (ceil - floor);
                    for (int y = Mathf.Max(0, (int)(cy - ry - 4)); y < Mathf.Min(gh, (int)(cy + ry + 4)); y++)
                        for (int x = Mathf.Max(0, (int)(cx - rx - 4)); x < Mathf.Min(gw, (int)(cx + rx + 4)); x++)
                        {
                            float dx = (x - cx) / rx, dy = (y - cy) / ry;
                            // Más plana por arriba (se puede pisar) y en punta por abajo.
                            if (dy < 0) dy *= 0.75f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            float wob = (Mathf.PerlinNoise(x * 0.12f + oIx + i * 37f, y * 0.12f) - 0.5f) * 0.5f;
                            float v = (1f - r + wob) * 3f;
                            int idx = y * gw + x;
                            if (v > field[idx]) field[idx] = v;
                        }
                    break;
                }
            }

            // ---------- 4) SUAVIZADO Y LIMPIEZA ----------
            for (int pass = 0; pass < 2; pass++) field = Blur(field, gw, gh);
            RemoveSmallBlobs(field, gw, gh, solid: true, minCells: 90);    // migas flotando
            RemoveSmallBlobs(field, gw, gh, solid: false, minCells: 50);   // agujeritos cerrados

            // ---------- 5) A PÍXELES ----------
            var res = new Result { solid = new bool[width * height], backdropLine = new int[width] };
            float oFx = Off(), oFy = Off();
            for (int y = 0; y < height; y++)
            {
                float gy = (y + 0.5f) / Cell - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, gh - 1), y1 = Mathf.Min(y0 + 1, gh - 1);
                float ty = Mathf.Clamp01(gy - y0);
                for (int x = 0; x < width; x++)
                {
                    float gx = (x + 0.5f) / Cell - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, gw - 1), x1 = Mathf.Min(x0 + 1, gw - 1);
                    float tx = Mathf.Clamp01(gx - x0);
                    float a = Mathf.Lerp(field[y0 * gw + x0], field[y0 * gw + x1], tx);
                    float b = Mathf.Lerp(field[y1 * gw + x0], field[y1 * gw + x1], tx);
                    float v = Mathf.Lerp(a, b, ty);
                    // Pizca de ruido fino en el borde: orgánico, no "vectorial".
                    v += (Mathf.PerlinNoise(x * 0.09f + oFx, y * 0.09f + oFy) - 0.5f) * 0.12f;
                    res.solid[y * width + x] = v > 0f;
                }
            }
            // LECHO MARINO: las 18 filas de abajo (bajo el agua, que está ~32 px
            // por encima del fondo) son siempre tierra. Sin esto, lo que caía a
            // un canal (un cadáver, una caja) seguía cayendo para siempre.
            for (int y = 0; y < 18 && y < height; y++)
                for (int x = 0; x < width; x++)
                    res.solid[y * width + x] = true;
            // Una franja de 2 px en los laterales siempre vacía: el mapa no
            // toca el borde de la textura (si no, el contorno quedaría abierto).
            for (int y = 0; y < height; y++)
                for (int x = 0; x < 2; x++)
                {
                    res.solid[y * width + x] = false;
                    res.solid[y * width + width - 1 - x] = false;
                }
            for (int x = 0; x < width; x++)
            {
                float gx = Mathf.Clamp(x / (float)Cell, 0, gw - 1);
                res.backdropLine[x] = Mathf.RoundToInt(prof[Mathf.FloorToInt(gx)] * Cell) - 44;   // margen: bajo un saliente se ve cielo
            }
            return res;
        }

        private static float[] Blur(float[] f, int w, int h)
        {
            var o = new float[f.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= w) continue;
                            s += Mathf.Clamp(f[yy * w + xx], -2f, 2f); n++;
                        }
                    }
                    o[y * w + x] = s / n;
                }
            return o;
        }

        /// <summary>
        ///  Quita las manchas pequeñas: islotes de tierra de menos de
        ///  <paramref name="minCells"/> celdas (solid=true) o burbujas de aire
        ///  cerradas igual de pequeñas (solid=false). El aire que toca el borde
        ///  del mapa nunca se rellena (es cielo o mar).
        /// </summary>
        private static void RemoveSmallBlobs(float[] f, int w, int h, bool solid, int minCells)
        {
            var seen = new bool[f.Length];
            var stack = new Stack<int>();
            var comp = new List<int>();
            for (int i = 0; i < f.Length; i++)
            {
                if (seen[i] || (f[i] > 0f) != solid) continue;
                comp.Clear();
                bool touchesBorder = false;
                stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    comp.Add(c);
                    int cx = c % w, cy = c / w;
                    if (cx == 0 || cy == 0 || cx == w - 1 || cy == h - 1) touchesBorder = true;
                    if (cx > 0) Visit(c - 1);
                    if (cx < w - 1) Visit(c + 1);
                    if (cy > 0) Visit(c - w);
                    if (cy < h - 1) Visit(c + w);
                }
                if (comp.Count >= minCells || (!solid && touchesBorder)) continue;
                foreach (int c in comp) f[c] = solid ? -0.5f : 0.5f;
            }

            void Visit(int n)
            {
                if (seen[n] || (f[n] > 0f) != solid) return;
                seen[n] = true;
                stack.Push(n);
            }
        }
    }
}
