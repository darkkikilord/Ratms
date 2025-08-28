using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scripts.Core.NET
{
    /// <summary>
    /// Manages the process of starting a game from the lobby. This is a server-authoritative singleton.
    /// It is responsible for:
    ///    - Checking if all players are ready.
    ///    - Initiating a countdown visible to all players.
    ///    - Cancelling the countdown if a player becomes unready or disconnects.
    ///    - Resetting player states before transitioning.
    ///    - Triggering the networked scene load to the game level.
    /// </summary>
    public class GameStartManager : NetworkBehaviour
    {
        // Public static reference to this instance for easy access (singleton pattern).
        public static GameStartManager Singleton { get; private set; }

        // --- PUBLIC EVENTS ---
        // These C# events are the bridge to the UI layer. UI scripts (like LobbyStartFeedbackUI)
        // subscribe to these to know when to show or hide feedback, without this script
        // needing any direct reference to the UI. This is a clean, decoupled approach.
        public event Action<int> OnGameStartInitiated; // Fired when the countdown begins. Passes the duration.
        public event Action OnGameStartCancelled;      // Fired when the countdown is aborted.

        [Header("Scene & Countdown Settings")]
        [Tooltip("The name of the gameplay scene to load when the game starts.")]
        [SerializeField] private string sceneToLoad = "GAME - Level 1";
        [Tooltip("The duration of the pre-game countdown in seconds.")]
        [SerializeField] private int countdownDuration = 5;
        
        // --- NETWORKED STATE ---
        // This NetworkVariable ensures that all clients are aware of whether a game start is in progress.
        // It prevents the host from attempting to start the game multiple times simultaneously.
        private readonly NetworkVariable<bool> _isStartingGame = new(false);
        
        // --- SERVER-SIDE STATE ---
        // A reference to the running countdown coroutine, so we can stop it if needed.
        private Coroutine _countdownCoroutine;

        #region Unity & Network Lifecycle

        private void Awake()
        {
            // Standard singleton implementation with persistence.
            if (Singleton != null && Singleton != this) { Destroy(gameObject); return; }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Called when this NetworkObject is spawned. We use this to subscribe to server-side events.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            // This logic should only run on the server.
            if(IsServer)
            {
                // Subscribe to events to know when a scene has finished loading or a client disconnects.
                // This is crucial for resetting state and handling edge cases.
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += Server_HandleSceneLoadComplete;
                NetworkManager.Singleton.OnClientDisconnectCallback += Server_HandleClientDisconnect;
            }
        }
        
        /// <summary>
        /// Called when this NetworkObject is despawned. Always unsubscribe from events to prevent memory leaks.
        /// </summary>
        public override void OnNetworkDespawn()
        {
             if(IsServer && NetworkManager.Singleton != null)
             {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= Server_HandleSceneLoadComplete;
                NetworkManager.Singleton.OnClientDisconnectCallback -= Server_HandleClientDisconnect;
             }
        }
        
        #endregion

        #region Public API & Server Logic

        /// <summary>
        /// This is the public entry point for starting the game.
        /// It is called by the UI (e.g., a "Start Game" button for the host).
        /// </summary>
        public void TryStartGame()
        {
            // Guard clauses: Only the server can start the game, and only if a game isn't already starting.
            if (!IsServer || _isStartingGame.Value) return;

            // Authoritative check: Ask the UserManager if all players are in a "Ready" state.
            if (UserManager.Singleton.AreAllPlayersReady())
            {
                // All conditions met. Start the countdown coroutine.
                _countdownCoroutine = StartCoroutine(StartGameCountdown_Co());
            }
            else
            {
                // Optional: Provide feedback to the host that they can't start the game.
                Debug.Log("GameStartManager: Cannot start game, not all players are ready.");
            }
        }

        /// <summary>
        /// SERVER-ONLY: This is called by Netcode's SceneManager when all clients have finished loading the new scene.
        /// We use it to reset our state, allowing a new game to be started later.
        /// </summary>
        private void Server_HandleSceneLoadComplete(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            // We only care about resetting state if the scene loaded was our game scene.
            if (sceneName == sceneToLoad)
            {
                 // Reset the flag so that after returning to the lobby, the "Start Game" button will work again.
                 _isStartingGame.Value = false;
            }
        }

        /// <summary>
        /// SERVER-ONLY: This handles a client disconnecting while in the lobby.
        /// </summary>
        private void Server_HandleClientDisconnect(ulong clientId)
        {
            // If a client disconnects *during* the countdown, the cancellation logic inside the
            // coroutine will detect the change in player count and stop itself.
            // This handler is mainly for logging and future-proofing.
            if (_isStartingGame.Value)
            {
                Debug.Log($"GameStartManager: A client disconnected during the countdown. The start will be cancelled.");
            }
        }
        
        
        
        #endregion

        #region Coroutine

        /// <summary>
        /// SERVER-ONLY: This coroutine manages the entire countdown and transition process.
        /// </summary>
        private IEnumerator StartGameCountdown_Co()
        {
            // --- 1. INITIATION ---
            // Set the networked flag to true. This informs all clients (and the server) that the start is in progress.
            _isStartingGame.Value = true;
            
            // Fire an RPC to tell all clients to show their countdown UI.
            NotifyGameStartClientRpc(countdownDuration);

            // --- 2. THE COUNTDOWN LOOP ---
            float timer = countdownDuration;
            int lastPlayerCount = NetworkManager.Singleton.ConnectedClients.Count;

            while (timer > 0)
            {
                // --- CONTINUOUS CANCELLATION CHECK ---
                // During every second of the countdown, we check for conditions that should abort the start.
                if (!UserManager.Singleton.AreAllPlayersReady() || NetworkManager.Singleton.ConnectedClients.Count != lastPlayerCount)
                {
                    Debug.Log("Game start cancelled: A player became un-ready or disconnected during the countdown.");
                    // Tell all clients to hide their countdown UI.
                    NotifyStartCancelledClientRpc();
                    // Reset the state flag.
                    _isStartingGame.Value = false;
                    // Exit the coroutine immediately.
                    yield break; 
                }
                
                yield return new WaitForSeconds(1.0f);
                timer--;
            }

            // --- 3. FINAL ROBUSTNESS CHECK ---
            // The countdown has finished. Before we commit to loading the scene, we perform one last, final check.
            // This closes the "race condition" window where a player might un-ready at the exact last moment.
            if (!UserManager.Singleton.AreAllPlayersReady())
            {
                Debug.Log("Game start aborted at the last second: A player un-readied just as the countdown finished.");
                NotifyStartCancelledClientRpc();
                _isStartingGame.Value = false;
                yield break;
            }

            // --- 4. COMMIT TO SCENE LOAD ---
            Debug.Log("Countdown complete and all players confirmed ready. Proceeding with scene load.");
            
            // Reset player "Ready" states in the UserManager for the next time they return to the lobby.
            UserManager.Singleton.ResetAllReadyStatesServerRpc();
            
            // Clear the chat history so the new game starts with a fresh log.
            if (GameChatManager.Singleton != null)
            {
                GameChatManager.Singleton.ClearChatLog();
            }
            
            // Use Netcode's SceneManager to perform a safe, networked scene transition.
            NetworkManager.Singleton.SceneManager.LoadScene(sceneToLoad, LoadSceneMode.Single);
        }

        #endregion
        
        #region RPCs

        /// <summary>
        /// A ClientRPC that the server calls to tell all clients the countdown has started.
        /// </summary>
        [ClientRpc]
        private void NotifyGameStartClientRpc(int duration)
        {
            // Invoke the C# event. The UI layer, which is subscribed to this, will react.
            OnGameStartInitiated?.Invoke(duration);
        }

        /// <summary>
        /// A ClientRPC that the server calls to tell all clients the countdown was cancelled.
        /// </summary>
        [ClientRpc]
        private void NotifyStartCancelledClientRpc()
        {
            // Invoke the C# event for cancellation.
            OnGameStartCancelled?.Invoke();
        }

        #endregion
    }
}