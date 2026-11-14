using System.Collections.Generic;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  TERRENO DESTRUCTIBLE estilo Worms — versión 0.8, Sesión 1.
    ///
    ///  Cómo funciona:
    ///   · Tenemos un <see cref="Texture2D"/> interno donde cada píxel es
    ///     "sólido" (alpha = 1) o "aire" (alpha = 0).
    ///   · El <see cref="SpriteRenderer"/> la muestra tal cual — lo que ves
    ///     es lo que hay.
    ///   · El <see cref="PolygonCollider2D"/> es la FORMA contra la que chocan
    ///     las ratas/proyectiles. Se recalcula desde el alpha de la textura
    ///     aprovechando que Unity, al crear un <see cref="Sprite"/> con
    ///     <see cref="SpriteMeshType.Tight"/>, auto-genera un outline a partir
    ///     de los píxeles. Extraemos ese outline con
    ///     <see cref="Sprite.GetPhysicsShape"/> y lo asignamos al collider.
    ///
    ///  Coordenadas:
    ///   · Pivot del sprite: esquina INFERIOR-IZQUIERDA (0, 0).
    ///     Así la posición del transform ES la esquina inferior-izquierda
    ///     del mapa — mucho más intuitivo para colocar terrenos.
    ///   · Conversión píxel ↔ mundo: divide/multiplica por
    ///     <see cref="pixelsPerUnit"/>.
    ///
    ///  API pública (lo que usarán las explosiones):
    ///   · <see cref="CarveHole(Vector2,float)"/> — carvea este terreno concreto.
    ///   · <see cref="CarveAllAt(Vector2,float)"/> (static) — carvea TODOS los
    ///     terrenos de la escena. Atajo que usarán misil/granada desde cualquier
    ///     sitio sin tener que buscar referencias.
    ///
    ///  Rendimiento (Sesión 1):
    ///   · Guardamos los píxeles en un <see cref="Color32[]"/> cacheado —
    ///     carvear es solo tocar ese array, no la textura (rápido).
    ///   · Al carvear SOLO marcamos "dirty"; en el siguiente LateUpdate
    ///     reconstruimos sprite+collider una única vez. Si 3 misiles explotan
    ///     en el mismo frame, solo regeneramos una vez.
    ///   · Regenerar el <see cref="PolygonCollider2D"/> de un mapa grande
    ///     TIENE un coste. Si el mapa es muy grande habrá que partirlo en
    ///     chunks (Sesión 3 o 4). Para 1024×512 va de sobra.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class DestructibleTerrain : MonoBehaviour
    {
        // ============================================================
        //  CONFIGURACIÓN
        // ============================================================
        [Header("Tamaño")]
        [Tooltip("Ancho de la textura del terreno en píxeles.")]
        [SerializeField] private int textureWidth = 1024;

        [Tooltip("Alto de la textura del terreno en píxeles.")]
        [SerializeField] private int textureHeight = 512;

        [Tooltip("Píxeles por unidad de mundo. Controla el TAMAÑO en el mundo: " +
                 "un terreno de 1024 px con PPU=32 ocupa 32 unidades de ancho. " +
                 "Sube PPU → mapa más pequeño pero más detalle.")]
        [SerializeField] private float pixelsPerUnit = 32f;

        [Header("Generación inicial")]
        [Tooltip("Si ON, al Start construye un terreno de prueba a mano (colinas " +
                 "y cuevas) para que podamos validar que el sistema funciona sin " +
                 "depender aún del generador procedural de la Sesión 2.")]
        [SerializeField] private bool generateTestTerrainOnStart = true;

        [Header("Colores del terreno de prueba")]
        [SerializeField] private Color32 dirtColor  = new Color32(130, 85, 50, 255);
        [SerializeField] private Color32 grassColor = new Color32(80, 160, 70, 255);
        [SerializeField] private int grassBandPx = 6;

        [Header("Debug")]
        [Tooltip("Dibuja el bounding box del terreno en la vista de Escena.")]
        [SerializeField] private bool drawGizmos = true;

        [Header("Collider — sistema")]
        [Tooltip("PixelContours (recomendado): el collider sigue EXACTAMENTE el dibujo, con cuevas, cráteres e islas. " +
                 "LegacySkyline: el sistema antiguo (solo la línea superior suavizada + cuevas cerradas). Déjalo por si hiciera falta volver atrás.")]
        [SerializeField] private TerrainColliderMode colliderMode = TerrainColliderMode.PixelContours;

        [Tooltip("Cuánto puede desviarse el collider del dibujo al simplificarlo (píxeles). 0.8 = casi exacto; más alto = menos vértices.")]
        [SerializeField, Range(0.3f, 3f)] private float contourSimplifyPx = 0.8f;

        [Tooltip("Bucles de contorno con menos puntos que esto (motas sueltas de tierra) no tienen collider.")]
        [SerializeField] private int minContourPoints = 12;

        [Tooltip("Grosor de los bordes físicos (u). Un poco de grosor evita que algo rápido los atraviese.")]
        [SerializeField] private float contourEdgeRadius = 0.015f;

        [Header("Collider — modo (sistema antiguo)")]
        [Tooltip("Si ON, ignora la magia automática de Sprite.Tight de Unity y SIEMPRE construye el collider con nuestro skyline (de abajo a arriba, ignorando islas flotantes). " +
                 "Recomendado quedarse en ON: Sprite.Tight ha demostrado producir colliders inconsistentes en runtime con texturas grandes (a veces shapeCount=0, a veces shapes solapados). " +
                 "Apágalo solo si quieres recuperar el comportamiento por defecto y depurar.")]
        [SerializeField] private bool forceSkylineCollider = true;

        [Tooltip("Si ON, detecta las cuevas internas del terreno (huecos rodeados de tierra) " +
                 "y las añade como AGUJEROS al PolygonCollider2D. Así la rata puede entrar en las cuevas " +
                 "en lugar de chocar contra 'paredes invisibles'.")]
        [SerializeField] private bool detectCavesAsColliderHoles = true;

        [Tooltip("Tamaño mínimo (en píxeles) para considerar un hueco como 'cueva navegable'. " +
                 "Huecos más pequeños se ignoran (no son transitables por una rata). " +
                 "Subir este valor si tu terreno tiene mucho ruido y salen miles de micro-cuevas.")]
        [SerializeField] private int minCavePixels = 300;

        [Tooltip("Si ON, escribe en consola cada vez que reconstruimos el collider qué método usó (Sprite.Tight, skyline o rectángulo) y cuántos vértices quedaron. Útil para entender si las paredes invisibles son culpa nuestra o de Unity.")]
        [SerializeField] private bool debugColliderBuild = true;

        // ============================================================
        //  ESTADO INTERNO
        // ============================================================
        private SpriteRenderer sr;
        private PolygonCollider2D poly;
        private Texture2D tex;
        private Color32[] pixels;    // cache de píxeles para carvear rápido
        private bool dirty;          // si true, reconstruiremos en LateUpdate

        // (0.19) Zona que ha cambiado de SÓLIDO/AIRE desde el último collider:
        // los contornos solo rehacen ese trozo de su rejilla. "Todo" tras
        // cargar un mapa nuevo.
        private bool solidChangedAll = true;
        private bool hasSolidChange;
        private RectInt solidChange;

        /// <summary>
        ///  Se avisa cada vez que el collider del terreno cambia de forma
        ///  (explosión...). Las ratas "aparcadas" se sueltan al oírlo: su
        ///  Rigidbody duerme y Unity no le recalcula los contactos, así que si una
        ///  explosión le quitaba el suelo sin empujarla, se quedaba flotando.
        /// </summary>
        public static event System.Action ShapeChanged;

        private void MarkSolidChanged(int xmin, int ymin, int xmax, int ymax)
        {
            if (!hasSolidChange) { solidChange = new RectInt(xmin, ymin, xmax - xmin, ymax - ymin); hasSolidChange = true; return; }
            int x0 = Mathf.Min(solidChange.xMin, xmin), y0 = Mathf.Min(solidChange.yMin, ymin);
            int x1 = Mathf.Max(solidChange.xMax, xmax), y1 = Mathf.Max(solidChange.yMax, ymax);
            solidChange = new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
        // Máscara de "tierra principal" (la conectada al suelo del mapa).
        // Se reconstruye en cada Rebuild — se usa para que el skyline
        // ignore islas flotantes. true = es parte del cuerpo principal.
        private bool[] mainGroundMask;

        /// <summary>Todos los terrenos activos en la escena — para CarveAllAt.</summary>
        private static readonly List<DestructibleTerrain> s_all = new();

        // Colliders de borde del sistema PixelContours (uno por bucle), en un hijo.
        private GameObject edgesHost;
        private readonly List<EdgeCollider2D> edgePool = new();

        // ============================================================
        //  PÍXELES SÓLIDOS vs DECORACIÓN
        // ============================================================
        /// <summary>
        ///  Alfa "mágico" para píxeles de DECORACIÓN (hierbajos, flores,
        ///  setas, raíces colgando...). Se pintan opacos a la vista (254 ≈ 255)
        ///  pero la física los ignora: la rata los atraviesa. Se borran con las
        ///  explosiones como el resto del terreno.
        /// </summary>
        public const byte DecorAlpha = 254;

        /// <summary>¿Este píxel es terreno SÓLIDO (choca)? Aire y decoración no.</summary>
        public static bool IsSolidPixel(Color32 c) => c.a > 0 && c.a != DecorAlpha;

        /// <summary>¿Este píxel es decoración (visible pero sin física)?</summary>
        public static bool IsDecorPixel(Color32 c) => c.a == DecorAlpha;

        /// <summary>¿Hay tierra SÓLIDA en este punto del mundo (en cualquier terreno de la escena)?</summary>
        public static bool IsSolidAtWorld(Vector2 worldPos)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var t = s_all[i];
                if (t == null || t.pixels == null) continue;
                Vector3 local = t.transform.InverseTransformPoint(worldPos);
                int x = Mathf.FloorToInt(local.x * t.pixelsPerUnit);
                int y = Mathf.FloorToInt(local.y * t.pixelsPerUnit);
                if (x < 0 || y < 0 || x >= t.textureWidth || y >= t.textureHeight) continue;
                if (IsSolidPixel(t.pixels[y * t.textureWidth + x])) return true;
            }
            return false;
        }

        /// <summary>
        ///  Busca el color de la tierra alrededor de un punto (en un anillo de
        ///  radio <paramref name="radius"/>). Lo usan los efectos de explosión
        ///  para que los trozos que salen volando sean del color del suelo.
        /// </summary>
        public static bool TrySampleColorAround(Vector2 worldPos, float radius, out Color32 color)
        {
            for (int k = 0; k < 12; k++)
            {
                float a = -Mathf.PI * 0.5f + (k % 2 == 0 ? 1 : -1) * (k / 2) * (Mathf.PI / 6f);   // empezamos por abajo
                Vector2 p = worldPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                for (int i = 0; i < s_all.Count; i++)
                {
                    var t = s_all[i];
                    if (t == null || t.pixels == null) continue;
                    Vector3 local = t.transform.InverseTransformPoint(p);
                    int x = Mathf.FloorToInt(local.x * t.pixelsPerUnit), y = Mathf.FloorToInt(local.y * t.pixelsPerUnit);
                    if (x < 0 || y < 0 || x >= t.textureWidth || y >= t.textureHeight) continue;
                    var c = t.pixels[y * t.textureWidth + x];
                    if (IsSolidPixel(c)) { color = c; return true; }
                }
            }
            color = default;
            return false;
        }

        /// <summary>
        ///  Si <paramref name="worldPos"/> está dentro de tierra sólida, devuelve
        ///  cuánto hay que SUBIR para llegar al primer hueco libre (hasta
        ///  <paramref name="maxUp"/>). Null si no está dentro o no hay hueco cerca.
        /// </summary>
        public static float? EscapeUpDistance(Vector2 worldPos, float maxUp)
        {
            if (!IsSolidAtWorld(worldPos)) return null;
            float step = 1f / 32f;
            for (float d = step; d <= maxUp; d += step)
                if (!IsSolidAtWorld(worldPos + Vector2.up * d)) return d;
            return null;
        }

        // API read-only
        public int WidthPx => textureWidth;
        public int HeightPx => textureHeight;
        public float PixelsPerUnit => pixelsPerUnit;
        public Vector2 WorldSize => new Vector2(textureWidth / pixelsPerUnit,
                                                textureHeight / pixelsPerUnit);

        // ============================================================
        //  CICLO DE VIDA
        // ============================================================
        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            poly = GetComponent<PolygonCollider2D>();
            s_all.Add(this);
        }

        private void OnDestroy()
        {
            s_all.Remove(this);
            if (tex != null) Destroy(tex);
        }

        private void Start()
        {
            if (generateTestTerrainOnStart)
            {
                BuildTestTerrain();
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            if (dirty)
            {
                Rebuild();
                dirty = false;
                visualDirty = false;
            }
            else if (visualDirty && tex != null)
            {
                // Solo cambió el COLOR (manchas de sangre): subimos la textura,
                // sin recalcular el collider (que es lo caro).
                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                visualDirty = false;
            }
        }

        private bool visualDirty;

        /// <summary>
        ///  Mancha la tierra en un punto del mundo (sangre, quemaduras...).
        ///  Solo cambia el color de píxeles SÓLIDOS que ya existen: no añade
        ///  ni quita terreno, así que no toca la física. Se pinta un pegote
        ///  de <paramref name="radiusPx"/> píxeles mezclado con <paramref name="strength"/>.
        /// </summary>
        public static void StainAtWorld(Vector2 worldPos, Color32 color, int radiusPx = 1, float strength = 0.75f)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var t = s_all[i];
                if (t == null || t.pixels == null) continue;
                Vector3 local = t.transform.InverseTransformPoint(worldPos);
                int cx = Mathf.FloorToInt(local.x * t.pixelsPerUnit), cy = Mathf.FloorToInt(local.y * t.pixelsPerUnit);
                for (int y = cy - radiusPx; y <= cy + radiusPx; y++)
                    for (int x = cx - radiusPx; x <= cx + radiusPx; x++)
                    {
                        if (x < 0 || y < 0 || x >= t.textureWidth || y >= t.textureHeight) continue;
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radiusPx * radiusPx + 1) continue;
                        int idx = y * t.textureWidth + x;
                        var c = t.pixels[idx];
                        if (!IsSolidPixel(c)) continue;
                        t.pixels[idx] = new Color32((byte)Mathf.Lerp(c.r, color.r, strength), (byte)Mathf.Lerp(c.g, color.g, strength),
                                                   (byte)Mathf.Lerp(c.b, color.b, strength), c.a);
                        t.visualDirty = true;
                    }
            }
        }

        // ============================================================
        //  CONSTRUCCIÓN DE LA TEXTURA
        // ============================================================

        /// <summary>
        ///  Inicializa el terreno copiando los píxeles de una textura externa.
        ///  Úsalo cuando tengas el generador procedural — le pasas lo que genere.
        ///  La textura FUENTE debe tener Read/Write Enabled en el Inspector.
        /// </summary>
        public void SetTerrain(Texture2D source)
        {
            if (source == null) { Debug.LogWarning("[DestructibleTerrain] SetTerrain con textura null"); return; }

            textureWidth = source.width;
            textureHeight = source.height;

            EnsureTexture();
            pixels = source.GetPixels32();
            solidChangedAll = true;
            dirty = true;
        }

        /// <summary>
        ///  Recibe un array de píxeles ya generado desde fuera (normalmente
        ///  <see cref="TerrainGenerator"/>) y lo vuelca directamente al terreno.
        ///  Evita el paso intermedio de crear un Texture2D temporal — va
        ///  del Color32[] del generador al Color32[] interno de un tirón.
        ///  Reconstruye sprite+collider al instante.
        /// </summary>
        public void ApplyPixels(Color32[] srcPixels, int srcWidth, int srcHeight)
        {
            if (srcPixels == null)
            {
                Debug.LogWarning("[DestructibleTerrain] ApplyPixels: array null");
                return;
            }
            if (srcPixels.Length != srcWidth * srcHeight)
            {
                Debug.LogWarning($"[DestructibleTerrain] ApplyPixels: longitud {srcPixels.Length} " +
                                 $"no coincide con {srcWidth}x{srcHeight}={srcWidth*srcHeight}");
                return;
            }

            textureWidth = srcWidth;
            textureHeight = srcHeight;
            EnsureTexture();

            // Copia segura — no nos quedamos con la referencia externa por si
            // el generador la reutiliza para otra cosa.
            System.Array.Copy(srcPixels, pixels, srcPixels.Length);

            solidChangedAll = true;
            Rebuild();
            dirty = false;
        }

        /// <summary>
        ///  Evita que Start() construya el terreno de prueba por su cuenta.
        ///  Lo llama <see cref="TerrainGenerator"/> desde su Awake para que
        ///  el terreno no se genere dos veces (una con los senos de prueba,
        ///  otra con el procedural). Awake corre antes de cualquier Start,
        ///  así que llamarlo ahí es seguro.
        /// </summary>
        public void SuppressTestTerrain()
        {
            generateTestTerrainOnStart = false;
        }

        /// <summary>
        ///  Reconstruye desde cero el terreno de prueba. Útil para el tester
        ///  ("tecla R") para volver al estado inicial sin reiniciar la escena.
        /// </summary>
        public void RegenerateTestTerrain()
        {
            BuildTestTerrain();
            Rebuild();
        }

        /// <summary>
        ///  Crea la textura interna si no existe o cambió de tamaño.
        /// </summary>
        private void EnsureTexture()
        {
            if (tex != null && tex.width == textureWidth && tex.height == textureHeight)
                return;

            if (tex != null) Destroy(tex);
            tex = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;   // estilo pixel art — sin suavizado
            tex.wrapMode = TextureWrapMode.Clamp;

            pixels = new Color32[textureWidth * textureHeight];
        }

        /// <summary>
        ///  Terreno simple hecho a mano: colinas onduladas con seno + dos capas
        ///  (tierra marrón, hierba verde encima) + tres cuevas pre-carveadas.
        ///  Sirve para probar que el sistema funciona. En la Sesión 2 lo
        ///  sustituirá un generador procedural con Perlin noise.
        /// </summary>
        private void BuildTestTerrain()
        {
            EnsureTexture();
            solidChangedAll = true;
            Color32 air = new Color32(0, 0, 0, 0);

            int groundLevel = Mathf.RoundToInt(textureHeight * 0.6f);

            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < textureWidth; x++)
                {
                    // Altura del suelo en este x (dos senos superpuestos = colinas)
                    int h = groundLevel
                          + Mathf.RoundToInt(Mathf.Sin(x * 0.015f) * 40f)
                          + Mathf.RoundToInt(Mathf.Sin(x * 0.05f)  * 15f);

                    int idx = y * textureWidth + x;

                    if (y > h)                       pixels[idx] = air;           // cielo
                    else if (y > h - grassBandPx)    pixels[idx] = grassColor;    // hierba
                    else                             pixels[idx] = dirtColor;     // tierra
                }
            }

            // Un par de cuevas pre-excavadas para que se note visualmente
            // que la forma NO es simplemente un rectángulo.
            PunchCircleInPixels(new Vector2Int(textureWidth / 4,     groundLevel - 40), 30);
            PunchCircleInPixels(new Vector2Int(textureWidth / 2,     groundLevel - 80), 45);
            PunchCircleInPixels(new Vector2Int(3 * textureWidth / 4, groundLevel - 30), 28);
        }

        /// <summary>
        ///  Borra un círculo de píxeles del array (no aplica a la textura aún).
        ///  Trabajar directamente sobre el Color32[] es muchísimo más rápido que
        ///  llamar a Texture2D.SetPixel una vez por píxel.
        ///
        ///  Versión PERFECTA: usada internamente por el terreno de prueba y las
        ///  cuevas predefinidas. Para explosiones se usa
        ///  <see cref="PunchIrregularCircleInPixels"/>.
        /// </summary>
        private void PunchCircleInPixels(Vector2Int center, int radius)
        {
            if (pixels == null) return;
            int r2 = radius * radius;
            int xmin = Mathf.Max(0, center.x - radius);
            int xmax = Mathf.Min(textureWidth - 1, center.x + radius);
            int ymin = Mathf.Max(0, center.y - radius);
            int ymax = Mathf.Min(textureHeight - 1, center.y + radius);

            Color32 air = new Color32(0, 0, 0, 0);
            for (int y = ymin; y <= ymax; y++)
            {
                int row = y * textureWidth;
                for (int x = xmin; x <= xmax; x++)
                {
                    int dx = x - center.x;
                    int dy = y - center.y;
                    if (dx * dx + dy * dy <= r2)
                        pixels[row + x] = air;
                }
            }
            MarkSolidChanged(xmin, ymin, xmax, ymax);
        }

        /// <summary>
        ///  Borra un círculo IRREGULAR (con ruido en el borde) — lo que se ve
        ///  como un cráter realista, no un círculo de compás.
        ///
        ///  Cómo funciona: para cada píxel cercano al centro calculamos su
        ///  ángulo y perturbamos el "radio efectivo" con Perlin(sin(ang), cos(ang)).
        ///  Resultado: el borde del cráter ondula un +-irregularity% respecto
        ///  al radio nominal. Además podemos añadir un poco de "dentado" (Hash
        ///  por píxel) para que el borde no sea suave del todo, más tipo tierra
        ///  reventada.
        /// </summary>
        /// <param name="center">Centro en coordenadas de píxel.</param>
        /// <param name="radius">Radio nominal en píxeles.</param>
        /// <param name="irregularity">Fracción +-r que puede variar el borde, 0..1. 0.15 = +-15%.</param>
        /// <param name="rng">Seed para el ruido de este cráter (posición mundo del impacto) — así cada agujero es único.</param>
        private void PunchIrregularCircleInPixels(Vector2Int center, int radius,
                                                  float irregularity, int rng)
        {
            if (pixels == null) return;
            if (radius <= 0) return;

            // Margen extra porque el borde irregular puede extenderse un poco
            // más allá del radio nominal.
            int expand = Mathf.CeilToInt(radius * (1f + irregularity + 0.1f));
            int xmin = Mathf.Max(0, center.x - expand);
            int xmax = Mathf.Min(textureWidth - 1, center.x + expand);
            int ymin = Mathf.Max(0, center.y - expand);
            int ymax = Mathf.Min(textureHeight - 1, center.y + expand);

            // Offsets Perlin a partir del seed — así dos explosiones en el
            // mismo sitio dan formas distintas.
            float offX = (rng & 0xFFFF) * 0.013f;
            float offY = ((rng >> 16) & 0xFFFF) * 0.017f;

            Color32 air = new Color32(0, 0, 0, 0);
            // CUENCO: la distancia vertical cuenta más que la horizontal, así
            // el agujero sale más ANCHO que hondo. Con un círculo, las paredes
            // eran casi verticales justo en el borde y la rata no podía salir
            // ni andando ni saltando.
            float vScale = 1f / Mathf.Max(0.3f, craterDepthRatio);
            int scorch = Mathf.Max(0, craterScorchPx);
            // EMBUDO: por encima de la parte ancha del cuenco, las paredes se
            // ABREN hacia arriba a 45°. Sin esto, una explosión un poco por
            // debajo del suelo dejaba la boca del agujero más estrecha que su
            // interior (paredes en voladizo) → imposible salir.
            float bowlB = radius * Mathf.Max(0.3f, craterDepthRatio);   // semieje vertical (px)
            // Empieza donde la pared del cuenco ya está a ~45° (más arriba se
            // pone vertical): desde ahí, paredes a 45° hasta arriba.
            float funnelStartY = -0.55f * bowlB;                          // dónde empieza a abrirse
            float funnelTop = radius * craterFunnelHeight;                // hasta dónde sube
            float funnelBaseHalf = radius * 0.835f;                       // = anchura del cuenco en funnelStartY
            // Caja a recorrer AJUSTADA a la forma (0.19): antes era un cuadrado
            // de ~3 radios por lado y la mayoría de píxeles caían lejos.
            //  · Cuenco: radio máximo con ruido = radius·(1+irr); en vertical
            //    se aplasta (dy·vScale), así que su alto es bowlB·(1+irr).
            //  · Embudo: llega hasta funnelTop por arriba y se abre a 45°.
            float maxR = radius * (1f + irregularity);
            float funnelMaxHalf = (funnelBaseHalf + (funnelTop - funnelStartY)) * (1f + irregularity * 0.4f);
            int halfW = Mathf.CeilToInt(Mathf.Max(maxR, funnelTop > funnelStartY ? funnelMaxHalf : 0f)) + scorch + 1;
            int down = Mathf.CeilToInt(maxR / vScale) + scorch + 1;
            int up = Mathf.CeilToInt(Mathf.Max(maxR / vScale, funnelTop)) + scorch + 1;
            ymin = Mathf.Max(0, center.y - down);
            ymax = Mathf.Min(textureHeight - 1, center.y + up);
            xmin = Mathf.Max(0, center.x - halfW);
            xmax = Mathf.Min(textureWidth - 1, center.x + halfW);
            // Distancia (del cuenco) a partir de la cual un píxel está fuera
            // del todo, aunque el ruido empuje el borde hacia fuera.
            float farOut = maxR + scorch;
            float farOutSq = farOut * farOut;

            for (int y = ymin; y <= ymax; y++)
            {
                int row = y * textureWidth;
                for (int x = xmin; x <= xmax; x++)
                {
                    int dx = x - center.x;
                    int dy = y - center.y;
                    float dyS = dy * vScale;
                    float distSq = dx * dx + dyS * dyS;
                    bool inFunnelBand = dy >= funnelStartY && dy <= funnelTop;
                    // DESCARTE RÁPIDO: lejos del cuenco y fuera del embudo → ni
                    // ruido ni trigonometría (es la gran mayoría de píxeles).
                    if (distSq > farOutSq &&
                        (!inFunnelBand || Mathf.Abs(dx) - (funnelBaseHalf + (dy - funnelStartY)) * (1f + irregularity * 0.4f) > scorch))
                        continue;
                    float dist = Mathf.Sqrt(distSq);

                    // Ángulo 0..2π → tomamos Perlin(cos, sin) para que varíe
                    // continuamente al girar. Escala 1.8 = ~3 "bollos" por vuelta.
                    float ang = Mathf.Atan2(dyS, dx);
                    float pert = Mathf.PerlinNoise(
                        Mathf.Cos(ang) * 1.8f + offX,
                        Mathf.Sin(ang) * 1.8f + offY);
                    // pert ~[0,1] → centrarla en 0 para que sea ±irregularity
                    float rNoise = (pert - 0.5f) * 2f * irregularity;
                    float effectiveR = radius * (1f + rNoise);

                    // "Cuánto fuera" está el píxel del agujero (≤0 = dentro).
                    // Es el mínimo entre el cuenco y el embudo.
                    float outside = dist - effectiveR;
                    if (inFunnelBand)
                    {
                        // Menos ruido en el embudo: con el ruido entero, la anchura
                        // subía y bajaba con la altura y dejaba mini-voladizos.
                        float half = (funnelBaseHalf + (dy - funnelStartY)) * (1f + rNoise * 0.4f);
                        outside = Mathf.Min(outside, Mathf.Abs(dx) - half);
                    }

                    int idx = row + x;
                    if (outside <= 0f)
                    {
                        pixels[idx] = air;
                    }
                    else if (scorch > 0 && outside <= scorch && IsSolidPixel(pixels[idx]))
                    {
                        // BORDE CHAMUSCADO: oscurecemos la tierra justo alrededor
                        // del agujero, más cuanto más cerca del borde, con un
                        // poco de ruido para que no sea un anillo perfecto.
                        float t = outside / scorch;                                   // 0 borde … 1 fuera
                        float jitter = ((x * 73 + y * 151) & 7) / 7f * 0.25f;         // determinista
                        float dark = craterScorchStrength * (1f - Mathf.Clamp01(t + jitter * t));
                        if (dark > 0f)
                        {
                            var c = pixels[idx];
                            float k = 1f - dark;
                            pixels[idx] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);
                        }
                    }
                }
            }

            MarkSolidChanged(xmin, ymin, xmax, ymax);
            ClearFloatingDecor(xmin, xmax, ymin, Mathf.Min(textureHeight - 1, ymax + 16));
        }

        /// <summary>
        ///  Tras una explosión: borra la DECORACIÓN que se ha quedado flotando
        ///  (una flor cuyo suelo ha volado). Recorremos de abajo a arriba: si
        ///  debajo de un píxel de decoración hay aire, desaparece; así las
        ///  briznas altas caen en cadena con su base. Las raíces que cuelgan
        ///  de un techo se comprueban al revés (necesitan algo ENCIMA).
        /// </summary>
        private void ClearFloatingDecor(int xmin, int xmax, int ymin, int ymax)
        {
            Color32 air = new Color32(0, 0, 0, 0);
            int rw = xmax - xmin + 1, rh = ymax - ymin + 1;
            if (rw <= 0 || rh <= 0) return;
            var fromBelow = new bool[rw * rh];
            var fromAbove = new bool[rw * rh];

            // ¿El píxel (x,y) sostiene a la decoración de encima/debajo?
            // Fuera de la región asumimos que la decoración sigue sujeta.
            bool Holds(int x, int y, bool[] support)
            {
                if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight) return false;
                var c = pixels[y * textureWidth + x];
                if (IsSolidPixel(c)) return true;
                if (!IsDecorPixel(c)) return false;
                if (x < xmin || x > xmax || y < ymin || y > ymax) return true;
                return support[(y - ymin) * rw + (x - xmin)];
            }

            // Apoyo DESDE ABAJO (hierba, flores, setas): fila a fila hacia arriba,
            // vale el píxel de debajo o los de las diagonales de abajo.
            for (int y = ymin; y <= ymax; y++)
                for (int x = xmin; x <= xmax; x++)
                {
                    if (!IsDecorPixel(pixels[y * textureWidth + x])) continue;
                    fromBelow[(y - ymin) * rw + (x - xmin)] =
                        Holds(x, y - 1, fromBelow) || Holds(x - 1, y - 1, fromBelow) || Holds(x + 1, y - 1, fromBelow);
                }

            // Colgando DESDE ARRIBA (raíces, goteos): fila a fila hacia abajo.
            for (int y = ymax; y >= ymin; y--)
                for (int x = xmin; x <= xmax; x++)
                {
                    if (!IsDecorPixel(pixels[y * textureWidth + x])) continue;
                    fromAbove[(y - ymin) * rw + (x - xmin)] =
                        Holds(x, y + 1, fromAbove) || Holds(x - 1, y + 1, fromAbove) || Holds(x + 1, y + 1, fromAbove);
                }

            for (int y = ymin; y <= ymax; y++)
                for (int x = xmin; x <= xmax; x++)
                {
                    int k = (y - ymin) * rw + (x - xmin);
                    int idx = y * textureWidth + x;
                    if (IsDecorPixel(pixels[idx]) && !fromBelow[k] && !fromAbove[k])
                        pixels[idx] = air;
                }
        }

        // ============================================================
        //  API DE CARVING (lo que llaman las explosiones)
        // ============================================================

        [Header("Forma del cráter de explosión")]
        [Tooltip("Fracción del radio nominal que realmente se carvea (0.85 = cráter un 15% más pequeño que la explosión). Los diseñadores suelen querer que el carving sea un pelín menor que el daño.")]
        [SerializeField, Range(0.5f, 1.2f)] private float craterRadiusScale = 0.85f;

        [Tooltip("Irregularidad del borde del cráter (0 = círculo perfecto, 0.2 = borde bastante orgánico). 0.15 va bien para Worms.")]
        [SerializeField, Range(0f, 0.4f)] private float craterIrregularity = 0.15f;

        [Tooltip("Profundidad del cráter respecto a su anchura. 1 = círculo (paredes verticales en el borde, difícil salir). " +
                 "0.65 = cuenco: más ancho que hondo, con paredes por las que la rata puede salir saltando.")]
        [SerializeField, Range(0.3f, 1f)] private float craterDepthRatio = 0.65f;

        [Tooltip("Altura del EMBUDO sobre el centro del cráter, en radios. Las paredes se abren a 45° hasta esta altura, " +
                 "así la boca nunca es más estrecha que el interior. 0 = sin embudo (puede haber voladizos). " +
                 "0.9 = una explosión hasta ~1 radio bajo el suelo abre boca hacia arriba; más hondo queda una cueva cerrada.")]
        [SerializeField, Range(0f, 1.5f)] private float craterFunnelHeight = 0.9f;

        [Tooltip("Anchura (px) del borde CHAMUSCADO alrededor del cráter. 0 = sin chamuscar.")]
        [SerializeField, Range(0, 12)] private int craterScorchPx = 5;

        [Tooltip("Cuánto se oscurece la tierra pegada al cráter (0-1).")]
        [SerializeField, Range(0f, 1f)] private float craterScorchStrength = 0.45f;

        /// <summary>
        ///  Carvea un agujero en ESTE terreno con forma IRREGULAR (no un círculo
        ///  perfecto). El radio visible es <c>worldRadius * craterRadiusScale</c>
        ///  y el borde se ondula ±<see cref="craterIrregularity"/>% por ruido Perlin.
        ///
        ///  Si el centro cae fuera del terreno pero el círculo toca sus bordes,
        ///  se carvea la intersección igualmente (el método interno recorta
        ///  los límites).
        /// </summary>
        public void CarveHole(Vector2 worldPos, float worldRadius)
        {
            if (pixels == null) return;

            // World → espacio local del terreno → píxel
            Vector3 local = transform.InverseTransformPoint(worldPos);
            int cx = Mathf.RoundToInt(local.x * pixelsPerUnit);
            int cy = Mathf.RoundToInt(local.y * pixelsPerUnit);
            int r  = Mathf.CeilToInt(worldRadius * craterRadiusScale * pixelsPerUnit);

            // Seed determinista a partir del punto de impacto (para que
            // clientes y host dibujen el MISMO cráter con el MISMO ruido).
            int seed = unchecked(cx * 73856093 ^ cy * 19349663);

            PunchIrregularCircleInPixels(new Vector2Int(cx, cy), r, craterIrregularity, seed);
            dirty = true;
        }

        /// <summary>
        ///  Atajo estático: carvea en TODOS los terrenos de la escena a la vez.
        ///  Úsalo desde misil / granada sin tener que buscar referencias.
        /// </summary>
        /// <summary>
        ///  SOLO PRUEBAS: rellena de tierra un rectángulo del mundo (para
        ///  levantar una pared y comprobar que la rata la escala). Local: no
        ///  pasa por la red.
        /// </summary>
        public static void TestFillRectWorld(Vector2 min, Vector2 max, Color32 color)
        {
            foreach (var t in s_all)
            {
                if (t == null || t.pixels == null) continue;
                Vector3 a = t.transform.InverseTransformPoint(min), b = t.transform.InverseTransformPoint(max);
                int x0 = Mathf.Clamp(Mathf.FloorToInt(a.x * t.pixelsPerUnit), 0, t.textureWidth - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(b.x * t.pixelsPerUnit), 0, t.textureWidth - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(a.y * t.pixelsPerUnit), 0, t.textureHeight - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(b.y * t.pixelsPerUnit), 0, t.textureHeight - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        t.pixels[y * t.textureWidth + x] = color;
                t.solidChangedAll = true;
                t.dirty = true;
            }
        }

        public static void CarveAllAt(Vector2 worldPos, float worldRadius)
        {
            for (int i = s_all.Count - 1; i >= 0; i--)
            {
                var t = s_all[i];
                if (t != null) t.CarveHole(worldPos, worldRadius);
            }
        }

        // ============================================================
        //  RECONSTRUCCIÓN DEL SPRITE Y DEL COLISIONADOR
        // ============================================================
        private void Rebuild()
        {
            if (tex == null || pixels == null) return;

            // 0) Seguridad: aseguramos que el collider está en un estado
            //    funcional antes de tocar nada. Si alguien lo desactivó o lo
            //    puso como trigger sin querer, lo devolvemos a su sitio.
            if (poly == null) poly = GetComponent<PolygonCollider2D>();
            if (poly != null)
            {
                poly.enabled = true;
                poly.isTrigger = false;
            }

            // 1) Vuelca el array de píxeles a la textura
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // 2) Crea un sprite nuevo con mesh TIGHT — Unity genera el outline
            //    por nosotros a partir del alpha de los píxeles.
            //    extrude=1 da algo más de margen y reduce el riesgo de
            //    "vertices too close" al construir el PolygonCollider2D.
            //    Con PixelContours el collider NO sale del sprite, así que usamos
            //    FullRect: Unity no tiene que calcular la malla ajustada de una
            //    textura enorme en cada explosión (mucho más barato).
            bool contours = colliderMode == TerrainColliderMode.PixelContours;
            Sprite newSprite = Sprite.Create(
                tex,
                new Rect(0, 0, textureWidth, textureHeight),
                new Vector2(0f, 0f),       // pivot = esquina inferior-izquierda
                pixelsPerUnit,
                extrude: 1,
                meshType: contours ? SpriteMeshType.FullRect : SpriteMeshType.Tight);

            // Destruye el sprite anterior para no acumular basura en memoria.
            var old = sr.sprite;
            sr.sprite = newSprite;
            if (old != null && old != newSprite) Destroy(old);

            if (contours)
            {
                BuildContourColliders();
                return;
            }
            else if (edgesHost != null && edgesHost.activeSelf)
            {
                edgesHost.SetActive(false);   // volviendo al sistema antiguo
            }

            // 3) Extrae los paths del sprite y aplícalos al PolygonCollider2D.
            //    GetPhysicsShape devuelve los puntos YA en unidades de mundo
            //    (respeta pixelsPerUnit), así que van directos al collider.
            //
            //  *** SHORT-CIRCUIT ***: si forceSkylineCollider está ON, saltamos
            //  Sprite.Tight directamente y nos vamos al fallback. Sprite.Tight
            //  ha demostrado generar colliders inconsistentes (ver doc del flag).
            int shapeCount = forceSkylineCollider ? 0 : newSprite.GetPhysicsShapeCount();

            var path = new List<Vector2>();
            int totalPointsRaw = 0;
            int totalPointsAfter = 0;
            int acceptedShapes = 0;

            // Tolerancia para RDP en UNIDADES DE MUNDO. 2 px de tolerancia es
            // imperceptible visualmente (las ratas son mucho más grandes) y
            // reduce vértices a la décima parte en mapas con mucho ruido
            // Perlin, evitando el rechazo "vertices too close" de Unity.
            float rdpEpsilon = 2f / pixelsPerUnit;
            // Además, distancia mínima estricta entre vértices consecutivos
            // (Unity internamente exige algo mayor que ~1 píxel).
            float minDist = 2f / pixelsPerUnit;
            float minDistSq = minDist * minDist;

            for (int i = 0; i < shapeCount; i++)
            {
                path.Clear();
                newSprite.GetPhysicsShape(i, path);
                totalPointsRaw += path.Count;
                if (path.Count < 3) continue; // shape degenerado

                // Paso A: Ramer–Douglas–Peucker. Quita vértices colineales y
                // detalles inferiores a rdpEpsilon. Es lo que más puntos elimina.
                var rdp = RamerDouglasPeucker(path, rdpEpsilon);

                // Paso B: red de seguridad. Por si tras RDP quedó algún par
                // de vértices muy juntos, los fusionamos.
                var simplified = DropClosePairs(rdp, minDistSq);
                if (simplified.Count < 3) continue; // quedó demasiado corto

                poly.SetPath(acceptedShapes, simplified.ToArray());
                acceptedShapes++;
                totalPointsAfter += simplified.Count;
            }

            // Ajustamos pathCount al número real de shapes aceptados.
            poly.pathCount = acceptedShapes;

            // FALLBACK DE EMERGENCIA: si Unity no nos dio ningún shape o los
            // rechazó todos, construimos nosotros un "skyline" columna a
            // columna leyendo el alpha de la textura. Es menos refinado que
            // Sprite.Tight (no detecta cuevas ni islas flotantes como agujeros)
            // pero garantiza que la rata tenga SUELO donde pisar y no caiga
            // al vacío.
            if (acceptedShapes == 0 && HasAnySolidPixel())
            {
                var skyline = BuildSkylinePolygon();
                if (skyline != null && skyline.Length >= 3)
                {
                    // El skyline cubre la TIERRA PRINCIPAL (todo lo conectado al
                    // suelo del mapa por flood-fill). Las islas flotantes han
                    // quedado fuera de la máscara → no tienen collider todavía.
                    // Las cazamos aparte y las añadimos como paths adicionales:
                    var islands = BuildIslandPolygons();

                    // NUEVO: detectar CUEVAS INTERNAS (huecos rodeados de tierra)
                    // y añadirlas como paths adicionales. PolygonCollider2D
                    // con múltiples paths interpretará los interiores como
                    // agujeros si están dentro del path principal (winding
                    // even/odd rule). Resultado: la rata puede entrar en las
                    // cuevas en lugar de chocar contra paredes invisibles.
                    List<Vector2[]> caves = detectCavesAsColliderHoles
                        ? BuildCavePolygons()
                        : new List<Vector2[]>();

                    poly.pathCount = 1 + islands.Count + caves.Count;
                    poly.SetPath(0, skyline);
                    for (int i = 0; i < islands.Count; i++)
                        poly.SetPath(i + 1, islands[i]);
                    for (int i = 0; i < caves.Count; i++)
                        poly.SetPath(1 + islands.Count + i, caves[i]);

                    if (debugColliderBuild)
                    {
                        string reason = forceSkylineCollider
                            ? "FORZADO por toggle 'forceSkylineCollider'"
                            : $"Sprite.Tight devolvió shapeCount={shapeCount}";
                        Debug.Log($"[DestructibleTerrain] Collider via SKYLINE ({reason}) — " +
                                  $"{skyline.Length} vértices en tierra principal, " +
                                  $"{islands.Count} isla(s) flotante(s), " +
                                  $"{caves.Count} cueva(s) navegable(s) como huecos, " +
                                  $"textura={textureWidth}x{textureHeight}, PPU={pixelsPerUnit}, " +
                                  $"layer={gameObject.layer} ({LayerMask.LayerToName(gameObject.layer)}).");
                    }
                    return;
                }

                // Último recurso: rectángulo envolvente. No debería hacer falta
                // nunca, pero así aseguramos "siempre hay collider".
                Vector2 bl = new Vector2(0f, 0f);
                Vector2 br = new Vector2(textureWidth / pixelsPerUnit, 0f);
                Vector2 tr = new Vector2(textureWidth / pixelsPerUnit, textureHeight / pixelsPerUnit);
                Vector2 tl = new Vector2(0f, textureHeight / pixelsPerUnit);
                poly.pathCount = 1;
                poly.SetPath(0, new[] { bl, br, tr, tl });
                Debug.LogError($"[DestructibleTerrain] Rebuild → skyline falló; aplicado " +
                               $"FALLBACK RECTANGULAR. Textura={textureWidth}x{textureHeight}.");
                return;
            }

            if (shapeCount == 0 && !HasAnySolidPixel())
            {
                // La textura sí está vacía — es legítimo, no hay collider.
                poly.pathCount = 0;
                Debug.LogWarning($"[DestructibleTerrain] Rebuild → shapeCount=0 y textura vacía. " +
                                 $"Sin collider (correcto). Textura={textureWidth}x{textureHeight}.");
                return;
            }

            if (debugColliderBuild)
            {
                Debug.Log($"[DestructibleTerrain] Collider via SPRITE.TIGHT (Unity) — " +
                          $"shapes originales={shapeCount}, aceptados={acceptedShapes}, " +
                          $"puntos tras simplificar={totalPointsAfter}, " +
                          $"textura={textureWidth}x{textureHeight}, PPU={pixelsPerUnit}, " +
                          $"layer={gameObject.layer} ({LayerMask.LayerToName(gameObject.layer)}).");
            }
        }

        /// <summary>
        ///  Collider EXACTO: un EdgeCollider2D por cada contorno del terreno
        ///  (borde exterior, cada cueva, cada cráter, cada isla).
        ///
        ///  Por qué bordes (EdgeCollider2D) y no un polígono relleno: un
        ///  polígono con "agujeros" depende de cómo Unity interprete qué es
        ///  dentro y qué es fuera, y con formas raras (cráteres abiertos de
        ///  lado, cuevas que tocan la superficie) se equivocaba → la física
        ///  creía que había tierra donde se veía aire. Los bordes no tienen
        ///  "dentro": la rata choca justo donde está la línea, por los dos lados.
        /// </summary>
        private void BuildContourColliders()
        {
            if (poly != null)
            {
                poly.pathCount = 0;
                poly.enabled = false;   // el antiguo no participa
            }

            if (edgesHost == null)
            {
                edgesHost = new GameObject("TerrainEdges");
                edgesHost.transform.SetParent(transform, worldPositionStays: false);
                edgesHost.layer = gameObject.layer;
            }
            if (!edgesHost.activeSelf) edgesHost.SetActive(true);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            RectInt? changed = solidChangedAll || !hasSolidChange ? (RectInt?)null : solidChange;
            if (!solidChangedAll && !hasSolidChange) changed = new RectInt(0, 0, 0, 0);   // nada cambió de forma
            var loops = PixelContours.Build(pixels, textureWidth, textureHeight, pixelsPerUnit,
                                            contourSimplifyPx, minContourPoints, changed);
            solidChangedAll = false;
            hasSolidChange = false;
            ShapeChanged?.Invoke();
            long contourMs = sw.ElapsedMilliseconds;

            // Reutilizamos los EdgeCollider2D que ya había (crear/destruir
            // componentes en cada explosión es caro).
            while (edgePool.Count < loops.Count)
            {
                var ec = edgesHost.AddComponent<EdgeCollider2D>();
                edgePool.Add(ec);
            }
            int points = 0;
            for (int i = 0; i < edgePool.Count; i++)
            {
                var ec = edgePool[i];
                if (i < loops.Count)
                {
                    ec.enabled = true;
                    ec.edgeRadius = contourEdgeRadius;
                    ec.points = loops[i];
                    points += loops[i].Length;
                }
                else ec.enabled = false;
            }

            if (debugColliderBuild)
                Debug.Log($"[DestructibleTerrain] Collider via CONTORNOS EXACTOS — {loops.Count} bucles, {points} vértices, " +
                          $"contornos {contourMs} ms + colliders {sw.ElapsedMilliseconds - contourMs} ms, " +
                          $"textura={textureWidth}x{textureHeight}, PPU={pixelsPerUnit}.");
        }

        /// <summary>
        ///  Construye un polígono SKYLINE leyendo la textura columna a columna:
        ///  para cada x, busca desde arriba el primer píxel sólido (alpha>0)
        ///  y registra esa altura como "superficie". Devuelve un polígono que
        ///  va por la superficie y se cierra por el borde inferior del mapa.
        ///
        ///  Limitaciones (aceptables como red de seguridad):
        ///   · No detecta cuevas (el hueco interno del mapa queda relleno de
        ///     collider — la rata no se mete en cuevas con este fallback).
        ///   · No detecta islas flotantes (se ignoran).
        ///   · Funciona genial para un mapa "cielo arriba, tierra abajo",
        ///     que es nuestro caso en el 99% de las generaciones.
        ///
        ///  Para reducir vértices, muestreamos cada sampleStep píxeles en x.
        ///  Tras muestrear aplicamos una MEDIA MÓVIL de 5 puntos a las alturas:
        ///  así las espigas estrechas (picos de aguja que bloqueaban el paso
        ///  de la rata) se suavizan, mientras que las montañas grandes quedan
        ///  casi iguales. Resultado: no hay muros invisibles.
        /// </summary>
        private Vector2[] BuildSkylinePolygon()
        {
            const int sampleStep = 3;    // más fino → perfil más liso
            const int smoothRadius = 2;  // media móvil de (2*2+1)=5 puntos
            const int caveSkipMax = 50;  // legacy, ya no se usa con la máscara conectada
            if (pixels == null) return null;

            // Antes de muestrear las columnas, calculamos qué píxeles son
            // "tierra principal" (= los que están conectados al suelo del
            // mapa por una cadena de píxeles sólidos vecinos). Las islas
            // flotantes son chunks aislados de tierra que NO tocan el suelo
            // por ningún camino, así que NO entran en la máscara y el
            // polígono las ignora completamente. Resultado: las islas se
            // quedan flotando como decoración visual y la rata pasa por
            // debajo de ellas sin chocar contra "paredes invisibles".
            mainGroundMask = BuildMainGroundMask();

            // --- Paso 1: sample de alturas (en píxeles) columna a columna ---
            int sampleCount = (textureWidth + sampleStep - 1) / sampleStep;
            if (sampleCount < 2) return null;
            int[] sampleX = new int[sampleCount + 1];
            float[] sampleYpx = new float[sampleCount + 1];
            int actualCount = 0;
            bool anySurfaceFound = false;

            // ANTI-TRINCHERA: cuando una columna NO tiene nada sólido (surfaceY=-1),
            // en vez de meter un punto a y=0 (lo que crearía una "V" en el polígono
            // y la rata caería ahí), usamos la última altura conocida. Esto da
            // como mucho un techo recto pasando por encima del hueco — sin
            // bajadas a cero. La rata camina sin caer por gaps en el ruido.
            float lastValidYpx = -1f;
            for (int s = 0; s < sampleCount; s++)
            {
                int x = s * sampleStep;
                if (x >= textureWidth) break;
                sampleX[actualCount] = x;
                int surfaceY = FindMainSurfaceY(x, caveSkipMax);
                if (surfaceY < 0)
                {
                    // Sin sólido en esta columna → reusamos la última altura
                    // válida (o 0 al principio del mapa si aún no había ninguna).
                    sampleYpx[actualCount] = lastValidYpx >= 0 ? lastValidYpx : 0f;
                }
                else
                {
                    float ypx = surfaceY + 1f; // justo encima
                    sampleYpx[actualCount] = ypx;
                    lastValidYpx = ypx;
                    anySurfaceFound = true;
                }
                actualCount++;
            }

            // Último x explícito si no cayó justo en múltiplo
            int lastX = textureWidth - 1;
            if (sampleX[actualCount - 1] != lastX)
            {
                sampleX[actualCount] = lastX;
                int surfaceY = FindMainSurfaceY(lastX, caveSkipMax);
                if (surfaceY < 0)
                {
                    sampleYpx[actualCount] = lastValidYpx >= 0 ? lastValidYpx : 0f;
                }
                else
                {
                    sampleYpx[actualCount] = surfaceY + 1f;
                    anySurfaceFound = true;
                }
                actualCount++;
            }

            if (!anySurfaceFound) return null;

            // SEGUNDA PASADA hacia atrás para rellenar columnas iniciales que
            // venían con sampleYpx=0 porque aún no había vista una columna válida.
            // Usamos la siguiente válida hacia adelante. Así NO empieza el mapa
            // con un acantilado a y=0 si las primeras columnas estaban vacías.
            float nextValid = -1f;
            for (int i = actualCount - 1; i >= 0; i--)
            {
                if (sampleYpx[i] > 0f) { nextValid = sampleYpx[i]; }
                else if (nextValid > 0f) { sampleYpx[i] = nextValid; }
            }

            // --- Paso 2: media móvil para suavizar picos-aguja ---
            float[] smoothed = new float[actualCount];
            for (int i = 0; i < actualCount; i++)
            {
                int from = Mathf.Max(0, i - smoothRadius);
                int to = Mathf.Min(actualCount - 1, i + smoothRadius);
                float sum = 0f;
                int count = 0;
                for (int j = from; j <= to; j++) { sum += sampleYpx[j]; count++; }
                smoothed[i] = sum / count;
            }

            // --- Paso 3: convertir a polígono en unidades de mundo ---
            var upper = new List<Vector2>(actualCount);
            for (int i = 0; i < actualCount; i++)
            {
                upper.Add(new Vector2(sampleX[i] / pixelsPerUnit, smoothed[i] / pixelsPerUnit));
            }

            // Cerramos el polígono bajando por la esquina inferior derecha y
            // volviendo por la inferior izquierda. Así el interior del polígono
            // es TODO el volumen de tierra.
            var poly = new List<Vector2>(upper.Count + 2);
            poly.AddRange(upper);
            poly.Add(new Vector2(lastX / pixelsPerUnit, 0f));
            poly.Add(new Vector2(0f, 0f));

            // Simplificamos para reducir vértices a lo esencial
            var simplified = RamerDouglasPeucker(poly, 2f / pixelsPerUnit);
            return simplified.ToArray();
        }

        /// <summary>
        ///  Encuentra la Y de la superficie del SUELO PRINCIPAL en una columna x.
        ///
        ///  Estrategia (la simple y la correcta cuando NO hay islas flotantes):
        ///  vamos de ARRIBA HACIA ABAJO buscando el primer píxel sólido. Eso es
        ///  exactamente la superficie visible — independientemente de cuántas
        ///  cuevas tenga la columna por dentro.
        ///
        ///  Por qué cambió: la versión anterior iba de abajo a arriba y trataba
        ///  las cuevas pequeñas (&lt; caveSkipMax px) como "parte del suelo".
        ///  Pero las cuevas reales del generador son MUY grandes (Max Cave
        ///  Depth = 280 px en el Inspector), así que el algoritmo se confundía
        ///  y plantaba la superficie en el SUELO de cada cueva, no en su techo.
        ///  Eso hacía que el polígono tuviera "trincheras" verticales bajando
        ///  por cada cueva — visibles como líneas rojas en el Gizmo, y agujeros
        ///  por los que la rata se caía al vacío.
        ///
        ///  El parámetro <paramref name="caveSkipMax"/> queda por compatibilidad
        ///  pero ya no se usa en esta versión.
        ///
        ///  Devuelve -1 si la columna no tiene ningún píxel sólido (cielo total).
        /// </summary>
        private int FindMainSurfaceY(int x, int caveSkipMax)
        {
            if (pixels == null) return -1;

            // PREFERIDO: si tenemos la máscara de tierra principal calculada,
            // ignoramos cualquier píxel sólido que NO sea parte del cuerpo
            // principal. Así las islas flotantes (chunks aislados) se saltan
            // y el skyline va por la superficie del terreno PRINCIPAL,
            // pasando por DEBAJO de las islas sin crear paredes verticales.
            if (mainGroundMask != null)
            {
                for (int y = textureHeight - 1; y >= 0; y--)
                {
                    int idx = y * textureWidth + x;
                    if (mainGroundMask[idx]) return y;
                }
                return -1;
            }

            // FALLBACK (sin máscara aún): de arriba hacia abajo, primer sólido.
            for (int y = textureHeight - 1; y >= 0; y--)
            {
                if (IsSolidPixel(pixels[y * textureWidth + x]))
                    return y;
            }
            return -1;
        }

        /// <summary>
        ///  Construye una máscara booleana del tamaño de la textura donde
        ///  true = "este píxel es parte de la TIERRA PRINCIPAL", es decir,
        ///  está conectado al suelo del mapa (fila inferior) por una cadena
        ///  de píxeles sólidos vecinos.
        ///
        ///  Cómo funciona (BFS / flood-fill 4-conexo):
        ///   1) Sembramos la cola con todos los píxeles sólidos de la fila
        ///      inferior (y=0). Esos forman el "suelo" desde el que crece
        ///      la región conectada.
        ///   2) Si la fila inferior está vacía (raro pero posible si el
        ///      generador deja un hueco abajo), buscamos el primer y desde
        ///      abajo que SÍ tenga sólidos y sembramos desde ahí.
        ///   3) Avanzamos en BFS marcando todo píxel sólido vecino (arriba,
        ///      abajo, izquierda, derecha). Las islas flotantes — chunks
        ///      aislados rodeados de aire — nunca son alcanzados, así que
        ///      su máscara queda en false y el skyline las ignora.
        ///
        ///  Coste: O(N) donde N = textureWidth × textureHeight (2 millones
        ///  para 2048×1024). En C# son pocos milisegundos. Se ejecuta solo
        ///  cuando reconstruimos el collider (después de carvear), así que
        ///  no impacta el frame rate del juego.
        /// </summary>
        private bool[] BuildMainGroundMask()
        {
            if (pixels == null) return null;
            int total = textureWidth * textureHeight;
            bool[] mask = new bool[total];
            var queue = new Queue<int>();

            // Paso 1: sembrar desde la fila inferior si tiene sólidos.
            int seedRow = -1;
            for (int x = 0; x < textureWidth; x++)
            {
                if (IsSolidPixel(pixels[x]))
                {
                    if (seedRow < 0) seedRow = 0;
                    mask[x] = true;
                    queue.Enqueue(x);
                }
            }

            // Paso 2: si la fila 0 estaba vacía, buscamos la primera fila
            // desde abajo con algún sólido.
            if (seedRow < 0)
            {
                for (int y = 1; y < textureHeight && seedRow < 0; y++)
                {
                    int rowStart = y * textureWidth;
                    for (int x = 0; x < textureWidth; x++)
                    {
                        int idx = rowStart + x;
                        if (IsSolidPixel(pixels[idx]))
                        {
                            seedRow = y;
                            mask[idx] = true;
                            queue.Enqueue(idx);
                        }
                    }
                }
            }

            if (seedRow < 0) return mask; // no hay sólidos — devolvemos máscara vacía

            // Paso 3: BFS 4-conexo (arriba, abajo, izquierda, derecha)
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int y = idx / textureWidth;
                int x = idx - y * textureWidth;

                // Arriba
                if (y + 1 < textureHeight)
                {
                    int n = idx + textureWidth;
                    if (!mask[n] && IsSolidPixel(pixels[n])) { mask[n] = true; queue.Enqueue(n); }
                }
                // Abajo
                if (y - 1 >= 0)
                {
                    int n = idx - textureWidth;
                    if (!mask[n] && IsSolidPixel(pixels[n])) { mask[n] = true; queue.Enqueue(n); }
                }
                // Izquierda
                if (x - 1 >= 0)
                {
                    int n = idx - 1;
                    if (!mask[n] && IsSolidPixel(pixels[n])) { mask[n] = true; queue.Enqueue(n); }
                }
                // Derecha
                if (x + 1 < textureWidth)
                {
                    int n = idx + 1;
                    if (!mask[n] && IsSolidPixel(pixels[n])) { mask[n] = true; queue.Enqueue(n); }
                }
            }

            return mask;
        }

        /// <summary>
        ///  Encuentra todos los chunks de tierra AISLADOS (= no conectados al
        ///  suelo del mapa) y devuelve un polígono cerrado para cada uno.
        ///  Cada polígono va como un PATH ADICIONAL del PolygonCollider2D, así
        ///  las islas tienen su propio collider y la rata puede pisarlas y los
        ///  misiles pueden explotar contra ellas.
        ///
        ///  Cómo funciona:
        ///   1) Recorremos toda la textura buscando píxeles SÓLIDOS que NO
        ///      estén en la máscara de tierra principal (ya calculada).
        ///   2) Por cada uno, lanzamos un flood-fill local que descubre todo
        ///      el chunk de la isla y lo marca como visitado.
        ///   3) Construimos un polígono CERRADO para esa isla recorriendo sus
        ///      columnas: para cada x, anotamos el píxel sólido más alto y el
        ///      más bajo. El contorno va por arriba de izquierda a derecha y
        ///      por abajo de derecha a izquierda → polígono cerrado simple
        ///      que cubre la silueta de la isla.
        ///   4) Lo simplificamos con RDP para reducir vértices.
        ///
        ///  Limitación conocida (aceptable): este método NO maneja islas con
        ///  AGUJEROS internos (cuevas dentro de la isla). El collider sería
        ///  "cerrado" — la cueva interna no sería accesible. Como las islas
        ///  son chunks pequeños del Perlin, casi nunca tienen cuevas internas;
        ///  si en el futuro fuese un problema, se puede usar marching squares.
        /// </summary>
        private List<Vector2[]> BuildIslandPolygons()
        {
            var result = new List<Vector2[]>();
            if (pixels == null || mainGroundMask == null) return result;

            int total = textureWidth * textureHeight;
            bool[] visited = new bool[total];
            var floodQueue = new Queue<int>();

            // Mínimo de píxeles para considerar algo "isla" — chunks más
            // pequeños que esto los ignoramos para no llenar el collider de
            // micropolígonos por ruido del Perlin (granos de píxel sueltos).
            const int minIslandPixels = 20;

            for (int idx = 0; idx < total; idx++)
            {
                if (visited[idx]) continue;
                if (mainGroundMask[idx]) continue;
                if (!IsSolidPixel(pixels[idx])) continue;

                // Inicio de una nueva isla: flood-fill localmente para
                // descubrir todos los píxeles del chunk.
                floodQueue.Clear();
                floodQueue.Enqueue(idx);
                visited[idx] = true;

                int minX = textureWidth, maxX = -1;
                int minY = textureHeight, maxY = -1;
                int chunkPixelCount = 0;

                // Para construir el polígono necesitamos saber, por columna,
                // el píxel sólido más alto y más bajo de la isla. Lo guardamos
                // en arrays que reutilizamos por isla (sparse — solo las
                // columnas que la isla cubre).
                // NOTE: usamos diccionarios pequeños para no allocar megabytes.
                var topByCol = new System.Collections.Generic.Dictionary<int, int>();
                var bottomByCol = new System.Collections.Generic.Dictionary<int, int>();

                while (floodQueue.Count > 0)
                {
                    int p = floodQueue.Dequeue();
                    int py = p / textureWidth;
                    int px = p - py * textureWidth;

                    chunkPixelCount++;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;

                    // Actualiza top/bottom de esa columna
                    if (!topByCol.TryGetValue(px, out int t) || py > t) topByCol[px] = py;
                    if (!bottomByCol.TryGetValue(px, out int b) || py < b) bottomByCol[px] = py;

                    // Vecinos
                    if (py + 1 < textureHeight)
                    {
                        int n = p + textureWidth;
                        if (!visited[n] && IsSolidPixel(pixels[n]) && !mainGroundMask[n])
                        { visited[n] = true; floodQueue.Enqueue(n); }
                    }
                    if (py - 1 >= 0)
                    {
                        int n = p - textureWidth;
                        if (!visited[n] && IsSolidPixel(pixels[n]) && !mainGroundMask[n])
                        { visited[n] = true; floodQueue.Enqueue(n); }
                    }
                    if (px - 1 >= 0)
                    {
                        int n = p - 1;
                        if (!visited[n] && IsSolidPixel(pixels[n]) && !mainGroundMask[n])
                        { visited[n] = true; floodQueue.Enqueue(n); }
                    }
                    if (px + 1 < textureWidth)
                    {
                        int n = p + 1;
                        if (!visited[n] && IsSolidPixel(pixels[n]) && !mainGroundMask[n])
                        { visited[n] = true; floodQueue.Enqueue(n); }
                    }
                }

                if (chunkPixelCount < minIslandPixels) continue; // ruido, lo saltamos

                // Construimos el polígono CERRADO de la isla: top contour
                // de izquierda a derecha, luego bottom contour de derecha
                // a izquierda. Solo procesamos columnas que la isla toca.
                var upper = new List<Vector2>();
                var lower = new List<Vector2>();
                for (int x = minX; x <= maxX; x++)
                {
                    if (!topByCol.TryGetValue(x, out int top)) continue;
                    int bot = bottomByCol[x];

                    // +1 / 0: el píxel de top se considera ocupado, así que el
                    // contorno superior va medio píxel por encima → top + 1.
                    upper.Add(new Vector2(x / pixelsPerUnit, (top + 1f) / pixelsPerUnit));
                    lower.Add(new Vector2(x / pixelsPerUnit, bot / pixelsPerUnit));
                }

                if (upper.Count < 2) continue;

                // Cerramos el polígono: upper (izq→der) + lower invertido (der→izq)
                var closed = new List<Vector2>(upper.Count + lower.Count);
                closed.AddRange(upper);
                for (int i = lower.Count - 1; i >= 0; i--) closed.Add(lower[i]);

                // Simplificamos con RDP para no inflar el collider con
                // cientos de vértices micro.
                var simplified = RamerDouglasPeucker(closed, 2f / pixelsPerUnit);
                if (simplified.Count < 3) continue;

                result.Add(simplified.ToArray());
            }

            return result;
        }

        /// <summary>
        ///  Detecta CUEVAS INTERNAS (huecos de aire rodeados de tierra) y
        ///  devuelve sus contornos como polígonos para añadir al PolygonCollider2D
        ///  como AGUJEROS. Sin esto, las cuevas se ven visualmente como huecos
        ///  pero el collider es sólido → la rata choca contra paredes invisibles.
        ///
        ///  Algoritmo:
        ///   1) Flood-fill desde los bordes de la textura sobre píxeles
        ///      transparentes → marca el "aire exterior" (cielo/atmósfera).
        ///   2) Cualquier píxel transparente NO en esa máscara es una CUEVA
        ///      (aire aislado dentro del terreno).
        ///   3) Flood-fill de cada cueva para descubrir sus píxeles.
        ///   4) Para cuevas suficientemente grandes (>= minCavePixels), extraer
        ///      contorno columna a columna igual que las islas flotantes.
        /// </summary>
        private List<Vector2[]> BuildCavePolygons()
        {
            var result = new List<Vector2[]>();
            if (pixels == null) return result;

            int total = textureWidth * textureHeight;

            // Paso 1: máscara de "aire exterior" — todo píxel transparente
            // conectado a los bordes de la textura por vecinos transparentes.
            bool[] exteriorAir = new bool[total];
            var q = new Queue<int>();

            // Sembrar desde los cuatro bordes
            for (int x = 0; x < textureWidth; x++)
            {
                int topIdx = (textureHeight - 1) * textureWidth + x;
                if (!IsSolidPixel(pixels[topIdx]) && !exteriorAir[topIdx])
                { exteriorAir[topIdx] = true; q.Enqueue(topIdx); }
                int botIdx = x;
                if (!IsSolidPixel(pixels[botIdx]) && !exteriorAir[botIdx])
                { exteriorAir[botIdx] = true; q.Enqueue(botIdx); }
            }
            for (int y = 0; y < textureHeight; y++)
            {
                int leftIdx = y * textureWidth;
                if (!IsSolidPixel(pixels[leftIdx]) && !exteriorAir[leftIdx])
                { exteriorAir[leftIdx] = true; q.Enqueue(leftIdx); }
                int rightIdx = y * textureWidth + (textureWidth - 1);
                if (!IsSolidPixel(pixels[rightIdx]) && !exteriorAir[rightIdx])
                { exteriorAir[rightIdx] = true; q.Enqueue(rightIdx); }
            }

            // BFS por vecinos transparentes
            while (q.Count > 0)
            {
                int p = q.Dequeue();
                int py = p / textureWidth;
                int px = p - py * textureWidth;
                if (py + 1 < textureHeight)
                {
                    int n = p + textureWidth;
                    if (!IsSolidPixel(pixels[n]) && !exteriorAir[n]) { exteriorAir[n] = true; q.Enqueue(n); }
                }
                if (py - 1 >= 0)
                {
                    int n = p - textureWidth;
                    if (!IsSolidPixel(pixels[n]) && !exteriorAir[n]) { exteriorAir[n] = true; q.Enqueue(n); }
                }
                if (px - 1 >= 0)
                {
                    int n = p - 1;
                    if (!IsSolidPixel(pixels[n]) && !exteriorAir[n]) { exteriorAir[n] = true; q.Enqueue(n); }
                }
                if (px + 1 < textureWidth)
                {
                    int n = p + 1;
                    if (!IsSolidPixel(pixels[n]) && !exteriorAir[n]) { exteriorAir[n] = true; q.Enqueue(n); }
                }
            }

            // Paso 2/3/4: para cada píxel transparente no en exteriorAir,
            // hacer flood-fill de la cueva y extraer contorno.
            bool[] visitedCave = new bool[total];
            var caveQueue = new Queue<int>();

            for (int idx = 0; idx < total; idx++)
            {
                if (visitedCave[idx]) continue;
                if (IsSolidPixel(pixels[idx])) continue;       // no es aire
                if (exteriorAir[idx]) continue;          // es aire exterior, no cueva

                caveQueue.Clear();
                caveQueue.Enqueue(idx);
                visitedCave[idx] = true;

                int minX = textureWidth, maxX = -1;
                int chunkPixelCount = 0;
                var topByCol = new Dictionary<int, int>();
                var bottomByCol = new Dictionary<int, int>();

                while (caveQueue.Count > 0)
                {
                    int p = caveQueue.Dequeue();
                    int py = p / textureWidth;
                    int px = p - py * textureWidth;

                    chunkPixelCount++;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (!topByCol.TryGetValue(px, out int t) || py > t) topByCol[px] = py;
                    if (!bottomByCol.TryGetValue(px, out int b) || py < b) bottomByCol[px] = py;

                    if (py + 1 < textureHeight)
                    {
                        int n = p + textureWidth;
                        if (!visitedCave[n] && !IsSolidPixel(pixels[n]) && !exteriorAir[n])
                        { visitedCave[n] = true; caveQueue.Enqueue(n); }
                    }
                    if (py - 1 >= 0)
                    {
                        int n = p - textureWidth;
                        if (!visitedCave[n] && !IsSolidPixel(pixels[n]) && !exteriorAir[n])
                        { visitedCave[n] = true; caveQueue.Enqueue(n); }
                    }
                    if (px - 1 >= 0)
                    {
                        int n = p - 1;
                        if (!visitedCave[n] && !IsSolidPixel(pixels[n]) && !exteriorAir[n])
                        { visitedCave[n] = true; caveQueue.Enqueue(n); }
                    }
                    if (px + 1 < textureWidth)
                    {
                        int n = p + 1;
                        if (!visitedCave[n] && !IsSolidPixel(pixels[n]) && !exteriorAir[n])
                        { visitedCave[n] = true; caveQueue.Enqueue(n); }
                    }
                }

                if (chunkPixelCount < minCavePixels) continue;

                // Contorno UN SOLO POLÍGONO por cueva (top de izq→der, bottom
                // de der→izq). Simple y RÁPIDO — captura la forma general de
                // la cueva sin explotar el vertex count.
                // Compromiso: cuevas con formas MUY complejas (S, 8) pueden
                // quedar aproximadas, pero el 99% de las del terreno procedural
                // son cámaras simples que este algoritmo captura perfectamente.
                var upper = new List<Vector2>();
                var lower = new List<Vector2>();
                for (int x = minX; x <= maxX; x++)
                {
                    if (!topByCol.TryGetValue(x, out int top)) continue;
                    int bot = bottomByCol[x];
                    upper.Add(new Vector2(x / pixelsPerUnit, (top + 1f) / pixelsPerUnit));
                    lower.Add(new Vector2(x / pixelsPerUnit, bot / pixelsPerUnit));
                }
                if (upper.Count < 2) continue;

                var closed = new List<Vector2>(upper.Count + lower.Count);
                closed.AddRange(upper);
                for (int i = lower.Count - 1; i >= 0; i--) closed.Add(lower[i]);

                // Simplificación con RDP — reduce vértices manteniendo la forma.
                var simplified = RamerDouglasPeucker(closed, 2f / pixelsPerUnit);
                if (simplified.Count < 3) continue;

                result.Add(simplified.ToArray());
            }

            return result;
        }

        /// <summary>
        ///  Comprueba si queda AL MENOS UN píxel sólido (alpha > 0) en el
        ///  array — solo para decidir si activar el fallback rectangular.
        /// </summary>
        private bool HasAnySolidPixel()
        {
            if (pixels == null) return false;
            // Muestreo: si hay un píxel sólido cada 64, lo pillamos enseguida
            // y ahorramos tiempo con texturas grandes.
            for (int i = 0; i < pixels.Length; i += 64)
                if (IsSolidPixel(pixels[i])) return true;
            return false;
        }

        /// <summary>
        ///  Ramer–Douglas–Peucker iterativo. Reduce un polígono a los vértices
        ///  que realmente aportan forma, descartando puntos cuya desviación
        ///  respecto a la línea "inicio → fin" sea menor que <paramref name="epsilon"/>.
        ///  Versión iterativa con stack (evita stack overflow en paths muy largos).
        /// </summary>
        private static List<Vector2> RamerDouglasPeucker(List<Vector2> points, float epsilon)
        {
            int n = points.Count;
            if (n < 3) return new List<Vector2>(points);

            bool[] keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));

            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                if (last <= first + 1) continue;

                float maxDist = 0f;
                int maxIdx = first;
                Vector2 a = points[first];
                Vector2 b = points[last];

                for (int i = first + 1; i < last; i++)
                {
                    float d = PerpendicularDistance(points[i], a, b);
                    if (d > maxDist)
                    {
                        maxDist = d;
                        maxIdx = i;
                    }
                }

                if (maxDist > epsilon)
                {
                    keep[maxIdx] = true;
                    stack.Push((first, maxIdx));
                    stack.Push((maxIdx, last));
                }
            }

            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
                if (keep[i]) result.Add(points[i]);
            return result;
        }

        /// <summary>
        ///  Distancia perpendicular del punto <paramref name="p"/> a la recta
        ///  que pasa por <paramref name="a"/> y <paramref name="b"/>.
        /// </summary>
        private static float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-12f) return Vector2.Distance(p, a); // a y b coinciden
            float num = Mathf.Abs(dy * p.x - dx * p.y + b.x * a.y - b.y * a.x);
            return num / Mathf.Sqrt(len2);
        }

        /// <summary>
        ///  Red de seguridad: elimina vértices consecutivos cuya distancia al
        ///  anterior sea menor que <paramref name="minDistSq"/>. Se usa después
        ///  del RDP como "limpieza final" antes de entregar al collider.
        /// </summary>
        private static List<Vector2> DropClosePairs(List<Vector2> input, float minDistSq)
        {
            var output = new List<Vector2>(input.Count);
            if (input.Count == 0) return output;
            output.Add(input[0]);
            for (int i = 1; i < input.Count; i++)
            {
                Vector2 last = output[output.Count - 1];
                if ((input[i] - last).sqrMagnitude >= minDistSq)
                    output.Add(input[i]);
            }
            // Asegurarnos de que el cierre no está pegado al primer punto
            if (output.Count >= 2 &&
                (output[output.Count - 1] - output[0]).sqrMagnitude < minDistSq)
            {
                output.RemoveAt(output.Count - 1);
            }
            return output;
        }

        // ============================================================
        //  GIZMOS
        // ============================================================
        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.7f);
            Vector3 size = new Vector3(textureWidth / pixelsPerUnit,
                                       textureHeight / pixelsPerUnit, 0);
            Vector3 center = transform.position + size * 0.5f;
            Gizmos.DrawWireCube(center, size);

            // PINTA EL POLÍGONO DEL COLLIDER EN ROJO BRILLANTE
            // Así vemos si el skyline tiene picos verticales raros
            // (= paredes invisibles) o trincheras (= huecos por los que cae la rata).
            // Se pinta SIN necesidad de seleccionar el GameObject.
            var p = GetComponent<PolygonCollider2D>();
            if (p == null) return;

            Gizmos.color = Color.red;
            Vector3 worldOffset = transform.position;
            for (int pathIdx = 0; pathIdx < p.pathCount; pathIdx++)
            {
                var pts = p.GetPath(pathIdx);
                if (pts == null || pts.Length < 2) continue;
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 a = worldOffset + (Vector3)(Vector2)pts[i];
                    Vector3 b = worldOffset + (Vector3)(Vector2)pts[(i + 1) % pts.Length];
                    Gizmos.DrawLine(a, b);
                }
            }
        }
    }
}
