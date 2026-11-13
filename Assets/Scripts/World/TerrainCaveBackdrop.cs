using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  Capa OSCURA detrás del terreno, con la FORMA del terreno (no un rectángulo).
    ///
    ///  Antes usaba un cuadrado que enmarcaba todo el mapa → feísimo, se veía
    ///  el borde rectangular. Ahora genera una textura del mismo tamaño que la
    ///  del terreno, donde solo los píxeles POR DEBAJO de la línea de altura
    ///  original son opacos. Los píxeles sobre el suelo (cielo + zonas de
    ///  cráteres altos) son transparentes → dejan ver el sky del BackgroundManager.
    ///
    ///  Resultado: las cuevas dentro del terreno se ven marrón oscuro (sensación
    ///  de "más tierra detrás"), y las islas flotantes / zonas de cielo siguen
    ///  viendo el cielo normal.
    ///
    ///  Se auto-instala desde TerrainGenerator si no existe en la escena.
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainCaveBackdrop : MonoBehaviour
    {
        [Header("Capa oscura de cuevas")]
        [Tooltip("SortingOrder de la capa. Debe estar ENTRE el sky del BackgroundManager " +
                 "(-30) y el terreno (que suele ser 0). Un valor de -5 va bien.")]
        [SerializeField] private int sortingOrder = -5;

        [Tooltip("Multiplicador de oscuridad respecto al color de tierra oscura del bioma. " +
                 "0 = negro puro (feo). 0.55 = marrón oscuro (bien). 1 = mismo tono que la tierra.")]
        [Range(0f, 1f)] [SerializeField] private float darknessFactor = 0.55f;

        [Tooltip("Intensidad del grano interno de la cueva (0 = plano, 1 = muy marcado). " +
                 "Da sensación de humedad/roca en lugar de un color plano.")]
        [Range(0f, 1f)] [SerializeField] private float caveGrainStrength = 0.35f;

        [Tooltip("Alpha del backdrop. 1 = totalmente opaco (recomendado — así no se ven " +
                 "las nubes/siluetas moviéndose detrás de las cuevas).")]
        [Range(0f, 1f)] [SerializeField] private float alpha = 1f;

        [Tooltip("Cuántos píxeles POR DEBAJO de la superficie empieza el fondo oscuro. " +
                 "Antes sobresalía 3 px POR ENCIMA de la hierba: se veía una línea negra en todo el borde del " +
                 "mapa, justo a la altura de los pies de la rata, y parecía que estaba hundida en el suelo. " +
                 "Con un valor positivo el fondo queda siempre escondido detrás del terreno.")]
        [SerializeField] private int insetBelowSurfacePx = 4;

        [Header("Debug")]
        [SerializeField] private bool debugLog = true;

        private GameObject backdropGO;
        private SpriteRenderer backdropRenderer;

        private void Start()
        {
            StartCoroutine(BuildAfterTerrainReady());
        }

        private System.Collections.IEnumerator BuildAfterTerrainReady()
        {
            // Esperar un frame a que TerrainGenerator.Generate() haya corrido
            // y tengamos heightLine disponible.
            yield return null;
            Build();
        }

        /// <summary>
        ///  Reconstruye la capa. Llamado desde BiomeSyncer si cambia el bioma
        ///  (los colores dependen del bioma activo).
        /// </summary>
        public void RebuildBackdrop()
        {
            if (backdropGO != null) DestroyImmediate(backdropGO);
            backdropGO = null;
            Build();
        }

        private void Build()
        {
            var terrainGen = Object.FindFirstObjectByType<TerrainGenerator>();
            var terrain = Object.FindFirstObjectByType<DestructibleTerrain>();
            if (terrainGen == null || terrain == null)
            {
                Debug.LogWarning("[TerrainCaveBackdrop] Falta TerrainGenerator o DestructibleTerrain en la escena.");
                return;
            }

            if (!terrainGen.HasHeightLine)
            {
                Debug.LogWarning("[TerrainCaveBackdrop] heightLine aún no está lista. Reintentando en 1 frame.");
                StartCoroutine(BuildAfterTerrainReady());
                return;
            }

            int[] heightLine = terrainGen.GetHeightLine();
            int w = terrainGen.TextureWidth;
            int h = terrainGen.TextureHeight;

            // Color del backdrop: mezcla el tono oscuro de tierra del bioma con negro
            Color baseColor = new Color32(85, 55, 30, 255);
            var bm = BiomeManager.Instance;
            if (bm != null && bm.Current != null)
                baseColor = bm.Current.dirtColorDark;

            // Color base oscuro y una variante más oscura para el grano.
            Color dark = Color.Lerp(Color.black, baseColor, darknessFactor);
            Color darker = Color.Lerp(Color.black, dark, 0.55f);
            dark.a = alpha;
            darker.a = alpha;
            Color32 transparent = new Color32(0, 0, 0, 0);

            // Generar textura con la forma del terreno + GRANO INTERNO tipo cueva.
            // Solo píxeles bajo el suelo son opacos; encima → transparente.
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                int surfaceY = heightLine[x] - Mathf.Max(0, insetBelowSurfacePx);
                for (int y = 0; y < h; y++)
                {
                    if (y < surfaceY)
                    {
                        // Grano interno con Perlin 2D — da sensación de roca
                        // húmeda o musgo oscuro dentro de las cuevas, no un
                        // color plano artificial.
                        float noise = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                        Color c = Color.Lerp(dark, darker, noise * caveGrainStrength);
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

            // El sprite ocupa exactamente el mismo tamaño de mundo que el terreno
            float ppu = terrain.PixelsPerUnit;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                                        new Vector2(0f, 0f),   // pivot bottom-left, igual que terreno
                                        ppu, 0, SpriteMeshType.FullRect);

            Vector3 tPos = terrain.transform.position;

            backdropGO = new GameObject("TerrainCaveBackdrop");
            backdropGO.transform.SetParent(transform, worldPositionStays: false);
            backdropRenderer = backdropGO.AddComponent<SpriteRenderer>();
            backdropRenderer.sprite = sprite;
            backdropRenderer.color = Color.white;   // el color va EN la textura
            backdropRenderer.sortingOrder = sortingOrder;
            backdropRenderer.sortingLayerName = "Default";
            backdropGO.transform.position = new Vector3(tPos.x, tPos.y, 0f);
            backdropGO.transform.localScale = Vector3.one;

            if (debugLog)
                Debug.Log($"[TerrainCaveBackdrop] Creado con máscara de terreno: " +
                          $"tamaño={w}x{h}px, ppu={ppu}, color={dark}, sortingOrder={sortingOrder}");
        }
    }
}
