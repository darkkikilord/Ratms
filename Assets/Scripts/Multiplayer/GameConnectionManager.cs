// --- START OF FILE GameConnectionManager.cs ---

using System;
using System.Threading.Tasks;
using Scripts.Core.NET;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scripts.Core.Networking
{
    /// <summary>
    /// The central manager for handling all network connection logic (hosting, joining, disconnecting).
    /// It provides a simplified API for starting hosts and clients via LAN or the Unity Relay service.
    /// It is a server-authoritative singleton and has no knowledge of the UI layer; instead, it communicates
    /// its status to the rest of the application via C# events.
    /// </summary>
    public class GameConnectionManager : NetworkBehaviour
    {
        // Public static reference to this instance for easy access (singleton pattern).
        public static GameConnectionManager Singleton { get; private set; }
        
        public enum SceneLoadReason
        {
            Normal,         // For a fresh start of the game
            FromGame,       // For a normal return after a match
            FromDisconnect  // For when we were unexpectedly disconnected
        }
        public static SceneLoadReason NextSceneLoadReason { get; set; } = SceneLoadReason.Normal;

        #region Events
        // This event-based system is the core of this manager's design. It allows the UI (or any other system)
        // to react to connection state changes without this script needing any direct references to the UI.
        // This is a clean, decoupled architecture.

        /// <summary>Fired when a connection attempt (as a host or client) begins.</summary>
        public event Action OnConnectionAttempt;
        
        /// <summary>Fired when a Relay host is successfully created. Passes the join code to the UI.</summary>
        public event Action<string> OnRelayHostData;
        
        /// <summary>Fired when the local client successfully connects to a game.</summary>
        public event Action OnConnectionSuccess;
        
        /// <summary>Fired when a connection attempt fails for any reason. Passes a reason string to the UI.</summary>
        public event Action<string> OnConnectionFailed;
        
        /// <summary>Fired when the local client is disconnected from a game. Passes a reason string to the UI.</summary>
        public event Action<string> OnDisconnected;
        
        #endregion

        /// <summary>
        /// Stores the name the local player has chosen. This is set before a connection attempt
        /// and can be read by other managers (like UserManager) after a successful connection.
        /// </summary>
        public string PlayerName { get; private set; }

        // A flag to track if Unity's backend services have been initialized.
        private bool _servicesInitialized;

        #region Unity Lifecycle & Singleton Setup

        private void Awake()
        {
            // Standard singleton pattern with persistence to survive scene loads.
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }
        
        private async void Start()
        {
            // When the game starts, we begin initializing Unity Services in the background.
            // We use 'async void' here, which is generally discouraged, but it is the standard
            // and accepted pattern for a "fire-and-forget" initialization task in a Unity Start method.
            await InitializeUnityServicesAsync();
        }
        
        #endregion

        #region NetworkManager Callbacks

        // We need to subscribe to NetworkManager's events to know when clients connect or disconnect.
        // OnNetworkSpawn is a reliable place to do this, as it's called after the NetworkObject is ready.
        public override void OnNetworkSpawn()
        {
            // The callbacks should only be handled by the server, as it's the authority on who is connected.
            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += Server_HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += Server_HandleClientDisconnected;
            }

            // The local client also needs to know when it connects or disconnects.
            NetworkManager.Singleton.OnClientConnectedCallback += HandleLocalClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleLocalClientDisconnected;
        }

        // Always unsubscribe from events to prevent memory leaks.
        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
            {
                if (IsServer)
                {
                    NetworkManager.Singleton.OnClientConnectedCallback -= Server_HandleClientConnected;
                    NetworkManager.Singleton.OnClientDisconnectCallback -= Server_HandleClientDisconnected;
                }
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleLocalClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleLocalClientDisconnected;
            }
        }
        
        #endregion

        /// <summary>
        /// Initializes Unity Services and authenticates the player anonymously.
        /// This is a prerequisite for using the Relay service.
        /// The 'async Task' signature allows other methods to 'await' this process.
        /// </summary>
        private async Task InitializeUnityServicesAsync()
        {
            // Using a try-catch block is essential when dealing with web services.
            try
            {
                // Only initialize if they are not already initializing or initialized.
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await UnityServices.InitializeAsync();
                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        // Sign the player in anonymously. This creates a temporary, unique ID for them.
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }
                }
                _servicesInitialized = true;
                Debug.Log("Unity Services Initialized Successfully.");
            }
            catch (Exception e)
            {
                _servicesInitialized = false;
                // We fire a connection failed event to inform the user that online features are unavailable.
                OnConnectionFailed?.Invoke("Unity Services failed to initialize.");
                Debug.LogError($"Unity Services failed to initialize: {e.Message}");
            }
        }

        #region Public Connection API

        /// <summary>
        /// Starts a LAN host session. The local machine acts as both the server and a client.
        /// </summary>
        public void StartLanHost(string playerName)
        {
            PlayerName = playerName;
            OnConnectionAttempt?.Invoke(); // Notify UI to show "Connecting..."
            
            // This single command configures and starts the networking session.
            // On a host, OnClientConnectedCallback will fire immediately for the local client (ID 0).
            // We let HandleLocalClientConnected() handle the OnConnectionSuccess event to ensure
            // consistent behavior between hosts and clients.
            bool success = NetworkManager.Singleton.StartHost();
            if (!success)
            {
                OnConnectionFailed?.Invoke("Failed to start LAN host.");
            }
            // Note: We removed the manual OnConnectionSuccess call here to prevent duplicate user addition.
            // The success will be handled by HandleLocalClientConnected() when the callback fires.
        }

        /// <summary>
        /// Initiates an attempt to join a LAN game as a client.
        /// This method is responsible for configuring the transport and starting the connection process.
        /// It does NOT signal success immediately. Instead, it relies on the OnClientConnectedCallback
        /// from the NetworkManager to determine the actual outcome of the connection attempt.
        /// </summary>
        /// <param name="playerName">The name the player has chosen for this session.</param>
        /// <param name="ipAddress">The IP address of the host server to connect to.</param>
        /// <param name="port">The port of the host server.</param>
        public void StartLanClient(string playerName, string ipAddress, ushort port)
        {
            // --- Step 1: Set Player State ---
            // Store the player's chosen name. This will be used later when creating their GameUserData.
            PlayerName = playerName;
            
            // --- Step 2: Configure the Transport ---
            // The UnityTransport component is the underlying system that handles sending and receiving data.
            // We must tell it where to send the connection request.
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("UnityTransport component not found on the NetworkManager!", this);
                OnConnectionFailed?.Invoke("Network transport is missing.");
                return;
            }
            transport.SetConnectionData(ipAddress, port);
            
            // --- Step 3: Notify the Application of the Attempt ---
            // Fire our public event to signal that a connection attempt is now in progress.
            // The UI layer (e.g., ConnectionFeedbackUI) listens to this to show a "Connecting..." message.
            OnConnectionAttempt?.Invoke();
            
            // --- Step 4: Initiate the Connection ---
            // NetworkManager.StartClient() begins the non-blocking process of trying to connect to the server.
            // It returns 'false' only if the client is already active or if the transport is not set,
            // which would be an immediate failure.
            //
            // The *actual success* is not known at this point. It will be signaled later by the
            // NetworkManager's OnClientConnectedCallback, which we handle in HandleLocalClientConnected.
            if (!NetworkManager.Singleton.StartClient())
            {
                // If StartClient returns false, the attempt failed before it even began.
                // We must immediately fire our failure event.
                OnConnectionFailed?.Invoke("Failed to initiate client connection.");
            }
        }


        /// <summary>
        /// Creates a Relay host session, gets a join code, and starts the host.
        /// This method is 'async Task' because it communicates with the Unity Relay web service.
        /// </summary>
        public async Task StartRelayHostAsync(string playerName)
        {
            PlayerName = playerName;
            OnConnectionAttempt?.Invoke();

            // First, ensure services are ready.
            if (!_servicesInitialized)
            {
                OnConnectionFailed?.Invoke("Unity Services are not available.");
                Debug.LogError("Cannot start Relay Host: Unity Services are not initialized.");
                return;
            }

            try
            {
                // 1. Create an "allocation" on the Relay server for a maximum of 2 players (host + 1 client).
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
                
                // 2. Get the unique, short "join code" for this allocation.
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                OnRelayHostData?.Invoke(joinCode); // Pass the join code to the UI to be displayed.
                
                // 3. Configure our local transport to use the Relay server information.
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(new RelayServerData()); // "dtls" is the connection type.
                
                // 4. Start the host.
                // Let HandleLocalClientConnected() handle the OnConnectionSuccess event to ensure
                // consistent behavior between hosts and clients.
                NetworkManager.Singleton.StartHost();
                // Note: We removed the manual OnConnectionSuccess call here to prevent duplicate user addition.
                // The success will be handled by HandleLocalClientConnected() when the callback fires.
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay host creation failed: {e.Message}");
                OnConnectionFailed?.Invoke($"Could not create Relay host: {e.Reason}");
            }
        }

        /// <summary>
        /// Joins a Relay game using a join code provided by the host.
        /// </summary>
        public async Task JoinRelayClientAsync(string playerName, string joinCode)
        {
            PlayerName = playerName;
            OnConnectionAttempt?.Invoke();

            if (!_servicesInitialized)
            {
                OnConnectionFailed?.Invoke("Unity Services are not available.");
                Debug.LogError("Cannot join Relay: Unity Services are not initialized.");
                return;
            }
            
            try
            {
                // 1. "Join" the allocation on the Relay service using the host's join code.
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                
                // 2. Configure our local transport to use the Relay server information.
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(new RelayServerData());
                
                // 3. Start the client.
                if(!NetworkManager.Singleton.StartClient())
                {
                    OnConnectionFailed?.Invoke("Failed to start Relay client.");
                }
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay client join failed: {e.Message}");
                OnConnectionFailed?.Invoke($"Could not join Relay: {e.Reason}");
            }
        }

        /// <summary>
        /// Shuts down the current network session, disconnecting all clients.
        /// This is called by the UI when a player wants to leave the lobby.
        /// </summary>
        public void Disconnect()
        {
            
            // If we are the host, we are responsible for cleaning up the user list
            // before shutting down the session.
            if (IsHost && UserManager.Singleton != null)
            {
                UserManager.Singleton.ClearUserList();
            }
            // The Shutdown method handles everything cleanly, including notifying clients.
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        #endregion

        #region Network Event Handlers

        /// <summary>
        /// SERVER-ONLY: This method is called on the server whenever a new client successfully connects.
        /// </summary>
        private void Server_HandleClientConnected(ulong clientId)
        {
            Debug.Log($"Server: Client {clientId} connected.");
            // On a host, the local client (ID 0) connects immediately. We've already handled this.
            // For remote clients, this is where we would trigger logic like adding them to the UserManager.
            // The MainMenuController currently handles this via an RPC after its OnConnectionSuccess, which is also a valid pattern.
        }
        
        /// <summary>
        /// SERVER-ONLY: This method is called on the server whenever a client disconnects.
        /// </summary>
        private void Server_HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"Server: Client {clientId} disconnected.");
            // The server needs to clean up any data associated with the disconnected player.
            if (UserManager.Singleton != null)
            {
                UserManager.Singleton.RemoveUser(clientId);
            }
        }
        
        /// <summary>
        /// This handler is called on all clients whenever *any* client connects to the server.
        /// We use it to detect when *our own* local client has successfully connected.
        /// </summary>
        private void HandleLocalClientConnected(ulong clientId)
        {
            // --- The Critical Check ---
            // We only care about this event if the client who just connected is us.
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                Debug.Log($"Local client (ID: {clientId}) has successfully connected to the server.");

                // This is the moment of success.
                // Fire our public event to notify the rest of the application (like the MainMenuController)
                // that the connection is established and it's time to move to the lobby.
                OnConnectionSuccess?.Invoke();
            }
        }

        /// <summary>
        /// LOCAL-CLIENT: This method is called on a client's machine when they disconnect from the server.
        /// </summary>
        private void HandleLocalClientDisconnected(ulong clientId)
        {
            if (clientId != NetworkManager.Singleton.LocalClientId) return;

            Debug.Log("Local client has been disconnected.");

            string reason = !string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason)
                ? NetworkManager.Singleton.DisconnectReason
                : "You have been disconnected.";
            
            // This is the failsafe for when we get disconnected while in a game.
            if (SceneManager.GetActiveScene().name != "GAME - Main Menu") // <-- Use your main menu scene name
            {
                // Set the reason and immediately load the main menu.
                NextSceneLoadReason = SceneLoadReason.FromDisconnect;
                SceneManager.LoadScene("GAME - Main Menu"); // <-- Use your main menu scene name
            }
            
            if (UserManager.Singleton != null)
            {
                UserManager.Singleton.ClearUserList();
            }
            
            OnDisconnected?.Invoke(reason);
        }


        #endregion
    }
}