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

        [Header("Erosión de bordes laterales")]
        [Tooltip("Ancho en píxeles de la zona de erosión en los bordes izquierdo y derecho del mapa. " +
                 "Los bordes se 'comen' con Perlin en esta franja para que no sean líneas rectas verticales " +
                 "sino formas orgánicas estilo Worms. 0 = sin erosión (bordes rectos), 80-150 = buen efecto.")]
        [SerializeField] private int edgeErosionWidthPx = 120;

        [Tooltip("Frecuencia del ruido de erosión en los bordes. Alto = erosión con muchos detalles pequeños, " +
                 "bajo = mordiscos grandes y suaves. 0.02-0.05 va bien.")]
        [SerializeField] private float edgeErosionFrequency = 0.035f;

        [Tooltip("Ancho en píxeles de la pendiente descendente en los bordes. La SUPERFICIE del terreno " +
                 "baja gradualmente hacia el agua en esta franja, formando una colina en vez de un " +
                 "acantilado recto. 0 = borde a máxima altura (acantilado), 300-500 = buena bajada tipo isla.")]
        [SerializeField] private int edgeSlopeWidthPx = 400;

        [Tooltip("Qué proporción del alto total baja la superficie en el borde. 1 = baja hasta el fondo (isla completa), " +
                 "0.5 = baja a la mitad, 0.3 = pendiente suave sin llegar al agua.")]
        [Range(0f, 1f)] [SerializeField] private float edgeSlopeDepth = 0.85f;

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
        [SerializeField] private Color32 dirtColorMid = new Color32(110, 72, 42, 255);
        [SerializeField] private Color32 dirtColorDark = new Color32(85, 55, 30, 255);

        [Tooltip("Frecuencia del ruido 2D que mezcla los dos tonos de tierra. " +
                 "Controla el tamaño de las 'manchas'.")]
        [SerializeField] private float dirtGrainFrequency = 0.08f;

        [Tooltip("Cuánto speckle por píxel (0 = manchas suaves, 1 = pura sal y pimienta).")]
        [SerializeField, Range(0f, 1f)] private float dirtSpeckleStrength = 0.25f;

        [Tooltip("Cuántas 'piedras' oscuras aparecen por m² de terreno. " +
                 "0 = terreno liso, 12-15 = terreno con carácter, 25+ = muy pedregoso.")]
        [SerializeField, Range(0f, 100f)] private float dirtPebbleDensity = 55f;

        [Tooltip("Radio mínimo de las piedras en píxeles. Sube esto si no las ves bien.")]
        [SerializeField, Range(2, 30)] private int pebbleMinRadius = 14;

        [Tooltip("Radio máximo de las piedras en píxeles. Debe ser >= minRadius.")]
        [SerializeField, Range(3, 40)] private int pebbleMaxRadius = 26;

        [Tooltip("Intensidad de las 'vetas' horizontales (líneas de estrato). " +
                 "0 = sin vetas, 1 = muy marcadas. 0.15 sugiere estratos sutiles.")]
        [SerializeField, Range(0f, 1f)] private float dirtStrataStrength = 0.18f;

        [Tooltip("Frecuencia de motas CLARAS tipo minerales/cristales por m². " +
                 "0 = sin motas, 2-4 = puntitos brillantes ocasionales.")]
        [SerializeField, Range(0f, 20f)] private float mineralSpeckleDensity = 3f;

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
        // Contador de píxeles pintados como piedra en el último Generate — debug.
        private int pebblePixelsPainted;
        /// <summary>Acceso de solo lectura a la línea de altura (px por columna).
        /// Lo usa TerrainCaveBackdrop para dibujar un fondo oscuro con la forma
        /// exacta del terreno (no un rectángulo).</summary>
        public int[] GetHeightLine() => lastHeightLine;
        public int TextureWidth => width;
        public int TextureHeight => height;
        public float PixelsPerUnit => terrain != null ? terrain.PixelsPerUnit : 32f;

        // Cimas de las islas flotantes generadas (posiciones LOCAL en píxeles).
        // Se rellena en cada Generate. Lo usa GameSceneSpawnManager para
        // poder spawnear ratas encima de las islas.
        private readonly System.Collections.Generic.List<Vector2Int> lastIslandTops = new();
        /// <summary>Posiciones (en píxeles del terreno) de las CIMAS de las islas
        /// flotantes del último Generate. Para convertir a mundo:
        /// worldPos = terrain.transform.position + (px + 0.5, py + 0.5) / PPU.</summary>
        public System.Collections.Generic.IReadOnlyList<Vector2Int> GetFloatingIslandTops() => lastIslandTops;

        // Todos los píxeles VÁLIDOS para spawn: aire con tierra sólida debajo,
        // espacio libre suficiente para una rata. Incluye suelo, cuevas e islas.
        private readonly System.Collections.Generic.List<Vector2Int> validSpawnPixels = new();
        /// <summary>Devuelve todos los píxeles donde una rata puede estar de pie
        /// sin caerse ni quedar clipeada. Incluye superficie del suelo principal,
        /// suelo de cuevas navegables y cimas de islas flotantes.</summary>
        public System.Collections.Generic.IReadOnlyList<Vector2Int> GetValidSpawnPixels() => validSpawnPixels;

        /// <summary>Elige un píxel de spawn ALEATORIO del pool. Devuelve WORLD position
        /// listo para spawnear una rata.</summary>
        public Vector3? GetRandomValidSpawnWorld()
        {
            if (validSpawnPixels.Count == 0 || terrain == null) return null;
            var p = validSpawnPixels[Random.Range(0, validSpawnPixels.Count)];
            float ppu = terrain.PixelsPerUnit;
            Vector3 tPos = terrain.transform.position;
            return new Vector3(
                tPos.x + (p.x + 0.5f) / ppu,
                tPos.y + (p.y + 0.5f) / ppu,
                tPos.z);
        }

        private void Awake()
        {
            terrain = GetComponent<DestructibleTerrain>();
            // Que el DestructibleTerrain no construya el terreno de senos
            // por su cuenta — nosotros le damos uno mucho mejor.
            terrain.SuppressTestTerrain();
        }

        private void Start()
        {
            // Auto-instalar TerrainCaveBackdrop si no existe en la escena.
            // Así las cuevas se ven oscuras (no muestran el sky) sin que el
            // usuario tenga que añadir el componente a mano.
            if (Object.FindFirstObjectByType<TerrainCaveBackdrop>() == null)
            {
                gameObject.AddComponent<TerrainCaveBackdrop>();
                Debug.Log("[TerrainGenerator] Auto-añadido TerrainCaveBackdrop al terreno.");
            }

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

            // LOG DE DIAGNÓSTICO — deja constancia de con qué valores se genera
            // el terreno, para poder debuggear por qué las piedras no aparecen.
            pebblePixelsPainted = 0;   // reset del contador
            Debug.Log($"[TerrainGenerator] Generate seed={seed}, " +
                      $"dirtPebbleDensity={dirtPebbleDensity}, " +
                      $"pebbleMin={pebbleMinRadius}, pebbleMax={pebbleMaxRadius}, " +
                      $"dirtColor={dirtColor}, dirtColorDark={dirtColorDark}");

            // Si hay un BiomeManager en la escena, sus colores mandan sobre los del
            // inspector. Así el mismo TerrainGenerator sirve para todos los biomas
            // (pradera, alcantarilla, etc.) sin duplicar componente.
            ApplyBiomeColorsIfAvailable();

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

            // --- 1.25) PENDIENTE DESCENDENTE EN LOS BORDES ---------------
            //  Para que el terreno no termine en acantilado recto, bajamos la
            //  altura del suelo hacia los bordes. Sale una colina/isla que
            //  desciende suavemente al agua en vez de un corte vertical.
            //  Perturbamos con Perlin para que no sea una línea diagonal
            //  perfecta — sale una playa orgánica.
            if (edgeSlopeWidthPx > 0 && edgeSlopeDepth > 0f)
            {
                float rng2 = (float)(new System.Random(seed ^ 12345).NextDouble() * 1000.0);
                for (int x = 0; x < width; x++)
                {
                    int distToEdge = Mathf.Min(x, width - 1 - x);
                    if (distToEdge >= edgeSlopeWidthPx) continue;

                    // t: 1 en el borde puro, 0 en distToEdge = edgeSlopeWidthPx
                    float t = 1f - (distToEdge / (float)edgeSlopeWidthPx);
                    // Curva ease-in cuadrática → descenso lento al principio,
                    // rápido cerca del borde. Se ve más natural.
                    float curve = t * t;
                    // Perturbación Perlin para romper la simetría — la playa
                    // no sale como una diagonal perfecta.
                    float perlinPert = Mathf.PerlinNoise(x * 0.02f + rng2, 0.7f);
                    curve *= 0.85f + perlinPert * 0.30f;

                    int drop = Mathf.RoundToInt(curve * edgeSlopeDepth * height);
                    heightLine[x] = Mathf.Max(3, heightLine[x] - drop);
                }
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

                    // ============ EROSIÓN LATERAL ============
                    // En las franjas cerca del borde izquierdo/derecho, aplicamos
                    // un ruido Perlin 2D como máscara. Si el ruido supera el
                    // umbral (que depende de la distancia al borde), el píxel
                    // se convierte en aire. Resultado: bordes irregulares
                    // estilo Worms en lugar de líneas rectas verticales.
                    if (edgeErosionWidthPx > 0)
                    {
                        int distToEdge = Mathf.Min(x, width - 1 - x);
                        if (distToEdge < edgeErosionWidthPx)
                        {
                            // Cuanto más cerca del borde → mayor probabilidad de erosión.
                            //   distToEdge=0     → t=1 (borde puro, casi todo aire)
                            //   distToEdge=width → t=0 (interior, cero erosión)
                            float t = 1f - (distToEdge / (float)edgeErosionWidthPx);
                            float erosion = Mathf.PerlinNoise(
                                x * edgeErosionFrequency + offCaveX,
                                y * edgeErosionFrequency + offCaveY);
                            // Umbral: en el borde puro (t=1) el umbral es bajo →
                            // muchos píxeles pasan. En el interior (t=0) el umbral
                            // es 1 → ningún píxel se erosiona.
                            if (erosion > 1f - t * 0.85f)
                            {
                                pixels[idx] = air;
                                continue;
                            }
                        }
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
            lastIslandTops.Clear();
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

                // Guardamos la CIMA de la isla (centro X, top Y) para que el
                // spawn manager pueda usarla como punto de aparición.
                int topPy = iy + Mathf.RoundToInt(ry) + 2;   // +2 = margen sobre la hierba
                lastIslandTops.Add(new Vector2Int(ix, topPy));
            }

            // --- 4) Volcar al terreno ---
            terrain.ApplyPixels(pixels, width, height);

            // --- 5) Escanear puntos válidos para spawn ---
            //     Después de que ya tenemos el terreno con superficie, cuevas,
            //     islas y erosión de bordes aplicada, buscamos TODOS los píxeles
            //     donde una rata puede estar de pie sin caerse ni quedar clipeada.
            ScanValidSpawnPixels(pixels);

            // Log del contador de píxeles de piedra — para diagnosticar cuántas
            // piedras se pintan realmente. Si esto es 0, no se pintan piedras.
            Debug.Log($"[TerrainGenerator] Generate terminado. Píxeles pintados como piedra: {pebblePixelsPainted}, " +
                      $"puntos válidos de spawn: {validSpawnPixels.Count}");

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
        ///  Color de un píxel de tierra con grano, PIEDRAS y VETAS de estrato.
        ///  Combina:
        ///   · Perlin 2D de grano fino (mezcla de tres tonos oscuro/medio/claro).
        ///   · Perlin 2D de baja frecuencia para VETAS horizontales (estratos rocosos).
        ///   · Ruido tipo Voronoi cheap → PIEDRAS oscuras aisladas por el terreno.
        ///   · Speckle por píxel para variación fina.
        /// </summary>
        private Color32 DirtPixel(int x, int y, float offGrainX, float offGrainY)
        {
            // ===== 1) Grano base =====
            float g = Mathf.PerlinNoise(x * dirtGrainFrequency + offGrainX,
                                        y * dirtGrainFrequency + offGrainY);
            float speckle = Hash01(x, y, seed);
            float mix = Mathf.Clamp01(g * (1f - dirtSpeckleStrength) +
                                      speckle * dirtSpeckleStrength);

            // ===== 2) Vetas horizontales (estratos) =====
            // Perlin 1D vertical con baja frecuencia → líneas horizontales sutiles
            // de tono más oscuro/claro, como si la tierra tuviese capas geológicas.
            float strata = (Mathf.PerlinNoise(0.5f + offGrainX, y * 0.015f + offGrainY) - 0.5f) * 2f;
            mix += strata * dirtStrataStrength;
            mix = Mathf.Clamp01(mix);

            // ===== 3) Tres tonos base =====
            Color32 baseColor;
            if (mix < 0.45f)
                baseColor = LerpColor32(dirtColorDark, dirtColorMid, mix / 0.45f);
            else
                baseColor = LerpColor32(dirtColorMid, dirtColor, (mix - 0.45f) / 0.55f);

            // ===== 3.5) Motas claras tipo minerales/cristales =====
            // Píxeles CLAROS aislados y bien espaciados — no clusters ni patrones
            // de cruz. Un píxel dorado suelto cada N píxeles al azar.
            if (mineralSpeckleDensity > 0f)
            {
                float mHash = FastHash(x, y, 34567);
                // Threshold muy alto → solo el ~0.1% de píxeles se pintan.
                // density 3 → ~0.012% de píxeles, unos pocos por m² visible.
                float mThreshold = 1f - mineralSpeckleDensity * 0.0008f;
                if (mHash > mThreshold)
                {
                    Color32 mineral = new Color32(210, 175, 100, 255);
                    return LerpColor32(baseColor, mineral, 0.65f);
                }
            }

            // ===== 4) Piedras/rocas oscuras esparcidas =====
            // Piedras grandes visibles. Celda de 48 px → piedras separadas.
            // El hash SÓLO usa cx y cy (no seed) para simplificar y evitar
            // que un mal hash devuelva siempre valores fuera de rango.
            if (dirtPebbleDensity > 0f)
            {
                const int CELL = 48;
                int cx = x / CELL;
                int cy = y / CELL;
                // Hash simplificado — probablemente el anterior con seed producía
                // valores raros que casi nunca pasaban el threshold.
                float cellHash = FastHash(cx, cy, 12345);
                float thresholdPeb = dirtPebbleDensity / 100f;   // 0-100 → 0-1
                if (cellHash < thresholdPeb)
                {
                    // Posición dentro de la celda con leve offset aleatorio
                    float offXH = FastHash(cx, cy, 99991);
                    float offYH = FastHash(cx, cy, 55511);
                    int px = cx * CELL + CELL / 2 + Mathf.RoundToInt((offXH - 0.5f) * (CELL * 0.5f));
                    int py = cy * CELL + CELL / 2 + Mathf.RoundToInt((offYH - 0.5f) * (CELL * 0.5f));

                    float sizeH = FastHash(cx, cy, 77733);
                    int radius = pebbleMinRadius +
                                 Mathf.RoundToInt(sizeH * (pebbleMaxRadius - pebbleMinRadius));

                    int dx = x - px, dy = y - py;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Borde ligeramente irregular — perturbación MUY suave.
                    // Multiplicador bajo (1.0) → una ola redondeada por lado.
                    // Variación pequeña (±15%) → sigue siendo una piedra
                    // reconocible, no estrella.
                    float ang = Mathf.Atan2(dy, dx);
                    float pert = Mathf.PerlinNoise(
                        Mathf.Cos(ang) * 1.0f + px * 0.07f,
                        Mathf.Sin(ang) * 1.0f + py * 0.07f);
                    float effectiveRadius = radius * (0.92f + (pert - 0.5f) * 0.3f);

                    if (dist <= effectiveRadius)
                    {
                        pebblePixelsPainted++;
                        float t = dist / effectiveRadius;
                        float tCurved = t * t;
                        Color32 core = new Color32(30, 22, 18, 255);
                        Color32 rim = LerpColor32(dirtColorDark, dirtColorMid, 0.35f);
                        return LerpColor32(core, rim, tCurved);
                    }
                }
            }

            return baseColor;
        }

        /// <summary>
        ///  Hash rápido determinista en [0,1] para dos enteros + salt.
        ///  Reemplaza al Hash01 anterior por si daba problemas de rango.
        /// </summary>
        private static float FastHash(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + salt * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h = h ^ (h >> 16);
                return (h & 0xFFFFFFu) / (float)0xFFFFFFu;
            }
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

        /// <summary>
        ///  Si en la escena hay un BiomeManager con un bioma activo, sobrescribe
        ///  los colores del terreno con los del bioma. Así el mismo TerrainGenerator
        ///  sirve para pradera, alcantarilla, o cualquier bioma futuro sin duplicar
        ///  el componente.
        /// </summary>
        private void ApplyBiomeColorsIfAvailable()
        {
            var bm = BiomeManager.Instance;
            if (bm == null || bm.Current == null) return;

            grassColor        = bm.Current.grassColor;
            grassOutlineColor = bm.Current.grassOutlineColor;
            dirtColor         = bm.Current.dirtColor;
            dirtColorMid      = bm.Current.dirtColorMid;
            dirtColorDark     = bm.Current.dirtColorDark;
        }

        /// <summary>
        ///  Recorre el terreno y guarda todos los píxeles donde una rata puede
        ///  aparecer sin problemas:
        ///   · Es AIRE (alpha 0) — la rata no queda enterrada.
        ///   · El píxel de DEBAJO es SÓLIDO — hay suelo donde apoyarse.
        ///   · Los siguientes N píxeles hacia ARRIBA son AIRE — cabe la rata.
        ///   · Los siguientes M píxeles hacia ABAJO son SÓLIDOS — no se cae inmediatamente.
        ///   · No está en la franja lateral erosionada — no cae al vacío/agua.
        ///
        ///  Esto incluye automáticamente:
        ///   · Superficie del suelo principal (top del terreno).
        ///   · Suelo de cuevas navegables (aire dentro de la tierra).
        ///   · Cimas de islas flotantes.
        /// </summary>
        private void ScanValidSpawnPixels(Color32[] pixels)
        {
            validSpawnPixels.Clear();
            // Requisitos: la rata mide ~32 px de alto → dejamos 32 px libres arriba.
            const int RAT_HEIGHT_PX = 32;
            const int GROUND_DEPTH_PX = 8;   // píxeles sólidos debajo del pie
            // Excluir la franja lateral erosionada (donde puede haber salientes al vacío)
            int lateralMargin = edgeErosionWidthPx + 10;
            // No spawnear más arriba de este límite (evita picos aislados en el cielo)
            int maxY = height - RAT_HEIGHT_PX - 5;

            for (int x = lateralMargin; x < width - lateralMargin; x++)
            {
                for (int y = GROUND_DEPTH_PX; y < maxY; y++)
                {
                    // Este píxel debe ser AIRE
                    if (pixels[y * width + x].a != 0) continue;
                    // El píxel de debajo debe ser SÓLIDO
                    if (pixels[(y - 1) * width + x].a == 0) continue;
                    // Espacio libre arriba (para el cuerpo de la rata)
                    bool hasHeadroom = true;
                    for (int dy = 1; dy < RAT_HEIGHT_PX; dy++)
                    {
                        if (y + dy >= height) { hasHeadroom = false; break; }
                        if (pixels[(y + dy) * width + x].a != 0) { hasHeadroom = false; break; }
                    }
                    if (!hasHeadroom) continue;
                    // Suelo sólido bajo los pies (para no caer inmediatamente)
                    bool hasGround = true;
                    for (int dy = 1; dy <= GROUND_DEPTH_PX; dy++)
                    {
                        if (y - dy < 0) { hasGround = false; break; }
                        if (pixels[(y - dy) * width + x].a == 0) { hasGround = false; break; }
                    }
                    if (!hasGround) continue;

                    validSpawnPixels.Add(new Vector2Int(x, y));
                    // Saltamos las siguientes filas para no acumular píxeles
                    // adyacentes (el mismo "suelo" ya está representado por este).
                    y += RAT_HEIGHT_PX;
                }
            }
        }
    }
}
