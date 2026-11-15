using Unity.Netcode;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  Sincroniza el bioma elegido en el menú (por el HOST) con TODOS los
    ///  clientes que se unen a la partida. Sin esto, el host vería su bioma
    ///  y los clientes verían el fallback → colores del terreno/agua/fondo
    ///  desincronizados en las máquinas.
    ///
    ///  Cómo funciona:
    ///   1) Al arrancar la escena del escenario, este componente se instancia
    ///      como NetworkObject (ponlo en un GameObject "BiomeSyncer" con
    ///      NetworkObject encima, en la escena del juego).
    ///   2) OnNetworkSpawn:
    ///        · Servidor lee <see cref="BiomeManager.SelectedBiomeType"/> static
    ///          (lo que el menú guardó) y lo escribe en el NetworkVariable.
    ///        · Todos los clientes reciben el valor por OnValueChanged y
    ///          actualizan su BiomeManager local + regeneran terreno/agua/fondo.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BiomeSyncer : NetworkBehaviour
    {
        /// <summary>
        ///  Índice del bioma (byte para minimizar el paquete de red).
        ///  Escribe: servidor. Lee: todos.
        /// </summary>
        private readonly NetworkVariable<byte> biomeTypeNet = new(
            value: (byte)BiomeType.Meadow,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>0.27: clima de la partida.</summary>
        private readonly NetworkVariable<byte> weatherNet = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>0.25: modo de juego (Fuertes cambia la forma del mapa en todos los PCs).</summary>
        private readonly NetworkVariable<byte> gameModeNet = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        ///  0.31: ancho del mapa en píxeles (0 = normal). Crece con el número de
        ///  ratas; TODOS los PCs deben generar con el mismo ancho o la misma
        ///  semilla daría mapas distintos.
        /// </summary>
        private readonly NetworkVariable<int> mapWidthNet = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>0.32: forma del mapa (Isla, Archipiélago, Montaña...). Igual que el ancho.</summary>
        private readonly NetworkVariable<byte> mapShapeNet = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            // El ANCHO va antes que nada: ApplyBiome regenera el mapa con él.
            if (IsServer)
            {
                var nm = NetworkManager;
                int teams = Ratms.Networking.MatchRules.Mode switch
                {
                    Ratms.Networking.MatchMode.Local => Ratms.Networking.MatchRules.EffectiveLocalTeams,
                    Ratms.Networking.MatchMode.Online => nm != null ? nm.ConnectedClientsIds.Count : 2,
                    _ => 1,
                };
                mapWidthNet.Value = Ratms.Networking.MatchRules.MapWidthFor(teams, Ratms.Networking.MatchRules.EffectiveRatsPerTeam);
                Debug.Log($"[BiomeSyncer] {teams} equipos → ancho del mapa {(mapWidthNet.Value > 0 ? mapWidthNet.Value.ToString() : "normal")}");
            }
            if (IsServer)
            {
                mapShapeNet.Value = (byte)Ratms.Networking.MatchRules.PickMapShape();
                Debug.Log($"[BiomeSyncer] Forma del mapa: {(MapShape)mapShapeNet.Value}");
            }
            TerrainGenerator.MatchShape = (MapShape)mapShapeNet.Value;
            mapShapeNet.OnValueChanged += (_, v) =>
            {
                if (IsServer || TerrainGenerator.MatchShape == (MapShape)v) return;
                TerrainGenerator.MatchShape = (MapShape)v;
                ApplyBiome((BiomeType)biomeTypeNet.Value);
            };
            TerrainGenerator.MatchWidth = mapWidthNet.Value;
            mapWidthNet.OnValueChanged += (_, v) =>
            {
                if (IsServer || TerrainGenerator.MatchWidth == v) return;
                TerrainGenerator.MatchWidth = v;
                ApplyBiome((BiomeType)biomeTypeNet.Value);
            };

            biomeTypeNet.OnValueChanged += OnBiomeTypeChanged;
            gameModeNet.OnValueChanged += (_, v) =>
            {
                if (IsServer) return;
                Ratms.Networking.MatchRules.Game = (Ratms.Networking.GameMode)v;
                ApplyBiome((BiomeType)biomeTypeNet.Value);
            };
            if (IsServer) gameModeNet.Value = (byte)Ratms.Networking.MatchRules.EffectiveGame;
            else Ratms.Networking.MatchRules.Game = (Ratms.Networking.GameMode)gameModeNet.Value;

            // 0.27: clima (el host lo sortea si está "al azar")
            weatherNet.OnValueChanged += (_, v) => Ratms.Networking.WeatherFx.Current = (Ratms.Networking.Weather)v;
            if (IsServer) weatherNet.Value = (byte)Ratms.Networking.MatchRules.PickWeather();
            Ratms.Networking.WeatherFx.Current = (Ratms.Networking.Weather)weatherNet.Value;
            Ratms.Networking.WeatherFx.Ensure();

            if (IsServer)
            {
                // El servidor lee lo que el menú guardó y publica.
                BiomeType t = BiomeManager.SelectedBiomeType ?? BiomeType.Meadow;
                biomeTypeNet.Value = (byte)t;
                Debug.Log($"[BiomeSyncer] Servidor publica bioma: {t}");

                // El servidor también aplica el cambio (por si ya estaba en Meadow
                // — el OnValueChanged no dispara si el valor no cambia).
                ApplyBiome(t);
            }
            else
            {
                // Cliente: aplica el valor actual del NetVar (que ya llegó al conectar).
                var t = (BiomeType)biomeTypeNet.Value;
                Debug.Log($"[BiomeSyncer] Cliente recibe bioma: {t}");
                ApplyBiome(t);
            }
        }

        public override void OnNetworkDespawn()
        {
            Ratms.Networking.WeatherFx.Current = Ratms.Networking.Weather.Despejado;
            TerrainGenerator.MatchWidth = 0;   // la siguiente partida decide de nuevo
            TerrainGenerator.MatchShape = MapShape.Normal;
            biomeTypeNet.OnValueChanged -= OnBiomeTypeChanged;
        }

        private void OnBiomeTypeChanged(byte oldValue, byte newValue)
        {
            ApplyBiome((BiomeType)newValue);
        }

        /// <summary>
        ///  Cambia el bioma activo Y regenera todos los visuales dependientes:
        ///  terreno, agua, fondo. Se llama en cada peer para que todos vean lo
        ///  mismo.
        /// </summary>
        private void ApplyBiome(BiomeType type)
        {
            BiomeManager.SelectedBiomeType = type;
            var bm = BiomeManager.Instance;
            if (bm != null) bm.SetCurrentBiome(type);

            // Regenerar terreno, agua y fondo con los colores del nuevo bioma.
            // Buscamos cada componente en la escena y le pedimos regenerar.
            // OJO: con la MISMA semilla. Antes se llamaba a Generate(), que
            // sortea semilla nueva: si en un PC este objeto arrancaba DESPUÉS
            // del NetworkDestructibleTerrain, ese PC se quedaba con otro mapa
            // (en la alcantarilla, el cliente veía a las ratas bajo tierra).
            var terrainGen = Object.FindFirstObjectByType<TerrainGenerator>();
            if (terrainGen != null) terrainGen.RegenerateSameSeed();

            var water = Object.FindFirstObjectByType<WaterDeathZone>();
            if (water != null) water.RebuildVisuals();

            var bg = Object.FindFirstObjectByType<BackgroundManager>();
            if (bg != null) bg.RebuildVisuals();

            var caveBackdrop = Object.FindFirstObjectByType<TerrainCaveBackdrop>();
            if (caveBackdrop != null) caveBackdrop.RebuildBackdrop();
        }
    }
}
