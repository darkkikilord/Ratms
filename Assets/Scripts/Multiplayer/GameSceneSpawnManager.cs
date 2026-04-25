using System.Collections.Generic;
using Ratms.World;
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

        [Header("Adaptar al terreno procedural")]
        [Tooltip("Si ON, al spawnear una rata ignoramos la Y del SpawnPoint y la pegamos a la superficie del mapa procedural actual. Usa la X del SpawnPoint como sitio lateral, pero la altura se calcula sola — así no salen ratas flotando ni enterradas aunque el mapa cambie cada partida.")]
        [SerializeField] private bool snapToTerrainSurface = true;

        [Tooltip("Radio lateral (en PÍXELES del mapa, no unidades de mundo) en el que buscamos la columna más plana alrededor del SpawnPoint. 0 = usa la X exacta. 80 px ≈ 2.5 unidades con PPU=32 — suele ser suficiente para evitar picos afilados. Sube si tus SpawnPoints pueden caer cerca de paredes.")]
        [SerializeField] private int surfaceSearchRadiusPx = 80;

        [Tooltip("Separación extra hacia ARRIBA (unidades de mundo) sobre el píxel de superficie. Evita que la rata aparezca con los pies clavados dentro de la hierba o, peor, se cuele dentro del terreno. 2.5 deja a la rata cayendo desde bastante arriba — se asienta sola por gravedad.")]
        [SerializeField] private float surfaceOffsetY = 2.5f;

        [Header("Retraso inicial antes de spawnear")]
        [Tooltip("Segundos que esperamos desde OnLoadEventCompleted antes de instanciar las ratas. Da tiempo a que el PolygonCollider2D del terreno destructible se reconstruya y esté listo para chocar. 0.4s suele bastar; súbelo si las ratas siguen atravesando el suelo.")]
        [SerializeField] private float initialSpawnDelay = 0.4f;

        [Header("Verificación anti-clipping")]
        [Tooltip("Si ON, al spawnear comprobamos con un OverlapCircle que no estamos metiendo a la rata dentro del terreno. Si la detectamos clipeada, subimos la Y hasta dejarla limpia.")]
        [SerializeField] private bool verifySpawnIsClear = true;

        [Tooltip("Radio del círculo de verificación (unidades de mundo). Aproximadamente el tamaño de la rata.")]
        [SerializeField] private float spawnClearRadius = 0.5f;

        [Tooltip("Layers que consideramos 'terreno sólido' para la comprobación anti-clipping. Déjalo en Default si tu terreno está en ese layer; si lo tienes en un layer propio (Terrain, Ground...), selecciónalo aquí.")]
        [SerializeField] private LayerMask terrainLayerMask = ~0;

        [Tooltip("Cuántas veces como mucho subimos la Y buscando un sitio limpio antes de rendirnos.")]
        [SerializeField] private int maxClearAttempts = 10;

        [Tooltip("Cuánto subimos la Y en cada intento (unidades de mundo).")]
        [SerializeField] private float clearAttemptStep = 0.6f;

        [Header("Red de seguridad — 'kill zone'")]
        [Tooltip("Si una rata cae por debajo de esta Y de mundo (por bug, explosión, lo que sea), el servidor la re-teleporta sobre la superficie del mapa en vez de dejarla caer al vacío infinito. Pon un valor claramente por debajo del terreno. -20 suele valer si el terreno arranca en Y=0.")]
        [SerializeField] private float fallKillY = -10f;

        [Tooltip("Cada cuántos segundos comprobamos si alguna rata ha caído al vacío. No hace falta revisar cada frame — con 0.5s es más que suficiente.")]
        [SerializeField] private float fallCheckInterval = 0.5f;

        private float fallCheckTimer;

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
        //   VIGILANCIA DE RATAS QUE CAEN AL VACÍO
        // ================================================================
        private void Update()
        {
            if (!IsServer) return;
            fallCheckTimer += Time.deltaTime;
            if (fallCheckTimer < fallCheckInterval) return;
            fallCheckTimer = 0f;

            foreach (var kv in spawnedRats)
            {
                var no = kv.Value;
                if (no == null || !no.IsSpawned) continue;
                if (no.transform.position.y > fallKillY) continue;

                // Rata en el limbo → le buscamos otra vez un sitio bueno encima del terreno
                // y la teleportamos allí. Paramos su velocidad para que no rebote en cuanto aterrice.
                int spIndex = 0;
                int i = 0;
                foreach (var k in spawnedRats.Keys)
                {
                    if (k == kv.Key) { spIndex = i; break; }
                    i++;
                }
                Vector3 rescue = ResolveSpawnPosition(spIndex);
                no.transform.position = rescue;
                var rb = no.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
                if (debugLogs)
                    Debug.Log($"[GameSceneSpawnManager] 💀→♻️ Rata clientId={kv.Key} se cayó al vacío (y<{fallKillY}). La reubico en {rescue}.");
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

            // Retrasamos el spawn unos frames para que el PolygonCollider2D del
            // terreno destructible haya terminado de reconstruirse. Si spawneamos
            // demasiado pronto, la rata cae antes de que exista el collider y
            // atraviesa el suelo → vacío.
            StartCoroutine(DelayedSpawnAll());
        }

        private System.Collections.IEnumerator DelayedSpawnAll()
        {
            yield return new WaitForSeconds(initialSpawnDelay);

            if (!IsServer) yield break;
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

            Vector3 pos = ResolveSpawnPosition(index);
            Quaternion rot = Quaternion.identity;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int sp = index % spawnPoints.Length;
                if (spawnPoints[sp] != null)
                    rot = spawnPoints[sp].rotation;
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

        /// <summary>
        ///  Calcula la posición de mundo donde debe aparecer una rata a partir
        ///  de su <paramref name="index"/> (0, 1, 2...). Usa el SpawnPoint del
        ///  array para la X y, si el terreno procedural está listo y el toggle
        ///  está ON, pega la Y a la superficie actual del mapa.
        ///
        ///  También se usa como "fallback de rescate" cuando una rata cae al
        ///  vacío — la re-teleportamos a este sitio.
        /// </summary>
        private Vector3 ResolveSpawnPosition(int index)
        {
            Vector3 pos = Vector3.zero;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int sp = index % spawnPoints.Length;
                if (spawnPoints[sp] != null)
                    pos = spawnPoints[sp].position;
            }

            if (snapToTerrainSurface)
            {
                var generator = FindFirstObjectByType<TerrainGenerator>();
                if (generator != null && generator.HasHeightLine)
                {
                    Vector3? surface = generator.GetSurfaceWorldPos(pos.x, surfaceSearchRadiusPx);
                    if (surface.HasValue)
                    {
                        Vector3 s = surface.Value;
                        // Si hay búsqueda de zona plana, usamos la X que eligió el generator.
                        pos.x = surfaceSearchRadiusPx > 0 ? s.x : pos.x;
                        pos.y = s.y + surfaceOffsetY;
                        if (debugLogs)
                            Debug.Log($"[GameSceneSpawnManager] Snap spawn #{index} a superficie: {pos}");
                    }
                    else if (debugLogs)
                    {
                        Debug.LogWarning($"[GameSceneSpawnManager] GetSurfaceWorldPos devolvió null para X={pos.x}. Usando SpawnPoint tal cual.");
                    }
                }
                else if (debugLogs)
                {
                    Debug.Log("[GameSceneSpawnManager] No hay TerrainGenerator con mapa generado todavía — spawn con SpawnPoint literal.");
                }
            }

            // Verificación anti-clipping: si por lo que sea (isla flotante, cueva
            // justo debajo del SpawnPoint, cálculo impreciso) el punto elegido
            // cae DENTRO del terreno, subimos la Y en pequeños pasos hasta
            // dejar a la rata en aire limpio.
            if (verifySpawnIsClear)
            {
                for (int attempt = 0; attempt < maxClearAttempts; attempt++)
                {
                    Collider2D overlap = Physics2D.OverlapCircle(
                        new Vector2(pos.x, pos.y),
                        spawnClearRadius,
                        terrainLayerMask);
                    if (overlap == null) break; // limpio
                    pos.y += clearAttemptStep;
                    if (debugLogs)
                        Debug.Log($"[GameSceneSpawnManager] Spawn #{index} clipeaba con '{overlap.name}', subo a Y={pos.y}");
                }
            }

            return pos;
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
