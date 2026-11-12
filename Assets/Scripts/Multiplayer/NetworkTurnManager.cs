using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Gestor de turnos en MULTIJUGADOR.
    ///
    ///  Cómo funciona:
    ///   • Es un NetworkBehaviour que vive en una única instancia en la escena
    ///     (ponlo en un GameObject vacío llamado "NetworkTurnManager" en la escena
    ///     de juego, con un NetworkObject encima).
    ///   • SOLO el servidor (el host) decide de quién es el turno y cuánto queda.
    ///   • Los clientes leen ese estado vía NetworkVariables y reaccionan
    ///     (habilitar/deshabilitar su input).
    ///
    ///  Flujo:
    ///   1. Al arrancar (servidor), mete los clientes conectados en una cola y
    ///      arranca el primer turno.
    ///   2. Cada frame el servidor decrementa el tiempo restante. Cuando llega a 0,
    ///      pasa al siguiente jugador (round-robin).
    ///   3. Cuando un jugador "termina su turno" explícitamente (por ejemplo al
    ///      disparar), llama a <see cref="EndTurnServerRpc"/>.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTurnManager : NetworkBehaviour
    {
        // =========================================================
        // Singleton para acceso fácil desde otros scripts
        // =========================================================
        public static NetworkTurnManager Instance { get; private set; }

        // =========================================================
        // Config (visible en Inspector)
        // =========================================================
        [Header("Turnos")]
        [Tooltip("Segundos que dura cada turno (como en Worms).")]
        [SerializeField] private float turnDuration = 30f;

        [Tooltip("Pequeño retardo entre un turno que acaba y el siguiente que empieza.")]
        [SerializeField] private float transitionDelay = 1.5f;

        [Header("Viento")]
        [Tooltip("Si está OFF, nunca hay viento (Wind siempre 0).")]
        [SerializeField] private bool windEnabled = true;

        [Tooltip("Probabilidad (0-1) de que un turno salga SIN viento (calma).")]
        [Range(0f, 1f)] [SerializeField] private float calmChance = 0.15f;

        [Tooltip("Aceleración horizontal (u/s²) que sufre un proyectil con windFactor=1 cuando el viento está al MÁXIMO (Wind = ±1). " +
                 "Referencia: la gravedad es 9.81. Con 3.5 un misil a media potencia se desvía unas 2-4 unidades.")]
        [SerializeField] private float maxWindAcceleration = 3.5f;

        /// <summary>Aceleración del viento al máximo (u/s²). La leen los proyectiles.</summary>
        public float MaxWindAcceleration => maxWindAcceleration;

        /// <summary>Segundos totales de cada turno (para la barra del HUD).</summary>
        public float TurnDuration => turnDuration;

        // =========================================================
        // Estado sincronizado
        // =========================================================
        /// <summary>
        ///  ID del cliente que tiene el turno ahora mismo.
        ///  ulong.MaxValue significa "todavía no hay turno" (pre-partida o transición).
        /// </summary>
        public readonly NetworkVariable<ulong> CurrentTurnClientId = new(
            value: ulong.MaxValue,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  Segundos restantes del turno actual.
        /// </summary>
        public readonly NetworkVariable<float> TurnTimeRemaining = new(
            value: 0f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  true = estamos en la ventanita de transición entre turnos.
        /// </summary>
        public readonly NetworkVariable<bool> InTransition = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  ID del cliente GANADOR. ulong.MaxValue = sin ganador aún (partida activa
        ///  o empate). Cuando cambia a otro valor, la pantalla de victoria se muestra.
        /// </summary>
        public readonly NetworkVariable<ulong> WinnerClientId = new(
            value: ulong.MaxValue,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  true = la partida ha terminado (ha habido un ganador o empate).
        ///  La UI de victoria se muestra cuando este flag se activa.
        /// </summary>
        public readonly NetworkVariable<bool> GameEnded = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  Viento del turno actual, de -1 (máximo hacia la IZQUIERDA) a +1
        ///  (máximo hacia la DERECHA). 0 = calma. Lo decide el servidor al
        ///  empezar cada turno; todos lo ven igual. Los proyectiles lo
        ///  convierten en fuerza con <see cref="WindAcceleration"/>.
        /// </summary>
        public readonly NetworkVariable<float> Wind = new(
            value: 0f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        ///  Aceleración del viento AHORA (u/s², positiva = hacia la derecha)
        ///  para un proyectil con factor 1. Atajo estático para los
        ///  proyectiles: 0 si no hay TurnManager.
        /// </summary>
        public static float WindAcceleration =>
            Instance != null ? Instance.Wind.Value * Instance.maxWindAcceleration : 0f;

        // =========================================================
        // Estado interno del servidor
        // =========================================================
        private readonly List<ulong> turnOrder = new();
        private int currentTurnIndex = -1;
        private float transitionTimer;
        private bool gameStarted;

        // =========================================================
        // CICLO DE VIDA
        // =========================================================
        public override void OnNetworkSpawn()
        {
            Instance = this;

            // HUD de turno (cuenta atrás + viento). Si la escena no tiene uno,
            // lo creamos aquí: así funciona en cualquier escenario sin tener
            // que montarlo a mano en el Editor. Es solo visual (local).
            if (FindFirstObjectByType<TurnHUD>() == null)
                gameObject.AddComponent<TurnHUD>();

            if (IsServer)
            {
                // Al arrancar el servidor, suscribirse a conexiones/desconexiones
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

                // Meter los clientes YA conectados (incluido el host) en la cola
                foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                    if (!turnOrder.Contains(id))
                        turnOrder.Add(id);

                // IMPORTANTE: si entramos desde el lobby, los clientes ya estaban
                // conectados ANTES de cargar esta escena — por tanto OnClientConnected
                // no se disparará para ellos aquí. Hay que arrancar el juego manualmente
                // esperando un poquito (1s) a que NGO termine de sincronizar la escena
                // y los objetos de jugador estén creados (los spawnea GameSceneSpawnManager).
                Debug.Log($"[NetworkTurnManager] OnNetworkSpawn → {turnOrder.Count} jugadores en la cola. Arranque programado.");
                Invoke(nameof(TryStartGameServer), 1.5f);
            }
        }

        /// <summary>
        ///  Llamado por Invoke tras un pequeño delay, para dar tiempo a que
        ///  las ratas se hayan spawneado en todos los clientes antes de decir
        ///  de quién es el turno.
        /// </summary>
        private void TryStartGameServer()
        {
            if (!IsServer) return;
            Debug.Log($"[NetworkTurnManager] TryStartGameServer — gameStarted={gameStarted}, jugadores={turnOrder.Count}");
            if (!gameStarted) StartGameIfReady();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!turnOrder.Contains(clientId))
                turnOrder.Add(clientId);

            // Si ya estaba esperando para arrancar y ahora hay mínimo 2, arrancamos
            if (!gameStarted && turnOrder.Count >= 1)
                StartGameIfReady();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // Con la partida en marcha, desconectarse cuenta como "eliminado":
            // así si solo queda 1 jugador, gana (antes la partida se quedaba
            // colgada con un único jugador y sin pantalla de victoria).
            if (gameStarted)
            {
                RemovePlayerServer(clientId);
                return;
            }

            // Antes de empezar (o ya terminada): solo lo quitamos de la cola.
            int idx = turnOrder.IndexOf(clientId);
            if (idx < 0) return;
            turnOrder.RemoveAt(idx);
            if (idx <= currentTurnIndex) currentTurnIndex--;
        }

        // =========================================================
        // SERVIDOR: arranque y bucle
        // =========================================================
        private void StartGameIfReady()
        {
            if (!IsServer || gameStarted) return;
            if (turnOrder.Count == 0) return;

            gameStarted = true;
            currentTurnIndex = -1;
            AdvanceTurnServer();
        }

        /// <summary>
        ///  Fuerza el inicio del juego. Llámalo desde un botón "Empezar partida"
        ///  cuando el host quiera arrancar, o deja que arranque automáticamente.
        /// </summary>
        [ContextMenu("Start Game (Server)")]
        public void StartGameServer()
        {
            if (IsServer) StartGameIfReady();
        }

        private void Update()
        {
            if (!IsServer || !gameStarted) return;

            if (InTransition.Value)
            {
                transitionTimer -= Time.deltaTime;
                if (transitionTimer <= 0f)
                {
                    InTransition.Value = false;
                    BeginNextTurnServer();
                }
                return;
            }

            if (TurnTimeRemaining.Value > 0f)
            {
                TurnTimeRemaining.Value -= Time.deltaTime;
                if (TurnTimeRemaining.Value <= 0f)
                {
                    TurnTimeRemaining.Value = 0f;
                    AdvanceTurnServer();
                }
            }
        }

        private void AdvanceTurnServer()
        {
            if (!IsServer) return;
            if (turnOrder.Count == 0)
            {
                CurrentTurnClientId.Value = ulong.MaxValue;
                TurnTimeRemaining.Value = 0f;
                return;
            }

            InTransition.Value = true;
            transitionTimer = transitionDelay;
            CurrentTurnClientId.Value = ulong.MaxValue;  // nadie durante la transición
            TurnTimeRemaining.Value = 0f;
        }

        private void BeginNextTurnServer()
        {
            if (!IsServer) return;
            if (turnOrder.Count == 0) return;

            currentTurnIndex = (currentTurnIndex + 1) % turnOrder.Count;
            CurrentTurnClientId.Value = turnOrder[currentTurnIndex];
            TurnTimeRemaining.Value = turnDuration;
            Wind.Value = RollWind();
        }

        /// <summary>
        ///  Viento nuevo para el turno: a veces calma, si no un valor entre
        ///  -1 y 1 redondeado a décimas (para que el HUD muestre niveles
        ///  claros). Elevamos al cuadrado la magnitud para que los vientos
        ///  flojos sean más comunes que los huracanes.
        /// </summary>
        private float RollWind()
        {
            if (!windEnabled || Random.value < calmChance) return 0f;
            float magnitude = Mathf.Max(0.1f, Random.value * Random.value * 1.4f);
            magnitude = Mathf.Min(1f, magnitude);
            float sign = Random.value < 0.5f ? -1f : 1f;
            return Mathf.Round(sign * magnitude * 10f) / 10f;
        }

        // =========================================================
        // RPCs llamados desde los jugadores
        // =========================================================
        /// <summary>
        ///  Lo llama el jugador que tiene el turno cuando termina su acción
        ///  (por ejemplo, al disparar). Validamos que sea realmente su turno.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void EndTurnServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (CurrentTurnClientId.Value != sender) return; // no es su turno, ignorar
            AdvanceTurnServer();
        }

        // =========================================================
        // Muerte de jugadores
        // =========================================================
        /// <summary>
        ///  Saca a un cliente de la rotación porque su rata ha muerto.
        ///  Llamarlo desde el servidor cuando <see cref="NetworkHealth.OnDiedServer"/> dispare.
        /// </summary>
        public void RemovePlayerServer(ulong clientId)
        {
            if (!IsServer) return;

            int idx = turnOrder.IndexOf(clientId);
            if (idx < 0) return;

            turnOrder.RemoveAt(idx);
            // "<=" y no "<": si cae el jugador del turno actual (o el anterior,
            // durante la transición), el siguiente se desliza a su hueco. Al
            // retroceder el índice, BeginNextTurnServer (+1) cae justo en él
            // en vez de saltárselo.
            if (idx <= currentTurnIndex) currentTurnIndex--;

            // Condición de victoria: si queda 1 o 0, se acabó la partida.
            //   · 1 jugador restante → GANADOR = ese cliente
            //   · 0 jugadores        → EMPATE (WinnerClientId = MaxValue)
            if (turnOrder.Count <= 1)
            {
                CurrentTurnClientId.Value = ulong.MaxValue;
                TurnTimeRemaining.Value = 0f;
                InTransition.Value = false;
                gameStarted = false;

                if (turnOrder.Count == 1)
                {
                    WinnerClientId.Value = turnOrder[0];
                    Debug.Log($"[NetworkTurnManager] ¡GANADOR! clientId={turnOrder[0]}");
                }
                else
                {
                    WinnerClientId.Value = ulong.MaxValue;   // empate
                    Debug.Log("[NetworkTurnManager] Partida terminada en empate.");
                }
                GameEnded.Value = true;
                return;
            }

            // Si cayó el que tenía turno, avanzamos ya
            if (CurrentTurnClientId.Value == clientId)
                AdvanceTurnServer();
        }

        // =========================================================
        // Helpers para los clientes
        // =========================================================
        /// <summary>
        ///  Devuelve true si el clientId dado tiene el turno AHORA.
        /// </summary>
        public bool IsTurnOf(ulong clientId) =>
            !InTransition.Value && CurrentTurnClientId.Value == clientId;
    }
}
