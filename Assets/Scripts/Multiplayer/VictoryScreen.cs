using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ratms.Networking
{
    /// <summary>
    ///  Pantalla de VICTORIA — se muestra cuando el NetworkTurnManager señala
    ///  que la partida ha terminado (GameEnded = true).
    ///
    ///  Muestra:
    ///   · Frase random tipo rata ("¡RATANTÁSTICO!", "La más rata del mapa", etc.)
    ///   · Nombre del ganador (o "Empate" si no hay ganador).
    ///   · Botón "Volver al menú" que desconecta y carga la escena del MainMenu.
    ///
    ///  Se activa a sí misma solo — pon el componente en un GameObject vacío
    ///  en la escena de juego, con las referencias UI conectadas en el Inspector.
    /// </summary>
    public class VictoryScreen : MonoBehaviour
    {
        [Header("Referencias UI")]
        [Tooltip("Panel raíz que se activa/oculta al ganar/perder. Debe empezar oculto.")]
        [SerializeField] private GameObject rootPanel;

        [Tooltip("Frase divertida tipo rata. Ej: 'RATANTÁSTICO!', 'La más rata!'")]
        [SerializeField] private TMP_Text funnyPhraseText;

        [Tooltip("Nombre del ganador. Ej: '¡[Chema] gana la partida!'")]
        [SerializeField] private TMP_Text winnerNameText;

        [Tooltip("Botón 'Volver al lobby'. Solo el HOST puede pulsarlo (él controla el cambio de escena en NGO). " +
                 "Los clientes lo ven deshabilitado con un mensaje de 'esperando al host'.")]
        [SerializeField] private Button btnBackToMenu;

        [Tooltip("Texto del botón — se cambia en runtime según seas host o cliente.")]
        [SerializeField] private TMP_Text btnBackToMenuLabel;

        [Tooltip("Botón 'Salir de la sala' — desconecta completamente y vuelve al MainMenu sin sala. " +
                 "Es opcional, si no tienes botón puedes dejarlo vacío.")]
        [SerializeField] private Button btnLeaveRoom;

        [Header("Escena a cargar al volver")]
        [Tooltip("Nombre EXACTO de la escena del MainMenu. Debe estar en Build Settings.")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("Frases de victoria (se elige una al azar)")]
        [SerializeField] private string[] winnerPhrases = new[]
        {
            "¡RATANTÁSTICO!",
            "¡La más rata del mapa!",
            "¡Rey de la alcantarilla!",
            "¡Rata legendaria!",
            "¡El más ROEDOR de todos!",
            "¡RATA DIVINA!",
            "¡Bigotes de campeón!",
            "¡Nadie te para, rata!",
            "¡Ratatouille aprobaría esto!",
            "¡La rata definitiva!",
        };

        [Header("Frases de empate")]
        [SerializeField] private string[] drawPhrases = new[]
        {
            "¡Todos muertos! Empate ratónico.",
            "¡Nadie sobrevive! Es un empate.",
            "¡El queso se lo llevan los cuervos!",
        };

        private NetworkTurnManager cachedTurnManager;

        private void Awake()
        {
            if (rootPanel != null) rootPanel.SetActive(false);
            if (btnBackToMenu != null) btnBackToMenu.onClick.AddListener(OnBackToLobbyClicked);
            if (btnLeaveRoom  != null) btnLeaveRoom.onClick.AddListener(OnLeaveRoomClicked);
        }

        private void Update()
        {
            // Enganche defensivo — el TurnManager puede aparecer más tarde que
            // este componente (spawnea con la escena en red). Suscribimos una
            // sola vez cuando lo detectamos.
            if (cachedTurnManager == null)
            {
                cachedTurnManager = NetworkTurnManager.Instance;
                if (cachedTurnManager != null)
                {
                    cachedTurnManager.GameEnded.OnValueChanged += OnGameEndedChanged;
                    // Si la partida YA ha terminado (late-join), mostramos ya
                    if (cachedTurnManager.GameEnded.Value) ShowVictoryScreen();
                }
            }
        }

        private void OnDestroy()
        {
            if (cachedTurnManager != null)
                cachedTurnManager.GameEnded.OnValueChanged -= OnGameEndedChanged;
        }

        private void OnGameEndedChanged(bool _, bool newValue)
        {
            if (newValue) ShowVictoryScreen();
        }

        private void ShowVictoryScreen()
        {
            if (rootPanel != null) rootPanel.SetActive(true);
            Sfx.Play(Sfx.Id.Victory, 1f, 0f);

            ulong winnerId = cachedTurnManager.WinnerClientId.Value;
            bool isDraw = winnerId == ulong.MaxValue;

            // Frase random
            if (funnyPhraseText != null)
            {
                string[] pool = isDraw ? drawPhrases : winnerPhrases;
                if (pool != null && pool.Length > 0)
                    funnyPhraseText.text = Ratms.UI.Loc.T(pool[Random.Range(0, pool.Length)]);
                else
                    funnyPhraseText.text = Ratms.UI.Loc.T(isDraw ? "Empate" : "¡Victoria!");
            }

            // Nombre del ganador (o "empate")
            if (winnerNameText != null)
            {
                if (isDraw)
                    winnerNameText.text = Ratms.UI.Loc.T("Nadie sobrevive.");
                else
                {
                    int team = cachedTurnManager.WinnerTeam.Value;
                    string winnerName = team >= 0 ? cachedTurnManager.TeamDisplayName(team) : PlayerNameRegistry.GetName(winnerId);
                    bool localTeams = MatchRules.Mode == MatchMode.Local;
                    winnerNameText.text = localTeams ? Ratms.UI.Loc.F("¡Ganan los {0}!", winnerName) : Ratms.UI.Loc.F("¡{0} gana!", winnerName);
                    winnerNameText.color = team >= 0 ? TurnHUD.TeamTextColor(team) : winnerNameText.color;
                }
            }

            // Actualizar botones según seas HOST o CLIENT
            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsHost;
            if (btnBackToMenu != null)
                btnBackToMenu.interactable = isHost;
            if (btnBackToMenuLabel != null)
                btnBackToMenuLabel.text = Ratms.UI.Loc.T(isHost ? "Volver al lobby" : "Esperando al host...");

            // Partida local / práctica: no hay lobby al que volver; el botón lleva al menú.
            if (MatchRules.Mode != MatchMode.Online && btnBackToMenuLabel != null)
                btnBackToMenuLabel.text = Ratms.UI.Loc.T("Volver al menú");

            ShowOverlay(isDraw, isHost);
        }

        /// <summary>
        ///  0.19: en vez del panel de la escena (texto suelto sobre el mapa) se
        ///  muestra el CARTEL con estadísticas y revancha (<see cref="VictoryOverlay"/>).
        ///  Los textos son los mismos que acabamos de calcular arriba.
        /// </summary>
        private void ShowOverlay(bool isDraw, bool isHost)
        {
            var ov = GetComponent<VictoryOverlay>();
            if (ov == null) ov = gameObject.AddComponent<VictoryOverlay>();
            ov.phrase = funnyPhraseText != null ? funnyPhraseText.text : Ratms.UI.Loc.T(isDraw ? "Empate" : "¡Victoria!");
            ov.winnerLine = winnerNameText != null ? winnerNameText.text : "";
            ov.winnerColor = winnerNameText != null && !isDraw ? winnerNameText.color : TrenchGUIPaper;
            ov.winnerTeam = isDraw ? -1 : cachedTurnManager.WinnerTeam.Value;
            string scene = SceneManager.GetActiveScene().name;
            if (MatchRules.Mode == MatchMode.Online)
            {
                // Online: la revancha es volver todos juntos a la sala (solo el host puede).
                ov.rematchEnabled = isHost;
                ov.rematchLabel = isHost ? "Revancha (volver a la sala)" : "Esperando al host...";
                ov.OnRematch = OnBackToLobbyClicked;
                ov.leaveLabel = "Salir de la sala";
            }
            else
            {
                var mode = MatchRules.Mode;
                ov.rematchEnabled = true;
                ov.rematchLabel = "¡REVANCHA!";
                ov.OnRematch = () => { ov.enabled = false; RematchRunner.Begin(mode, scene); };
                ov.leaveLabel = "Volver al menú";
            }
            ov.OnLeave = OnLeaveRoomClicked;
            if (rootPanel != null) rootPanel.SetActive(false);
            ov.enabled = true;
        }

        private static readonly Color TrenchGUIPaper = new Color(0.94f, 0.90f, 0.80f);

        /// <summary>
        ///  El HOST vuelve al lobby SIN desconectarse — carga la escena del MainMenu
        ///  vía NGO, así todos los clientes vuelven con él manteniendo la sala.
        ///  Pueden empezar otra partida sin reconectar.
        /// </summary>
        private void OnBackToLobbyClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost)
            {
                Debug.LogWarning("[VictoryScreen] Solo el host puede volver al lobby.");
                return;
            }

            if (MatchRules.Mode != MatchMode.Online) { OnLeaveRoomClicked(); return; }

            // Usamos NGO SceneManager para que TODOS los clientes carguen la
            // escena a la vez, manteniendo la conexión y el estado de sala.
            nm.SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
        }

        /// <summary>
        ///  Salir COMPLETAMENTE de la sala — desconecta y vuelve al MainMenu
        ///  sin conexión. Cualquiera puede pulsarlo (host o cliente).
        /// </summary>
        private void OnLeaveRoomClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsHost || nm.IsClient || nm.IsServer))
                nm.Shutdown();

            PlayerNameRegistry.Clear();
            MatchRules.Mode = MatchMode.Online;
            SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
        }
    }
}
