using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Core
{
    public static class SceneLoader
    {
        public static IEnumerator LoadSceneAsync(string sceneName, System.Action<float> onProgress = null)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                onProgress?.Invoke(op.progress / 0.9f);
                yield return null;
            }

            onProgress?.Invoke(1f);
            op.allowSceneActivation = true;
        }

        public static void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
