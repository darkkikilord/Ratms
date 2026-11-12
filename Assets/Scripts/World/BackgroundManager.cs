using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  FONDO PROCEDURAL — cielo con degradado, nubes que scrollan, y dos
    ///  capas de parallax con siluetas al fondo (colinas o edificios según
    ///  el bioma activo). Todo generado en runtime, cero assets externos.
    ///
    ///  Se auto-configura leyendo el <see cref="BiomeManager"/> de la escena.
    ///  Si no hay bioma activo, usa el fallback del inspector (Meadow).
    ///
    ///  Uso:
    ///   1) Añade este componente a un GameObject vacío llamado "Background".
    ///   2) Colócalo en el mismo sitio que la cámara (X central del mapa).
    ///   3) Los sortingOrder son MUY negativos para que quede detrás de todo.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackgroundManager : MonoBehaviour
    {
        [Header("Tamaño del fondo (unidades de mundo)")]
        [Tooltip("Ancho total del fondo. Debe ser mayor que el mapa + zoom-out máximo de la cámara.")]
        [SerializeField] private float bgWidth = 400f;

        [Tooltip("Alto total del fondo.")]
        [SerializeField] private float bgHeight = 80f;

        [Header("Auto-posicionado")]
        [Tooltip("Si ON, se centra automáticamente sobre el terreno como hace WaterDeathZone.")]
        [SerializeField] private bool autoPositionFromTerrain = true;

        [Header("Parallax")]
        [Tooltip("Velocidad de scroll de las nubes (unidades/segundo).")]
        [SerializeField] private float cloudSpeed = 0.8f;

        [Tooltip("Si ON, el fondo sigue a la cámara principal para que siempre sea visible. " +
                 "Sin esto, cuando la cámara se aleja del centro del terreno el fondo se queda atrás y no se ve.")]
        [SerializeField] private bool followCamera = true;

        [Tooltip("Factor de parallax del CIELO. 0 = pegado a la cámara (no se mueve visualmente). " +
                 "1 = se mueve tanto como la cámara (no da parallax). Recomendado ~0.05 para movimiento sutil.")]
        [Range(0f, 1f)] [SerializeField] private float skyParallax = 0.05f;

        [Tooltip("Factor de parallax de la silueta LEJANA. Poco movimiento (más lejos = más lento).")]
        [Range(0f, 1f)] [SerializeField] private float silhouetteFarParallax = 0.15f;

        [Tooltip("Factor de parallax de la silueta CERCANA. Más movimiento que la lejana.")]
        [Range(0f, 1f)] [SerializeField] private float silhouetteNearParallax = 0.35f;

        [Tooltip("Factor de parallax de las NUBES. Casi pegadas al cielo, movimiento muy sutil.")]
        [Range(0f, 1f)] [SerializeField] private float cloudsParallax = 0.08f;

        [Header("Capas — sortingOrder (más negativo = más al fondo)")]
        [SerializeField] private int skySortingOrder      = -30;
        [SerializeField] private int silhouetteFarOrder   = -25;
        [SerializeField] private int silhouetteNearOrder  = -22;
        [SerializeField] private int cloudsOrder          = -20;

        [Header("Posición en Z (importante para la cámara)")]
        [Tooltip("Z absoluta a la que se colocan las capas del fondo. Debe ser MAYOR que Camera.z + nearClip (típico -9.7). " +
                 "0 es siempre seguro con una cámara 2D estándar (Z=-10, nearClip=0.3).")]
        [SerializeField] private float forcedZ = 0f;

        [Header("Debug")]
        [SerializeField] private bool debugLog = true;

        // Runtime
        private BiomeDefinition biome;
        private GameObject skyGO;
        private GameObject cloudsGO;
        private GameObject silhouetteFarGO;
        private GameObject silhouetteNearGO;
        private float cloudScrollX;
        // Guardamos la posición "ancla" (donde iría el fondo si no siguiera cámara).
        // El follow calcula offsets desde este ancla para dar parallax.
        private Vector3 anchorWorldPos;
        // Centro del terreno guardado al auto-posicionar. Se usa como ancla fija
        // para el parallax, así no depende de si el LateUpdate ya movió el manager.
        private Vector3 terrainCenterAnchor;
        private bool hasTerrainAnchor;
        private Camera cachedCamera;

        private void Start()
        {
            // Aviso si por error hay más de un BackgroundManager: causaría capas
            // duplicadas y "dos fondos" superpuestos.
            var allMgrs = Object.FindObjectsByType<BackgroundManager>(FindObjectsSortMode.None);
            if (allMgrs.Length > 1)
                Debug.LogWarning($"[BackgroundManager] ¡Hay {allMgrs.Length} instancias en la escena! " +
                                 $"Solo debería haber una. Elimina los duplicados.");

            // Coge el bioma activo. Si no hay, usa Meadow por defecto (fallback).
            var bm = BiomeManager.Instance;
            biome = (bm != null && bm.Current != null)
                ? bm.Current
                : new BiomeDefinition();   // valores por defecto = Meadow

            if (autoPositionFromTerrain)
                StartCoroutine(AutoPositionThenBuild());
            else
                Build();
        }

        private System.Collections.IEnumerator AutoPositionThenBuild()
        {
            yield return null;   // esperar 1 frame por si el terreno se genera en su Start
            TryAutoPosition();
            Build();
        }

        private void TryAutoPosition()
        {
            var terrain = Object.FindFirstObjectByType<DestructibleTerrain>();
            if (terrain == null) return;

            Vector2 tSize = terrain.WorldSize;
            Vector3 tPos = terrain.transform.position;

            // Centrar el fondo horizontalmente sobre el terreno, y en el medio
            // vertical (así el cielo cubre por encima y por debajo del terreno).
            Vector3 p = transform.position;
            p.x = tPos.x + tSize.x * 0.5f;
            p.y = tPos.y + tSize.y * 0.5f;
            transform.position = p;

            // GUARDAMOS EL CENTRO DEL TERRENO COMO ANCLA fija. Así el parallax
            // no depende de dónde esté el transform en el momento del Build
            // (que puede haberse movido si LateUpdate ya corrió).
            terrainCenterAnchor = p;
            hasTerrainAnchor = true;

            // Ajustar bgWidth para que cubra bien: 3x el ancho del mapa (parallax
            // se ve chungo si el fondo no cubre bien cuando la cámara se mueve).
            bgWidth = Mathf.Max(bgWidth, tSize.x * 3f);
            bgHeight = Mathf.Max(bgHeight, tSize.y * 2.5f);

            if (debugLog)
                Debug.Log($"[BackgroundManager] Auto-posicionado sobre terreno: " +
                          $"centro=({p.x:F1}, {p.y:F1}), bgWidth={bgWidth:F1}, bgHeight={bgHeight:F1}");
        }

        private void Build()
        {
            // FORZAMOS Z SEGURA: si el GameObject Background estaba puesto en una
            // Z rara (p.ej. -20 porque copiaste de otro sitio), sus hijos heredarían
            // esa Z y quedarían detrás del near clipping plane de la cámara → invisible.
            Vector3 p = transform.position;
            p.z = forcedZ;
            transform.position = p;

            // LIMPIEZA DEFENSIVA: destruimos inmediatamente CUALQUIER hijo con
            // nombres conocidos. Así aunque Build se llame varias veces por
            // razones que se nos escapen (BiomeSyncer, hot-reload…), nunca
            // quedan hijos duplicados que causen líneas fantasma al superponerse.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name == "Sky" || child.name == "Clouds"
                    || child.name == "SilhouetteFar" || child.name == "SilhouetteNear")
                {
                    DestroyImmediate(child);
                }
            }
            skyGO = null; cloudsGO = null;
            silhouetteFarGO = null; silhouetteNearGO = null;

            // Ancla del parallax: si tenemos el centro del terreno guardado, lo
            // usamos (es estable). Si no, usamos la posición actual (puede ser
            // incorrecta si LateUpdate ya movió el manager).
            anchorWorldPos = hasTerrainAnchor
                ? new Vector3(terrainCenterAnchor.x, terrainCenterAnchor.y, forcedZ)
                : transform.position;

            BuildSky();
            BuildSilhouetteFar();
            BuildSilhouetteNear();
            if (biome.hasClouds) BuildClouds();
        }

        /// <summary>
        ///  Destruye todas las capas y reconstruye desde cero con el bioma
        ///  ACTUALMENTE activo. Llamado por BiomeSyncer al sincronizar el
        ///  bioma elegido por el host en el menú.
        /// </summary>
        public void RebuildVisuals()
        {
            var bm = BiomeManager.Instance;
            biome = (bm != null && bm.Current != null) ? bm.Current : new BiomeDefinition();

            // Limpieza AGRESIVA: destruimos inmediatamente cualquier hijo con
            // los nombres conocidos (Sky, Clouds, SilhouetteFar, SilhouetteNear).
            // Usamos DestroyImmediate porque Destroy() es asíncrono y si construimos
            // nuevos en el mismo frame quedan superpuestos → efecto "dos fondos".
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name == "Sky" || child.name == "Clouds"
                    || child.name == "SilhouetteFar" || child.name == "SilhouetteNear")
                {
                    DestroyImmediate(child);
                }
            }
            skyGO = null; cloudsGO = null;
            silhouetteFarGO = null; silhouetteNearGO = null;

            Build();
        }

        // ==============================================================
        //  CIELO — degradado vertical
        // ==============================================================
        private void BuildSky()
        {
            // Textura MÁS ANCHA (32 columnas iguales) para reducir el factor de
            // escala. Con solo 2 columnas y scale = 6400, Unity acumula errores
            // de precisión float que aparecen como una línea fina en el degradado.
            const int SKY_W = 32;
            var tex = new Texture2D(SKY_W, 128, TextureFormat.RGBA32, false);
            // Point (sin interpolación) elimina líneas fantasma de bordes al
            // escalar. El degradado se hace en el for de abajo por píxeles.
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[SKY_W * 128];
            for (int y = 0; y < 128; y++)
            {
                float t = y / 127f;
                Color c = Color.Lerp(biome.skyBottom, biome.skyTop, t);
                for (int x = 0; x < SKY_W; x++)
                    pixels[y * SKY_W + x] = c;
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, SKY_W, 128), new Vector2(0.5f, 0.5f), 32f,
                                        0, SpriteMeshType.FullRect);

            skyGO = new GameObject("Sky");
            skyGO.transform.SetParent(transform, worldPositionStays: false);
            var sr = skyGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // IMPORTANTE: NO usamos Sliced/Tiled aquí porque requieren sprite
            // con bordes o size específico. Usamos Simple + localScale, que
            // funciona SIEMPRE con sprites creados por Sprite.Create.
            sr.drawMode = SpriteDrawMode.Simple;
            sr.sortingOrder = skySortingOrder;
            sr.sortingLayerName = "Default";
            // El sprite mide 32×128 px con PPU=32 → 1×4 unidades. Escalamos
            // por transform para llegar a bgWidth × bgHeight de mundo.
            skyGO.transform.localScale = new Vector3(bgWidth / (32f / 32f), bgHeight / (128f / 32f), 1f);
            skyGO.transform.localPosition = Vector3.zero;

            if (debugLog)
                Debug.Log($"[BackgroundManager] Sky creado. scale={skyGO.transform.localScale}, sortingOrder={sr.sortingOrder}, worldPos={skyGO.transform.position}");
        }

        // ==============================================================
        //  SILUETAS PARALLAX
        // ==============================================================
        private void BuildSilhouetteFar()
        {
            // Silueta LEJANA: amplitud/frecuencia originales (buenas). Aumento
            // SOLO la altura del sprite (128→260 → +132 px = +4.125 unidades)
            // y compenso el yOffset con esa cantidad DIVIDIDA POR halfCam
            // para que la CIMA quede en el mismo sitio y solo se estire la base.
            float halfCam = GetCameraHalfHeight();
            float extraUnitsFar = (260 - 128) * 0.5f / 32f;   // ppu = 32
            silhouetteFarGO = BuildSilhouetteLayer(
                "SilhouetteFar",
                biome.silhouetteFar,
                width: 1024, height: 260,
                amplitude: 32f,
                frequency: 0.008f,
                seed: 12345,
                yOffset: halfCam * 0.35f + extraUnitsFar,
                sortingOrder: silhouetteFarOrder);
        }

        private void BuildSilhouetteNear()
        {
            // Silueta CERCANA: mismos parámetros originales, altura mayor
            // (180→340 → +160 px = +5 unidades), yOffset compensado para
            // que la cima quede FIJA y solo la base se estire hacia abajo.
            float halfCam = GetCameraHalfHeight();
            float extraUnitsNear = (340 - 180) * 0.5f / 32f;
            silhouetteNearGO = BuildSilhouetteLayer(
                "SilhouetteNear",
                biome.silhouetteNear,
                width: 1024, height: 340,
                amplitude: 55f,
                frequency: 0.012f,
                seed: 67890,
                yOffset: halfCam * 0.15f + extraUnitsNear,
                sortingOrder: silhouetteNearOrder);
        }

        /// <summary>
        ///  Devuelve la mitad de la altura visible en la cámara en unidades
        ///  de mundo. Sirve para colocar las capas del fondo en el "horizonte"
        ///  o cerca del borde superior sin importar el zoom.
        /// </summary>
        private float GetCameraHalfHeight()
        {
            if (cachedCamera == null) cachedCamera = Camera.main;
            if (cachedCamera != null && cachedCamera.orthographic)
                return cachedCamera.orthographicSize;
            return 10f;   // fallback razonable
        }

        private GameObject BuildSilhouetteLayer(string name, Color color, int width, int height,
                                                 float amplitude, float frequency, int seed,
                                                 float yOffset, int sortingOrder)
        {
            var tex = biome.parallaxStyle == ParallaxStyle.Buildings
                ? GenerateBuildingSilhouette(width, height, color, seed)
                : GenerateHillSilhouette(width, height, color, amplitude, frequency, seed);

            var sprite = Sprite.Create(tex, new Rect(0, 0, width, height),
                                       new Vector2(0.5f, 0.5f), 32f,
                                       0, SpriteMeshType.FullRect);

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(bgWidth, height / 32f);
            sr.sortingOrder = sortingOrder;
            sr.sortingLayerName = "Default";
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            if (debugLog)
                Debug.Log($"[BackgroundManager] {name} creado. size={sr.size}, sortingOrder={sortingOrder}, worldPos={go.transform.position}");
            return go;
        }

        /// <summary>
        ///  Silueta de colinas suaves — perfil sinusoidal + perlin. Debajo
        ///  del perfil el color, encima transparente.
        /// </summary>
        private Texture2D GenerateHillSilhouette(int w, int h, Color color, float amp, float freq, int seed)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[w * h];
            Color32 transparent = new Color32(0, 0, 0, 0);

            System.Random rng = new System.Random(seed);
            float o1 = (float)(rng.NextDouble() * 1000.0);
            float o2 = (float)(rng.NextDouble() * 1000.0);
            float o3 = (float)(rng.NextDouble() * 1000.0);

            // Pre-calculamos el perfil de las columnas — MÁS ORGÁNICO:
            // combinamos tres octavas de Perlin de distintas frecuencias
            // + ridge noise (picos afilados) para que unas montañas sean
            // suaves y otras puntiagudas.
            // Usamos fold-blend en X para que la textura sea TILEABLE.
            float[] surface = new float[w];
            for (int x = 0; x < w; x++)
            {
                surface[x] = SampleHillProfile(x, w, freq, amp, o1, o2, o3);
            }

            // Cálculo del rango real para el gradiente vertical
            float minY = h, maxY = 0f;
            for (int i = 0; i < w; i++)
            {
                if (surface[i] < minY) minY = surface[i];
                if (surface[i] > maxY) maxY = surface[i];
            }

            // Sin gradiente vertical marcado — antes la cima quedaba más clara
            // y parecía un hueco luminoso entre las siluetas. Ahora solo un
            // sombreado MUY sutil hacia abajo (color base uniforme + pequeñas
            // variaciones internas para no ser un color plano).
            Color baseCol = color;
            Color darkerCol = DarkenColor(color, 0.12f);

            for (int x = 0; x < w; x++)
            {
                float surfaceY = surface[x];
                for (int y = 0; y < h; y++)
                {
                    if (y < surfaceY)
                    {
                        // Textura interna con perlin para que no sea plano,
                        // pero el color base es igual arriba y abajo → ninguna
                        // "franja clara" que parezca un hueco luminoso.
                        float noise = Mathf.PerlinNoise(x * 0.04f + o1, y * 0.05f + o2);
                        Color c = Color.Lerp(baseCol, darkerCol, noise * 0.5f);
                        c.a = color.a;
                        pixels[y * w + x] = c;
                    }
                    else
                    {
                        pixels[y * w + x] = transparent;
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        ///  Muestra un perfil de colina en la columna x. Combina tres octavas
        ///  de Perlin + ridge noise (picos) + fold-blend para ser tileable.
        /// </summary>
        private static float SampleHillProfile(int x, int w, float freq, float amp,
                                               float o1, float o2, float o3)
        {
            // Fold-blend: Sample(x) y Sample(x-w) mezclados con peso x/w
            // → columna 0 y columna w tienen mismo valor (tileable).
            float weight = x / (float)w;
            float a = SamplePerlinOctaves(x, freq, amp, o1, o2, o3);
            float b = SamplePerlinOctaves(x - w, freq, amp, o1, o2, o3);
            return Mathf.Lerp(a, b, weight);
        }

        private static float SamplePerlinOctaves(int x, float freq, float amp,
                                                 float o1, float o2, float o3)
        {
            // Octava 1: montañas grandes (baja frecuencia, gran amplitud)
            float n1 = (Mathf.PerlinNoise(x * freq * 1.0f + o1, 0.3f) - 0.5f) * amp * 1.4f;
            // Octava 2: colinas medianas
            float n2 = (Mathf.PerlinNoise(x * freq * 3.0f + o2, 1.7f) - 0.5f) * amp * 0.6f;
            // Octava 3: rugosidad fina (picos y valles pequeños)
            float n3 = (Mathf.PerlinNoise(x * freq * 8.0f + o3, 5.1f) - 0.5f) * amp * 0.2f;

            // Ridge noise para picos ocasionales — fórmula 1 - |perlin*2 - 1|
            // da un patrón de crestas afiladas. Multiplicamos por poco para no
            // exagerar (algunas montañas son picudas, otras suaves).
            float rn = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq * 1.5f + o1 + 100f, 3.0f) * 2f - 1f);
            float peaks = rn * amp * 0.45f;

            // Altura base — restamos un poco para que el "suelo" de la silueta
            // esté algo por debajo del centro.
            return 60f + n1 + n2 + n3 + peaks;
        }

        private static Color LightenColor(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        private static Color DarkenColor(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r - amount),
                Mathf.Clamp01(c.g - amount),
                Mathf.Clamp01(c.b - amount),
                c.a);
        }

        /// <summary>
        ///  Silueta de edificios rectangulares con alturas y anchos variables
        ///  — estética urbana / rascacielos. Perfecto para el bioma Sewer.
        ///  Añade también ventanitas iluminadas si el bioma tiene neón.
        /// </summary>
        private Texture2D GenerateBuildingSilhouette(int w, int h, Color color, int seed)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[w * h];
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 fill = color;
            Color32 neon = biome.neonColor;

            // Rellenar todo con transparente primero
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;

            System.Random rng = new System.Random(seed);

            // Generar edificios de anchos y alturas variables
            int x = 0;
            while (x < w)
            {
                int buildingWidth = 40 + rng.Next(60);   // 40-100 px
                int buildingHeight = h / 2 + rng.Next(h / 2); // media a total

                int x0 = x;
                int x1 = Mathf.Min(x + buildingWidth, w);

                // Silueta del edificio
                for (int px = x0; px < x1; px++)
                    for (int py = 0; py < buildingHeight; py++)
                        pixels[py * w + px] = fill;

                // Ventanitas iluminadas (solo si el bioma tiene neón)
                if (biome.hasNeonLights)
                {
                    int windowSpacingX = 8;
                    int windowSpacingY = 12;
                    for (int wy = 10; wy < buildingHeight - 6; wy += windowSpacingY)
                    {
                        for (int wx = x0 + 4; wx < x1 - 4; wx += windowSpacingX)
                        {
                            // Solo el 30% de las ventanas encendidas
                            if (rng.Next(100) < 30)
                            {
                                for (int dx = 0; dx < 3; dx++)
                                    for (int dy = 0; dy < 4; dy++)
                                    {
                                        int px = wx + dx;
                                        int py = wy + dy;
                                        if (px < x1 && py < buildingHeight)
                                            pixels[py * w + px] = neon;
                                    }
                            }
                        }
                    }
                }

                x = x1 + rng.Next(4);   // pequeño gap ocasional entre edificios
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ==============================================================
        //  NUBES — textura procedural + scroll horizontal
        // ==============================================================
        private void BuildClouds()
        {
            var tex = GenerateCloudsTexture(1024, 256);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1024, 256),
                                       new Vector2(0.5f, 0.5f), 32f,
                                       0, SpriteMeshType.FullRect);

            cloudsGO = new GameObject("Clouds");
            cloudsGO.transform.SetParent(transform, worldPositionStays: false);
            var sr = cloudsGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            // Franja de nubes más ALTA (12u) para que ocupen bien el cielo
            // y desde donde camina la rata se vean grandes en el horizonte.
            sr.size = new Vector2(bgWidth, 12f);
            sr.sortingOrder = cloudsOrder;
            sr.sortingLayerName = "Default";
            // Nubes en el CIELO MUY ALTO — deben llenar el cielo bien arriba
            // para que no se vea espacio vacío entre nubes y montañas.
            float halfCam = GetCameraHalfHeight();
            cloudsGO.transform.localPosition = new Vector3(0f, halfCam * 0.95f, 0f);

            if (debugLog)
                Debug.Log($"[BackgroundManager] Clouds creadas. size={sr.size}, sortingOrder={cloudsOrder}, worldPos={cloudsGO.transform.position}");
        }

        private Texture2D GenerateCloudsTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            // Point (no interpolación) evita que Unity pinte líneas fantasma
            // al muestrear entre el último píxel de un tile y el primero del
            // siguiente. Con Bilinear, esa interpolación crea una franja visible.
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[w * h];
            Color32 cloud = biome.cloudColor;
            Color32 transparent = new Color32(cloud.r, cloud.g, cloud.b, 0);

            // Umbral MÁS ALTO → más cielo despejado y nubes con cuerpo definido
            // en lugar de niebla continua.
            float threshold = 0.60f - biome.cloudDensity * 0.25f;

            for (int y = 0; y < h; y++)
            {
                // Fade vertical más suave — nubes esponjosas arriba/abajo.
                float t = y / (float)(h - 1);
                float edgeFadeV = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.3f);

                for (int x = 0; x < w; x++)
                {
                    // ===== PERLIN TILEABLE en X (fold-blend) =====
                    // Técnica correcta: N(x) = Lerp(Perlin(x), Perlin(x - w), x/w)
                    //   · En x=0 → 100% Perlin(0)
                    //   · En x=w → 100% Perlin(-w + w) = Perlin(0)  ← MISMO valor
                    //   · En medio: crossfade suave
                    // Resultado: la columna 0 y la columna w tienen exactamente
                    // el mismo valor → sin costura al hacer tiled.
                    float weight = x / (float)w;

                    float n1a = Mathf.PerlinNoise(x * 0.003f,       y * 0.008f);
                    float n1b = Mathf.PerlinNoise((x - w) * 0.003f, y * 0.008f);
                    float n1 = Mathf.Lerp(n1a, n1b, weight);

                    float n2a = Mathf.PerlinNoise(x * 0.010f,       y * 0.020f);
                    float n2b = Mathf.PerlinNoise((x - w) * 0.010f, y * 0.020f);
                    float n2 = Mathf.Lerp(n2a, n2b, weight);

                    float n = (n1 + n2 * 0.4f) / 1.4f;

                    float density = (n - threshold) * edgeFadeV;
                    if (density > 0f)
                    {
                        // Curva del alpha más agresiva → nubes con centro
                        // opaco y bordes que caen rápido. Definidas, no niebla.
                        byte alpha = (byte)(Mathf.Clamp01(density * 4.5f) * cloud.a);
                        pixels[y * w + x] = new Color32(cloud.r, cloud.g, cloud.b, alpha);
                    }
                    else
                    {
                        pixels[y * w + x] = transparent;
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        ///  Peso para hacer la textura de nubes TILEABLE en horizontal.
        ///  Devuelve 0 en el centro (usa n1) y sube hacia los bordes
        ///  (mezcla con n1_espejo). Así los píxeles del borde izquierdo
        ///  y derecho tienen el mismo valor → sin costura al hacer tiled.
        /// </summary>
        private static float MirrorWeight(float t)
        {
            // t ∈ [0, 1]. Queremos peso alto en 0 y en 1, bajo en 0.5.
            // Curva simétrica tipo (1 - 4·(t-0.5)²)⁻¹ suavizado.
            float dist = Mathf.Abs(t - 0.5f) * 2f;   // 0 en centro, 1 en bordes
            return Mathf.Pow(dist, 3f);              // curva agresiva: solo cerca de los bordes mezcla
        }

        // ==============================================================
        //  UPDATE — scroll de nubes
        // ==============================================================
        private void Update()
        {
            if (cloudsGO == null) return;

            cloudScrollX -= cloudSpeed * Time.deltaTime;
            // Wrap para que no crezca infinito (Tiled ya se encarga del loop visual)
            if (cloudScrollX < -1000f) cloudScrollX += 1000f;

            // NOTA: el offset X de las nubes se aplica DENTRO de LateUpdate,
            // combinado con el parallax de cámara.
        }

        // ==============================================================
        //  LATE UPDATE — parallax siguiendo a la cámara
        //  Se hace en LateUpdate para que corra DESPUÉS de que CameraFollow
        //  haya movido la cámara este frame. Si no, quedaría un frame de
        //  retraso visible.
        // ==============================================================
        private void LateUpdate()
        {
            if (!followCamera) return;
            if (cachedCamera == null) cachedCamera = Camera.main;
            if (cachedCamera == null) return;

            // El BackgroundManager (raíz) sigue la cámara al 100% en XY, con Z FORZADA
            // al valor seguro (por defecto 0). Nunca hereda la Z de la cámara (típico -10)
            // ni la Z inicial del GameObject (podría estar mal puesta en la escena).
            Vector3 camPos = cachedCamera.transform.position;
            transform.position = new Vector3(camPos.x, camPos.y, forcedZ);

            // Offset del ancla respecto a la cámara — es la "distancia perdida"
            // por seguir a la cámara. Cada capa la contrarresta según su factor
            // de parallax para simular profundidad.
            Vector2 delta = new Vector2(camPos.x - anchorWorldPos.x, camPos.y - anchorWorldPos.y);

            float halfCam = GetCameraHalfHeight();
            float extraFar  = (260 - 128) * 0.5f / 32f;
            float extraNear = (340 - 180) * 0.5f / 32f;
            ApplyParallaxLayer(skyGO,            delta, skyParallax,           yBase: 0f);
            ApplyParallaxLayer(silhouetteFarGO,  delta, silhouetteFarParallax,  yBase: halfCam * 0.35f + extraFar);
            ApplyParallaxLayer(silhouetteNearGO, delta, silhouetteNearParallax, yBase: halfCam * 0.15f + extraNear);
            ApplyParallaxLayer(cloudsGO,         delta, cloudsParallax,         yBase: halfCam * 0.95f,
                                                                                 extraX: cloudScrollX);
        }

        /// <summary>
        ///  Contrarresta el follow de cámara según el factor de parallax de la capa.
        ///  factor=0 → capa totalmente estática respecto al mundo (no se mueve visualmente).
        ///  factor=1 → capa se mueve exactamente igual que la cámara (no da parallax).
        ///  Entre medias → efecto de profundidad.
        /// </summary>
        private void ApplyParallaxLayer(GameObject layer, Vector2 delta, float factor,
                                        float yBase, float extraX = 0f)
        {
            if (layer == null) return;
            // localPosition = -delta * (1 - factor) para "cancelar" parte del movimiento
            // de la cámara. Cuanto menor el factor, más se cancela → parece más lejos.
            float x = -delta.x * (1f - factor) + extraX;
            float y = -delta.y * (1f - factor) + yBase;
            layer.transform.localPosition = new Vector3(x, y, 0f);
        }
    }
}
