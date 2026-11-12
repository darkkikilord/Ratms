using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  MANAGER DE BIOMAS — singleton en la escena de juego.
    ///
    ///  Responsabilidades:
    ///   · Guardar la lista de biomas disponibles (Meadow, Sewer, ...).
    ///   · Decidir cuál está activo en esta partida.
    ///   · Servir el bioma activo a TerrainGenerator, WaterDeathZone y
    ///     BackgroundManager al arrancar.
    ///
    ///  Cómo se elige el bioma:
    ///   1) Si <see cref="SelectedBiomeType"/> está seteado externamente
    ///      (por el menú multiplayer al crear la partida), se usa ese.
    ///   2) Si no, se usa el <see cref="fallbackType"/> del inspector.
    ///
    ///  Es un MonoBehaviour normal — no necesita NetworkBehaviour porque
    ///  el bioma se elige ANTES de cargar la escena de juego (viaja como
    ///  simple estado global static). El servidor propaga la elección
    ///  cargando la escena con el bioma correcto para todos.
    /// </summary>
    [DisallowMultipleComponent]
    public class BiomeManager : MonoBehaviour
    {
        public static BiomeManager Instance { get; private set; }

        [Header("Biomas disponibles")]
        [Tooltip("Lista de biomas builtin. Si la dejas vacía, se generan Meadow y Sewer por defecto.")]
        [SerializeField] private BiomeDefinition[] biomes;

        [Header("Bioma por defecto")]
        [Tooltip("Se usa si el menú no seleccionó ninguno (partidas launcheadas directamente en el editor).")]
        [SerializeField] private BiomeType fallbackType = BiomeType.Meadow;

        /// <summary>
        ///  Tipo de bioma elegido por el menú antes de cargar la escena.
        ///  Es static para sobrevivir al cambio de escena sin necesitar
        ///  DontDestroyOnLoad. El MultiplayerMenuController lo setea y el
        ///  BiomeManager lo lee en su Awake.
        /// </summary>
        public static BiomeType? SelectedBiomeType;

        /// <summary>Bioma activo en esta partida. Null hasta que Awake corra.</summary>
        public BiomeDefinition Current { get; private set; }

        private void Awake()
        {
            Instance = this;

            // Si el inspector no trae biomas, usamos los builtin
            if (biomes == null || biomes.Length == 0)
                biomes = BuildDefaultBiomes();

            // Determinamos qué bioma sale esta partida
            BiomeType chosen = SelectedBiomeType ?? fallbackType;
            Current = FindBiome(chosen) ?? biomes[0];

            Debug.Log($"[BiomeManager] Bioma activo: {Current.biomeName} (type={Current.type})");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private BiomeDefinition FindBiome(BiomeType type)
        {
            foreach (var b in biomes)
                if (b != null && b.type == type) return b;
            return null;
        }

        /// <summary>
        ///  Devuelve la lista de biomas conocidos. Útil para el menú
        ///  (mostrar opciones al host).
        /// </summary>
        public BiomeDefinition[] GetAllBiomes() => biomes;

        /// <summary>
        ///  Cambia el bioma activo en runtime (llamado por BiomeSyncer cuando el
        ///  servidor publica su elección). Los generadores dependientes
        ///  (TerrainGenerator, WaterDeathZone, BackgroundManager) deben regenerarse
        ///  después con sus métodos propios — este método NO los toca directamente.
        /// </summary>
        public void SetCurrentBiome(BiomeType type)
        {
            var found = FindBiome(type);
            if (found == null)
            {
                Debug.LogWarning($"[BiomeManager] SetCurrentBiome({type}) — no encontrado. Manteniendo {Current?.biomeName}.");
                return;
            }
            Current = found;
            Debug.Log($"[BiomeManager] Bioma cambiado en runtime a: {Current.biomeName}");
        }

        // ==============================================================
        //  Biomas builtin — se usan si no hay lista en el inspector
        // ==============================================================
        private static BiomeDefinition[] BuildDefaultBiomes()
        {
            return new[] { BuildMeadow(), BuildSewer() };
        }

        private static BiomeDefinition BuildMeadow()
        {
            return new BiomeDefinition
            {
                biomeName = "Pradera",
                type = BiomeType.Meadow,

                // Terreno clásico Worms
                grassColor        = new Color32(75, 150, 65, 255),
                grassOutlineColor = new Color32(200, 230, 90, 255),
                dirtColor         = new Color32(140, 92, 55, 255),
                dirtColorMid      = new Color32(110, 72, 42, 255),
                dirtColorDark     = new Color32(75, 48, 26, 255),

                // Agua mar turquesa
                waterColor      = new Color(0.08f, 0.42f, 0.48f, 0.95f),
                waterDeepColor  = new Color(0.02f, 0.15f, 0.25f, 1f),
                foamColor       = new Color(1f, 1f, 1f, 1f),
                foamAccentColor = new Color(0.75f, 0.9f, 1f, 0.9f),

                // Cielo azul con horizonte cálido
                skyTop    = new Color(0.42f, 0.62f, 0.88f, 1f),
                skyBottom = new Color(0.9f, 0.85f, 0.65f, 1f),

                hasClouds = true,
                cloudColor = new Color(1f, 1f, 1f, 0.88f),
                cloudDensity = 0.55f,

                silhouetteFar  = new Color(0.42f, 0.55f, 0.45f, 0.55f),
                silhouetteNear = new Color(0.22f, 0.35f, 0.28f, 0.85f),
                parallaxStyle  = ParallaxStyle.Hills,

                hasNeonLights = false,
            };
        }

        private static BiomeDefinition BuildSewer()
        {
            return new BiomeDefinition
            {
                biomeName = "Alcantarilla",
                type = BiomeType.Sewer,

                // Terreno: hormigón sucio + musgo verdoso
                grassColor        = new Color32(95, 115, 55, 255),   // musgo/moho oliva
                grassOutlineColor = new Color32(140, 165, 75, 255),  // musgo brillante en el borde
                dirtColor         = new Color32(95, 88, 82, 255),    // hormigón medio
                dirtColorMid      = new Color32(75, 70, 66, 255),    // hormigón sucio
                dirtColorDark     = new Color32(50, 46, 44, 255),    // hormigón oscuro

                // Agua alcantarilla verdosa
                waterColor      = new Color(0.28f, 0.42f, 0.15f, 0.95f),   // verde turbio
                waterDeepColor  = new Color(0.12f, 0.18f, 0.06f, 1f),      // verde muy oscuro
                foamColor       = new Color(0.85f, 0.9f, 0.65f, 0.9f),     // espuma amarillenta
                foamAccentColor = new Color(0.55f, 0.65f, 0.35f, 0.85f),   // acento verde-marrón

                // Cielo contaminado gris-amarillento
                skyTop    = new Color(0.28f, 0.28f, 0.32f, 1f),
                skyBottom = new Color(0.55f, 0.48f, 0.38f, 1f),   // horizonte sucio

                hasClouds = true,
                cloudColor = new Color(0.35f, 0.32f, 0.3f, 0.85f),   // humo gris
                cloudDensity = 0.7f,   // más denso, más "cielo tapado"

                silhouetteFar  = new Color(0.18f, 0.16f, 0.2f, 0.7f),
                silhouetteNear = new Color(0.08f, 0.07f, 0.1f, 0.95f),
                parallaxStyle  = ParallaxStyle.Buildings,

                hasNeonLights = true,
                neonColor = new Color(1f, 0.35f, 0.6f, 0.85f),   // rosa neón
            };
        }
    }
}
