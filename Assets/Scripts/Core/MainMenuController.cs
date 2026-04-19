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

        [Header("Config")]
        [Tooltip("Escena single-player que carga el botón 'Jugar' cuando NO va a multijugador.")]
        [SerializeField] private string firstLevelSceneName = "Level_01";
        [SerializeField] private bool useAsyncLoading = false;

        private void Awake()
        {
            ShowMain();
            if (loadingPanel) loadingPanel.SetActive(false);

            if (btnPlay) btnPlay.onClick.AddListener(OnPlay);
            if (btnMultiplayer) btnMultiplayer.onClick.AddListener(OnMultiplayer);
            if (btnOptions) btnOptions.onClick.AddListener(ShowOptions);
            if (btnQuit) btnQuit.onClick.AddListener(OnQuit);
        }

        public void ShowMain()
        {
            if (mainPanel) mainPanel.SetActive(true);
            if (optionsPanel) optionsPanel.SetActive(false);
        }

        public void ShowOptions()
        {
            if (mainPanel) mainPanel.SetActive(false);
            if (optionsPanel) optionsPanel.SetActive(true);
        }

        public void OnBackFromOptions()
        {
            ShowMain();
        }

        private void OnPlay()
        {
            // Opción: el botón 'Jugar' va directo al panel multijugador.
            if (playGoesToMultiplayer && multiplayerMenu != null)
            {
                OnMultiplayer();
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

        /// <summary>
        ///  Llamado por el botón 'Multijugador'. Abre el panel de red.
        /// </summary>
        public void OnMultiplayer()
        {
            if (multiplayerMenu == null)
            {
                Debug.LogWarning("[MainMenu] No hay multiplayerMenu asignado en el inspector.");
                return;
            }
            multiplayerMenu.ShowMultiplayer();
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
