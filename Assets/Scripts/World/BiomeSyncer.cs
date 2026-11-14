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

        public override void OnNetworkSpawn()
        {
            biomeTypeNet.OnValueChanged += OnBiomeTypeChanged;

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
