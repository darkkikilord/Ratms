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

        // ==============================================================
        // SALIR AL MENÚ: ESC abre la pausa (y desde ella, "Salir al menú")
        // ==============================================================

        private Ratms.UI.PauseMenu pause;

        /// <summary>Abre la pausa (ESC). Público para la prueba automática.</summary>
        public void OpenPause()
        {
            if (pause == null)
            {
                pause = gameObject.AddComponent<Ratms.UI.PauseMenu>();
                pause.OnExitToMenu = ExitToMenu;
            }
            pause.Open();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || Ratms.UI.PauseMenu.IsOpen) return;
            if (pause == null)
            {
                pause = gameObject.AddComponent<Ratms.UI.PauseMenu>();
                pause.OnExitToMenu = ExitToMenu;
            }
            pause.Open();
        }

        /// <summary>
        ///  Sale de la partida y vuelve al menú principal (desconecta de la
        ///  sala). Si eras el host, la partida se acaba para todos.
        /// </summary>
        public static void ExitToMenu()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            PlayerNameRegistry.Clear();
            MatchRules.Mode = MatchMode.Online;
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
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

            // TUERTA: en tu turno ves por un solo ojo (bordes negros y rojizos).
            var turnRat = tm.InTransition.Value ? null : tm.CurrentRat;
            var turnHealth = turnRat != null ? turnRat.GetComponent<NetworkHealth>() : null;
            if (turnRat != null && turnRat.IsOwner && turnHealth != null && turnHealth.HeadHurt)
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), BlindTex());

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float virtualWidth = Screen.width / scale;
            var panel = new Rect((virtualWidth - panelSize.x) * 0.5f, topMargin, panelSize.x, panelSize.y);
            GUI.DrawTexture(panel, panelTex);

            // --- Fila 1: de quién es el turno -------------------------
            string who;
            bool myTurn = false;
            float remaining = Mathf.Max(0f, tm.TurnTimeRemaining.Value);

            Color teamCol = normalColor;
            if (tm.InTransition.Value) who = Ratms.UI.Loc.T("— Cambiando de turno —");
            else if (tm.CurrentRatId.Value == ulong.MaxValue) who = Ratms.UI.Loc.T("Esperando para empezar...");
            else
            {
                var rat = tm.CurrentRat;
                int team = tm.CurrentTeam.Value;
                teamCol = TeamTextColor(team);
                string ratName = rat != null ? rat.RatName.Value.ToString() : "?";
                myTurn = rat != null && rat.IsOwner;
                // En partida local todas las ratas son "tuyas": decimos de qué equipo es el turno.
                who = myTurn && !IsHotseat(tm)
                    ? Ratms.UI.Loc.F("¡TU TURNO!  ·  {0}", ratName)
                    : Ratms.UI.Loc.F("Turno de {0}  ·  {1}", tm.TeamDisplayName(team), ratName);
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

            whoStyle.normal.textColor = tm.CurrentTeam.Value >= 0 ? teamCol : normalColor;
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
                if (!warning && IsHotseat(tm)) timeStyle.normal.textColor = teamCol;
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

            // --- Aviso de salida (ESC) ----------------------------------
            {
                bool confirming = false;
                string hint = Ratms.UI.Loc.T("ESC: pausa");
                var r = new Rect(virtualWidth - 360f, 8f, 350f, 24f);
                var prevAlign = windStyle.alignment;
                var prevCol = windStyle.normal.textColor;
                windStyle.alignment = TextAnchor.MiddleRight;
                windStyle.normal.textColor = confirming ? new Color(1f, 0.6f, 0.4f) : new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(r, hint, windStyle);
                windStyle.alignment = prevAlign;
                windStyle.normal.textColor = prevCol;
            }

            // --- Reloj de partida / muerte súbita (arriba a la izquierda) ---
            DrawMatchClock(tm);

            // --- Barras de vida de los equipos (abajo en el centro) ------
            DrawTeamBars(tm, virtualWidth, Screen.height / scale);

            // --- Aviso grande (eliminado, muerte súbita...) ---------------
            DrawAnnouncement(virtualWidth, Screen.height / scale);

            // --- Debajo del panel: tu arma (solo en tu turno) ----------
            if (myTurn)
            {
                var menu = LocalWeaponsMenu();
                string weapon = menu != null ? WeaponsMenuUI.ShortName(menu.SelectedWeapon) : "?";
                int ammo = menu != null && menu.SelectedWeapon != WeaponsMenuUI.WeaponId.None ? menu.AmmoLeft(menu.SelectedWeapon) : -1;
                string ammoText = ammo >= 0 ? $" x{ammo}" : "";
                bool armless = menu != null && !menu.HandsOk && menu.SelectedWeapon == WeaponsMenuUI.WeaponId.None;
                GUI.Label(new Rect(panel.x, panel.y + panelSize.y + 2f, panel.width, 24f),
                          armless ? Ratms.UI.Loc.T("Arma: ¡MORDISCO! (ESPACIO)   · sin brazos") : Ratms.UI.Loc.F("Arma: {0}{1}   (1-8 o TAB)", weapon, ammoText), windStyle);
                // Lesiones de la rata del turno
                string injuries = turnHealth != null ? turnHealth.InjuryText : "";
                if (!string.IsNullOrEmpty(injuries))
                {
                    var prevCol = windStyle.normal.textColor;
                    windStyle.normal.textColor = new Color(1f, 0.45f, 0.35f);
                    GUI.Label(new Rect(panel.x, panel.y + panelSize.y + 24f, panel.width, 24f), Ratms.UI.Loc.F("Heridas: {0}", injuries), windStyle);
                    windStyle.normal.textColor = prevCol;
                }
            }

            GUI.matrix = previous;
        }

        /// <summary>Menú de armas de la rata del turno (si es mía).</summary>
        private static WeaponsMenuUI LocalWeaponsMenu()
        {
            var tm = NetworkTurnManager.Instance;
            var rat = tm != null ? tm.CurrentRat : null;
            return rat != null && rat.IsOwner ? rat.GetComponentInChildren<WeaponsMenuUI>() : null;
        }

        // ==============================================================
        // EQUIPOS
        // ==============================================================
        public static Color TeamColor(int team) => team < 0 ? Color.white : (Color)RatAppearance.ColorForTeam(team);

        /// <summary>Color del equipo aclarado para TEXTO (se lee mejor sobre el panel oscuro).</summary>
        public static Color TeamTextColor(int team) => Color.Lerp(TeamColor(team), Color.white, 0.35f);

        /// <summary>¿Partida local? (los equipos tienen nombre propio en vez del del jugador).</summary>
        private static bool IsHotseat(NetworkTurnManager tm) => MatchRules.Mode == MatchMode.Local;

        private struct TeamInfo { public int hp, max, alive, total; }
        private readonly System.Collections.Generic.SortedDictionary<int, TeamInfo> teamInfo = new();
        private float teamInfoAt = -1f;

        private void RefreshTeamInfo()
        {
            if (Time.unscaledTime - teamInfoAt < 0.25f) return;
            teamInfoAt = Time.unscaledTime;
            teamInfo.Clear();
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!m.IsSpawned || m.IsTestDummy || m.TeamIndex.Value < 0) continue;
                var h = m.GetComponent<NetworkHealth>();
                if (h == null) continue;
                teamInfo.TryGetValue(m.TeamIndex.Value, out var ti);
                ti.max += h.MaxHealth;
                ti.total++;
                if (!h.IsDead.Value) { ti.hp += Mathf.Max(0, h.CurrentHealth.Value); ti.alive++; }
                teamInfo[m.TeamIndex.Value] = ti;
            }
        }

        /// <summary>Una barra por equipo: nombre, vida total que queda y ratas vivas.</summary>
        private void DrawTeamBars(NetworkTurnManager tm, float vw, float vh)
        {
            RefreshTeamInfo();
            if (teamInfo.Count < 2) return;   // práctica: no hace falta
            const float rowH = 22f, barW = 260f, nameW = 150f;
            float totalW = nameW + barW + 90f;
            float y = vh - 12f - teamInfo.Count * (rowH + 4f);
            float x = (vw - totalW) * 0.5f;
            GUI.DrawTexture(new Rect(x - 10f, y - 8f, totalW + 20f, teamInfo.Count * (rowH + 4f) + 12f), panelTex);

            var prevAlign = windStyle.alignment;
            var prevCol = windStyle.normal.textColor;
            int prevSize = windStyle.fontSize;
            windStyle.fontSize = 15;
            foreach (var kv in teamInfo)
            {
                Color c = TeamColor(kv.Key);
                bool dead = kv.Value.alive == 0;
                bool turn = tm.CurrentTeam.Value == kv.Key;
                windStyle.alignment = TextAnchor.MiddleRight;
                windStyle.normal.textColor = dead ? new Color(0.6f, 0.6f, 0.6f, 0.7f) : TeamTextColor(kv.Key);
                GUI.Label(new Rect(x, y, nameW - 8f, rowH), (turn ? "> " : "") + tm.TeamDisplayName(kv.Key), windStyle);

                var bar = new Rect(x + nameW, y + 4f, barW, rowH - 8f);
                GUI.DrawTexture(bar, barBackTex);
                float frac = kv.Value.max > 0 ? Mathf.Clamp01(kv.Value.hp / (float)kv.Value.max) : 0f;
                var pc = GUI.color;
                GUI.color = c;
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * frac, bar.height), barFillTex);
                GUI.color = pc;

                windStyle.alignment = TextAnchor.MiddleLeft;
                windStyle.normal.textColor = dead ? new Color(0.6f, 0.6f, 0.6f, 0.7f) : Color.white;
                GUI.Label(new Rect(x + nameW + barW + 8f, y, 90f, rowH), dead ? "KO" : $"{kv.Value.hp}  ({kv.Value.alive})", windStyle);
                y += rowH + 4f;
            }
            windStyle.alignment = prevAlign;
            windStyle.normal.textColor = prevCol;
            windStyle.fontSize = prevSize;
        }

        /// <summary>Reloj de partida y cuenta atrás hasta la muerte súbita.</summary>
        private void DrawMatchClock(NetworkTurnManager tm)
        {
            var prevAlign = windStyle.alignment;
            var prevCol = windStyle.normal.textColor;
            windStyle.alignment = TextAnchor.MiddleLeft;
            string text;
            if (tm.SuddenDeath.Value)
            {
                text = Ratms.UI.Loc.T("¡MUERTE SÚBITA!  El agua sube");
                windStyle.normal.textColor = Color.Lerp(warningColor, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            }
            else
            {
                float t = tm.MatchTime.Value;
                text = Ratms.UI.Loc.T("Partida") + " " + Clock(t);
                if (tm.SuddenDeathAt.Value > 0f)
                {
                    float left = Mathf.Max(0f, tm.SuddenDeathAt.Value - t);
                    text += "   ·   " + Ratms.UI.Loc.F("Muerte súbita en {0}", Clock(left));
                    windStyle.normal.textColor = left < 60f ? warningColor : new Color(1f, 1f, 1f, 0.75f);
                }
                else windStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }
            GUI.Label(new Rect(12f, 8f, 520f, 24f), text, windStyle);
            windStyle.alignment = prevAlign;
            windStyle.normal.textColor = prevCol;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ==============================================================
        // AVISOS GRANDES
        // ==============================================================
        private string announceText;
        private Color announceColor;
        private float announceUntil;
        private GUIStyle announceStyle;

        private void OnEnable() => NetworkTurnManager.OnAnnouncement += Announce;
        private void OnDisable() => NetworkTurnManager.OnAnnouncement -= Announce;

        /// <summary>Último aviso mostrado (lo lee la prueba automática).</summary>
        public static string LastAnnouncement { get; private set; }

        private void Announce(string text, Color color)
        {
            // Llega en español (o empaquetado con datos): se traduce AQUÍ, en
            // cada PC, para que cada jugador lo lea en su idioma.
            text = Ratms.UI.Loc.Show(text);
            announceText = text;
            LastAnnouncement = text;
            announceColor = color;
            announceUntil = Time.unscaledTime + 3f;
            Sfx.Play(Sfx.Id.TurnStart, 0.8f, 0f);
        }

        private static Texture2D blindTex;

        /// <summary>Viñeta de "un solo ojo": centro despejado, bordes casi negros con tinte de sangre.</summary>
        private static Texture2D BlindTex()
        {
            if (blindTex != null) return blindTex;
            const int N = 128;
            blindTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // Óvalo algo desplazado: el ojo bueno es uno solo
                    float dx = (x - N * 0.52f) / (N * 0.5f), dy = (y - N * 0.5f) / (N * 0.5f);
                    float d = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 1.25f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.95f, d)) * 0.93f;
                    float red = Mathf.Clamp01(1f - Mathf.Abs(d - 0.6f) * 4f) * 0.35f;
                    px[y * N + x] = new Color32((byte)(20 + red * 160), 4, 4, (byte)(a * 255));
                }
            blindTex.SetPixels32(px);
            blindTex.Apply();
            return blindTex;
        }

        private void DrawAnnouncement(float vw, float vh)
        {
            if (string.IsNullOrEmpty(announceText) || Time.unscaledTime > announceUntil) return;
            if (announceStyle == null)
                announceStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 44, fontStyle = FontStyle.Bold };
            float left = announceUntil - Time.unscaledTime;
            float a = Mathf.Clamp01(left / 0.5f);
            var r = new Rect(0f, vh * 0.3f, vw, 120f);
            announceStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f * a);
            GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), announceText, announceStyle);
            announceStyle.normal.textColor = new Color(announceColor.r, announceColor.g, announceColor.b, a);
            GUI.Label(r, announceText, announceStyle);
        }

        /// <summary>
        ///  Texto del viento: flechas hacia donde sopla. 1 flecha = flojo,
        ///  5 = huracán. "Viento: calma" si es 0.
        /// </summary>
        public static string WindText(float wind)
        {
            float mag = Mathf.Abs(wind);
            if (mag < 0.05f) return Ratms.UI.Loc.T("Viento: calma");
            string wordWind = Ratms.UI.Loc.T("Viento");
            int arrows = Mathf.Clamp(Mathf.CeilToInt(mag * 5f), 1, 5);
            // ASCII a propósito: la fuente por defecto de IMGUI no garantiza
            // los caracteres de flecha Unicode en todas las plataformas.
            return wind < 0f
                ? $"{new string('<', arrows)}  {wordWind}  {new string(' ', arrows)}"
                : $"{new string(' ', arrows)}  {wordWind}  {new string('>', arrows)}";
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
