using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Ratms.Core;

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
        [SerializeField] private Button btnOptions;
        [SerializeField] private Button btnQuit;

        [Header("Loading UI (optional)")]
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text loadingText;

        [Header("Config")]
        [SerializeField] private string firstLevelSceneName = "Level_01";
        [SerializeField] private bool useAsyncLoading = false;

        private void Awake()
        {
            ShowMain();
            if (loadingPanel) loadingPanel.SetActive(false);

            if (btnPlay) btnPlay.onClick.AddListener(OnPlay);
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
            if (useAsyncLoading && loadingPanel && progressBar && loadingText)
            {
                StartCoroutine(LoadAsyncRoutine());
            }
            else
            {
                SceneLoader.LoadScene(firstLevelSceneName);
            }
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
