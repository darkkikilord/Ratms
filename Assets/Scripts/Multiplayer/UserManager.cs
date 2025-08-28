using System;
using Unity.Netcode;

namespace Scripts.Core.NET
{
    /// <summary>
    /// Manages the list of all players currently connected to the game session. This is a server-authoritative singleton.
    /// It uses a NetworkList to synchronize player data, which requires manual memory management to prevent leaks.
    /// </summary>
    public class UserManager : NetworkBehaviour
    {
        public static UserManager Singleton { get; private set; }

        public event Action<NetworkList<GameUserData>> OnUserListChanged;

        // --- CORRECTED INITIALIZATION ---
        // We declare the NetworkList here, but we DO NOT initialize it yet.
        // This prevents the very early memory allocation that causes issues on shutdown.
        private NetworkList<GameUserData> _users;

        #region Unity & Network Lifecycle

        private void Awake()
        {
            // --- INITIALIZE THE NETWORKLIST HERE ---
            // By initializing in Awake, we align the allocation of the list's native memory
            // with Unity's standard component lifecycle. This makes its disposal in OnDestroy reliable.
            _users = new NetworkList<GameUserData>();
            
            // Standard singleton logic
            if (Singleton != null && Singleton != this) 
            {
                Destroy(gameObject); 
                return; 
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// OnDestroy is a standard Unity message called when a MonoBehaviour instance is being destroyed.
        /// This is the ideal place to perform cleanup of unmanaged resources.
        /// </summary>
        public override void OnDestroy()
        {
            // --- CRITICAL FIX FOR MEMORY LEAK ---
            // Now that the list is initialized in Awake, this Disposes call will be timed correctly
            // with the object's destruction, preventing the leak.
            _users?.Dispose();
        }

        public override void OnNetworkSpawn()
        {
            if (IsClient)
            {
                _users.OnListChanged += HandleUsersListChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsClient)
            {
                _users.OnListChanged -= HandleUsersListChanged;
            }
        }

        #endregion

        #region Public API & Data Access

        // This method needs to be updated to return the correct type now.
        public NetworkList<GameUserData> GetUserList() => _users;
        
        public GameUserData? GetUser(ulong clientId)
        {
            // This logic works with both NetworkList and IReadOnlyList
            foreach (var user in _users)
            {
                if (user.playerId == clientId)
                {
                    return user;
                }
            }
            return null;
        }
        
        public bool AreAllPlayersReady()
        {
            if (!IsServer || _users.Count == 0) return false;

            foreach (var user in _users)
            {
                if (!user.isReady) return false;
            }

            return true;
        }

        public void RemoveUser(ulong clientId)
        {
            if (!IsServer) return;

            for (int i = 0; i < _users.Count; i++)
            {
                if (_users[i].playerId == clientId)
                {
                    _users.RemoveAt(i);
                    return; 
                }
            }
        }
        
        /// <summary>
        /// SERVER-ONLY: Clears the entire list of users. This should be called
        /// by the host before shutting down a lobby to ensure a clean state for the next session.
        /// </summary>
        public void ClearUserList()
        {
            // This must only ever be run on the server.
            if (!IsServer) return;

            // The NetworkList.Clear() method removes all items.
            // This change will automatically sync to any connected clients before they are disconnected.
            _users.Clear();
        }

        #endregion

        #region Event Handlers

        private void HandleUsersListChanged(NetworkListEvent<GameUserData> changeEvent)
        {
            // Pass the underlying NetworkList to subscribers who may need its specific features
            OnUserListChanged?.Invoke(_users);
        }
        
        #endregion
        
        #region ServerRPCs

        [ServerRpc(RequireOwnership = false)]
        public void AddUserServerRpc(GameUserData userData)
        {
            _users.Add(userData);
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void SetReadyStatusServerRpc(ulong clientId, bool isReady)
        {
            for (int i = 0; i < _users.Count; i++)
            {
                if (_users[i].playerId == clientId)
                {
                    GameUserData updatedUser = _users[i];
                    updatedUser.isReady = isReady;
                    _users[i] = updatedUser; 
                    return;
                }
            }
        }
        
        [ServerRpc(RequireOwnership = false)]
        public void ResetAllReadyStatesServerRpc()
        {
            if (!IsServer) return;
            for (int i = 0; i < _users.Count; i++)
            {
                GameUserData updatedUser = _users[i];
                updatedUser.isReady = false;
                _users[i] = updatedUser;
            }
        }

        #endregion
    }
}