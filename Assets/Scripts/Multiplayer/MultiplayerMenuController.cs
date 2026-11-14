using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Ratms.World;

namespace Ratms.Networking
{
    /// <summary>
    ///  Controlador del PANEL DE MULTIJUGADOR dentro del MainMenu.
    ///
    ///  Sustituye al NetworkBootstrap (que era IMGUI rústico) por una UI
    ///  bonita con Canvas + Buttons + TMP_InputField.
    ///
    ///  Flujo:
    ///    1. Usuario pulsa "Jugar" en el MainMenu → se muestra este panel.
    ///    2. El HOST pulsa "Crear sala"   → RelayManager crea sala, recibe
    ///       código corto (ej. ABC123) y lo enseña en pantalla. Se muestra
    ///       la zona "Esperando jugadores..." con un botón "Empezar" que
    ///       solo se activa cuando hay ≥ 2 jugadores conectados.
    ///    3. El CLIENTE introduce el código → RelayManager.JoinAsync. Cuando
    ///       conecta, entra automáticamente en la zona de espera (no necesita
    ///       pulsar "Empezar", eso es cosa del host).
    ///    4. Al pulsar "Empezar partida" el host llama a
    ///       NetworkManager.SceneManager.LoadScene("Escenario 1"). NGO
    ///       sincroniza la carga → todos aterrizan en el escenario a la vez.
    ///    5. El GameSceneSpawnManager (en el Escenario 1) spawneará cada
    ///       rata en su SpawnPoint correspondiente.
    ///
    ///  Notas:
    ///    • Este GameObject debe vivir como HIJO de un GameObject persistente
    ///      (DontDestroyOnLoad) junto con el NetworkManager y el RelayManager,
    ///      porque tiene que seguir vivo al cargar el Escenario 1 para poder
    ///      escuchar desconexiones y volver al menú. PERO el PANEL de UI
    ///      solo existe en el MainMenu; al cambiar de escena lo ocultamos.
    ///
    ///    • Si prefieres mantenerlo simple (no persistente), coméntame y lo
    ///      dejamos solo en el MainMenu — perderemos la capacidad de volver
    ///      al menú sin reiniciar, pero ahorramos complicaciones.
    /// </summary>
    public class MultiplayerMenuController : MonoBehaviour
    {
        [Header("Dependencias")]
        [Tooltip("El RelayManager (normalmente en el mismo GameObject persistente que el NetworkManager).")]
        [SerializeField] private RelayManager relayManager;

        [Header("Paneles del menú")]
        [Tooltip("Panel raíz del MAIN del menú (con botones Jugar / Opciones / Salir).")]
        [SerializeField] private GameObject mainPanel;

        [Tooltip("Panel de elección HOST / CLIENT (con los botones 'Crear sala' y 'Unirse').")]
        [SerializeField] private GameObject multiplayerPanel;

        [Tooltip("Panel de espera: muestra el código y 'Esperando jugadores...' para el host.")]
        [SerializeField] private GameObject lobbyPanel;

        [Header("Botones — elección Host / Client")]
        [SerializeField] private Button btnCreateRoom;
        [SerializeField] private Button btnJoinRoom;
        [SerializeField] private Button btnBackFromMultiplayer;
        [Tooltip("Campo donde el cliente introduce el código de sala (ej. ABC123).")]
        [SerializeField] private TMP_InputField joinCodeInput;

        [Header("UI del lobby (tras crear / unirse)")]
        [Tooltip("Texto que muestra 'Código: XXXXXX' al host.")]
        [SerializeField] private TMP_Text lobbyCodeText;

        [Tooltip("Texto con el número de jugadores conectados ('2/4 jugadores en la sala').")]
        [SerializeField] private TMP_Text lobbyPlayerCountText;

        [Tooltip("Botón 'Empezar partida'. Solo el HOST lo ve y solo se activa con ≥2 jugadores.")]
        [SerializeField] private Button btnStartGame;

        [Tooltip("Botón 'Cancelar / Desconectar' — sale de la sala y vuelve al menú.")]
        [SerializeField] private Button btnCancelLobby;

        [Tooltip("Mensajes de estado ('Creando sala...', 'Error', etc.).")]
        [SerializeField] private TMP_Text statusText;

        [Header("Config")]
        [Tooltip("Nombre EXACTO de la escena del escenario de juego. Añádela en Build Settings.")]
        [SerializeField] private string gameSceneName = "Escenario 1";

        /// <summary>Escena de juego (la usa también el modo Práctica).</summary>
        public string GameSceneName => gameSceneName;

        [Tooltip("Mínimo de jugadores para poder empezar (por defecto 2 — host + 1).")]
        [SerializeField] private int minPlayersToStart = 2;

        [Header("Nombre del jugador")]
        [Tooltip("Input donde el jugador escribe su nombre (max 16 chars). Se guarda en PlayerPrefs " +
                 "y se sincroniza por red al conectarse. Se muestra en la etiqueta de vida y en la " +
                 "pantalla de victoria.")]
        [SerializeField] private TMP_InputField playerNameInput;

        [Header("Selector de bioma (solo host)")]
        [Tooltip("Dropdown TMP con los biomas disponibles. Solo el HOST puede tocarlo. " +
                 "Al elegir, se guarda en BiomeManager.SelectedBiomeType antes de cargar la escena.")]
        [SerializeField] private TMP_Dropdown biomeDropdown;

        [Tooltip("Etiqueta que muestra al cliente el bioma elegido por el host (readonly).")]
        [SerializeField] private TMP_Text biomeLabelForClient;

        // ----------------------------------------------------------------
        private bool isBusy;

        // ================================================================
        /// <summary>
        ///  El controlador VIVO (el del objeto persistente). Úsalo en vez de una
        ///  referencia del Inspector: al recargar el MainMenu, la copia nueva de
        ///  la escena se destruye y sus referencias quedan muertas.
        /// </summary>
        public static MultiplayerMenuController Instance { get; private set; }

        /// <summary>¿Seguimos dentro de una sala online (conectados y sin estar cerrando)?</summary>
        public static bool InOnlineRoom
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && !nm.ShutdownInProgress
                       && (nm.IsHost || nm.IsClient) && MatchRules.Mode == MatchMode.Online;
            }
        }

        private void Awake()
        {
            // ¿Ya hay un controlador persistente vivo? Entonces este es la COPIA
            // que trae el MainMenu al recargarse (volver de una partida). Antes
            // las dos copias manejaban paneles a la vez: el persistente apuntaba
            // a los paneles de la escena vieja (destruidos) y la copia enseñaba
            // el lobby mientras el menú principal también se encendía → botones
            // de los dos paneles uno encima de otro. Ahora el persistente ADOPTA
            // los paneles nuevos y la copia se apaga sin hacer nada.
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Instance.AdoptSceneUI(this);
                return;
            }
            Instance = this;

            LobbySync.Ensure();
            if (GetComponent<LobbyCard>() == null) gameObject.AddComponent<LobbyCard>().Init(this);
            // Auto-resolver el RelayManager. Orden de búsqueda:
            //   1. Mismo GameObject  2. Padres  3. Cualquier sitio en la escena.
            if (relayManager == null) relayManager = GetComponent<RelayManager>();
            if (relayManager == null) relayManager = GetComponentInParent<RelayManager>();
            if (relayManager == null) relayManager = FindFirstObjectByType<RelayManager>();

            WireSceneUI();
            ShowInitialPanel();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Engancha botones, nombre y bioma de los paneles actuales.</summary>
        private void WireSceneUI()
        {
            if (btnCreateRoom          != null) btnCreateRoom.onClick.AddListener(OnCreateRoomClicked);
            if (btnJoinRoom            != null) btnJoinRoom.onClick.AddListener(OnJoinRoomClicked);
            if (btnBackFromMultiplayer != null) btnBackFromMultiplayer.onClick.AddListener(ShowMain);
            if (btnStartGame           != null) btnStartGame.onClick.AddListener(OnStartGameClicked);
            if (btnCancelLobby         != null) btnCancelLobby.onClick.AddListener(OnCancelLobbyClicked);

            // Rellenamos el dropdown de biomas y suscribimos su cambio
            InitializeBiomeDropdown();

            // Rellenamos el input de nombre con el guardado + escuchamos cambios
            InitializePlayerNameInput();
        }

        /// <summary>
        ///  Arranque: si venimos de una partida y seguimos en la sala, LOBBY
        ///  directamente; si no, menú principal.
        /// </summary>
        private void ShowInitialPanel()
        {
            if (InOnlineRoom) ShowLobby(); else ShowMain();
            SetStatus("");
            if (InOnlineRoom) RefreshLobbyUI();
        }

        /// <summary>
        ///  Toma los paneles y botones de la copia recién cargada del MainMenu
        ///  (los nuestros murieron con la escena anterior).
        /// </summary>
        private void AdoptSceneUI(MultiplayerMenuController fresh)
        {
            mainPanel              = fresh.mainPanel;
            multiplayerPanel       = fresh.multiplayerPanel;
            lobbyPanel             = fresh.lobbyPanel;
            btnCreateRoom          = fresh.btnCreateRoom;
            btnJoinRoom            = fresh.btnJoinRoom;
            btnBackFromMultiplayer = fresh.btnBackFromMultiplayer;
            joinCodeInput          = fresh.joinCodeInput;
            lobbyCodeText          = fresh.lobbyCodeText;
            lobbyPlayerCountText   = fresh.lobbyPlayerCountText;
            btnStartGame           = fresh.btnStartGame;
            btnCancelLobby         = fresh.btnCancelLobby;
            statusText             = fresh.statusText;
            playerNameInput        = fresh.playerNameInput;
            biomeDropdown          = fresh.biomeDropdown;
            biomeLabelForClient    = fresh.biomeLabelForClient;
            isBusy = false;

            WireSceneUI();
            // Si venimos de una partida online acabada, "¡Listo!" vuelve a no.
            if (InOnlineRoom && LobbySync.LocalReady) LobbySync.LocalReady = false;
            ShowInitialPanel();
        }

        private void OnEnable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback    += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback   += HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStarted              += HandleServerStarted;
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback    -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback   -= HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStarted              -= HandleServerStarted;
            }
        }

        // ================================================================
        // NAVEGACIÓN DE PANELES
        // ================================================================
        /// <summary>
        ///  Llamado desde el botón "Jugar" del MainMenuController.
        ///  Muestra el panel de elección Host/Client.
        /// </summary>
        public void ShowMultiplayer()
        {
            SetActive(mainPanel, false);
            SetActive(multiplayerPanel, true);
            SetActive(lobbyPanel, false);
            SetStatus("");
        }

        public void ShowMain()
        {
            SetActive(mainPanel, true);
            SetActive(multiplayerPanel, false);
            SetActive(lobbyPanel, false);
            SetStatus("");
        }

        public void ShowLobby()
        {
            SetActive(mainPanel, false);
            SetActive(multiplayerPanel, false);
            SetActive(lobbyPanel, true);
            RefreshLobbyUI();
        }

        /// <summary>
        ///  Oculta TODOS los paneles del menú. Se llama al cargar el Escenario 1
        ///  para que no tape el juego.
        /// </summary>
        /// <summary>¿Está abierta la sala de espera? (lo lee LobbyCard)</summary>
        public bool LobbyVisible => lobbyPanel != null && lobbyPanel.activeInHierarchy;

        public void HideAllPanels()
        {
            SetActive(mainPanel, false);
            SetActive(multiplayerPanel, false);
            SetActive(lobbyPanel, false);
        }

        // ================================================================
        // ACCIONES (botones)
        // ================================================================
        private async void OnCreateRoomClicked()
        {
            if (isBusy || relayManager == null) return;
            isBusy = true;
            SetStatus("Creando sala...");

            string code = await relayManager.CreateHostAsync();

            isBusy = false;

            if (string.IsNullOrEmpty(code))
            {
                SetStatus("❌ Error creando sala. Revisa la consola.");
                return;
            }

            // Al crear el host, pasamos al lobby.
            ShowLobby();
        }

        private async void OnJoinRoomClicked()
        {
            if (isBusy || relayManager == null) return;

            string code = joinCodeInput != null ? joinCodeInput.text.Trim().ToUpper() : "";
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Introduce un código para unirte.");
                return;
            }

            isBusy = true;
            SetStatus(Ratms.UI.Loc.F("Uniéndose a {0}...", code));

            bool ok = await relayManager.JoinAsync(code);

            isBusy = false;

            if (!ok)
            {
                SetStatus("❌ Error al unirse. ¿El código es correcto?");
                return;
            }

            // Cliente conectado → pasamos al lobby (se mostrará "Esperando al host...").
            ShowLobby();
        }

        private void OnStartGameClicked()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;
            if (NetworkManager.Singleton.ConnectedClients.Count < minPlayersToStart)
            {
                SetStatus(Ratms.UI.Loc.F("Necesitas al menos {0} jugadores.", minPlayersToStart));
                return;
            }
            if (!LobbySync.AllReady)
            {
                SetStatus("Esperando a que todos marquen ¡Listo!");
                return;
            }

            // Ocultamos el menú antes del cambio de escena para evitar que se vea
            // sobrepuesto durante la transición.
            HideAllPanels();

            // NGO carga la escena SINCRONIZADA en todos los clientes.
            MatchRules.Mode = MatchMode.Online;
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }

        private void OnCancelLobbyClicked()
        {
            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
            {
                NetworkManager.Singleton.Shutdown();
            }

            // Damos un pequeño retardo para que el Shutdown se procese antes
            // de volver a mostrar el menú principal.
            StartCoroutine(ReturnToMainAfterFrame());
        }

        private IEnumerator ReturnToMainAfterFrame()
        {
            yield return null;
            ShowMain();
        }

        // ================================================================
        // EVENTOS DE RED
        // ================================================================
        private void HandleServerStarted()
        {
            // El HOST ya está servidor + cliente — el RefreshLobbyUI se encarga
            // de mostrar el código y habilitar/deshabilitar el botón Empezar.
            RefreshLobbyUI();
        }

        private void HandleClientConnected(ulong clientId)
        {
            RefreshLobbyUI();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            // Si el que se fue soy yo (cliente, no host), vuelvo al menú.
            var nm = NetworkManager.Singleton;
            if (nm != null && !nm.IsServer && clientId == nm.LocalClientId)
            {
                SetStatus("Te has desconectado de la sala.");
                ShowMain();
                return;
            }

            RefreshLobbyUI();
        }

        // ================================================================
        // UI del lobby
        // ================================================================
        private void Update()
        {
            // Refresco defensivo mientras el lobby esté abierto, por si se pierde
            // algún evento (poco común pero nos curamos en salud).
            if (lobbyPanel != null && lobbyPanel.activeInHierarchy)
                RefreshLobbyUI();
        }

        private void RefreshLobbyUI()
        {
            if (lobbyPanel == null || !lobbyPanel.activeInHierarchy) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            RefreshBiomeVisibility();

            // Código de sala (solo el host tiene uno real; el cliente ya lo metió él).
            if (lobbyCodeText != null)
            {
                if (nm.IsHost && relayManager != null && !string.IsNullOrEmpty(relayManager.LastJoinCode))
                    lobbyCodeText.text = Ratms.UI.Loc.F("Código: {0}", relayManager.LastJoinCode);
                else if (nm.IsClient)
                    lobbyCodeText.text = "Conectado a la sala";
                else
                    lobbyCodeText.text = "";
            }

            // Jugadores conectados
            int count = nm.ConnectedClientsIds != null ? nm.ConnectedClientsIds.Count : 0;
            if (lobbyPlayerCountText != null)
                lobbyPlayerCountText.text = Ratms.UI.Loc.F("{0} jugadores en la sala", count);

            // Botón Empezar: solo el host lo ve y solo si hay suficientes jugadores.
            if (btnStartGame != null)
            {
                btnStartGame.gameObject.SetActive(nm.IsHost);
                btnStartGame.interactable = nm.IsHost && count >= minPlayersToStart;
            }

            // Mensaje de estado por defecto del lobby
            if (statusText != null && string.IsNullOrEmpty(statusText.text))
            {
                SetStatus(nm.IsHost
                    ? (count >= minPlayersToStart ? "Listos para empezar" : "Esperando jugadores...")
                    : "Esperando a que el host empiece...");
            }
        }

        // ================================================================
        private void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        // ================================================================
        // NOMBRE DEL JUGADOR
        // ================================================================
        private void InitializePlayerNameInput()
        {
            if (playerNameInput == null) return;

            playerNameInput.characterLimit = 16;
            playerNameInput.text = PlayerNameRegistry.LocalName;
            playerNameInput.onEndEdit.AddListener(OnPlayerNameChanged);
        }

        private void OnPlayerNameChanged(string newName)
        {
            PlayerNameRegistry.LocalName = newName;   // guarda en PlayerPrefs + memoria
            // Refrescamos el input por si el registry sanitizó el texto (trim, max length)
            if (playerNameInput != null && playerNameInput.text != PlayerNameRegistry.LocalName)
                playerNameInput.text = PlayerNameRegistry.LocalName;
        }

        // ================================================================
        // BIOMA (selector solo para el host)
        // ================================================================
        private void InitializeBiomeDropdown()
        {
            if (biomeDropdown == null) return;

            biomeDropdown.ClearOptions();
            var options = new System.Collections.Generic.List<string> { Ratms.UI.Loc.T("Pradera"), Ratms.UI.Loc.T("Alcantarilla") };
            biomeDropdown.AddOptions(options);
            biomeDropdown.value = 0;
            // Si cambian el idioma con el menú abierto, rehacemos los nombres.
            Ratms.UI.Loc.Changed -= RelabelBiomes;
            Ratms.UI.Loc.Changed += RelabelBiomes;

            biomeDropdown.onValueChanged.AddListener(OnBiomeDropdownChanged);
            // Aplicamos el valor inicial (Meadow) para que el static ya tenga algo
            OnBiomeDropdownChanged(0);
        }

        private void RelabelBiomes()
        {
            if (biomeDropdown == null) { Ratms.UI.Loc.Changed -= RelabelBiomes; return; }
            string[] es = { "Pradera", "Alcantarilla" };
            for (int i = 0; i < biomeDropdown.options.Count && i < es.Length; i++)
                biomeDropdown.options[i].text = Ratms.UI.Loc.T(es[i]);
            biomeDropdown.RefreshShownValue();
        }

        private void OnBiomeDropdownChanged(int index)
        {
            // DOBLE SEGURIDAD: si por lo que sea un cliente logra cambiar el
            // dropdown (bug de UI, atajo de teclado, etc.), IGNORAMOS el cambio.
            // Solo el host puede setear el bioma que se cargará en la partida.
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsClient && !nm.IsHost)
            {
                // Revertir visualmente al valor del BiomeManager (si es que
                // hay uno), y no tocar SelectedBiomeType.
                Debug.Log("[MultiplayerMenu] Cliente intentó cambiar bioma — ignorado.");
                return;
            }

            // El índice mapea directamente al enum BiomeType (0=Meadow, 1=Sewer)
            BiomeManager.SelectedBiomeType = (BiomeType)index;
        }

        /// <summary>
        ///  Ocultamos el dropdown a los clientes (solo el host elige el bioma)
        ///  y mostramos una etiqueta con un mensaje readonly. Al cargar la escena
        ///  de juego, el BiomeSyncer sincronizará el bioma real elegido por el host.
        /// </summary>
        private void RefreshBiomeVisibility()
        {
            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsHost;
            bool isClient = nm != null && nm.IsClient && !nm.IsHost;

            if (biomeDropdown != null)
            {
                // Doble protección: dropdown oculto Y no interactivo para clientes.
                biomeDropdown.gameObject.SetActive(isHost);
                biomeDropdown.interactable = isHost;
            }
            if (biomeLabelForClient != null)
            {
                biomeLabelForClient.gameObject.SetActive(isClient);
                // Mensaje readonly claro: el cliente sabrá el bioma exacto al
                // arrancar la partida (el BiomeSyncer lo revelará entonces).
                biomeLabelForClient.text = "Bioma: lo elige el host";
            }
        }
    }
}
