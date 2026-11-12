using UnityEngine;
using Unity.Netcode;
using Ratms.Networking;

namespace Ratms.World
{
    /// <summary>
    ///  ZONA DE AGUA — muerte al caer al vacío estilo Worms.
    ///
    ///  Qué hace:
    ///   · Pone una "altura del agua" Y en el mundo. Cualquier rata cuyo
    ///     transform.position.y baje por debajo de esa altura recibe 9999
    ///     puntos de daño y muere al instante.
    ///   · Pinta un sprite horizontal azul translúcido en esa altura para
    ///     que el jugador vea claramente "aquí muere si caigo".
    ///   · Anima el agua con un sube/baja muy suave para que tenga vida.
    ///
    ///  Sólo el SERVIDOR aplica daño (autoritativo). Los clientes ven el agua
    ///  animada para feedback visual; cuando una rata muere, NetworkHealth
    ///  ya se encarga de propagar la animación de muerte por la red.
    ///
    ///  Cómo configurarlo en el escenario:
    ///   1) Crea un GameObject vacío llamado "Water".
    ///   2) Añádele este componente (WaterDeathZone).
    ///   3) Pon su Y en el mundo a la altura que quieras (p. ej. -2 si tu
    ///      terreno está sobre Y=0 — así "fuera del mapa" = caída por debajo
    ///      del terreno = muerte).
    ///   4) (Opcional) Asigna un sprite cuadrado a Water Sprite. Si no lo
    ///      asignas, generamos uno blanco programáticamente.
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterDeathZone : MonoBehaviour
    {
        [Header("Auto-posicionado desde el terreno")]
        [Tooltip("Si ON, el agua se coloca AUTOMÁTICAMENTE debajo del DestructibleTerrain " +
                 "de la escena. Ignora waterY/waterWidth y usa el tamaño real del mapa " +
                 "generado. Ideal cuando el terreno es procedural y cambia cada partida.")]
        [SerializeField] private bool autoPositionFromTerrain = true;

        [Tooltip("Offset del agua respecto al borde inferior del terreno. " +
                 "Positivo = agua MÁS ABAJO (queda hueco). " +
                 "Negativo = agua se SOLAPA con el terreno (recomendado, así siempre se tocan). " +
                 "0 = agua justo pegada al borde inferior.")]
        [SerializeField] private float autoMarginBelow = -1f;

        [Tooltip("Cuántas unidades EXTRA a cada lado del terreno cubre el agua. " +
                 "Sube esto si notas que se ve dónde termina el mar por los laterales.")]
        [SerializeField] private float autoMarginSides = 20f;

        [Header("Lógica de muerte (manual)")]
        [Tooltip("Altura del agua en unidades de mundo. Solo se usa si autoPositionFromTerrain está OFF. " +
                 "Cualquier rata cuya Y sea menor que esto, muere.")]
        [SerializeField] private float waterY = -2f;

        [Tooltip("Cada cuántos segundos comprobamos qué ratas han caído. " +
                 "0.1 = 10 chequeos por segundo, suficiente para sentirse instantáneo " +
                 "sin sobrecargar el servidor.")]
        [SerializeField] private float checkInterval = 0.1f;

        [Header("Visual del agua")]
        [Tooltip("Si ON, pintamos una franja azul translúcida en waterY. Quítalo si " +
                 "ya tienes un visual de agua propio en la escena.")]
        [SerializeField] private bool drawWaterVisual = true;

        [Tooltip("Color del agua. Por defecto verde-turquesa oscuro para distinguirse del cielo azul.")]
        [SerializeField] private Color waterColor = new Color(0.08f, 0.42f, 0.48f, 0.95f);

        [Tooltip("Color secundario en la parte más profunda del agua. Da sensación de profundidad.")]
        [SerializeField] private Color waterDeepColor = new Color(0.02f, 0.15f, 0.25f, 1f);

        [Tooltip("Color de la ESPUMA principal — la línea blanca donde empieza la muerte.")]
        [SerializeField] private Color foamColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("Color de las crestas secundarias — un poco más oscuras que la espuma principal.")]
        [SerializeField] private Color foamAccentColor = new Color(0.75f, 0.9f, 1f, 0.9f);

        [Tooltip("Alto en unidades de la banda de espuma principal. 0 = sin espuma.")]
        [SerializeField] private float foamThickness = 0.18f;

        [Tooltip("Si ON, IGNORA el color serializado en la escena y aplica el default del script. " +
                 "Útil si ya tenías un GameObject Water con colores viejos y no quieres editarlos a mano.")]
        [SerializeField] private bool forceDefaultColors = true;

        [Tooltip("Anchura del agua en unidades de mundo. Conviene que cubra todo el ancho del mapa.")]
        [SerializeField] private float waterWidth = 200f;

        [Tooltip("Profundidad visual del agua (alto del rectángulo desde waterY hacia abajo).")]
        [SerializeField] private float waterDepth = 50f;

        [Tooltip("Amplitud del bamboleo del agua en unidades de mundo (sube/baja).")]
        [SerializeField] private float waveAmplitude = 0.08f;

        [Tooltip("Velocidad del bamboleo del agua en ciclos por segundo.")]
        [SerializeField] private float waveFrequency = 0.5f;

        [Header("Debug")]
        [Tooltip("Muestra un Debug.Log cuando una rata muere por agua.")]
        [SerializeField] private bool debugLog = true;

        // Estado interno
        private float nextCheckTime;
        private SpriteRenderer waterRenderer;
        private GameObject waterVisual;
        private GameObject waterDeepVisual;
        private GameObject foamVisual;
        private float visualBaseY;

        private void Awake()
        {
            // Si el usuario tiene una instancia vieja del componente con colores
            // serializados de otra versión (turquesa flojo, etc.), forzamos los
            // defaults sin que tenga que borrar y recrear el GameObject.
            if (forceDefaultColors)
            {
                waterColor      = new Color(0.08f, 0.42f, 0.48f, 0.95f);
                waterDeepColor  = new Color(0.02f, 0.15f, 0.25f, 1f);
                foamColor       = new Color(1f, 1f, 1f, 1f);
                foamAccentColor = new Color(0.75f, 0.9f, 1f, 0.9f);
            }

            // Si hay bioma activo, sus colores mandan (pisan los del inspector
            // y los forceDefault). Así pradera → agua turquesa, alcantarilla →
            // agua verde turbia, etc. — un solo componente para todos los biomas.
            var bm = BiomeManager.Instance;
            if (bm != null && bm.Current != null)
            {
                waterColor      = bm.Current.waterColor;
                waterDeepColor  = bm.Current.waterDeepColor;
                foamColor       = bm.Current.foamColor;
                foamAccentColor = bm.Current.foamAccentColor;
            }

            // El visual lo construimos aquí, pero con posición temporal.
            // El auto-posicionado va en Start para que TerrainGenerator ya
            // haya corrido su Awake/Start y el tamaño real del mapa esté fijado.
            if (drawWaterVisual) BuildWaterVisual();
        }

        private void Start()
        {
            if (autoPositionFromTerrain)
                StartCoroutine(AutoPositionAfterOneFrame());
        }

        /// <summary>
        ///  Destruye todas las capas visuales del agua y las reconstruye desde
        ///  cero, releyendo los colores del bioma activo. Se llama desde
        ///  <see cref="BiomeSyncer"/> cuando el servidor publica un cambio de
        ///  bioma en runtime (típicamente al arrancar la escena).
        /// </summary>
        public void RebuildVisuals()
        {
            // Actualizamos los colores desde el bioma activo antes de reconstruir
            var bm = BiomeManager.Instance;
            if (bm != null && bm.Current != null)
            {
                waterColor      = bm.Current.waterColor;
                waterDeepColor  = bm.Current.waterDeepColor;
                foamColor       = bm.Current.foamColor;
                foamAccentColor = bm.Current.foamAccentColor;
            }

            if (waterDeepVisual != null) Destroy(waterDeepVisual);
            if (waterVisual     != null) Destroy(waterVisual);
            if (foamVisual      != null) Destroy(foamVisual);
            waterDeepVisual = null;
            waterVisual = null;
            foamVisual = null;

            if (drawWaterVisual) BuildWaterVisual();
        }

        /// <summary>
        ///  Esperamos un frame para asegurar que TerrainGenerator ya ha llamado
        ///  a Generate() en su propio Start(), y por tanto el tamaño real del
        ///  terreno ya está fijado (no el valor del prefab).
        /// </summary>
        private System.Collections.IEnumerator AutoPositionAfterOneFrame()
        {
            yield return null;   // esperar 1 frame
            TryAutoPositionFromTerrain();
            RepositionAllVisualLayers();
        }

        /// <summary>
        ///  Recoloca todas las capas del agua (fondo profundo, cuerpo, espuma y
        ///  crestas) según los waterY/waterWidth actuales. Se llama tras el
        ///  auto-posicionado y podría reusarse si el terreno se regenera.
        /// </summary>
        private void RepositionAllVisualLayers()
        {
            if (!drawWaterVisual) return;
            visualBaseY = waterY;
            float bodyDepth = Mathf.Min(waterDepth, 8f);

            if (waterDeepVisual != null)
            {
                waterDeepVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
                waterDeepVisual.transform.localScale = new Vector3(waterWidth, waterDepth, 1f);
            }
            if (waterVisual != null)
            {
                waterVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
                if (waterRenderer != null && waterRenderer.drawMode != SpriteDrawMode.Simple)
                    waterRenderer.size = new Vector2(waterWidth, bodyDepth);
                else
                    waterVisual.transform.localScale = new Vector3(waterWidth, bodyDepth, 1f);
            }
            if (foamVisual != null)
            {
                foamVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
                var sr = foamVisual.GetComponent<SpriteRenderer>();
                if (sr != null && sr.drawMode != SpriteDrawMode.Simple)
                    sr.size = new Vector2(waterWidth, sr.size.y);
                else
                    foamVisual.transform.localScale = new Vector3(waterWidth, foamThickness, 1f);
            }
        }

        /// <summary>
        ///  Busca el DestructibleTerrain en la escena y ajusta waterY, waterWidth
        ///  y la X del propio GameObject para que el agua caiga automáticamente
        ///  debajo del mapa. Así funciona con terrenos procedurales (semilla
        ///  distinta cada partida) sin tener que tocar nada a mano.
        ///
        ///  Cómo lo calcula:
        ///   · El terreno vive en su transform con dimensiones WorldSize
        ///     (ancho × alto en unidades de mundo).
        ///   · El borde INFERIOR del terreno está en terrainTransform.y (el
        ///     terreno se dibuja desde y=0 local hacia arriba).
        ///   · La línea letal se pone autoMarginBelow unidades por debajo de
        ///     ese borde — así hay un pequeño "hueco" antes de morir.
        ///   · El agua abarca todo el ancho del mapa + autoMarginSides a cada
        ///     lado para que no se vea el borde.
        /// </summary>
        private void TryAutoPositionFromTerrain()
        {
            var terrain = Object.FindFirstObjectByType<DestructibleTerrain>();
            if (terrain == null)
            {
                Debug.LogWarning("[WaterDeathZone] autoPositionFromTerrain=ON pero no encuentro " +
                                 "ningún DestructibleTerrain en la escena. Usando valores manuales.");
                return;
            }

            Vector2 terrainSize = terrain.WorldSize;
            Vector3 terrainPos = terrain.transform.position;

            // Borde inferior del terreno en Y de mundo
            float terrainBottomY = terrainPos.y;
            waterY = terrainBottomY - autoMarginBelow;

            // Ancho: cubre todo el mapa + margen extra a los lados
            waterWidth = terrainSize.x + autoMarginSides * 2f;

            // X del GameObject: centro del terreno horizontalmente
            Vector3 selfPos = transform.position;
            selfPos.x = terrainPos.x + terrainSize.x * 0.5f;
            selfPos.y = waterY;   // opcional, para que se vea bien en el editor
            transform.position = selfPos;

            Debug.Log($"[WaterDeathZone] Auto-posicionado bajo el terreno: " +
                      $"waterY={waterY:F2}, waterWidth={waterWidth:F2}, x={selfPos.x:F2}");
        }

        /// <summary>
        ///  Construye el agua con TRES capas usando texturas procedurales que
        ///  encajan con la estética Worms/pixel-art del terreno:
        ///
        ///    1) WaterDeep — plano oscuro que cubre todo el fondo (evita ver
        ///       el cielo si la cámara baja).
        ///    2) WaterBody — cuerpo con textura de GRANO (Perlin 2D mezclando
        ///       dos tonos de turquesa, igual que la tierra mezcla dos marrones).
        ///    3) WaterSurface — franja con OLAS onduladas orgánicas (perfil
        ///       sinusoidal + perlin) que se scrollan horizontalmente para dar
        ///       sensación de mar en movimiento.
        ///
        ///  Todas las texturas se generan en runtime — cero assets externos.
        /// </summary>
        private void BuildWaterVisual()
        {
            visualBaseY = waterY;

            // ---- Capa 1: fondo profundo (plano, un solo tono muy oscuro) ----
            var flatTex = new Texture2D(1, 1);
            flatTex.SetPixel(0, 0, Color.white);
            flatTex.Apply();
            var spriteDown = Sprite.Create(flatTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 1f), 1f);

            waterDeepVisual = new GameObject("WaterDeep");
            waterDeepVisual.transform.SetParent(transform, worldPositionStays: false);
            var deepR = waterDeepVisual.AddComponent<SpriteRenderer>();
            deepR.sprite = spriteDown;
            deepR.color = waterDeepColor;
            deepR.sortingOrder = 4;
            waterDeepVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
            waterDeepVisual.transform.localScale = new Vector3(waterWidth, waterDepth, 1f);

            // ---- Capa 2: cuerpo con textura granulada ----
            var bodyTex = GenerateWaterBodyTexture(256, 128);
            var bodyPPU = 32f; // mismo PPU que el terreno para que el grano combine
            var bodySprite = Sprite.Create(
                bodyTex,
                new Rect(0, 0, bodyTex.width, bodyTex.height),
                new Vector2(0.5f, 1f),   // pivot arriba
                bodyPPU,
                0, SpriteMeshType.FullRect);

            waterVisual = new GameObject("WaterBody");
            waterVisual.transform.SetParent(transform, worldPositionStays: false);
            waterRenderer = waterVisual.AddComponent<SpriteRenderer>();
            waterRenderer.sprite = bodySprite;
            waterRenderer.drawMode = SpriteDrawMode.Tiled;   // repite la textura
            waterRenderer.tileMode = SpriteTileMode.Continuous;
            waterRenderer.color = Color.white;               // el color va EN la textura
            waterRenderer.sortingOrder = 5;
            waterVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
            // Tamaño en unidades — no localScale, porque con drawMode=Tiled
            // Unity usa size del SpriteRenderer.
            float bodyDepth = Mathf.Min(waterDepth, 8f);
            waterRenderer.size = new Vector2(waterWidth, bodyDepth);

            // ---- Capa 3: superficie con olas onduladas ----
            var waveTex = GenerateWaveSurfaceTexture(512, 48);
            var wavePPU = 32f;
            var waveSprite = Sprite.Create(
                waveTex,
                new Rect(0, 0, waveTex.width, waveTex.height),
                new Vector2(0.5f, 0.5f),
                wavePPU,
                0, SpriteMeshType.FullRect);

            foamVisual = new GameObject("WaterSurface");
            foamVisual.transform.SetParent(transform, worldPositionStays: false);
            var surfR = foamVisual.AddComponent<SpriteRenderer>();
            surfR.sprite = waveSprite;
            surfR.drawMode = SpriteDrawMode.Tiled;
            surfR.tileMode = SpriteTileMode.Continuous;
            surfR.color = Color.white;
            surfR.sortingOrder = 8;
            // La textura de superficie mide 48 px de alto = 1.5 unidades con PPU=32.
            // El centro se pone en waterY (así la mitad superior queda POR ENCIMA
            // de la línea letal y la mitad inferior por debajo, difuminando la
            // transición con el cuerpo del agua).
            foamVisual.transform.position = new Vector3(transform.position.x, waterY, 0f);
            surfR.size = new Vector2(waterWidth, 48f / wavePPU);
        }

        /// <summary>
        ///  Genera una textura del CUERPO del agua con grano tipo terreno:
        ///  mezcla dos tonos (waterColor y waterDeepColor) por Perlin 2D
        ///  para que la superficie no sea un color plano.
        /// </summary>
        private Texture2D GenerateWaterBodyTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;   // pixel-art crisp
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[w * h];
            const float scale = 0.05f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float n = Mathf.PerlinNoise(x * scale, y * scale);
                    // Un poco de speckle aleatorio para grano fino
                    float speckle = ((x * 92821 + y * 63841) & 15) / 15f * 0.08f;
                    float t = Mathf.Clamp01(n + speckle - 0.04f);
                    Color c = Color.Lerp(waterDeepColor, waterColor, t);
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        ///  Genera la textura de la SUPERFICIE con olas onduladas orgánicas.
        ///  El borde superior de cada columna se calcula como sinusoide + perlin,
        ///  de forma que la silueta del agua no es una línea recta sino un
        ///  perfil de olas variables. La banda superior (justo bajo el perfil)
        ///  es espuma blanca; debajo, degradado hacia el agua.
        /// </summary>
        private Texture2D GenerateWaveSurfaceTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[w * h];
            Color transparent = new Color(0, 0, 0, 0);
            float halfH = h * 0.5f;

            for (int x = 0; x < w; x++)
            {
                // Perfil de la ola en el píxel x — combinamos varias ondas
                // de distinta frecuencia para que no sea una sinusoide monótona.
                float phase = (x / (float)w) * Mathf.PI * 2f;
                float wave1 = Mathf.Sin(phase * 3f) * 4f;
                float wave2 = Mathf.Sin(phase * 7.3f + 1.4f) * 2f;
                float noise = (Mathf.PerlinNoise(x * 0.03f, 0.5f) - 0.5f) * 6f;
                float surfaceY = halfH + wave1 + wave2 + noise;   // en píxeles

                for (int y = 0; y < h; y++)
                {
                    if (y > surfaceY)
                    {
                        // Por encima del perfil de la ola → transparente (cielo).
                        pixels[y * w + x] = transparent;
                    }
                    else
                    {
                        float depth = surfaceY - y;   // píxeles desde la superficie
                        Color c;
                        if (depth < 1.5f)
                        {
                            // Cresta / espuma blanca en el borde exacto de la ola
                            c = foamColor;
                        }
                        else if (depth < 5f)
                        {
                            // Zona clara justo bajo la cresta
                            float t = (depth - 1.5f) / 3.5f;
                            c = Color.Lerp(foamAccentColor, waterColor, t);
                        }
                        else
                        {
                            // Cuerpo del agua con grano fino
                            float perlin = Mathf.PerlinNoise(x * 0.06f, y * 0.06f);
                            c = Color.Lerp(waterColor, waterDeepColor, perlin * 0.4f);
                        }
                        pixels[y * w + x] = c;
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private void Update()
        {
            if (!drawWaterVisual) return;

            float t = Time.time * waveFrequency * 2f * Mathf.PI;
            float wobbleMain = Mathf.Sin(t) * waveAmplitude;

            // El cuerpo sube/baja suave
            if (waterVisual != null)
                waterVisual.transform.position = new Vector3(transform.position.x, visualBaseY + wobbleMain, 0f);

            // La superficie con olas ADEMÁS se scroll horizontalmente lento
            // para dar sensación de corriente. El bamboleo vertical va desfasado
            // 90° para que no coincida en fase con el cuerpo (parece agua real).
            if (foamVisual != null)
            {
                float scrollX = (Time.time * 0.6f) % waterWidth;
                float wobbleSurface = Mathf.Sin(t + Mathf.PI * 0.5f) * waveAmplitude * 0.8f;
                foamVisual.transform.position = new Vector3(
                    transform.position.x + scrollX * 0.15f - 5f,
                    visualBaseY + wobbleSurface,
                    0f);
            }

            // El fondo profundo NO se mueve — da estabilidad al color base.
        }

        private void FixedUpdate()
        {
            // SOLO el servidor decide muertes. Los clientes solo ven el agua.
            if (!IsServerActive()) return;
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + checkInterval;

            // Buscamos todas las ratas en escena. Como suele haber 2-4 en
            // multijugador 1v1/2v2, FindObjectsByType es perfectamente barato
            // a 10Hz (no es FindObjectsOfType cada frame).
            var rats = Object.FindObjectsByType<NetworkPlayerMovement>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var rat in rats)
            {
                if (rat == null) continue;
                if (rat.transform.position.y >= waterY) continue;

                // Cae al vacío → matar de un golpe.
                var hp = rat.GetComponent<NetworkHealth>();
                if (hp == null) continue;
                if (hp.IsDead.Value) continue;

                // 9999 fuerza la muerte aunque la vida máxima sea menor.
                // Tipo Water → el popup saldrá en azul con texto "AGUA!".
                hp.ApplyDamageServer(9999, rat.transform.position, DamageType.Water);

                if (debugLog)
                {
                    Debug.Log($"[WaterDeathZone] Rata clientId={rat.OwnerClientId} " +
                              $"cayó al agua (Y={rat.transform.position.y:F2} < {waterY:F2}) → muerte instantánea.");
                }
            }
        }

        /// <summary>
        ///  ¿Estamos corriendo como servidor (host o servidor dedicado)?
        ///  Si NetworkManager aún no ha arrancado (escena en edición o
        ///  pre-conexión), devolvemos false — no aplicamos daño.
        /// </summary>
        private static bool IsServerActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsServer || nm.IsHost);
        }

        // ============================================================
        //  GIZMOS — visualización de la línea letal en el editor
        // ============================================================
        private void OnDrawGizmos()
        {
            // Línea horizontal grande en cyan brillante a la altura letal.
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
            Vector3 a = new Vector3(transform.position.x - waterWidth * 0.5f, waterY, 0f);
            Vector3 b = new Vector3(transform.position.x + waterWidth * 0.5f, waterY, 0f);
            Gizmos.DrawLine(a, b);

            // Triangulitos cada cierto trecho para hacerla bien visible
            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.5f);
            float step = 5f;
            for (float x = a.x; x <= b.x; x += step)
            {
                Gizmos.DrawLine(new Vector3(x, waterY, 0f),
                                new Vector3(x, waterY - 0.5f, 0f));
            }
        }
    }
}
