using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  HUD del turno, arriba en el centro de la pantalla:
    ///   • De quién es el turno (con su nombre) — "¡TU TURNO!" si es el tuyo.
    ///   • CUENTA ATRÁS grande. En los últimos <see cref="warningSeconds"/>
    ///     segundos se pone roja y "late" para meter prisa.
    ///   • Barra de tiempo que se vacía.
    ///   • VIENTO: flechas hacia el lado al que sopla, más flechas = más fuerte.
    ///
    ///  No hace falta ponerlo en la escena: si no hay ninguno,
    ///  <see cref="NetworkTurnManager"/> lo añade solo al arrancar.
    ///
    ///  Está hecho con IMGUI (OnGUI) a propósito: cero prefabs, cero Canvas,
    ///  se escala con la resolución. Los estilos son PROPIOS (no tocamos
    ///  GUI.skin global, que afectaría a otros OnGUI como el menú de armas).
    /// </summary>
    public class TurnHUD : MonoBehaviour
    {
        [Header("Apariencia")]
        [SerializeField] private Vector2 panelSize = new Vector2(440f, 138f);
        [SerializeField] private float topMargin = 14f;
        [Tooltip("Resolución vertical de referencia: a esta altura de pantalla la escala es 1.")]
        [SerializeField] private float referenceHeight = 1080f;

        [Header("Cuenta atrás")]
        [Tooltip("Segundos finales en los que el número se pone rojo y late.")]
        [SerializeField] private float warningSeconds = 5f;
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f);
        [SerializeField] private Color myTurnColor = new Color(0.55f, 1f, 0.55f);
        [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.25f);

        [Header("Viento")]
        [SerializeField] private Color windColor = new Color(0.6f, 0.85f, 1f);

        private GUIStyle whoStyle, timeStyle, windStyle;
        private bool wasMyTurn;
        private int lastTickSecond = -1;
        private Texture2D panelTex, barBackTex, barFillTex;
        private float stylesForScale = -1f;

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !(nm.IsClient || nm.IsServer)) return;

            var tm = NetworkTurnManager.Instance;
            if (tm == null || tm.GameEnded.Value) return;

            float scale = Mathf.Max(0.6f, Screen.height / referenceHeight);
            EnsureStyles(scale);

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float virtualWidth = Screen.width / scale;
            var panel = new Rect((virtualWidth - panelSize.x) * 0.5f, topMargin, panelSize.x, panelSize.y);
            GUI.DrawTexture(panel, panelTex);

            // --- Fila 1: de quién es el turno -------------------------
            string who;
            bool myTurn = false;
            float remaining = Mathf.Max(0f, tm.TurnTimeRemaining.Value);

            if (tm.InTransition.Value) who = "— Cambiando de turno —";
            else if (tm.CurrentTurnClientId.Value == ulong.MaxValue) who = "Esperando para empezar...";
            else
            {
                ulong turnOf = tm.CurrentTurnClientId.Value;
                myTurn = turnOf == nm.LocalClientId;
                who = myTurn ? "¡TU TURNO!" : $"Turno de {PlayerNameRegistry.GetName(turnOf)}";
            }
            // Sonidos: "ding" al empezar TU turno y tic-tac en los últimos segundos.
            if (Event.current.type == EventType.Repaint)
            {
                if (myTurn && !wasMyTurn) Sfx.Play(Sfx.Id.TurnStart, 1f, 0f);
                wasMyTurn = myTurn;
                int sec = Mathf.CeilToInt(remaining);
                if (myTurn && sec <= warningSeconds && sec > 0 && sec != lastTickSecond && !tm.InTransition.Value)
                    Sfx.Play(Sfx.Id.Tick, 0.8f, 0f);
                lastTickSecond = sec;
            }

            whoStyle.normal.textColor = myTurn ? myTurnColor : normalColor;
            GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 26f), who, whoStyle);

            // --- Fila 2: cuenta atrás grande ---------------------------
            bool active = !tm.InTransition.Value && tm.CurrentTurnClientId.Value != ulong.MaxValue;
            if (active)
            {
                int seconds = Mathf.CeilToInt(remaining);
                bool warning = remaining <= warningSeconds;

                // Latido: en los últimos segundos el número crece y encoge
                // una vez por segundo (justo al cambiar de número).
                float pulse = 1f;
                if (warning)
                {
                    float frac = remaining - Mathf.Floor(remaining);   // 1→0 dentro de cada segundo
                    pulse = 1f + 0.35f * Mathf.Clamp01((frac - 0.6f) / 0.4f);
                }
                timeStyle.normal.textColor = warning ? warningColor : (myTurn ? myTurnColor : normalColor);
                timeStyle.fontSize = Mathf.RoundToInt(46f * pulse);
                GUI.Label(new Rect(panel.x, panel.y + 30f, panel.width, 56f), seconds.ToString(), timeStyle);

                // Barra de tiempo
                float total = Mathf.Max(1f, tm.TurnDuration);
                var bar = new Rect(panel.x + 20f, panel.y + 88f, panel.width - 40f, 8f);
                GUI.DrawTexture(bar, barBackTex);
                var fill = new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(remaining / total), bar.height);
                var prevColor = GUI.color;
                GUI.color = warning ? warningColor : (myTurn ? myTurnColor : normalColor);
                GUI.DrawTexture(fill, barFillTex);
                GUI.color = prevColor;
            }

            // --- Fila 3: viento ----------------------------------------
            GUI.Label(new Rect(panel.x, panel.y + 102f, panel.width, 28f), WindText(tm.Wind.Value), windStyle);

            // --- Debajo del panel: tu arma (solo en tu turno) ----------
            if (myTurn)
            {
                var menu = LocalWeaponsMenu();
                string weapon = menu != null ? WeaponsMenuUI.ShortName(menu.SelectedWeapon) : "?";
                GUI.Label(new Rect(panel.x, panel.y + panelSize.y + 2f, panel.width, 24f),
                          $"Arma: {weapon}   (1-7 o TAB)", windStyle);
            }

            GUI.matrix = previous;
        }

        private WeaponsMenuUI cachedLocalMenu;

        /// <summary>Menú de armas de MI rata (la que controlo).</summary>
        private WeaponsMenuUI LocalWeaponsMenu()
        {
            if (cachedLocalMenu != null && cachedLocalMenu.IsOwner && cachedLocalMenu.isActiveAndEnabled) return cachedLocalMenu;
            cachedLocalMenu = null;
            foreach (var m in FindObjectsByType<WeaponsMenuUI>(FindObjectsSortMode.None))
            {
                var move = m.GetComponentInParent<NetworkPlayerMovement>();
                if (m.IsOwner && (move == null || !move.IsTestDummy)) { cachedLocalMenu = m; break; }
            }
            return cachedLocalMenu;
        }

        /// <summary>
        ///  Texto del viento: flechas hacia donde sopla. 1 flecha = flojo,
        ///  5 = huracán. "Viento: calma" si es 0.
        /// </summary>
        public static string WindText(float wind)
        {
            float mag = Mathf.Abs(wind);
            if (mag < 0.05f) return "Viento: calma";
            int arrows = Mathf.Clamp(Mathf.CeilToInt(mag * 5f), 1, 5);
            // ASCII a propósito: la fuente por defecto de IMGUI no garantiza
            // los caracteres de flecha Unicode en todas las plataformas.
            return wind < 0f
                ? $"{new string('<', arrows)}  Viento  {new string(' ', arrows)}"
                : $"{new string(' ', arrows)}  Viento  {new string('>', arrows)}";
        }

        private void EnsureStyles(float scale)
        {
            if (whoStyle != null && Mathf.Approximately(stylesForScale, scale)) return;
            stylesForScale = scale;

            panelTex   = MakeTex(new Color(0f, 0f, 0f, 0.55f));
            barBackTex = MakeTex(new Color(1f, 1f, 1f, 0.15f));
            barFillTex = MakeTex(Color.white);

            whoStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };
            timeStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 46,
                fontStyle = FontStyle.Bold,
            };
            windStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
            };
            windStyle.normal.textColor = windColor;
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnDestroy()
        {
            if (panelTex != null) Destroy(panelTex);
            if (barBackTex != null) Destroy(barBackTex);
            if (barFillTex != null) Destroy(barFillTex);
        }
    }
}
