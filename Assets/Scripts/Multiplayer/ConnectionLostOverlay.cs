using Ratms.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  CONEXIÓN PERDIDA (0.23, lado del cliente).
    ///
    ///  Si estabas jugando online como cliente y se corta la conexión (sin
    ///  haber pulsado "Salir"), sale este aviso con "Volver a la partida": te
    ///  lleva al menú y vuelve a entrar SOLO con el último código de sala. El
    ///  host te guarda el equipo <see cref="NetworkTurnManager.ReconnectGrace"/> s.
    /// </summary>
    public class ConnectionLostOverlay : MonoBehaviour
    {
        /// <summary>Lo lee MultiplayerMenuController al abrir el menú: unirse ya con este código.</summary>
        public static string PendingRejoinCode;
        /// <summary>Lo pone LobbySync.SayGoodbye: te has ido tú, no es un corte.</summary>
        public static bool LeftOnPurpose;

        private static ConnectionLostOverlay instance;
        private bool wasPlayingAsClient, showing, gameWasOver;
        /// <summary>El host avisó de que cerraba ("ratms_hostbye"): no hay a dónde volver.</summary>
        public static bool HostClosed;
        private float lostAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("ConnectionLostOverlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ConnectionLostOverlay>();
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool inGame = SceneManager.GetActiveScene().name != "MainMenu";
            bool connectedClient = nm != null && nm.IsListening && nm.IsConnectedClient && !nm.IsServer;

            if (!inGame) { wasPlayingAsClient = false; showing = false; return; }
            if (connectedClient && MatchRules.Mode == MatchMode.Online)
            {
                gameWasOver = NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.GameEnded.Value;
                HostClosed = false;
                wasPlayingAsClient = true;
                LeftOnPurpose = false;
                showing = false;
                return;
            }
            if (wasPlayingAsClient && !connectedClient && !showing && !LeftOnPurpose)
            {
                showing = true;
                lostAt = Time.unscaledTime;
            }
            if (connectedClient) gameWasOver = NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.GameEnded.Value;
        }

        private void OnGUI()
        {
            if (!showing) return;
            GUI.depth = -900;
            TrenchGUI.Begin();
            TrenchGUI.Dim(0.7f);
            var r = new Rect((TrenchGUI.VW - 760f) * 0.5f, (TrenchGUI.VH - 360f) * 0.5f, 760f, 360f);
            TrenchGUI.Panel(r);
            bool hostGone = HostClosed || gameWasOver;
            int left = Mathf.Max(0, Mathf.CeilToInt(NetworkTurnManager.ReconnectGrace - (Time.unscaledTime - lostAt)));
            if (hostGone)
            {
                TrenchGUI.Title(new Rect(r.x, r.y + 24f, r.width, 50f), "FIN DE LA SALA");
                TrenchGUI.Label(new Rect(r.x + 50f, r.y + 100f, r.width - 100f, 90f), "El host ha cerrado la partida.");
            }
            else
            {
                TrenchGUI.Title(new Rect(r.x, r.y + 24f, r.width, 50f), "¡SE CORTÓ LA CONEXIÓN!");
                TrenchGUI.Label(new Rect(r.x + 50f, r.y + 100f, r.width - 100f, 90f),
                    left > 0 ? Loc.F("Tu equipo te espera en la trinchera {0} s más.", left) : Loc.T("Se acabó el tiempo para volver."));
            }
            bool canRejoin = !hostGone && left > 0 && !string.IsNullOrEmpty(RelayManager.LastJoinedCode);
            if (canRejoin && TrenchGUI.Button(new Rect(r.x + 50f, r.yMax - 110f, 360f, 64f), "Volver a la partida", big: true))
            {
                PendingRejoinCode = RelayManager.LastJoinedCode;
                Leave();
            }
            if (TrenchGUI.Button(new Rect(canRejoin ? r.x + 430f : r.x + 200f, r.yMax - 110f, canRejoin ? 280f : 360f, 64f), "Salir al menú"))
            {
                PendingRejoinCode = null;
                Leave();
            }
            TrenchGUI.End();
        }

        private void Leave()
        {
            showing = false;
            wasPlayingAsClient = false;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            PlayerNameRegistry.Clear();
            MatchRules.Mode = MatchMode.Online;
            SceneManager.LoadScene("MainMenu");
        }
    }
}
