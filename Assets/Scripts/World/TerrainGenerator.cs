using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  GENERADOR PROCEDURAL de terreno estilo Worms — versión 0.8, Sesión 2
    ///  (Tanda 1: Perlin + colinas + cuevas + hierba con outline + grano).
    ///
    ///  Qué hace, en plata:
    ///   1) Para CADA columna x, calcula una altura del suelo sumando varias
    ///      octavas de ruido Perlin 1D (una gorda = colinas, una media =
    ///      ondulación, una fina = rugosidad). Resultado: un perfil ondulado
    ///      orgánico, nada matemático (adiós senos).
    ///   2) Para CADA píxel, decide si es AIRE (por encima del suelo) o SÓLIDO
    ///      (por debajo).
    ///   3) Los píxeles sólidos se pintan así:
    ///        · Top 2–3 px → color de outline (el borde amarillo chillón típico
    ///          de Worms que hace que la silueta del mapa se lea de lejos).
    ///        · Siguientes N px → hierba.
    ///        · Resto → tierra con GRANO (mezcla dos tonos con otro Perlin 2D
    ///          + speckle por píxel → no se ve una mancha marrón plana).
    ///   4) Cuevas orgánicas con Perlin 2D: si el ruido en (x,y) supera un
    ///      umbral, ese píxel pasa a aire. Solo se carvean cuevas a cierta
    ///      profundidad para no comerse la hierba.
    ///   5) Al final, llama a <see cref="DestructibleTerrain.ApplyPixels"/>
    ///      con el array entero de un golpe.
    ///
    ///  Uso:
    ///   · Añade este componente al MISMO GameObject que <see cref="DestructibleTerrain"/>.
    ///   · Marca <see cref="generateOnStart"/> si quieres que haga un mapa al empezar.
    ///   · Desde código o menú contextual ("Regenerar") puedes lanzar otro mapa.
    ///
    ///  Semilla:
    ///   · Si <see cref="randomSeedEachGenerate"/> está ON, cada Generate() usa
    ///     un seed distinto → mapa diferente. Si está OFF, siempre sale el
    ///     mismo mapa (útil para depurar).
    ///
    ///  Siguiente paso (Tanda 2): biomas como ScriptableObject, puntos de
    ///  spawn seguros detectados automáticamente, y decoración opcional
    ///  (piedritas / matas de hierba).
    /// </summary>
    [RequireComponent(typeof(DestructibleTerrain))]
    public class TerrainGenerator : MonoBehaviour
    {
        // ============================================================
        //  TAMAÑO
        // ============================================================
        [Header("Tamaño del mapa (píxeles)")]
        [Tooltip("Ancho en píxeles. Con PPU=32 del DestructibleTerrain → 2048 px = 64 unidades de mundo.")]
        [SerializeField] private int width = 2048;

        [Tooltip("Alto en píxeles. Con PPU=32 → 1024 px = 32 unidades de mundo.")]
        [SerializeField] private int height = 1024;

        // ============================================================
        //  SEMILLA
        // ============================================================
        [Header("Semilla")]
        [Tooltip("Semilla para el ruido. Mismo valor = mismo mapa exacto.")]
        [SerializeField] private int seed = 0;

        [Tooltip("Si ON, cada llamada a Generate() elige un seed aleatorio y sale un mapa nuevo.")]
        [SerializeField] private bool randomSeedEachGenerate = true;

        // ============================================================
        //  COLINAS (perfil del suelo)
        // ============================================================
        [Header("Colinas (perfil del suelo)")]
        [Tooltip("Altura base del suelo respecto al alto total. 0.5 = el suelo está justo a la mitad, dejando bastante cielo.")]
        [SerializeField, Range(0.2f, 0.8f)] private float baseHeightRatio = 0.5f;

        [Tooltip("Ruido de BAJA frecuencia — montañas grandes. Súbelo para picos más marcados. Con frequency alta salen más montañas a lo largo del mapa.")]
        [SerializeField] private float hillAmplitude = 70f;
        [SerializeField] private float hillFrequency = 0.0035f;

        [Tooltip("Ruido MEDIO — ondulación dentro de las colinas. Bajado para que las pendientes sean subibles para la rata.")]
        [SerializeField] private float detailAmplitude = 22f;
        [SerializeField] private float detailFrequency = 0.010f;

        [Tooltip("Ruido FINO — rugosidad local. Lo bajamos a 4 para que el suelo sea casi liso de cerca y la rata no se trabe en bordes diminutos.")]
        [SerializeField] private float microAmplitude = 4f;
        [SerializeField] private float microFrequency = 0.05f;

        [Tooltip("RIDGED NOISE — picos afilados. PUESTO A 0 por defecto: salían crestas demasiado angulosas y la rata no podía subir. Sube esto si quieres montañas más alpinas; mantelo en 0 para colinas suaves estilo Worms 2.")]
        [SerializeField] private float ridgeAmplitude = 0f;
        [SerializeField] private float ridgeFrequency = 0.008f;

        [Header("Suavizado del perfil")]
        [Tooltip("Pasadas de media móvil (5 columnas) que aplicamos al perfil del suelo después de calcularlo. " +
                 "Cada pasada redondea más las puntas. 0 = perfil bruto, 2-3 = colinas muy lisas. Útil para que la rata pueda subir cualquier pendiente.")]
        [SerializeField, Range(0, 5)] private int profileSmoothPasses = 2;

        // ============================================================
        //  CUEVAS
        // ============================================================
        [Header("Cuevas")]
        [Tooltip("Si OFF, el mapa es sólido por debajo del suelo (sin cuevas).")]
        [SerializeField] private bool generateCaves = true;

        [Tooltip("Frecuencia del ruido de cuevas. Más bajo = cuevas más grandes y tendidas. Más alto = red de pasillos finos.")]
        [SerializeField] private float caveFrequency = 0.012f;

        [Tooltip("Umbral: si el ruido Perlin en (x,y) > este valor, el píxel es AIRE. " +
                 "Subir → menos cuevas. Bajar → más. Sweet spot: 0.60–0.70.")]
        [SerializeField, Range(0.3f, 0.9f)] private float caveThreshold = 0.68f;

        [Tooltip("Píxeles de profundidad mínima respecto a la superficie antes de carvear cuevas. " +
                 "Evita que las cuevas se coman la hierba.")]
        [SerializeField] private int caveDepthMargin = 35;

        [Tooltip("Profundidad MÁXIMA (px) a la que aún aparecen cuevas. Por debajo de esta profundidad el mapa " +
                 "es TIERRA SÓLIDA (bedrock) — así no sale todo agujereado hasta abajo.")]
        [SerializeField] private int maxCaveDepth = 280;

        // ============================================================
        //  HIERBA
        // ============================================================
        [Header("Hierba")]
        [SerializeField] private Color32 grassColor = new Color32(75, 150, 65, 255);

        [Tooltip("Grosor (px) de la banda de hierba bajo la superficie.")]
        [SerializeField] private int grassBandPx = 20;

        [Tooltip("Color del contorno brillante en el borde superior — el amarillo chillón típico de Worms.")]
        [SerializeField] private Color32 grassOutlineColor = new Color32(200, 230, 90, 255);

        [Tooltip("Grosor (px) del contorno brillante. 2–3 es suficiente para que se vea de lejos.")]
        [SerializeField] private int grassOutlinePx = 3;

        // ============================================================
        //  TIERRA (con grano)
        // ============================================================
        [Header("Tierra (con grano)")]
        [SerializeField] private Color32 dirtColor = new Color32(135, 90, 55, 255);
        [SerializeField] private Color32 dirtColorDark = new Color32(85, 55, 30, 255);

        [Tooltip("Frecuencia del ruido 2D que mezcla los dos tonos de tierra. " +
                 "Controla el tamaño de las 'manchas'.")]
        [SerializeField] private float dirtGrainFrequency = 0.08f;

        [Tooltip("Cuánto speckle por píxel (0 = manchas suaves, 1 = pura sal y pimienta).")]
        [SerializeField, Range(0f, 1f)] private float dirtSpeckleStrength = 0.25f;

        // ============================================================
        //  ISLAS FLOTANTES
        // ============================================================
        [Header("Islas flotantes")]
        [Tooltip("Cuántas islas flotantes intentamos colocar en el cielo. " +
                 "OJO: ahora mismo PUESTO A 0 por defecto — Unity Sprite.Tight y nuestro skyline fallback no las soportan bien y crean colliders raros (paredes invisibles bajo ellas). " +
                 "Pendiente de añadir un PolygonCollider2D propio por isla en una sesión futura. Mientras tanto, déjalas a 0.")]
        [SerializeField] private int floatingIslandCount = 0;

        [Tooltip("Radio mínimo y máximo (px) del eje mayor de la isla (usamos elipses ligeramente aplastadas).")]
        [SerializeField] private int floatingIslandMinRadius = 35;
        [SerializeField] private int floatingIslandMaxRadius = 90;

        [Tooltip("Separación mínima (px) entre la superficie principal y el centro de una isla flotante.")]
        [SerializeField] private int floatingIslandMinAboveGround = 70;

        [Tooltip("Separación mínima (px) desde el techo del mapa al centro de una isla.")]
        [SerializeField] private int floatingIslandTopMargin = 80;

        // ============================================================
        //  ARRANQUE
        // ============================================================
        [Header("Arranque")]
        [Tooltip("Si ON, genera un mapa en Start. En multijugador querremos OFF y que lo lance el host (Sesión 3).")]
        [SerializeField] private bool generateOnStart = true;

        // ============================================================
        //  INTERNO
        // ============================================================
        private DestructibleTerrain terrain;

        // Cacheamos la línea de altura (altura del suelo px por cada columna x)
        // del último mapa generado. La usa GetSurfaceWorldPos para que los
        // spawns se peguen a la superficie actual del mapa.
        private int[] lastHeightLine;
        public bool HasHeightLine => lastHeightLine != null;

        private void Awake()
        {
            terrain = GetComponent<DestructibleTerrain>();
            // Que el DestructibleTerrain no construya el terreno de senos
            // por su cuenta — nosotros le damos uno mucho mejor.
            terrain.SuppressTestTerrain();
        }

        private void Start()
        {
            if (generateOnStart)
                Generate();
        }

        /// <summary>
        ///  Evita que Start() dispare Generate() por su cuenta. Lo usa
        ///  <see cref="Ratms.Networking.NetworkDestructibleTerrain"/> en Awake
        ///  para tomar el control: en multijugador el seed lo decide el host
        ///  y lo manda por red, así que no queremos que el cliente genere
        ///  un mapa random local antes de recibirlo.
        /// </summary>
        public void SuppressGenerateOnStart()
        {
            generateOnStart = false;
        }

        // ============================================================
        //  API PÚBLICA
        // ============================================================

        /// <summary>
        ///  Genera un mapa nuevo. Elige seed aleatorio si el toggle lo pide.
        ///  Expuesto en el menú contextual del Inspector para probar sin Play.
        /// </summary>
        [ContextMenu("Regenerar mapa")]
        public void Generate()
        {
            if (randomSeedEachGenerate)
                seed = Random.Range(int.MinValue, int.MaxValue);
            GenerateWithSeed(seed);
        }

        /// <summary>
        ///  Genera un mapa con una semilla concreta. En multijugador, el host
        ///  elegirá la semilla y la mandará por red → todos generan el mismo mapa.
        /// </summary>
        public void GenerateWithSeed(int newSeed)
        {
            seed = newSeed;
            if (terrain == null) terrain = GetComponent<DestructibleTerrain>();

            // Offsets aleatorios derivados del seed → desplazan las funciones Perlin
            // para que semillas distintas den terrenos realmente distintos.
            System.Random rng = new System.Random(seed);
            float offHill   = (float)(rng.NextDouble() * 10000.0);
            float offDet    = (float)(rng.NextDouble() * 10000.0);
            float offMic    = (float)(rng.NextDouble() * 10000.0);
            float offRidge  = (float)(rng.NextDouble() * 10000.0);
            float offCaveX  = (float)(rng.NextDouble() * 10000.0);
            float offCaveY  = (float)(rng.NextDouble() * 10000.0);
            float offGrainX = (float)(rng.NextDouble() * 10000.0);
            float offGrainY = (float)(rng.NextDouble() * 10000.0);

            // --- 1) Calcular la línea de altura del suelo para cada x ---
            int baseHeight = Mathf.RoundToInt(height * baseHeightRatio);
            int[] heightLine = new int[width];
            for (int x = 0; x < width; x++)
            {
                // Centramos cada Perlin en 0 (restando 0.5 y multiplicando por 2)
                // para que pueda subir Y bajar respecto al baseHeight.
                float n1 = (Mathf.PerlinNoise(x * hillFrequency   + offHill, 0.37f) - 0.5f) * 2f;
                float n2 = (Mathf.PerlinNoise(x * detailFrequency + offDet,  0.71f) - 0.5f) * 2f;
                float n3 = (Mathf.PerlinNoise(x * microFrequency  + offMic,  0.93f) - 0.5f) * 2f;

                // RIDGED: 1 - |2p - 1| → valores entre 0 (valle) y 1 (cresta).
                // Siempre suma (solo pone picos encima, no los quita). Crea la
                // sensación de "montaña con cumbre" en vez de pura onda suave.
                float r = Mathf.PerlinNoise(x * ridgeFrequency + offRidge, 0.55f);
                float ridged = 1f - Mathf.Abs(r * 2f - 1f);

                int h = baseHeight
                      + Mathf.RoundToInt(n1 * hillAmplitude)
                      + Mathf.RoundToInt(n2 * detailAmplitude)
                      + Mathf.RoundToInt(n3 * microAmplitude)
                      + Mathf.RoundToInt(ridged * ridgeAmplitude);

                // Dejamos margen arriba y abajo para no pegarnos a los bordes.
                heightLine[x] = Mathf.Clamp(h, 20, height - 20);
            }

            // --- 1.5) Suavizado del perfil (media móvil) -----------------
            //  Cada pasada redondea las puntas. Aplica a heightLine para que
            //  la silueta dibujada Y los colliders queden suaves. Si el usuario
            //  pone profileSmoothPasses=0, este bloque no hace nada.
            if (profileSmoothPasses > 0)
            {
                int[] tmp = new int[width];
                int radius = 2; // ventana de 5 columnas (centro + 2 a cada lado)
                for (int pass = 0; pass < profileSmoothPasses; pass++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int from = Mathf.Max(0, x - radius);
                        int to   = Mathf.Min(width - 1, x + radius);
                        int sum = 0;
                        for (int i = from; i <= to; i++) sum += heightLine[i];
                        tmp[x] = sum / (to - from + 1);
                    }
                    System.Array.Copy(tmp, heightLine, width);
                }
            }

            // --- 2) Rellenar píxeles de la silueta principal ---
            Color32[] pixels = new Color32[width * height];
            Color32 air = new Color32(0, 0, 0, 0);

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x;
                    int surfaceY = heightLine[x];

                    // Cielo
                    if (y > surfaceY)
                    {
                        pixels[idx] = air;
                        continue;
                    }

                    int distToSurface = surfaceY - y; // 0 en la cresta, crece hacia abajo

                    // Cuevas — solo en una BANDA bajo la superficie:
                    //  · por debajo de caveDepthMargin (no agujereamos la hierba)
                    //  · por encima de maxCaveDepth (debajo hay roca sólida)
                    if (generateCaves
                        && distToSurface > caveDepthMargin
                        && distToSurface < maxCaveDepth)
                    {
                        float cave = Mathf.PerlinNoise(x * caveFrequency + offCaveX,
                                                       y * caveFrequency + offCaveY);
                        if (cave > caveThreshold)
                        {
                            pixels[idx] = air;
                            continue;
                        }
                    }

                    // Colorear: outline top, luego hierba, luego tierra con grano.
                    if (distToSurface < grassOutlinePx)
                    {
                        pixels[idx] = grassOutlineColor;
                    }
                    else if (distToSurface < grassBandPx)
                    {
                        pixels[idx] = grassColor;
                    }
                    else
                    {
                        pixels[idx] = DirtPixel(x, y, offGrainX, offGrainY);
                    }
                }
            }

            // --- 3) Islas flotantes ---
            for (int i = 0; i < floatingIslandCount; i++)
            {
                int ix = rng.Next(floatingIslandMaxRadius + 5,
                                  width - floatingIslandMaxRadius - 5);
                int floorY = heightLine[ix] + floatingIslandMinAboveGround;
                int ceilY  = height - floatingIslandTopMargin;
                if (floorY >= ceilY) continue;  // no hay hueco para meterla

                int iy = rng.Next(floorY, ceilY);
                float rx = (float)(rng.NextDouble() *
                                   (floatingIslandMaxRadius - floatingIslandMinRadius))
                         + floatingIslandMinRadius;
                // Aplanamos un poco la isla en vertical para que parezca
                // flotar y no ser una pelotaza.
                float ry = rx * (0.55f + (float)rng.NextDouble() * 0.25f);
                float perturbOffset = (float)(rng.NextDouble() * 1000.0);

                PaintFloatingIsland(pixels, ix, iy, rx, ry, perturbOffset,
                                    heightLine, offGrainX, offGrainY);
            }

            // --- 4) Volcar al terreno ---
            terrain.ApplyPixels(pixels, width, height);

            // Cacheamos la línea de altura para que el sistema de spawn pueda
            // preguntarnos "¿a qué altura está el suelo en esta X de mundo?"
            // y pegar a las ratas a la superficie actual del mapa procedural.
            lastHeightLine = heightLine;
        }

        // ============================================================
        //  CONSULTA DE SUPERFICIE (para spawns)
        // ============================================================

        /// <summary>
        ///  Devuelve la posición del MUNDO (x, y) de la superficie del mapa
        ///  para la X de mundo indicada. Es decir: dado un punto horizontal,
        ///  te dice dónde cae el suelo ahí, justo encima de la hierba.
        ///
        ///  <paramref name="searchRadiusPx"/> > 0 activa una "búsqueda de zona
        ///  plana": en un rango de +/- ese número de píxeles alrededor de la X
        ///  pedida, escogemos la columna cuya altura sea más parecida a la media
        ///  local (para que las ratas no spawneen justo en una pared vertical
        ///  o en la punta de un pico). Si no se pasa, devuelve justo la X dada.
        ///
        ///  Devuelve null si no hay línea de altura cacheada (aún no se ha
        ///  generado ningún mapa) o si la X queda fuera del rango del mapa.
        ///
        ///  NOTA: usa el transform local del GameObject (pivot bottom-left)
        ///  y el PixelsPerUnit del DestructibleTerrain para convertir píxeles
        ///  a unidades de mundo, así que funciona aunque muevas el terreno.
        /// </summary>
        public Vector3? GetSurfaceWorldPos(float worldX, int searchRadiusPx = 0)
        {
            if (lastHeightLine == null) return null;
            if (terrain == null) terrain = GetComponent<DestructibleTerrain>();
            if (terrain == null) return null;

            float ppu = terrain.PixelsPerUnit;
            Vector3 origin = transform.position;

            // Mundo → píxel (redondeado).
            int px = Mathf.RoundToInt((worldX - origin.x) * ppu);
            if (px < 0 || px >= lastHeightLine.Length) return null;

            int chosenX = px;

            // Búsqueda de zona plana: dentro del rango, preferimos la columna
            // cuya altura difiera menos de la media local. Así evitamos spawnear
            // a una rata sobre un pico de aguja o al lado de un muro.
            if (searchRadiusPx > 0)
            {
                int from = Mathf.Max(0, px - searchRadiusPx);
                int to   = Mathf.Min(lastHeightLine.Length - 1, px + searchRadiusPx);

                // Media de alturas en la ventana
                long sum = 0;
                int count = to - from + 1;
                for (int i = from; i <= to; i++) sum += lastHeightLine[i];
                float avg = sum / (float)count;

                // Elegimos la X cuya altura se acerca más a la media
                float bestScore = float.MaxValue;
                for (int i = from; i <= to; i++)
                {
                    float score = Mathf.Abs(lastHeightLine[i] - avg);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        chosenX = i;
                    }
                }
            }

            int surfacePy = lastHeightLine[chosenX];

            // Píxel → mundo. Sumamos 1 píxel extra para quedar justo ENCIMA
            // del outline de hierba, no clavados dentro.
            float wx = origin.x + (chosenX + 0.5f) / ppu;
            float wy = origin.y + (surfacePy + 1f) / ppu;
            return new Vector3(wx, wy, origin.z);
        }

        // ============================================================
        //  ISLAS FLOTANTES
        // ============================================================

        /// <summary>
        ///  Dibuja una isla flotante elíptica con borde perturbado (blob orgánico)
        ///  y le pinta su propio borde de hierba + outline encima. Solo toca
        ///  píxeles que sean AIRE y estén suficientemente lejos del terreno
        ///  principal (para no fundirse con una montaña por casualidad).
        /// </summary>
        private void PaintFloatingIsland(Color32[] pixels,
                                         int cx, int cy,
                                         float radiusX, float radiusY,
                                         float perturbOffset,
                                         int[] heightLine,
                                         float offGrainX, float offGrainY)
        {
            int maxR = Mathf.CeilToInt(Mathf.Max(radiusX, radiusY) * 1.4f);
            int xmin = Mathf.Max(0, cx - maxR);
            int xmax = Mathf.Min(width - 1, cx + maxR);
            int ymin = Mathf.Max(0, cy - maxR);
            int ymax = Mathf.Min(height - 1, cy + maxR);

            int lw = xmax - xmin + 1;
            int lh = ymax - ymin + 1;
            bool[] inside = new bool[lw * lh];

            // Paso 1: marcar qué píxeles caen dentro del blob
            for (int y = ymin; y <= ymax; y++)
            {
                for (int x = xmin; x <= xmax; x++)
                {
                    // Evita fundir la isla con la montaña de debajo
                    if (y <= heightLine[x] + 10) continue;

                    float dx = (x - cx) / radiusX;
                    float dy = (y - cy) / radiusY;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Perturbamos el radio efectivo con un Perlin dependiendo
                    // del ÁNGULO → borde ondulado en vez de elipse perfecta.
                    float ang = Mathf.Atan2(y - cy, x - cx);
                    float pert = Mathf.PerlinNoise(Mathf.Cos(ang) * 1.8f + perturbOffset,
                                                   Mathf.Sin(ang) * 1.8f + perturbOffset);
                    float threshold = 1f - (pert - 0.5f) * 0.55f; // ±27% variación

                    if (d < threshold)
                        inside[(y - ymin) * lw + (x - xmin)] = true;
                }
            }

            // Paso 2: pintar. Para cada columna, escaneamos de arriba a abajo
            // contando cuántos píxeles llevamos dentro → así sabemos cuáles son
            // "top" (outline), "subtop" (hierba) y "profundo" (tierra).
            for (int lx = 0; lx < lw; lx++)
            {
                int depth = -1;
                // y=ymax es el top (pixel más alto), y=ymin el fondo
                for (int ly = lh - 1; ly >= 0; ly--)
                {
                    bool solid = inside[ly * lw + lx];
                    if (!solid)
                    {
                        depth = -1; // al salir de sólido, reseteamos
                        continue;
                    }
                    depth = (depth < 0) ? 0 : depth + 1;

                    int x = lx + xmin;
                    int y = ly + ymin;
                    int idx = y * width + x;

                    if (depth < grassOutlinePx)
                        pixels[idx] = grassOutlineColor;
                    else if (depth < grassBandPx)
                        pixels[idx] = grassColor;
                    else
                        pixels[idx] = DirtPixel(x, y, offGrainX, offGrainY);
                }
            }
        }

        /// <summary>
        ///  Color de un píxel de tierra con grano (mezcla de dos tonos +
        ///  speckle por píxel). Lo usa tanto la silueta principal como las islas.
        /// </summary>
        private Color32 DirtPixel(int x, int y, float offGrainX, float offGrainY)
        {
            float g = Mathf.PerlinNoise(x * dirtGrainFrequency + offGrainX,
                                        y * dirtGrainFrequency + offGrainY);
            float speckle = Hash01(x, y, seed);
            float mix = Mathf.Clamp01(g * (1f - dirtSpeckleStrength) +
                                      speckle * dirtSpeckleStrength);
            return LerpColor32(dirtColorDark, dirtColor, mix);
        }

        // ============================================================
        //  UTILIDADES
        // ============================================================

        /// <summary>
        ///  Hash determinista por píxel → valor 0..1. Lo usamos para el "speckle"
        ///  de la tierra, que es ruido blanco por píxel (sal y pimienta fina).
        /// </summary>
        private static float Hash01(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0xFFFFFFu;
            }
        }

        /// <summary>
        ///  Lerp byte a byte entre dos Color32 (más rápido y sin allocs
        ///  comparado con Color.Lerp + conversión).
        /// </summary>
        private static Color32 LerpColor32(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.r, b.r, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.g, b.g, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.b, b.b, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.a, b.a, t)));
        }
    }
}
