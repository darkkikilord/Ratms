using System;
using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  Panel del botón "Jugar": elegir entre PRÁCTICA (tú solo contra
    ///  dianas) y PARTIDA LOCAL en el mismo PC, con sus opciones básicas
    ///  (equipos, ratas por equipo y minutos hasta la muerte súbita).
    ///
    ///  Las opciones se guardan en <see cref="MatchRules"/> (PlayerPrefs), así
    ///  que se recuerdan para la próxima vez. Hecho con IMGUI: cero prefabs.
    /// </summary>
    public class PlayModeMenu : MonoBehaviour
    {
        public Action OnPractice;
        public Action OnLocal;
        public Action OnBack;

        private GUIStyle title, label, button, selected, big;
        private Texture2D panelTex;
        private float stylesScale = -1f;

        private static readonly float[] SuddenDeathChoices = { 5f, 10f, 15f, 0f };

        private void OnGUI()
        {
            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            EnsureStyles(scale);
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float vw = Screen.width / scale, vh = Screen.height / scale;

            var panel = new Rect((vw - 640f) * 0.5f, (vh - 640f) * 0.5f, 640f, 640f);
            GUI.DrawTexture(panel, panelTex);
            float x = panel.x + 40f, w = panel.width - 80f, y = panel.y + 24f;

            GUI.Label(new Rect(x, y, w, 50f), "¿A QUÉ JUGAMOS?", title);
            y += 70f;

            if (GUI.Button(new Rect(x, y, w, 56f), "Práctica  ·  tú solo contra ratas diana", big)) OnPractice?.Invoke();
            y += 84f;

            GUI.Label(new Rect(x, y, w, 30f), "PARTIDA LOCAL  (mismo PC, os pasáis el teclado)", label);
            y += 44f;

            y = Row(x, y, w, "Equipos", new[] { "2", "3", "4" }, MatchRules.LocalTeams - 2, i => MatchRules.LocalTeams = i + 2);
            y = Row(x, y, w, "Ratas por equipo", new[] { "1", "2", "3", "4" }, MatchRules.RatsPerTeam - 1, i => MatchRules.RatsPerTeam = i + 1);
            int sdIdx = Array.IndexOf(SuddenDeathChoices, MatchRules.SuddenDeathMinutes);
            y = Row(x, y, w, "Muerte súbita", new[] { "5 min", "10 min", "15 min", "Nunca" }, sdIdx,
                    i => MatchRules.SuddenDeathMinutes = SuddenDeathChoices[i]);
            y += 14f;

            if (GUI.Button(new Rect(x, y, w, 64f), "¡A LA TRINCHERA!", big)) { PlayerPrefs.Save(); OnLocal?.Invoke(); }
            y += 86f;

            if (GUI.Button(new Rect(x + w * 0.3f, y, w * 0.4f, 44f), "Volver", button)) OnBack?.Invoke();

            GUI.matrix = prev;
        }

        /// <summary>Fila "Etiqueta: [a] [b] [c]" con la opción elegida resaltada.</summary>
        private float Row(float x, float y, float w, string text, string[] options, int current, Action<int> pick)
        {
            GUI.Label(new Rect(x, y, 200f, 44f), text, label);
            float bx = x + 210f, bw = (w - 210f) / options.Length;
            for (int i = 0; i < options.Length; i++)
                if (GUI.Button(new Rect(bx + i * bw + 3f, y, bw - 6f, 44f), options[i], i == current ? selected : button))
                    pick(i);
            return y + 56f;
        }

        private void EnsureStyles(float scale)
        {
            if (title != null && Mathf.Approximately(stylesScale, scale)) return;
            stylesScale = scale;
            panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panelTex.SetPixel(0, 0, new Color(0.08f, 0.06f, 0.05f, 0.92f));
            panelTex.Apply();

            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(0.95f, 0.8f, 0.45f);
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = false, clipping = TextClipping.Overflow, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            label.normal.textColor = new Color(0.9f, 0.88f, 0.8f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
            selected = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            selected.normal.textColor = selected.hover.textColor = new Color(1f, 0.85f, 0.3f);
            var selTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            selTex.SetPixel(0, 0, new Color(0.45f, 0.32f, 0.12f, 1f));
            selTex.Apply();
            selected.normal.background = selected.hover.background = selTex;
            big = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
        }

        private void OnDestroy()
        {
            if (panelTex != null) Destroy(panelTex);
        }
    }
}
