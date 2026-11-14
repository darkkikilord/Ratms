using Ratms.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.UI
{
    /// <summary>
    ///  PANTALLA DE CARGA (0.22).
    ///
    ///  Tapa el salto del menú a la partida: la generación del terreno, la
    ///  aparición de las ratas y el primer encuadre de la cámara. Muestra una
    ///  rata corriendo, puntos de "cargando" y un consejo al azar.
    ///
    ///  Se enciende sola cuando NGO empieza a cargar una escena (host y
    ///  clientes) o con <see cref="Show"/> (cargas sin red). Se apaga cuando la
    ///  escena nueva ya está y, si es la de partida, cuando hay ratas en ella
    ///  (con un tope de seguridad para no quedarse colgada).
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        private static LoadingScreen instance;
        private static readonly string[] Tips =
        {
            "Consejo: TAB abre el arsenal; cada arma tiene su tecla.",
            "Consejo: con V ves el mapa entero y con el botón derecho mueves la cámara.",
            "Consejo: el viento desvía bazucas, gas y palomas, pero no el mortero.",
            "Consejo: el agua no perdona. Nada de bañarse.",
            "Consejo: los barriles explotan en cadena. Úsalos a tu favor.",
            "Consejo: una rata sin patas no salta. Cuida tus piezas.",
            "Consejo: el queso apestoso atrae a las ratas enemigas... y revienta.",
            "Consejo: las cajas pueden traer armas, vida... o una trampa.",
            "Consejo: los sacos terreros sirven de muro o de puente.",
            "Consejo: la voltereta (doble salto) sube más que un salto normal.",
        };

        private bool showing;
        private float shownAt, hideAt = -1f, alpha;
        private string tip;
        private Texture2D ratTex;
        private GUIStyle tipStyle;
        private bool subscribed;
        private const float MaxShow = 12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LoadingScreen>();
            SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.OnSceneLoaded(s); };
        }

        /// <summary>Enciende la pantalla de carga (si no lo estaba).</summary>
        public static void Show()
        {
            if (instance == null) Boot();
            instance.Begin();
        }

        private void Begin()
        {
            if (showing && hideAt < 0f) return;
            showing = true;
            hideAt = -1f;
            shownAt = Time.unscaledTime;
            tip = Tips[Random.Range(0, Tips.Length)];
        }

        private void OnSceneLoaded(Scene s)
        {
            if (!showing) return;
            // Al menú: basta con que esté cargado. A la partida: esperamos a las ratas (Update).
            if (s.name == "MainMenu") hideAt = Time.unscaledTime + 0.25f;
        }

        private void Update()
        {
            // Engancharse a las cargas de escena de NGO (su SceneManager nace al arrancar la red).
            var nm = NetworkManager.Singleton;
            if (!subscribed && nm != null && nm.IsListening && nm.SceneManager != null)
            {
                nm.SceneManager.OnSceneEvent += OnSceneEvent;
                subscribed = true;
            }
            else if (subscribed && (nm == null || !nm.IsListening)) subscribed = false;

            if (!showing) { alpha = 0f; return; }

            float now = Time.unscaledTime;
            if (hideAt < 0f)
            {
                bool inGame = SceneManager.GetActiveScene().name != "MainMenu";
                bool ready = inGame && NetworkTurnManager.Instance != null
                             && FindAnyObjectByType<NetworkPlayerMovement>() != null;
                if (ready) hideAt = now + 0.6f;   // que caigan y la cámara se coloque
                else if (now - shownAt > MaxShow) hideAt = now;
            }

            if (hideAt >= 0f && now >= hideAt)
            {
                alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime * 3f);
                if (alpha <= 0f) showing = false;
            }
            else alpha = Mathf.MoveTowards(alpha, 1f, Time.unscaledDeltaTime * 8f);
        }

        private void OnSceneEvent(SceneEvent e)
        {
            if (e.SceneEventType == SceneEventType.Load && e.ClientId == NetworkManager.Singleton.LocalClientId)
            {
                Begin();
                alpha = 1f;   // al instante: la carga congela el juego un momento
            }
        }

        private void OnGUI()
        {
            if (!showing || alpha <= 0.001f) return;
            GUI.depth = -1000;   // por encima de todo
            TrenchGUI.Begin();
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            float vw = TrenchGUI.VW, vh = TrenchGUI.VH;
            GUI.color = new Color(0.16f, 0.11f, 0.07f, alpha * 0.9f);
            GUI.DrawTexture(new Rect(0f, 0f, vw, vh), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);

            float t = Time.unscaledTime - shownAt;
            int dots = 1 + (int)(t * 2.5f) % 3;
            TrenchGUI.Title(new Rect(0f, vh * 0.5f - 70f, vw, 60f), Loc.T("Cavando trincheras") + new string('.', dots));

            // Rata corriendo de lado a lado (con saltitos)
            if (ratTex == null)
            {
                var sp = WeaponArt.RatBombIcon();
                if (sp != null) ratTex = sp.texture;
            }
            if (ratTex != null)
            {
                float size = 96f;
                float run = Mathf.Repeat(t * 0.35f, 1f);
                float x = Mathf.Lerp(vw * 0.5f - 260f, vw * 0.5f + 260f - size, run);
                float hop = Mathf.Abs(Mathf.Sin(t * 12f)) * 10f;
                var r = new Rect(x, vh * 0.5f + 20f - hop, size, size);
                GUI.DrawTexture(r, ratTex, ScaleMode.ScaleToFit);
                // Suelo
                GUI.color = new Color(0.45f, 0.62f, 0.25f, alpha);
                GUI.DrawTexture(new Rect(vw * 0.5f - 270f, vh * 0.5f + 20f + size, 540f, 6f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, alpha);
            }

            if (tipStyle == null)
                tipStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 24, wordWrap = true,
                    font = TrenchGUI.Typewriter, normal = { textColor = new Color(1f, 0.88f, 0.62f) },
                };
            GUI.Label(new Rect(vw * 0.5f - 500f, vh * 0.5f + 140f, 1000f, 70f), Loc.T(tip), tipStyle);

            GUI.color = prev;
            TrenchGUI.End();
        }
    }
}
