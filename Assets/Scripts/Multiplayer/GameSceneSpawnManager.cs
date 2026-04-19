using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  Manager del ESCENARIO DE JUEGO (Escenario 1, 2, 3...).
    ///
    ///  Responsabilidades:
    ///    • Al entrar en la escena EN EL SERVIDOR, spawnea una rata por cada
    ///      cliente conectado, colocándolas en los SpawnPoints asignados.
    ///    • Asigna a cada rata un COLOR distinto (rojo, azul, verde, amarillo)
    ///      según el orden de conexión, para que se distingan en pantalla.
    ///    • Oculta el menú del MainMenu si seguía visible (defensa).
    ///
    ///  CUÁNDO SPAWNEA (muy importante):
    ///    El spawn NO se hace en OnNetworkSpawn como antes, porque ese momento
    ///    ocurría DEMASIADO PRONTO: el servidor intentaba spawnear al cliente
    ///    cuando el cliente aún no había terminado de cargar la escena, y los
    ///    mensajes de spawn se perdían (resultado: el cliente veía 0 ratas).
    ///
    ///    Ahora escuchamos a NGO.SceneManager.OnLoadEventCompleted, que se
    ///    dispara CUANDO TODOS LOS CLIENTES han acabado de cargar la escena.
    ///    Ahí sí es seguro spawnear — el cliente ya está listo para recibir.
    ///
    ///  Requisitos:
    ///    • Este script debe vivir en la escena del escenario (añadido a un
    ///      GameObject vacío llamado p.ej. "GameSceneManager") con NetworkObject.
    ///    • Tiene que tener asignado el prefab de la rata (rat.network) y los
    ///      SpawnPoints en el inspector.
    ///    • IMPORTANTE: Desactiva AutoSpawnPlayerPrefabClientSide y deja el
    ///      PlayerPrefab VACÍO en el NetworkManager, porque aquí hacemos el
    ///      spawn manual para controlar posición y color.
    /// </summary>
    public class GameSceneSpawnManager : NetworkBehaviour
    {
        [Header("Spawn")]
        [Tooltip("Prefab de la rata en red. Debe tener NetworkObject + NetworkPlayerMovement + NetworkHealth + NetworkBazooka + WeaponsMenuUI.")]
        [SerializeField] private GameObject ratNetworkPrefab;

        [Tooltip("Puntos de aparición. El orden de conexión define qué punto toca a cada cliente (0 al primero, 1 al segundo, etc.).")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("Colores por equipo (opcional)")]
        [Tooltip("Se asignan por orden de conexión al NetworkPlayerMovement.teamColor, si existe.")]
        [SerializeField]
        private Color[] teamColors = new Color[]
        {
            new Color(0.90f, 0.25f, 0.25f), // rojo
            new Color(0.25f, 0.55f, 0.95f), // azul
            new Color(0.30f, 0.85f, 0.40f), // verde
            new Color(0.95f, 0.85f, 0.30f), // amarillo
        };

        [Header("Debug")]
        [Tooltip("Si está activo, escribe logs claros en la Consola para depurar quién se spawnea y cuándo.")]
        [SerializeField] private bool debugLogs = true;

        // Mantenemos referencia a las ratas spawneadas por si queremos limpiarlas al salir.
        private readonly Dictionary<ulong, NetworkObject> spawnedRats = new();

        // Para evitar suscribirnos dos veces si OnNetworkSpawn se llama más de una vez
        private bool subscribed;

        // ================================================================
        public override void OnNetworkSpawn()
        {
            // Al cargar la escena, escondemos el menú si quedó visible.
            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            if (menu != null) menu.HideAllPanels();

            // Aviso si no hay NetworkTurnManager en la escena: sin él, el juego no
            // respeta turnos y todos los jugadores pueden moverse a la vez.
            if (FindFirstObjectByType<NetworkTurnManager>() == null)
            {
                Debug.LogWarning("[GameSceneSpawnManager] ⚠️ No hay NetworkTurnManager en la escena. Añade el componente NetworkTurnManager al GameObject 'GameSceneManager' (mismo objeto que este script) para que funcionen los turnos.");
            }

            if (!IsServer) return;

            if (debugLogs)
                Debug.Log($"[GameSceneSpawnManager] OnNetworkSpawn (Server). ConnectedClients en este momento: {NetworkManager.Singleton.ConnectedClientsIds.Count}");

            // Nos suscribimos al evento "todos los clientes han cargado la escena".
            // Este es el momento CORRECTO para spawnear, no OnNetworkSpawn, porque
            // OnLoadEventCompleted garantiza que el cliente está listo para recibir
            // los mensajes de spawn.
            if (!subscribed && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
                subscribed = true;
            }

            // Fallback: si el GameSceneManager se activa en una escena que ya estaba
            // cargada (por ejemplo, si alguien entra directo a Escenario 1 desde el
            // editor sin pasar por el menú), spawneamos con los clientes actuales.
            // En el flujo normal (host pulsa "Empezar" en lobby) esto no dispara
            // nada porque spawnedRats estará vacío pero OnLoadEventCompleted lo hará.
            if (spawnedRats.Count == 0)
            {
                // Pequeño delay para que, si estamos en medio de un scene load,
                // llegue primero OnLoadEventCompleted. Si no llega, spawneamos aquí.
                Invoke(nameof(FallbackSpawnIfNeeded), 1.0f);
            }

            // Si llega uno más tarde (reconexión, etc.)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedLate;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedLate;
                if (NetworkManager.Singleton.SceneManager != null && subscribed)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                    subscribed = false;
                }
            }
        }

        // ================================================================
        //   EVENTO: todos los clientes han terminado de cargar la escena
        // ================================================================
        private void HandleLoadEventCompleted(string sceneName,
                                              LoadSceneMode loadSceneMode,
                                              List<ulong> clientsCompleted,
                                              List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;

            if (debugLogs)
            {
                Debug.Log($"[GameSceneSpawnManager] OnLoadEventCompleted → escena '{sceneName}'. " +
                          $"Clientes que terminaron de cargar: {clientsCompleted.Count} " +
                          $"({string.Join(", ", clientsCompleted)}). " +
                          $"TimedOut: {clientsTimedOut.Count}");
            }

            // Iteramos TODOS los clientes conectados (no solo los que completaron
            // esta carga, por si alguno ya estaba dentro). Usamos un índice incremental
            // basado en spawnedRats.Count para ir repartiendo SpawnPoints y colores.
            var all = NetworkManager.Singleton.ConnectedClientsIds;
            foreach (var clientId in all)
            {
                if (spawnedRats.ContainsKey(clientId)) continue;
                int index = spawnedRats.Count;
                SpawnRatForClient(clientId, index);
            }
        }

        // ================================================================
        //   FALLBACK: si OnLoadEventCompleted no llegó (p.ej. entramos a
        //   la escena en el editor sin pasar por el menú), spawneamos igual.
        // ================================================================
        private void FallbackSpawnIfNeeded()
        {
            if (!IsServer) return;
            if (spawnedRats.Count > 0) return; // ya se spawneó por el evento correcto

            if (debugLogs)
                Debug.Log($"[GameSceneSpawnManager] Fallback: no llegó OnLoadEventCompleted en 1s, spawneo a los {NetworkManager.Singleton.ConnectedClientsIds.Count} clientes conectados.");

            var all = NetworkManager.Singleton.ConnectedClientsIds;
            foreach (var clientId in all)
            {
                if (spawnedRats.ContainsKey(clientId)) continue;
                int index = spawnedRats.Count;
                SpawnRatForClient(clientId, index);
            }
        }

        // ================================================================
        //   Reconexión o client que llega tarde (raro pero posible)
        // ================================================================
        private void OnClientConnectedLate(ulong clientId)
        {
            if (!IsServer) return;
            if (spawnedRats.ContainsKey(clientId)) return;

            if (debugLogs)
                Debug.Log($"[GameSceneSpawnManager] OnClientConnectedLate → clientId={clientId}, le spawneamos su rata.");

            int index = spawnedRats.Count;
            SpawnRatForClient(clientId, index);
        }

        // ================================================================
        //   SPAWN
        // ================================================================
        private void SpawnRatForClient(ulong clientId, int index)
        {
            if (ratNetworkPrefab == null)
            {
                Debug.LogError("[GameSceneSpawnManager] ratNetworkPrefab no asignado en el inspector.");
                return;
            }

            // Posición: SpawnPoint correspondiente (o (0,0,0) si no hay suficientes).
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int sp = index % spawnPoints.Length;
                if (spawnPoints[sp] != null)
                {
                    pos = spawnPoints[sp].position;
                    rot = spawnPoints[sp].rotation;
                }
            }

            GameObject go = Instantiate(ratNetworkPrefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            if (no == null)
            {
                Debug.LogError("[GameSceneSpawnManager] El prefab no tiene NetworkObject.");
                Destroy(go);
                return;
            }

            // SpawnAsPlayerObject: marca esta rata como el "player object" de ese cliente,
            // así NGO asigna la propiedad correctamente.
            no.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            spawnedRats[clientId] = no;

            if (debugLogs)
                Debug.Log($"[GameSceneSpawnManager] ✅ Spawned Rat para clientId={clientId} en {pos} (index {index}). NetId={no.NetworkObjectId}");

            // Color de equipo (opcional, si el NetworkPlayerMovement lo soporta).
            ApplyTeamColor(go, index);
        }

        private void ApplyTeamColor(GameObject ratGo, int index)
        {
            if (teamColors == null || teamColors.Length == 0) return;
            Color col = teamColors[index % teamColors.Length];

            // Buscamos un SpriteRenderer hijo y le ponemos el color; si hay
            // varios, coloreamos todos. Esto es un "tint" suave.
            var srs = ratGo.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (var sr in srs)
            {
                // No queremos colorear cosas que no son el cuerpo (p.ej. el bazooka
                // que ya tiene su propia imagen). Aplicamos el tint solo a los
                // sprites cuyo nombre NO contenga "bazooka" ni "lanzamisiles".
                string n = sr.gameObject.name.ToLowerInvariant();
                if (n.Contains("bazooka") || n.Contains("lanzamisiles") || n.Contains("dot")) continue;

                // Mezcla suave para no perder el sprite original.
                sr.color = Color.Lerp(Color.white, col, 0.6f);
            }
        }
    }
}
