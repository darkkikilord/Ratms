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

            if (IsServer)
            {
                // Al arrancar el servidor, suscribirse a conexiones/desconexiones
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

                // Meter los clientes YA conectados (incluido el host) en la cola
                foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
                    if (!turnOrder.Contains(id))
                        turnOrder.Add(id);
            }
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
            int idx = turnOrder.IndexOf(clientId);
            if (idx < 0) return;

            turnOrder.RemoveAt(idx);
            // Si se desconectó antes del turno actual, ajustamos el índice
            if (idx < currentTurnIndex) currentTurnIndex--;

            // Si se desconectó el que tenía turno, pasar al siguiente
            if (CurrentTurnClientId.Value == clientId)
                AdvanceTurnServer();
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
        // Helpers para los clientes
        // =========================================================
        /// <summary>
        ///  Devuelve true si el clientId dado tiene el turno AHORA.
        /// </summary>
        public bool IsTurnOf(ulong clientId) =>
            !InTransition.Value && CurrentTurnClientId.Value == clientId;
    }
}
