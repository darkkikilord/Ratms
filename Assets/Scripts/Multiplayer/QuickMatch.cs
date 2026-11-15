using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.36 — PARTIDA RÁPIDA online, sin pasarse códigos.
    ///
    ///  Usa el servicio LOBBY de Unity (de la misma familia que el Relay que ya
    ///  usamos para conectar). El Lobby solo sirve de "tablón de anuncios":
    ///   • Quien pulsa "Partida rápida" pide al Lobby una sala ABIERTA de su
    ///     misma versión del juego (<c>QuickJoinLobbyAsync</c>, filtro S1 =
    ///     versión). Si hay, lee el código de Relay que dejó el anfitrión, se
    ///     sale del anuncio y entra por Relay como siempre.
    ///   • Si no hay ninguna, crea su sala normal (Relay) y la ANUNCIA en el
    ///     Lobby (pública, con el código de Relay visible solo a quien entra).
    ///   • El anfitrión mantiene vivo el anuncio (latido cada 15 s; el Lobby
    ///     borra los que no laten) y lo CIERRA mientras se juega o si la sala
    ///     está llena (10). Al volver a la sala, se reabre. Al cerrar la sala,
    ///     se borra.
    ///
    ///  Hace falta tener el servicio Lobby activado en el proyecto de Unity
    ///  Cloud (como el Relay). Si no responde, se avisa en el menú.
    /// </summary>
    public static class QuickMatch
    {
        /// <summary>Último error para enseñar en el menú (null = todo bien).</summary>
        public static string LastError { get; private set; }

        /// <summary>¿Esta sala está anunciada en la partida rápida?</summary>
        public static bool Hosting => runner != null && !string.IsNullOrEmpty(runner.lobbyId);

        private static QuickMatchRunner runner;
        private const string KeyVersion = "ver", KeyRelay = "relay";

        /// <summary>
        ///  Busca una sala abierta. Devuelve su código de Relay, o null si no hay
        ///  ninguna (o si el servicio falla: entonces <see cref="LastError"/>).
        /// </summary>
        public static async Task<string> FindAsync(RelayManager relay)
        {
            LastError = null;
            try
            {
                await relay.InitializeAsync();
                var options = new QuickJoinLobbyOptions
                {
                    Filter = new List<QueryFilter>
                    {
                        new QueryFilter(QueryFilter.FieldOptions.S1, Ratms.UI.MenuDressing.Version, QueryFilter.OpOptions.EQ),
                    },
                };
                Lobby lobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);
                string code = lobby.Data != null && lobby.Data.TryGetValue(KeyRelay, out var d) ? d.Value : null;
                // Ya tenemos el código: nos salimos del anuncio (el anfitrión
                // controla las plazas cerrándolo cuando se llena).
                try { await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId); }
                catch (System.Exception e) { Debug.Log($"[Partida rápida] No pude salir del anuncio: {e.Message}"); }
                Debug.Log($"[Partida rápida] Encontrada sala {lobby.Name} → código {code}");
                return code;
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.NoOpenLobbies)
            {
                Debug.Log("[Partida rápida] No hay salas abiertas.");
                return null;
            }
            catch (System.Exception e)
            {
                LastError = Describe(e);
                Debug.LogWarning($"[Partida rápida] Fallo al buscar: {e.Message}");
                return null;
            }
        }

        /// <summary>(Anfitrión) Anuncia la sala recién creada con su código de Relay.</summary>
        public static async Task<bool> PublishAsync(string relayCode, string hostName)
        {
            LastError = null;
            try
            {
                var options = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { KeyRelay, new DataObject(DataObject.VisibilityOptions.Member, relayCode) },
                        { KeyVersion, new DataObject(DataObject.VisibilityOptions.Public, Ratms.UI.MenuDressing.Version, DataObject.IndexOptions.S1) },
                    },
                };
                string name = string.IsNullOrWhiteSpace(hostName) ? "Ratms" : "Ratms · " + hostName;
                Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(name, MatchRules.MaxPlayers, options);
                if (runner == null)
                {
                    var go = new GameObject("PartidaRapida");
                    Object.DontDestroyOnLoad(go);
                    runner = go.AddComponent<QuickMatchRunner>();
                }
                runner.Begin(lobby.Id);
                Debug.Log($"[Partida rápida] Sala anunciada ({lobby.Id}) con código {relayCode}");
                return true;
            }
            catch (System.Exception e)
            {
                LastError = Describe(e);
                Debug.LogWarning($"[Partida rápida] No pude anunciar la sala: {e.Message}");
                return false;
            }
        }

        /// <summary>Texto del error para el menú.</summary>
        private static string Describe(System.Exception e)
        {
            if (e is LobbyServiceException le)
            {
                if (le.Reason == LobbyExceptionReason.RateLimited) return Ratms.UI.Loc.T("Demasiados intentos: espera unos segundos.");
                if (le.Reason == LobbyExceptionReason.NetworkError) return Ratms.UI.Loc.T("Sin conexión con el servicio de partidas.");
            }
            return Ratms.UI.Loc.T("El servicio de partida rápida no responde (¿Lobby activado en Unity Cloud?).");
        }

        /// <summary>
        ///  Mantiene el anuncio: latido, abrir/cerrar según la sala y borrarlo
        ///  cuando el anfitrión deja de serlo.
        /// </summary>
        private class QuickMatchRunner : MonoBehaviour
        {
            public string lobbyId;
            private bool locked;
            private Coroutine loop;

            public void Begin(string id)
            {
                lobbyId = id;
                locked = false;
                if (loop != null) StopCoroutine(loop);
                loop = StartCoroutine(Loop());
            }

            private IEnumerator Loop()
            {
                float nextBeat = Time.unscaledTime + 15f;
                while (!string.IsNullOrEmpty(lobbyId))
                {
                    yield return new WaitForSecondsRealtime(2f);
                    var nm = NetworkManager.Singleton;
                    if (nm == null || !nm.IsListening || !nm.IsServer)
                    {
                        // Ya no hay sala: borramos el anuncio.
                        var t = Delete(lobbyId);
                        lobbyId = null;
                        while (!t.IsCompleted) yield return null;
                        break;
                    }
                    // Cerrado mientras se juega o si la sala está llena.
                    bool inMatch = SceneManager.GetActiveScene().name != "MainMenu";
                    bool full = nm.ConnectedClientsIds.Count >= MatchRules.MaxPlayers;
                    bool wantLocked = inMatch || full;
                    if (wantLocked != locked)
                    {
                        locked = wantLocked;
                        var t = SetLocked(lobbyId, wantLocked);
                        while (!t.IsCompleted) yield return null;
                    }
                    if (Time.unscaledTime >= nextBeat)
                    {
                        nextBeat = Time.unscaledTime + 15f;
                        var t = Beat(lobbyId);
                        while (!t.IsCompleted) yield return null;
                    }
                }
                loop = null;
            }

            private static async Task Beat(string id)
            {
                try { await LobbyService.Instance.SendHeartbeatPingAsync(id); }
                catch (System.Exception e) { Debug.Log($"[Partida rápida] Latido fallido: {e.Message}"); }
            }

            private static async Task SetLocked(string id, bool value)
            {
                try
                {
                    await LobbyService.Instance.UpdateLobbyAsync(id, new UpdateLobbyOptions { IsLocked = value });
                    Debug.Log($"[Partida rápida] Sala {(value ? "cerrada" : "abierta")}.");
                }
                catch (System.Exception e) { Debug.Log($"[Partida rápida] No pude {(value ? "cerrar" : "abrir")} la sala: {e.Message}"); }
            }

            private static async Task Delete(string id)
            {
                try { await LobbyService.Instance.DeleteLobbyAsync(id); Debug.Log("[Partida rápida] Anuncio borrado."); }
                catch (System.Exception e) { Debug.Log($"[Partida rápida] No pude borrar el anuncio: {e.Message}"); }
            }

            private void OnApplicationQuit()
            {
                // Al cerrar el juego, intentamos borrar el anuncio (si no, el Lobby
                // lo borra solo al dejar de recibir latidos, en ~30 s).
                if (!string.IsNullOrEmpty(lobbyId)) _ = Delete(lobbyId);
            }
        }
    }
}
