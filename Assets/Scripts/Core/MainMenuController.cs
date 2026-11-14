using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Ratms.Core;
using Ratms.Networking;

namespace Ratms.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject optionsPanel;
        [SerializeField] private GameObject loadingPanel;

        [Header("Buttons (Main)")]
        [SerializeField] private Button btnPlay;
        [SerializeField] private Button btnMultiplayer;
        [SerializeField] private Button btnOptions;
        [SerializeField] private Button btnQuit;

        [Header("Loading UI (optional)")]
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text loadingText;

        [Header("Multijugador")]
        [Tooltip("Controlador del panel multijugador (NetworkManager + lobby). Deja vacío para un MainMenu puramente single-player.")]
        [SerializeField] private MultiplayerMenuController multiplayerMenu;

        [Tooltip("Si está marcado, el botón 'Jugar' abre DIRECTAMENTE el panel multijugador (como si fuera Multijugador). Útil si el MainMenu solo tiene un botón Jugar.")]
        [SerializeField] private bool playGoesToMultiplayer = false;

        [Header("Fondo")]
        [Tooltip("Imagen de fondo del menú. Si se deja vacío, se busca un objeto llamado 'Background'. " +
                 "Se le añade UIImageCover para que llene la pantalla sin bandas a los lados.")]
        [SerializeField] private Image backgroundImage;

        [Header("Config")]
        [Tooltip("Escena single-player que carga el botón 'Jugar' cuando NO va a multijugador.")]
        [SerializeField] private string firstLevelSceneName = "Level_01";
        [SerializeField] private bool useAsyncLoading = false;

        /// <summary>
        ///  El controlador multijugador VIVO. Al volver de una partida, el del
        ///  Inspector es la copia de la escena (que se destruye); el bueno es
        ///  el persistente, <see cref="MultiplayerMenuController.Instance"/>.
        /// </summary>
        private MultiplayerMenuController MP =>
            MultiplayerMenuController.Instance != null ? MultiplayerMenuController.Instance : multiplayerMenu;

        private void Awake()
        {
            // Si volvemos de una partida online y seguimos en la sala, el que
            // manda es el lobby: NO encendemos el menú principal encima.
            if (MultiplayerMenuController.InOnlineRoom)
            {
                if (mainPanel) mainPanel.SetActive(false);
                if (optionsPanel) optionsPanel.SetActive(false);
            }
            else ShowMain();
            if (loadingPanel) loadingPanel.SetActive(false);

            if (btnPlay) btnPlay.onClick.AddListener(OnPlay);
            if (btnMultiplayer) btnMultiplayer.onClick.AddListener(OnMultiplayer);
            if (btnOptions) btnOptions.onClick.AddListener(ShowOptions);
            if (btnQuit) btnQuit.onClick.AddListener(OnQuit);

            SetupBackgroundCover();

            // Ambiente de guerra: tinte, humo, obuses lejanos y desfile de ratas.
            var canvasGo = GameObject.Find("MainCanvas");
            var canvas = canvasGo != null ? canvasGo.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
            MenuDressing.Install(canvas, backgroundImage != null ? backgroundImage.transform : null);
            // Textos de la escena (botones, paneles del multijugador) en el idioma elegido.
            if (canvas != null) LocText.Install(canvas.transform);
        }

        /// <summary>
        ///  Hace que el fondo cubra toda la pantalla (sin bandas azules en
        ///  pantallas más anchas que la imagen). Ver <see cref="UIImageCover"/>.
        /// </summary>
        private void SetupBackgroundCover()
        {
            if (backgroundImage == null)
            {
                var go = GameObject.Find("Background");
                if (go != null) backgroundImage = go.GetComponent<Image>();
            }
            if (backgroundImage == null) return;

            if (backgroundImage.GetComponent<UIImageCover>() == null)
                backgroundImage.gameObject.AddComponent<UIImageCover>();
        }

        public void ShowMain()
        {
            if (mainPanel) mainPanel.SetActive(true);
            if (optionsPanel) optionsPanel.SetActive(false);
        }

        private OptionsOverlay optionsOverlay;

        public void ShowOptions()
        {
            // Opciones nuevas (estilo trinchera): volúmenes, sangre, risas, temblor y pantalla.
            if (mainPanel) mainPanel.SetActive(false);
            if (optionsPanel) optionsPanel.SetActive(false);
            if (optionsOverlay == null)
            {
                optionsOverlay = gameObject.AddComponent<OptionsOverlay>();
                optionsOverlay.OnClose = () => { optionsOverlay.enabled = false; ShowMain(); };
            }
            optionsOverlay.enabled = true;
            return;
        }

        private void ShowOldOptionsPanel()
        {
            if (mainPanel) mainPanel.SetActive(false);
            if (optionsPanel) optionsPanel.SetActive(true);
        }

        public void OnBackFromOptions()
        {
            ShowMain();
        }

        /// <summary>Botón "Jugar" (público para poder lanzarlo desde la prueba automática).</summary>
        public void OnPlay()
        {
            // Opción: el botón 'Jugar' va directo al panel multijugador.
            if (playGoesToMultiplayer && MP != null)
            {
                OnMultiplayer();
                return;
            }

            // Panel para elegir: PRÁCTICA (contra dianas) o PARTIDA LOCAL.
            if (MP != null)
            {
                MP.HideAllPanels();
                if (mainPanel) mainPanel.SetActive(false);
                if (optionsPanel) optionsPanel.SetActive(false);
                if (playModeMenu == null)
                {
                    playModeMenu = gameObject.AddComponent<PlayModeMenu>();
                    playModeMenu.OnPractice = StartPractice;
                    playModeMenu.OnLocal = StartLocalMatch;
                    playModeMenu.OnBack = () => { ClosePlayModeMenu(); ShowMain(); };
                    playModeMenu.OnTeam = () =>
                    {
                        playModeMenu.enabled = false;
                        if (teamEditor == null)
                        {
                            teamEditor = gameObject.AddComponent<TeamEditor>();
                            teamEditor.OnClose = () => { teamEditor.enabled = false; playModeMenu.enabled = true; };
                        }
                        teamEditor.enabled = true;
                    };
                }
                playModeMenu.enabled = true;
                return;
            }

            // Single-player clásico.
            if (useAsyncLoading && loadingPanel && progressBar && loadingText)
            {
                StartCoroutine(LoadAsyncRoutine());
            }
            else
            {
                SceneLoader.LoadScene(firstLevelSceneName);
            }
        }

        private PlayModeMenu playModeMenu;
        private TeamEditor teamEditor;

        private void ClosePlayModeMenu()
        {
            if (playModeMenu != null) playModeMenu.enabled = false;
        }

        /// <summary>MODO PRÁCTICA: tú solo contra ratas diana (público para la prueba automática).</summary>
        public void StartPractice()
        {
            ClosePlayModeMenu();
            if (MP == null) return;
            MP.HideAllPanels();
            if (mainPanel) mainPanel.SetActive(false);
            PracticeSession.Begin(MP.GameSceneName);
        }

        /// <summary>PARTIDA LOCAL en el mismo PC con las reglas de MatchRules.</summary>
        public void StartLocalMatch()
        {
            ClosePlayModeMenu();
            if (MP == null) return;
            MP.HideAllPanels();
            if (mainPanel) mainPanel.SetActive(false);
            if (!LocalMatchSession.Begin(MP.GameSceneName)) ShowMain();
        }

        /// <summary>
        ///  Llamado por el botón 'Multijugador'. Abre el panel de red.
        /// </summary>
        public void OnMultiplayer()
        {
            if (MP == null)
            {
                Debug.LogWarning("[MainMenu] No hay multiplayerMenu asignado en el inspector.");
                return;
            }
            MP.ShowMultiplayer();
            if (mainPanel) mainPanel.SetActive(false);
            if (optionsPanel) optionsPanel.SetActive(false);
        }

        private IEnumerator LoadAsyncRoutine()
        {
            loadingPanel.SetActive(true);
            progressBar.value = 0f;
            loadingText.text = "Cargando...";

            yield return StartCoroutine(SceneLoader.LoadSceneAsync(firstLevelSceneName, p =>
            {
                progressBar.value = p;
                loadingText.text = $"Cargando... {(int)(p * 100f)}%";
            }));
        }

        private void OnQuit()
        {
            SceneLoader.QuitGame();
        }
    }
}
