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
    /// <summary>
    ///  0.32 — FORMA del mapa (regla "Mapa"). Todas salen de la misma semilla
    ///  en cada PC; la elige el host y viaja en <see cref="BiomeSyncer"/>.
    /// </summary>
    public enum MapShape : byte
    {
        /// <summary>La isla de siempre: colinas, salientes, túneles, alguna isla flotante.</summary>
        Normal,
        /// <summary>Muchas islas medianas separadas por canales de agua.</summary>
        Archipielago,
        /// <summary>Un pico enorme en el centro y laderas que bajan al mar.</summary>
        Montana,
        /// <summary>Dos mesetas altas a los lados y un valle con agua en medio.</summary>
        Canon,
        /// <summary>Casi todo tierra: galerías largas, cuevas grandes y chimeneas al cielo.</summary>
        Cavernas,
        /// <summary>Solo islas flotantes sobre el mar.</summary>
        Cielo,
        // 0.40
        /// <summary>Una cuña de queso gigante llena de agujeros redondos.</summary>
        Queso,
        /// <summary>Llanura con zanjas, parapetos de sacos y búnkeres bajo tierra.</summary>
        Trincheras,
        /// <summary>Pilares altísimos que salen del mar.</summary>
        Torres,
        /// <summary>Dos acantilados unidos por un arco fino de roca sobre el abismo.</summary>
        Puente,
        /// <summary>Una mole de tierra con un laberinto de galerías dentro.</summary>
        Laberinto,
        /// <summary>Un volcán con cráter, chimenea y cámara por dentro.</summary>
        Volcan,
    }

    public static class WormsTerrainShape
    {
        public const int ShapeCount = 12;

        public struct Result
        {
            /// <summary>true = píxel sólido (tierra). Índice y·ancho + x.</summary>
            public bool[] solid;
            /// <summary>Perfil base de colinas (px) por columna: lo que queda por
            /// debajo se ve como "tierra de fondo" en las cuevas y túneles.</summary>
            public int[] backdropLine;
        }

        private const int Cell = 4;

        public static Result Build(int width, int height, int seed, MapShape shape = MapShape.Normal)
        {
            var rng = new System.Random(seed ^ 0x77A11);
            float R() => (float)rng.NextDouble();
            float Off() => R() * 10000f;

            int gw = width / Cell, gh = height / Cell;
            float[] field = new float[gw * gh];
            // Mapas GRANDES (muchas ratas): el ruido va por celdas, así que más
            // ancho = más colinas sin estirarlas. Lo que se cuenta "por mapa"
            // (bordes, canales, islas) se escala con esto para que no quede vacío.
            float scale = Mathf.Max(1f, width / 2048f);

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

            int cheeseDir = rng.Next(2) == 0 ? 1 : -1;   // 0.40: hacia dónde sube la cuña de queso
            // 0.32: cada FORMA retoca el perfil. El ruido de arriba se conserva
            // (más suave) para que ninguna forma quede de regla y cartabón.
            for (int x = 0; x < gw; x++)
            {
                float wig = prof[x] - baseH;                       // colinas del ruido (±)
                float u = x / (float)(gw - 1);                     // 0..1 de izquierda a derecha
                switch (shape)
                {
                    case MapShape.Montana:
                    {
                        float t = (u - 0.5f) / 0.2f;
                        prof[x] = gh * 0.28f + wig * 0.55f + gh * 0.46f * Mathf.Exp(-t * t);
                        break;
                    }
                    case MapShape.Canon:
                    {
                        float d = Mathf.Abs(u - 0.5f) * 2f;        // 0 en el centro, 1 en los bordes
                        float k = Mathf.Clamp01((d - 0.3f) / 0.22f);
                        k = k * k * (3f - 2f * k);
                        prof[x] = Mathf.Lerp(-gh * 0.1f, gh * 0.64f + wig * 0.5f, k);
                        break;
                    }
                    case MapShape.Cavernas:
                        prof[x] = gh * 0.70f + wig * 0.45f;
                        break;
                    case MapShape.Cielo:
                        prof[x] = -gh * 0.12f;                     // todo mar: la tierra son las islas
                        break;
                    case MapShape.Archipielago:
                        prof[x] = gh * 0.30f + wig * 0.8f;          // islas bajas
                        break;
                    case MapShape.Queso:
                    {
                        float uu = Mathf.Clamp01((cheeseDir > 0 ? u : 1f - u) * 1.15f - 0.08f);
                        prof[x] = gh * (0.2f + 0.6f * uu) + wig * 0.15f;   // cuña: sube de un lado al otro
                        break;
                    }
                    case MapShape.Trincheras:
                        prof[x] = gh * 0.42f + wig * 0.25f;          // llanura casi plana
                        break;
                    case MapShape.Torres:
                        prof[x] = -gh * 0.12f;                       // mar: las torres se ponen luego
                        break;
                    case MapShape.Puente:
                    {
                        float d = Mathf.Abs(u - 0.5f) * 2f;
                        float k = Mathf.Clamp01((d - 0.4f) / 0.08f);
                        k = k * k * (3f - 2f * k);
                        prof[x] = Mathf.Lerp(-gh * 0.12f, gh * 0.66f + wig * 0.35f, k);
                        break;
                    }
                    case MapShape.Laberinto:
                        prof[x] = gh * 0.8f + wig * 0.12f;
                        break;
                    case MapShape.Volcan:
                    {
                        float t = Mathf.Abs(u - 0.5f) / 0.3f;
                        float cone = Mathf.Pow(Mathf.Max(0f, 1f - t), 1.15f);
                        prof[x] = gh * 0.2f + wig * 0.3f + gh * 0.62f * cone;
                        if (t < 0.13f) prof[x] -= gh * 0.13f * (1f - t / 0.13f);   // cráter
                        break;
                    }
                }
                prof[x] = Mathf.Min(prof[x], gh * 0.86f);          // siempre algo de cielo para disparar
            }

            // Bordes: el terreno baja hasta el agua (isla), con curva irregular.
            float oE = Off();
            int edge = Mathf.RoundToInt(gw / scale * (0.13f + R() * 0.05f));   // la playa mide lo mismo en mapas grandes
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
            if (scale > 1.2f) gaps += 1;   // mapa grande: un canal más para que no sea un solo continente
            if (shape == MapShape.Archipielago) gaps = Mathf.RoundToInt((3 + rng.Next(2)) * scale);
            else if (shape != MapShape.Normal && shape != MapShape.Cavernas) gaps = 0;
            float gapSpacing = shape == MapShape.Archipielago ? 0.13f : 0.22f;
            var gapCenters = new List<int>();
            for (int g = 0; g < gaps; g++)
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    int cx = Mathf.RoundToInt(gw * (scale > 1.2f || shape == MapShape.Archipielago ? 0.16f + R() * 0.68f : 0.28f + R() * 0.44f));
                    bool ok = true;
                    foreach (int o in gapCenters) if (Mathf.Abs(o - cx) < gw * gapSpacing / scale) ok = false;
                    if (!ok) continue;
                    gapCenters.Add(cx);
                    float half = 6f + R() * 6f;          // 12-24 celdas = 1.5-3 u de canal
                    if (shape == MapShape.Archipielago) half += 4f;   // canales más anchos entre islas
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
            // Cavernas: túneles mucho más hondos y abundantes, y más cuevas grandes.
            bool caverns = shape == MapShape.Cavernas;
            float tunnelMax = caverns ? 190f : 75f, maskMin = caverns ? 0.36f : 0.47f, ridgeW = caverns ? 0.06f : 0.045f, caveMin = caverns ? 0.66f : 0.74f;
            // 0.40: queso, laberinto y torres tienen sus propios huecos: sin túneles al azar
            if (shape == MapShape.Queso || shape == MapShape.Laberinto || shape == MapShape.Torres) { tunnelMax = 0f; caveMin = 2f; }
            else if (shape == MapShape.Trincheras || shape == MapShape.Puente) { maskMin = 0.55f; caveMin = 0.8f; }
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
                    if (depth > 5f && depth < tunnelMax && y > 8)
                    {
                        float tn = Mathf.PerlinNoise(x * 0.012f + oTx, y * 0.02f + oTy);
                        float ridge = Mathf.Abs(tn - 0.5f);
                        float mask = Mathf.PerlinNoise(x * 0.02f + oMx, y * 0.02f + oMy);
                        if (mask > maskMin)
                        {
                            float w = ridgeW * Mathf.Clamp01((mask - maskMin) * 6f);
                            if (ridge < w) d = Mathf.Min(d, -0.6f * (1f - ridge / w) - 0.05f);
                        }
                        // Alguna cueva grande y redonda (galería), pero pocas.
                        float cave = Mathf.PerlinNoise(x * 0.03f + oPx, y * 0.045f + oPy);
                        if (cave > caveMin) d = Mathf.Min(d, -(cave - caveMin) * 10f);
                    }

                    field[y * gw + x] = d;
                }
            }

            // ---------- 2b) CHIMENEAS (Cavernas) ----------
            // Pozos verticales que unen las galerías con el cielo: por ahí se
            // entra y se sale, y las ratas pueden aparecer en las cuevas (solo
            // valen sitios conectados con el cielo).
            if (caverns)
            {
                int shafts = Mathf.RoundToInt((4 + rng.Next(3)) * scale);
                float oS = Off();
                for (int i = 0; i < shafts; i++)
                {
                    float sx = gw * (0.1f + 0.8f * (i + R() * 0.8f) / shafts);
                    int top = Mathf.RoundToInt(Mathf.Min(prof[Mathf.Clamp((int)sx, 0, gw - 1)] + 8f, gh - 1));
                    int bottom = Mathf.Max(10, top - 50 - rng.Next(70));
                    float rad = 3f + R() * 2.5f;
                    for (int y = bottom; y <= top; y++)
                    {
                        float cx = sx + (Mathf.PerlinNoise(y * 0.05f + oS + i * 13f, 0.4f) - 0.5f) * 14f;
                        for (int x = Mathf.Max(0, (int)(cx - rad - 1)); x <= Mathf.Min(gw - 1, (int)(cx + rad + 1)); x++)
                        {
                            float k = 1f - Mathf.Abs(x - cx) / rad;
                            if (k > 0f) field[y * gw + x] = Mathf.Min(field[y * gw + x], -k * 1.2f - 0.1f);
                        }
                    }
                }
            }

            // ---------- 2c) RASGOS DE LAS FORMAS 0.40 ----------
            ShapeFeatures(field, gw, gh, prof, shape, rng, scale);

            // ---------- 3) ISLAS FLOTANTES ----------
            int islands = Mathf.RoundToInt((1 + rng.Next(3)) * scale);   // más islas en mapas grandes
            if (shape == MapShape.Laberinto || shape == MapShape.Puente || shape == MapShape.Torres) islands = 0;
            else if (shape == MapShape.Queso || shape == MapShape.Trincheras) islands = Mathf.Min(islands, 1);
            if (shape == MapShape.Cielo) islands = Mathf.RoundToInt((8 + rng.Next(3)) * scale);
            else if (shape == MapShape.Canon) islands += 2;              // pasaderas sobre el valle
            else if (shape == MapShape.Cavernas) islands = Mathf.Max(0, islands - 1);
            float oIx = Off();
            var placed = new List<Vector3>();   // (x, y, radio) de las islas ya puestas
            for (int i = 0; i < islands; i++)
            {
                for (int tries = 0; tries < 16; tries++)
                {
                    bool sky = shape == MapShape.Cielo;
                    int cx = Mathf.RoundToInt(gw * (sky ? 0.06f + R() * 0.88f : shape == MapShape.Canon && i < 2 ? 0.38f + R() * 0.24f : 0.15f + R() * 0.7f));
                    float rx = sky ? 16f + R() * 18f : 12f + R() * 16f;
                    float ry = rx * (0.4f + R() * 0.2f);
                    float floor = Mathf.Max(prof[Mathf.Clamp(cx, 0, gw - 1)], sky ? gh * 0.14f : 0f) + ry + 22f;
                    float ceil = gh - ry - 22f - (sky ? gh * 0.12f : 0f);
                    if (floor >= ceil) continue;
                    float cy = floor + R() * (ceil - floor);
                    // En el cielo, que no se monten unas encima de otras.
                    bool clash = false;
                    if (sky) foreach (var o in placed) if (new Vector2(o.x - cx, (o.y - cy) * 1.6f).magnitude < o.z + rx + 6f) { clash = true; break; }
                    if (clash) continue;
                    placed.Add(new Vector3(cx, cy, rx));
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

        // ================================================================
        //  0.40 — RASGOS DE LAS FORMAS NUEVAS (sobre el campo de densidad:
        //  > 0 tierra, < 0 aire). Todo con el mismo generador de la semilla.
        // ================================================================
        private static void Carve(float[] f, int gw, int gh, float cx, float cy, float r, float depth = 2f)
        {
            for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(gh - 1, (int)(cy + r + 1)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(gw - 1, (int)(cx + r + 1)); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d < r) f[y * gw + x] = Mathf.Min(f[y * gw + x], -(1f - d / r) * depth - 0.15f);
                }
        }

        private static void FillRect(float[] f, int gw, int gh, int x0, int y0, int x1, int y1, float v = 2f)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(gh - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(gw - 1, x1); x++)
                    f[y * gw + x] = Mathf.Max(f[y * gw + x], v);
        }

        private static void CarveRect(float[] f, int gw, int gh, int x0, int y0, int x1, int y1)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(gh - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(gw - 1, x1); x++)
                    f[y * gw + x] = Mathf.Min(f[y * gw + x], -1.2f);
        }

        private static void ShapeFeatures(float[] f, int gw, int gh, float[] prof, MapShape shape, System.Random rng, float scale)
        {
            float R() => (float)rng.NextDouble();
            switch (shape)
            {
                case MapShape.Queso:
                {
                    // Agujeros redondos de todos los tamaños (los de arriba se abren al cielo)
                    int holes = Mathf.RoundToInt(55 * scale);
                    for (int i = 0; i < holes; i++)
                    {
                        int cx = rng.Next(gw);
                        float top = prof[cx];
                        if (top < 14f) continue;
                        float r = 3f + R() * R() * 13f;
                        float cy = 6f + R() * (top - 4f);
                        Carve(f, gw, gh, cx, cy, r);
                    }
                    break;
                }
                case MapShape.Trincheras:
                {
                    // Zanjas con parapeto de sacos a los lados y búnkeres debajo
                    int n = Mathf.RoundToInt((4 + rng.Next(2)) * scale);
                    for (int i = 0; i < n; i++)
                    {
                        int x0 = (int)(gw * (0.12f + 0.76f * (i + R() * 0.5f) / n));
                        int len = 22 + rng.Next(22);
                        for (int x = x0; x < Mathf.Min(gw - 1, x0 + len); x++)
                            CarveRect(f, gw, gh, x, (int)prof[x] - 7, x, (int)prof[x] + 6);
                        int yl = (int)prof[Mathf.Clamp(x0 - 1, 0, gw - 1)], yr = (int)prof[Mathf.Clamp(x0 + len, 0, gw - 1)];
                        FillRect(f, gw, gh, x0 - 5, yl - 1, x0 - 1, yl + 3);           // sacos terreros
                        FillRect(f, gw, gh, x0 + len, yr - 1, x0 + len + 4, yr + 3);
                        if (rng.Next(2) == 0)
                        {
                            // Búnker: sala bajo la zanja, con bajada
                            int bx = x0 + len / 2, by = (int)prof[Mathf.Clamp(bx, 0, gw - 1)] - 20;
                            CarveRect(f, gw, gh, bx - 9, by, bx + 9, by + 6);
                            CarveRect(f, gw, gh, bx - 2, by + 6, bx + 2, (int)prof[Mathf.Clamp(bx, 0, gw - 1)] - 6);
                        }
                    }
                    break;
                }
                case MapShape.Torres:
                {
                    int n = Mathf.RoundToInt((6 + rng.Next(4)) * scale);
                    float oN = R() * 1000f;
                    for (int i = 0; i < n; i++)
                    {
                        float u = 0.07f + 0.86f * (i + 0.25f + R() * 0.5f) / n;
                        int cx = (int)(gw * u), half = 5 + rng.Next(4);
                        int top = (int)(gh * (0.35f + R() * 0.42f));
                        for (int y = 0; y <= top; y++)
                        {
                            // bordes irregulares y un poco más ancha abajo
                            float wob = (Mathf.PerlinNoise(y * 0.15f + oN + i * 9f, 0.5f) - 0.5f) * 4f;
                            int hw = half + (int)wob + (int)((top - y) * 0.03f);
                            FillRect(f, gw, gh, cx - hw, y, cx + hw, y);
                        }
                        // Repisa a un lado (para trepar)
                        if (rng.Next(2) == 0)
                        {
                            int ly = (int)(top * (0.4f + R() * 0.3f)), dir = rng.Next(2) == 0 ? -1 : 1;
                            FillRect(f, gw, gh, Mathf.Min(cx, cx + dir * (half + 8)), ly, Mathf.Max(cx, cx + dir * (half + 8)), ly + 2);
                        }
                    }
                    break;
                }
                case MapShape.Puente:
                {
                    // Arco fino de roca de acantilado a acantilado (se puede volar…)
                    // Los extremos se meten en la roca de los acantilados; la altura sale
                    // de la cima de las mesetas (no del borde, que ya baja al abismo).
                    int xl = (int)(gw * 0.25f), xr = (int)(gw * 0.75f), cx = gw / 2;
                    float half = (xr - xl) * 0.5f;
                    float baseY = Mathf.Min(prof[(int)(gw * 0.18f)], prof[(int)(gw * 0.82f)]) - gh * 0.1f;
                    for (int x = xl - 4; x <= xr + 4; x++)
                    {
                        float k = (x - cx) / half;
                        float yb = baseY + gh * 0.05f * (1f - k * k);
                        FillRect(f, gw, gh, x, (int)(yb - 4f - Mathf.Abs(k) * 3f), x, (int)yb);
                    }
                    break;
                }
                case MapShape.Laberinto:
                {
                    // Laberinto "perfecto" (búsqueda en profundidad) bajo la superficie
                    const int C = 15, W = 4;   // celda y medio pasillo (en celdas del campo)
                    int x0 = (int)(gw * 0.1f), x1 = (int)(gw * 0.9f);
                    int y0 = (int)(gh * 0.1f), y1 = (int)(gh * 0.66f);
                    int cols = Mathf.Max(2, (x1 - x0) / C), rows = Mathf.Max(2, (y1 - y0) / C);
                    var seen = new bool[cols * rows];
                    var stack = new Stack<int>();
                    int start = rng.Next(cols * rows);
                    stack.Push(start); seen[start] = true;
                    Vector2Int Center(int id) => new Vector2Int(x0 + (id % cols) * C + C / 2, y0 + (id / cols) * C + C / 2);
                    var c0 = Center(start);
                    CarveRect(f, gw, gh, c0.x - W, c0.y - W, c0.x + W, c0.y + W);
                    var nbr = new List<int>();
                    while (stack.Count > 0)
                    {
                        int cur = stack.Peek();
                        int cx = cur % cols, cy = cur / cols;
                        nbr.Clear();
                        if (cx > 0 && !seen[cur - 1]) nbr.Add(cur - 1);
                        if (cx < cols - 1 && !seen[cur + 1]) nbr.Add(cur + 1);
                        if (cy > 0 && !seen[cur - cols]) nbr.Add(cur - cols);
                        if (cy < rows - 1 && !seen[cur + cols]) nbr.Add(cur + cols);
                        if (nbr.Count == 0) { stack.Pop(); continue; }
                        int nx = nbr[rng.Next(nbr.Count)];
                        seen[nx] = true;
                        var a = Center(cur); var b = Center(nx);
                        CarveRect(f, gw, gh, Mathf.Min(a.x, b.x) - W, Mathf.Min(a.y, b.y) - W, Mathf.Max(a.x, b.x) + W, Mathf.Max(a.y, b.y) + W);
                        stack.Push(nx);
                    }
                    // Pozos desde arriba para entrar (y para que se pueda aparecer dentro)
                    int shafts = Mathf.RoundToInt((3 + rng.Next(3)) * scale);
                    for (int i = 0; i < shafts; i++)
                    {
                        int col = (int)((i + 0.5f) * cols / shafts);
                        var c = Center((rows - 1) * cols + Mathf.Clamp(col, 0, cols - 1));
                        CarveRect(f, gw, gh, c.x - W, c.y, c.x + W, (int)prof[Mathf.Clamp(c.x, 0, gw - 1)] + 4);
                    }
                    break;
                }
                case MapShape.Volcan:
                {
                    // Chimenea desde el cráter hasta una cámara, y tubos de lava a las laderas
                    int cx = gw / 2;
                    float craterY = prof[cx];
                    float chamberY = gh * 0.38f;
                    for (float y = chamberY; y <= craterY + 2f; y += 1f)
                        Carve(f, gw, gh, cx + Mathf.Sin(y * 0.15f) * 2f, y, 3.2f);   // chimenea estrecha
                    Carve(f, gw, gh, cx, chamberY, 12f + R() * 3f);
                    int tubes = 2 + rng.Next(2);
                    for (int i = 0; i < tubes; i++)
                    {
                        int dir = i % 2 == 0 ? -1 : 1;
                        float ex = cx + dir * gw * (0.13f + R() * 0.08f);
                        float ey = prof[Mathf.Clamp((int)ex, 0, gw - 1)] - 2f;
                        for (float t = 0f; t <= 1f; t += 0.01f)
                            Carve(f, gw, gh, Mathf.Lerp(cx, ex, t), Mathf.Lerp(chamberY, ey, t * t) + Mathf.Sin(t * 9f) * 3f, 3.6f);
                    }
                    break;
                }
            }
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
