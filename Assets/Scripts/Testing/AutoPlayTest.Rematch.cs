using System.Collections;
using System.Linq;
using Ratms.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.36.1 — REVANCHA ONLINE (papeles "rematchhost" / "rematchclient", los
    ///  lanza <c>tools\autotest-multi.ps1 -Rematch</c>). Partida 1 → se acaba →
    ///  el anfitrión pulsa "Revancha (volver a la sala)" → los dos vuelven a la
    ///  sala, marcan ¡Listo! → partida 2. En la partida 2 el cliente debe poder
    ///  moverse Y ACTUAR (soltar dinamita) y el anfitrión debe VERLO.
    ///  (Bug que vieron los amigos: tras la revancha se movían pero no podían
    ///  hacer nada y los demás no los veían.)
    /// </summary>
    public partial class AutoPlayTest
    {
        private string RatsDebug()
        {
            var all = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None);
            return $"{all.Length} ratas: " + string.Join(" · ", all.Select(m =>
                $"{m.name}[id {(m.IsSpawned ? m.NetworkObjectId.ToString() : "SIN RED")} dueño {(m.IsSpawned ? m.OwnerClientId.ToString() : "?")}{(m.IsOwner ? " MÍA" : "")} eq {m.TeamIndex.Value} escena {m.gameObject.scene.name}]"));
        }

        private IEnumerator CaptureLater(string name, float delay)
        {
            yield return new WaitForSeconds(delay);
            yield return Capture(name);
        }

        private IEnumerator RunRematchHost()
        {
            Log("Papel: REVANCHA ONLINE (anfitrión)");
            MatchRules.OverrideRatsPerTeam = 1;
            yield return new WaitForSeconds(2f);
            var nm = NetworkManager.Singleton;
            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            menu?.HideAllPanels();
            nm.GetComponent<UnityTransport>()?.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");
            if (!nm.StartHost()) { Finish("No se pudo arrancar el host"); yield break; }
            menu?.ShowLobby();
            yield return WaitFor(() => nm.ConnectedClientsIds.Count >= 2 && LobbySync.AllReady, 60f, "cliente conectado y listo");
            if (finished) yield break;
            // 0.37: chat en la sala de espera
            yield return WaitFor(() => ChatSystem.Received >= 1, 10f, "el chat del cliente en la sala", fatal: false);
            Check($"Chat en la sala → mensajes recibidos: {ChatSystem.Received}", ChatSystem.Received >= 1, "Chat: el anfitrión no recibe el mensaje de la sala");
            yield return Capture("R00_host_chat_sala");

            for (int match = 1; match <= 2; match++)
            {
                Log($"---- Partida {match} ----");
                nm.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);
                NetworkTurnManager tm = null;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null && tm.CurrentRatId.Value != ulong.MaxValue, 30f, "empezar la partida");
                if (finished) yield break;
                yield return new WaitForSeconds(1.5f);
                Log("Anfitrión ve " + RatsDebug());
                if (match == 2)
                {
                    // Turno del cliente
                    for (int k = 0; k < 4 && tm.CurrentTurnClientId.Value == nm.LocalClientId; k++)
                    {
                        ulong cur = tm.CurrentRatId.Value;
                        tm.EndTurnServerRpc();
                        yield return WaitFor(() => tm.CurrentRatId.Value != cur && !tm.InTransition.Value, 12f, "turno del cliente");
                    }
                    var theirs = tm.CurrentRat;
                    Vector2 start = theirs != null ? (Vector2)theirs.transform.position : Vector2.zero;
                    Log($"Turno del cliente con '{(theirs != null ? theirs.RatName.Value.ToString() : "?")}' en {start}");
                    int recv0 = ChatSystem.Received, emo0 = ChatSystem.EmotesPlayed;
                    float t0 = Time.time;
                    bool sawDynamite = false;
                    bool shot = false;
                    while (Time.time - t0 < 9f)
                    {
                        if (!shot && ChatSystem.EmotesPlayed > emo0) { shot = true; StartCoroutine(CaptureLater("R03_host_chat_emote", 0.5f)); }
                        if (FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).Any(g => g.IsDynamite.Value)) sawDynamite = true;
                        yield return null;
                    }
                    // 0.37: el cliente ha escrito y hecho un emote al empezar su turno
                    Check($"Chat y emotes en partida → mensajes nuevos {ChatSystem.Received - recv0} · emotes vistos {ChatSystem.EmotesPlayed - emo0}",
                          ChatSystem.Received > recv0 && ChatSystem.EmotesPlayed > emo0, "Chat/emotes: el anfitrión no ve lo del cliente");
                    ChatSystem.SendChat("¡Ni de broma, sarnoso!");
                    ChatSystem.SendEmote(1);
                    yield return new WaitForSeconds(3f);
                    float moved = theirs != null ? Vector2.Distance(start, theirs.transform.position) : 0f;
                    Check($"Revancha → el anfitrión ve moverse al cliente: {moved:F2} u · ve su dinamita: {sawDynamite}",
                          moved > 0.4f && sawDynamite, "Revancha: el cliente no puede actuar o el anfitrión no lo ve");
                    yield return Capture("R02_host_revancha");
                    Finish(null);
                    yield break;
                }
                // Partida 1: se acaba enseguida (revientan las ratas del cliente).
                foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Where(m => m.IsSpawned && !m.IsOwner))
                {
                    var h = m.GetComponent<NetworkHealth>();
                    h.ApplyDamageServer(999, h.BodyCenter, DamageType.Explosion);
                }
                yield return WaitFor(() => tm.GameEnded.Value, 15f, "fin de la partida 1");
                yield return new WaitForSeconds(3f);
                yield return Capture("R01_host_victoria");
                Log("Anfitrión: REVANCHA (volver a la sala)");
                var ov = FindFirstObjectByType<VictoryOverlay>();
                if (ov != null && ov.OnRematch != null) ov.OnRematch();   // el botón de verdad
                else { Problem("Revancha: no hay botón de revancha en el cartel"); nm.SceneManager.LoadScene("MainMenu", LoadSceneMode.Single); }
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 20f, "volver a la sala");
                yield return new WaitForSeconds(2f);
                Log("Anfitrión en la sala: " + RatsDebug());
                yield return WaitFor(() => LobbySync.Roster.Count >= 2 && LobbySync.AllReady, 30f, "que el cliente marque ¡Listo! otra vez");
                if (finished) yield break;
            }
        }

        private IEnumerator RunRematchClient()
        {
            Log("Papel: REVANCHA ONLINE (cliente)");
            yield return new WaitForSeconds(3f);
            var nm = NetworkManager.Singleton;
            FindFirstObjectByType<MultiplayerMenuController>()?.HideAllPanels();
            nm.GetComponent<UnityTransport>()?.SetConnectionData("127.0.0.1", 7777);
            bool connected = false;
            for (int attempt = 0; attempt < 10 && !connected && !finished; attempt++)
            {
                nm.StartClient();
                float t = 0f;
                while (t < 5f && !nm.IsConnectedClient) { t += Time.deltaTime; yield return null; }
                connected = nm.IsConnectedClient;
                if (!connected) { nm.Shutdown(); yield return new WaitForSeconds(1f); }
            }
            if (!connected) { Finish("El cliente no pudo conectarse"); yield break; }
            FindFirstObjectByType<MultiplayerMenuController>()?.ShowLobby();
            yield return new WaitForSeconds(1f);
            ChatSystem.SendChat("¡Hola! ¿Jugamos?");   // 0.37
            yield return new WaitForSeconds(0.5f);
            yield return Capture("R00_cliente_chat_sala");
            LobbySync.LocalReady = true;

            // Partida 1
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && NetworkTurnManager.Instance != null, 40f, "partida 1");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);
            Log("Cliente (partida 1) ve " + RatsDebug());
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 40f, "volver a la sala");
            if (finished) yield break;
            yield return new WaitForSeconds(2f);
            Log("Cliente en la sala: " + RatsDebug() + $" · sala visible: {MultiplayerMenuController.Instance != null && MultiplayerMenuController.Instance.LobbyVisible}");
            yield return Capture("R01_cliente_sala");
            LobbySync.LocalReady = true;

            // Partida 2
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null && FindLocalRat() != null, 40f, "partida 2");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);
            Log("Cliente (partida 2) ve " + RatsDebug());
            yield return WaitFor(() => IsMyTurn(nm), 40f, "mi turno en la revancha");
            if (finished) yield break;
            var mine = tm.CurrentRat;
            Log($"Cliente: mi turno · rata '{mine.RatName.Value}' MÍA={mine.IsOwner} · puedo moverla: {mine.CanControlNow} · CanAct en el servidor lo dirá la dinamita");
            // 0.37: primero chat y emote
            int before = ChatSystem.Received, emoBefore = ChatSystem.EmotesPlayed;
            ChatSystem.SendChat("¡Toma dinamita, rata!");
            yield return new WaitForSeconds(1.3f);
            ChatSystem.SendEmote(2);   // baile
            yield return new WaitForSeconds(0.6f);
            yield return Capture("R03_cliente_emote");
            mine.TestInputX = 1f;
            yield return new WaitForSeconds(1.5f);
            mine.TestInputX = 0f;
            var weapons = mine.GetComponentInChildren<WeaponsMenuUI>();
            weapons.SelectedIndexNet.Value = (int)WeaponsMenuUI.WeaponId.Dynamite;
            yield return new WaitForSeconds(0.4f);
            mine.TestPressFire();
            yield return new WaitForSeconds(1f);
            bool dyn = FindObjectsByType<NetworkGrenade>(FindObjectsSortMode.None).Any(g => g.IsDynamite.Value);
            Check($"Revancha (cliente) → mi dinamita existe en red: {dyn}", dyn, "Revancha: el cliente no puede usar armas");
            // La respuesta del anfitrión (mensaje + risa)
            yield return WaitFor(() => ChatSystem.Received > before + 1 && ChatSystem.EmotesPlayed > emoBefore + 1, 15f, "la respuesta del anfitrión", fatal: false);
            Check($"Chat y emotes (cliente) → mensajes nuevos {ChatSystem.Received - before} · emotes {ChatSystem.EmotesPlayed - emoBefore}",
                  ChatSystem.Received > before + 1 && ChatSystem.EmotesPlayed > emoBefore + 1, "Chat/emotes: el cliente no recibe la respuesta");
            yield return Capture("R02_cliente_revancha");
            yield return new WaitForSeconds(5f);
            Finish(null);
        }
    }
}
