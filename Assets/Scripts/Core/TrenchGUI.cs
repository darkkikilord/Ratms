using System;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  ESTILO COMÚN de los menús hechos con IMGUI (Jugar, Opciones, Pausa,
    ///  Lobby): paneles de "caja de munición" marrón oscuro con borde de
    ///  latón, títulos dorados y botones de madera. Así todos los menús
    ///  nuevos se ven iguales y de trinchera.
    ///
    ///  Uso: dentro de OnGUI → <c>TrenchGUI.Begin()</c> (escala a 1080p),
    ///  dibujar con Panel/Title/Button/Choice/Toggle/Slider y <c>End()</c>.
    /// </summary>
    public static class TrenchGUI
    {
        public static float Scale { get; private set; } = 1f;
        public static float VW => Screen.width / Scale;
        public static float VH => Screen.height / Scale;

        public static GUIStyle TitleStyle, LabelStyle, SmallStyle, ButtonStyle, SelectedStyle, BigButtonStyle, FieldStyle;
        private static Texture2D panelTex, borderTex, buttonTex, buttonHoverTex, selectedTex, darkTex, fillTex;
        private static float builtFor = -1f;
        private static Matrix4x4 saved;

        public static readonly Color Gold = new Color(0.96f, 0.78f, 0.38f);
        public static readonly Color Paper = new Color(0.92f, 0.88f, 0.78f);
        public static readonly Color Blood = new Color(0.85f, 0.22f, 0.18f);

        public static void Begin()
        {
            Scale = Mathf.Max(0.6f, Screen.height / 1080f);
            Ensure();
            saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        }

        public static void End() => GUI.matrix = saved;

        /// <summary>Panel con borde de latón y remaches.</summary>
        public static void Panel(Rect r)
        {
            GUI.DrawTexture(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), borderTex);
            GUI.DrawTexture(r, panelTex);
            // Remaches en las esquinas
            foreach (var p in new[] { new Vector2(r.x + 6f, r.y + 6f), new Vector2(r.xMax - 12f, r.y + 6f),
                                      new Vector2(r.x + 6f, r.yMax - 12f), new Vector2(r.xMax - 12f, r.yMax - 12f) })
                GUI.DrawTexture(new Rect(p.x, p.y, 6f, 6f), borderTex);
        }

        /// <summary>Oscurece todo lo de detrás (para menús encima de la partida).</summary>
        public static void Dim(float alpha = 0.55f)
        {
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, VW, VH), fillTex);
            GUI.color = c;
        }

        public static void Title(Rect r, string text) => GUI.Label(r, text, TitleStyle);
        public static void Label(Rect r, string text) => GUI.Label(r, text, LabelStyle);
        public static void Small(Rect r, string text) => GUI.Label(r, text, SmallStyle);

        public static bool Button(Rect r, string text, bool selected = false, bool big = false) =>
            GUI.Button(r, text, selected ? SelectedStyle : big ? BigButtonStyle : ButtonStyle);

        /// <summary>Fila "Etiqueta: [a] [b] [c]" con la opción elegida resaltada. Devuelve la nueva elección.</summary>
        public static int Choice(Rect r, string label, string[] options, int current, float labelWidth = 230f)
        {
            GUI.Label(new Rect(r.x, r.y, labelWidth, r.height), label, LabelStyle);
            float bx = r.x + labelWidth, bw = (r.width - labelWidth) / options.Length;
            int result = current;
            for (int i = 0; i < options.Length; i++)
                if (GUI.Button(new Rect(bx + i * bw + 3f, r.y, bw - 6f, r.height), options[i], i == current ? SelectedStyle : ButtonStyle))
                    result = i;
            return result;
        }

        /// <summary>Interruptor Sí/No.</summary>
        public static bool Toggle(Rect r, string label, bool value, float labelWidth = 300f)
        {
            int v = Choice(r, label, new[] { "Sí", "No" }, value ? 0 : 1, labelWidth);
            return v == 0;
        }

        /// <summary>Barra deslizante 0..1 con porcentaje.</summary>
        public static float Slider(Rect r, string label, float value, float labelWidth = 230f)
        {
            GUI.Label(new Rect(r.x, r.y, labelWidth, r.height), label, LabelStyle);
            var bar = new Rect(r.x + labelWidth, r.y + r.height * 0.5f - 8f, r.width - labelWidth - 80f, 16f);
            GUI.DrawTexture(bar, darkTex);
            var c = GUI.color;
            GUI.color = Gold;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), fillTex);
            GUI.color = c;
            float nv = GUI.HorizontalSlider(new Rect(bar.x, bar.y, bar.width, bar.height), value, 0f, 1f, GUIStyle.none, GUIStyle.none);
            GUI.Label(new Rect(bar.xMax + 10f, r.y, 70f, r.height), $"{Mathf.RoundToInt(nv * 100f)}%", LabelStyle);
            return nv;
        }

        private static void Ensure()
        {
            if (TitleStyle != null && Mathf.Approximately(builtFor, Scale) && panelTex != null) return;
            builtFor = Scale;
            panelTex = Tex(new Color(0.13f, 0.10f, 0.08f, 0.95f));
            borderTex = Tex(new Color(0.55f, 0.42f, 0.18f, 1f));
            buttonTex = Tex(new Color(0.32f, 0.24f, 0.15f, 1f));
            buttonHoverTex = Tex(new Color(0.42f, 0.31f, 0.18f, 1f));
            selectedTex = Tex(new Color(0.56f, 0.40f, 0.14f, 1f));
            darkTex = Tex(new Color(0.06f, 0.05f, 0.04f, 1f));
            fillTex = Tex(Color.white);

            TitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            TitleStyle.normal.textColor = Gold;
            LabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
            LabelStyle.normal.textColor = Paper;
            SmallStyle = new GUIStyle(LabelStyle) { fontSize = 16, fontStyle = FontStyle.Normal, wordWrap = true, alignment = TextAnchor.UpperLeft };
            SmallStyle.normal.textColor = new Color(0.8f, 0.76f, 0.66f);

            ButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            ButtonStyle.normal.background = buttonTex;
            ButtonStyle.hover.background = buttonHoverTex;
            ButtonStyle.active.background = selectedTex;
            ButtonStyle.normal.textColor = Paper;
            ButtonStyle.hover.textColor = Color.white;
            ButtonStyle.active.textColor = Color.white;
            ButtonStyle.border = new RectOffset(0, 0, 0, 0);

            SelectedStyle = new GUIStyle(ButtonStyle);
            SelectedStyle.normal.background = SelectedStyle.hover.background = selectedTex;
            SelectedStyle.normal.textColor = SelectedStyle.hover.textColor = new Color(1f, 0.93f, 0.55f);

            BigButtonStyle = new GUIStyle(ButtonStyle) { fontSize = 28 };

            FieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 6, 6) };
            FieldStyle.normal.background = FieldStyle.focused.background = FieldStyle.hover.background = darkTex;
            FieldStyle.normal.textColor = FieldStyle.focused.textColor = FieldStyle.hover.textColor = Gold;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // ============================================================
        //  OPCIONES compartidas (menú principal y pausa)
        // ============================================================
        public static bool GoreOn { get => PlayerPrefs.GetInt("opt_gore", 1) == 1; set => PlayerPrefs.SetInt("opt_gore", value ? 1 : 0); }
        public static bool LaughsOn { get => PlayerPrefs.GetInt("opt_laughs", 1) == 1; set => PlayerPrefs.SetInt("opt_laughs", value ? 1 : 0); }
        public static bool ShakeOn { get => PlayerPrefs.GetInt("opt_shake", 1) == 1; set => PlayerPrefs.SetInt("opt_shake", value ? 1 : 0); }

        /// <summary>Se avisa cuando cambian los volúmenes (OptionsMenu lo aplica al mezclador).</summary>
        public static event Action OnVolumesChanged;

        /// <summary>Dibuja el bloque de opciones en <paramref name="area"/>. Devuelve la altura usada.</summary>
        public static float OptionsBlock(Rect area)
        {
            float y = area.y, h = 42f, gap = 52f;
            float m = Slider(new Rect(area.x, y, area.width, h), "Volumen general", PlayerPrefs.GetFloat("opt_master", 0.8f)); y += gap;
            float mu = Slider(new Rect(area.x, y, area.width, h), "Música", PlayerPrefs.GetFloat("opt_music", 0.8f)); y += gap;
            float sf = Slider(new Rect(area.x, y, area.width, h), "Efectos", PlayerPrefs.GetFloat("opt_sfx", 0.8f)); y += gap;
            if (!Mathf.Approximately(m, PlayerPrefs.GetFloat("opt_master", 0.8f)) ||
                !Mathf.Approximately(mu, PlayerPrefs.GetFloat("opt_music", 0.8f)) ||
                !Mathf.Approximately(sf, PlayerPrefs.GetFloat("opt_sfx", 0.8f)))
            {
                PlayerPrefs.SetFloat("opt_master", m); PlayerPrefs.SetFloat("opt_music", mu); PlayerPrefs.SetFloat("opt_sfx", sf);
                OnVolumesChanged?.Invoke();
            }
            y += 6f;
            GoreOn = Toggle(new Rect(area.x, y, area.width, h), "Sangre y vísceras", GoreOn); y += gap;
            LaughsOn = Toggle(new Rect(area.x, y, area.width, h), "Risas y frases de las ratas", LaughsOn); y += gap;
            ShakeOn = Toggle(new Rect(area.x, y, area.width, h), "Temblor de cámara", ShakeOn); y += gap;
            bool full = Toggle(new Rect(area.x, y, area.width, h), "Pantalla completa", Screen.fullScreen);
            if (full != Screen.fullScreen) Screen.fullScreen = full;
            y += gap;
            return y - area.y;
        }
    }
}
