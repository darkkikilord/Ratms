using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Puente entre el <see cref="DestructibleTerrain"/> (single-player, ignora
    ///  la red) y el MULTIJUGADOR de NGO. Responsabilidades:
    ///
    ///   1) Sincronizar la SEMILLA del mapa: el host la elige al arrancar y la
    ///      mete en un NetworkVariable. Cuando un cliente se une, la recibe
    ///      automáticamente y regenera el MISMO mapa con el
    ///      <see cref="TerrainGenerator"/> local.
    ///
    ///   2) Sincronizar las DESTRUCCIONES: cuando una explosión del servidor
    ///      quiere carvear, llama a <see cref="ServerCarveAt"/>. El servidor
    ///      manda un <see cref="CarveClientRpc"/> a TODOS los clientes (incluido
    ///      el host-como-cliente), que carvean el mismo punto. Resultado:
    ///      todas las máquinas ven el mismo agujero en el mismo sitio.
    ///
    ///  Cómo se coloca en la escena:
    ///   · En el MISMO GameObject donde ya tienes <see cref="DestructibleTerrain"/>
    ///     + <see cref="TerrainGenerator"/>, añade un <see cref="NetworkObject"/>
    ///     (marcado como Scene Object automáticamente) y este script.
    ///   · El NetworkManager persistente del menú spawneará el NetworkObject
    ///     del terreno al cargar la escena → OnNetworkSpawn salta y genera mapa.
    ///
    ///  Comprobación de seguridad: si no hay NetworkManager en la escena
    ///  (estás probando solo, sin pasar por el menú), este componente se
    ///  queda quieto y deja que el <see cref="TerrainGenerator"/> haga su
    ///  Start normal → puedes probar el terreno sin red.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkDestructibleTerrain : NetworkBehaviour
    {
        /// <summary>Singleton para que misil/granada lo encuentren sin referencias explícitas.</summary>
        public static NetworkDestructibleTerrain Instance { get; private set; }

        [Header("Referencias (auto en Awake si están vacías)")]
        [SerializeField] private DestructibleTerrain terrain;
        [SerializeField] private TerrainGenerator generator;

        // ---------------- SEMILLA EN RED ----------------
        // Valor que solo escribe el servidor. Clientes lo reciben al entrar.
        // Ponemos un valor "centinela" (0) para detectar "aún no asignado".
        private readonly NetworkVariable<int> mapSeed =
            new NetworkVariable<int>(0,
                readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);

        private bool generatedForCurrentSeed;

        // ============================================================
        private void Awake()
        {
            Instance = this;

            if (terrain == null)   terrain   = GetComponent<DestructibleTerrain>();
            if (generator == null) generator = GetComponent<TerrainGenerator>();

            // Si hay NetworkManager en la escena, asumimos modo multijugador
            // y tomamos el control: el generator NO debe generar por su cuenta.
            // Si no hay NetworkManager (prueba local), dejamos al generator
            // hacer su Start normal y no nos metemos.
            if (NetworkManager.Singleton != null && generator != null)
            {
                generator.SuppressGenerateOnStart();
            }
        }

        public override void OnNetworkSpawn()
        {
            // Suscribirnos antes que nada — por si el server ya ha escrito el seed
            // antes de que este callback corra en cliente (poco probable, pero...).
            mapSeed.OnValueChanged += OnSeedChanged;

            if (IsServer)
            {
                // El host elige semilla random y la publica. OnValueChanged
                // saltará en este mismo frame en el host y en todos los clientes.
                int seed = Random.Range(int.MinValue, int.MaxValue);
                mapSeed.Value = seed;
                GenerateLocal(seed);
            }
            else
            {
                // Cliente: si el seed ya llegó con el initial sync, úsalo ya.
                if (mapSeed.Value != 0)
                    GenerateLocal(mapSeed.Value);
                // Si aún no (vale 0), esperamos al OnValueChanged.
            }
        }

        public override void OnNetworkDespawn()
        {
            mapSeed.OnValueChanged -= OnSeedChanged;
            if (Instance == this) Instance = null;
        }

        private void OnSeedChanged(int previous, int current)
        {
            // En el host ya hemos generado en OnNetworkSpawn — evitamos
            // regenerar 2 veces por el mismo seed.
            if (generatedForCurrentSeed) return;
            GenerateLocal(current);
        }

        private void GenerateLocal(int seed)
        {
            if (generator == null)
            {
                Debug.LogWarning("[NetworkDestructibleTerrain] No hay TerrainGenerator — no puedo generar el mapa.");
                return;
            }
            generator.GenerateWithSeed(seed);
            generatedForCurrentSeed = true;
        }

        // ============================================================
        //  API DE CARVING EN RED
        // ============================================================

        /// <summary>
        ///  Llámalo desde código del SERVIDOR (ej. <see cref="NetworkMissile.Explode"/>)
        ///  cuando haya que hacer un agujero. El servidor propaga la orden a
        ///  todos los clientes vía ClientRpc — el host también lo ejecuta
        ///  como cliente, así que no hacemos doble carve aquí.
        /// </summary>
        /// <summary>
        ///  (Servidor) Cada explosión que abre un agujero lo avisa aquí: así los
        ///  barriles explotan en cadena y las cajas saltan por los aires.
        /// </summary>
        public static event System.Action<Vector2, float> OnServerExplosion;

        public void ServerCarveAt(Vector2 worldPos, float worldRadius)
        {
            if (!IsServer) return;
            if (worldRadius <= 0f) return;
            CarveClientRpc(worldPos, worldRadius);
            OnServerExplosion?.Invoke(worldPos, worldRadius);
        }

        [ClientRpc]
        private void CarveClientRpc(Vector2 worldPos, float worldRadius)
        {
            // Cada cliente (y el host) carvea localmente en TODOS los terrenos
            // destruibles de la escena. Como el mapa es determinista (misma
            // seed en todos), el agujero cae en el mismo píxel para todos.
            DestructibleTerrain.CarveAllAt(worldPos, worldRadius);
        }

        // ============================================================
        //  REGENERACIÓN EN CALIENTE (útil para probar, opcional)
        // ============================================================

        /// <summary>
        ///  Solo server: vuelve a elegir una semilla y hace que todo el mundo
        ///  regenere el mapa. Útil para hotkey "nueva partida" o pruebas.
        /// </summary>
        public void ServerRegenerateNewMap()
        {
            if (!IsServer) return;
            int newSeed;
            do
            {
                newSeed = Random.Range(int.MinValue, int.MaxValue);
            } while (newSeed == mapSeed.Value); // que no repita justo el mismo

            generatedForCurrentSeed = false;
            mapSeed.Value = newSeed;
            GenerateLocal(newSeed);
        }
    }
}
