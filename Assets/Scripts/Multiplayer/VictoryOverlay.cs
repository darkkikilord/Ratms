using System;
using System.Collections;
using Ratms.UI;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  CARTEL DE VICTORIA (0.19): sustituye al texto suelto sobre el mapa por
    ///  una caja de munición con la frase, el ganador, la TABLA DE ESTADÍSTICAS
    ///  por equipo (<see cref="MatchStats"/>) y los botones de REVANCHA o salir.
    ///  Lo enciende <see cref="VictoryScreen"/>; los botones llaman a sus acciones.
    /// </summary>
    public class VictoryOverlay : MonoBehaviour
    {
        public string phrase, winnerLine;
        public Color winnerColor = Color.white;
        public int winnerTeam = -1;
        public Action OnRematch, OnLeave;
        public bool rematchEnabled = true;
        public string rematchLabel = "¡REVANCHA!", leaveLabel = "Volver al menú";

        private float shownAt;

        private void OnEnable() => shownAt = Time.unscaledTime;

        private void OnGUI()
        {
            GUI.depth = -20;
            TrenchGUI.Begin();
            // Entra con un pequeño "golpe" (crece de 0.9 a 1)
            float t = Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.35f);
            TrenchGUI.Dim(0.6f * t);

            var tm = NetworkTurnManager.Instance;
            var stats = MatchStats.Last;
            int rows = stats != null ? stats.teams.Count : 0;
            float w = 1020f, h = 470f + rows * 48f;
            var r = new Rect((TrenchGUI.VW - w) * 0.5f, (TrenchGUI.VH - h) * 0.5f, w, h);
            float s = Mathf.Lerp(0.9f, 1f, 1f - (1f - t) * (1f - t));
            var prev = GUI.matrix;
            var c = new Vector2(r.center.x * TrenchGUI.Scale, r.center.y * TrenchGUI.Scale);
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), c);

            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 22f, r.width, 56f), phrase);
            var wl = new Rect(r.x, r.y + 96f, r.width, 50f);
            var old = TrenchGUI.LabelStyle.normal.textColor;
            var oldAlign = TrenchGUI.LabelStyle.alignment;
            int oldSize = TrenchGUI.LabelStyle.fontSize;
            TrenchGUI.LabelStyle.normal.textColor = winnerColor;
            TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
            TrenchGUI.LabelStyle.fontSize = 34;
            TrenchGUI.Label(wl, winnerLine);
            TrenchGUI.LabelStyle.fontSize = oldSize;

            // ---------- Tabla ----------
            float y = r.y + 170f, x = r.x + 50f, tw = r.width - 100f;
            float[] col = { 0f, 0.28f, 0.42f, 0.635f, 0.865f };   // anchas: "Damage dealt" en inglés
            string[] head = { "Equipo", "Bajas", "Daño hecho", "Daño recibido", "Vivas" };
            TrenchGUI.LabelStyle.normal.textColor = TrenchGUI.Gold;
            for (int i = 0; i < head.Length; i++)
            {
                TrenchGUI.LabelStyle.alignment = i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                float cw = (i + 1 < col.Length ? col[i + 1] : 1f) - col[i];
                TrenchGUI.Label(new Rect(x + tw * col[i], y, tw * cw, 36f), head[i]);
            }
            y += 44f;
            if (stats == null)
            {
                TrenchGUI.Small(new Rect(x, y, tw, 30f), "…");
                y += 48f;
            }
            else
            {
                foreach (var row in stats.teams)
                {
                    bool win = row.team == winnerTeam;
                    if (win && Event.current.type == EventType.Repaint)
                    {
                        var gc = GUI.color;
                        GUI.color = new Color(1f, 0.85f, 0.3f, 0.18f);
                        GUI.DrawTexture(new Rect(x - 12f, y - 2f, tw + 24f, 44f), Texture2D.whiteTexture);
                        GUI.color = gc;
                    }
                    string name = tm != null ? tm.TeamDisplayName(row.team) : Loc.F("Equipo {0}", row.team + 1);
                    string kills = row.ownKills > 0 ? Loc.F("{0}  (+{1} propias)", row.kills, row.ownKills) : row.kills.ToString();
                    string[] cells = { (win ? "★ " : "") + name, kills, row.dealt.ToString(), row.taken.ToString(), row.alive.ToString() };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        TrenchGUI.LabelStyle.alignment = i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                        TrenchGUI.LabelStyle.normal.textColor = i == 0 ? TurnHUD.TeamTextColor(row.team) : TrenchGUI.Paper;
                        float cw = (i + 1 < col.Length ? col[i + 1] : 1f) - col[i];
                        TrenchGUI.Label(new Rect(x + tw * col[i], y, tw * cw, 40f), cells[i]);
                    }
                    y += 48f;
                }
            }
            TrenchGUI.LabelStyle.normal.textColor = old;
            TrenchGUI.LabelStyle.alignment = oldAlign;

            // ---------- Curiosidades ----------
            y += 6f;
            if (stats != null)
            {
                int secs = Mathf.FloorToInt(stats.seconds);
                string dur = Loc.F("Duración: {0}:{1:00}", secs / 60, secs % 60);
                string best = stats.bestHit > 0 ? Loc.F("Mejor golpe: {0} de daño, por {1}", stats.bestHit, stats.bestHitBy) : "";
                TrenchGUI.Small(new Rect(x, y, tw, 28f), string.IsNullOrEmpty(best) ? dur : dur + "    ·    " + best);
            }

            // ---------- Botones ----------
            float by = r.yMax - 92f, bw = (r.width - 140f) * 0.5f;
            bool en = GUI.enabled;
            GUI.enabled = rematchEnabled;
            // Letra grande solo si el texto es corto ("¡REVANCHA!"); el de online es largo.
            if (TrenchGUI.Button(new Rect(r.x + 50f, by, bw, 62f), rematchLabel, big: Loc.T(rematchLabel).Length <= 14)) OnRematch?.Invoke();
            GUI.enabled = en;
            if (TrenchGUI.Button(new Rect(r.xMax - 50f - bw, by, bw, 62f), leaveLabel)) OnLeave?.Invoke();

            GUI.matrix = prev;
            TrenchGUI.End();
        }
    }

    /// <summary>
    ///  REVANCHA en partida local o práctica: cierra la sesión de red, espera a
    ///  que se apague del todo y arranca otra igual (mismas reglas, mapa nuevo).
    ///  Vive en un objeto propio que sobrevive al cambio de escena.
    /// </summary>
    public class RematchRunner : MonoBehaviour
    {
        public static void Begin(MatchMode mode, string gameScene)
        {
            var go = new GameObject("[Revancha]");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<RematchRunner>();
            runner.StartCoroutine(runner.Run(mode, gameScene));
        }

        private IEnumerator Run(MatchMode mode, string gameScene)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            float t = 0f;
            while (nm != null && (nm.IsListening || nm.ShutdownInProgress) && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            // La práctica anterior se cierra sola al ver la red apagada (mira cada 0.5 s).
            while (PracticeSession.Active && t < 6f) { t += Time.unscaledDeltaTime; yield return null; }
            // Pasamos un instante por el MENÚ y arrancamos desde allí, igual que una
            // partida normal: si se rearranca el host dentro de la escena de juego,
            // Netcode se queda con objetos de red ya destruidos en su lista
            // (NullReference en cada fotograma).
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            yield return null;
            while (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu") yield return null;
            yield return null;
            yield return null;
            MatchStats.Last = null;
            if (mode == MatchMode.Practice) PracticeSession.Begin(gameScene);
            else if (!LocalMatchSession.Begin(gameScene)) TurnHUD.ExitToMenu();
            Destroy(gameObject);
        }
    }
}
