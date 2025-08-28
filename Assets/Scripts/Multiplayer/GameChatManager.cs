// --- START OF FILE GameChatManager.cs ---

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Scripts.Core.NET
{
    /// <summary>
    /// Manages the in-game chat system. This is a server-authoritative singleton.
    /// 
    /// HOW IT WORKS:
    /// 1. A client wants to send a message. They call the 'SendMessageServerRpc' and pass their raw message text.
    /// 2. The ServerRPC runs on the server. The server receives the raw message.
    /// 3. **Crucially, the server looks up the sending player's name from the UserManager.** This is a security
    ///    measure to prevent a malicious client from sending a message pretending to be someone else.
    /// 4. The server formats the final message (e.g., "[PlayerName]: Hello world").
    /// 5. The server then calls a 'ReceiveMessageClientRpc', broadcasting the final, formatted message to ALL clients.
    /// 6. All clients (including the original sender) receive the ClientRPC. They add the formatted message to their
    ///    local chat log and fire a C# event to notify the UI to update.
    /// </summary>
    public class GameChatManager : NetworkBehaviour
    {
        // Public static reference to this instance for easy access (singleton pattern).
        public static GameChatManager Singleton { get; private set; }

        // --- PUBLIC EVENTS ---
        // This C# event is the bridge to the UI layer (LobbyChatUI). It fires on a client
        // whenever a new chat message is received from the server.
        // The string parameter will be the fully formatted message.
        public event Action<string> OnMessageReceived;

        // --- LOCAL STATE ---
        // Each client (and the server) maintains its own local history of the chat log.
        // This is not a NetworkList because we don't need the complexity of synchronizing the entire log.
        // We only need to sync individual messages as they happen. This list is used to repopulate
        // the chat UI if it's disabled and re-enabled.
        private readonly List<string> _chatLog = new();

        #region Unity & Network Lifecycle

        private void Awake()
        {
            // Standard singleton implementation with persistence.
            if (Singleton != null && Singleton != this) { Destroy(gameObject); return; }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Provides a way for the UI to get the entire chat history when it first becomes active.
        /// </summary>
        /// <returns>A read-only list of all messages received so far.</returns>
        public IReadOnlyList<string> GetChatLog() => _chatLog;

        /// <summary>
        /// Clears the local chat log. This is typically called by the server right before a game starts,
        /// and the server's call is broadcast to clients via the OnMessageReceived event with a null message.
        /// </summary>
        public void ClearChatLog()
        {
            _chatLog.Clear();
            // We invoke the event with 'null' as a special signal to the UI to clear its display.
            OnMessageReceived?.Invoke(null); 
        }

        #endregion

        #region RPCs

        /// <summary>
        /// A ServerRPC called by a client who wants to send a chat message.
        /// </summary>
        /// <param name="message">The raw text entered by the player.</param>
        /// <param name="senderId">The network ID of the client who sent the message. Netcode automatically provides this.</param>
        [ServerRpc(RequireOwnership = false)]
        public void SendMessageServerRpc(string message, ServerRpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            // --- Server-Side Validation and Formatting ---
            if (string.IsNullOrWhiteSpace(message))
            {
                // Ignore empty or whitespace-only messages.
                return;
            }

            // Get the user's data from the UserManager to find their name.
            // This is the authoritative source for the player's name.
            var user = UserManager.Singleton.GetUser(senderId);
            if (user.HasValue)
            {
                // Format the message on the server.
                string formattedMessage = $"[{user.Value.playerName}]: {message}";
                
                // Broadcast the final, secure message to all clients.
                ReceiveMessageClientRpc(formattedMessage);
            }
            else
            {
                Debug.LogWarning($"Chat message received from unknown client ID: {senderId}");
            }
        }

        /// <summary>
        /// A ClientRPC called by the server to broadcast a formatted chat message to all clients.
        /// </summary>
        /// <param name="formattedMessage">The final message string, prepared by the server.</param>
        [ClientRpc]
        private void ReceiveMessageClientRpc(string formattedMessage)
        {
            // This code runs on every client's machine.

            // Add the message to our local chat history.
            _chatLog.Add(formattedMessage);
            
            // Fire the public event to notify the UI layer that a new message is ready to be displayed.
            OnMessageReceived?.Invoke(formattedMessage);
        }

        #endregion
    }
}