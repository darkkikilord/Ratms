using System;
using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  ESTILO COMÚN de los menús hechos con IMGUI (Jugar, Opciones, Pausa,
    ///  Lobby, Mi equipo...). 0.19: rehecho para que parezca de TRINCHERA de
    ///  verdad y no "rectángulos de colores":
    ///   · Paneles de CAJA DE MUNICIÓN: chapa verde oliva con grano, bisel,
    ///     tornillos en las esquinas y letras de plantilla.
    ///   · Botones de TABLÓN DE MADERA (vetas, clavos, luz arriba/sombra abajo)
    ///     que se iluminan y "levantan" al pasar el ratón.
    ///   · Fuentes con personalidad: Black Ops One (plantilla militar) para
    ///     títulos y botones y Special Elite (máquina de escribir) para textos.
    ///     Están en Resources/Fonts (licencias en LICENCIAS.txt).
    ///   · TODO texto que pasa por aquí se TRADUCE solo (<see cref="Loc.T"/>):
    ///     los menús escriben en español y salen en el idioma elegido.
    ///
    ///  Uso: dentro de OnGUI → <c>TrenchGUI.Begin()</c> (escala a 1080p),
    ///  dibujar con Panel/Title/Button/Choice/Toggle/Slider y <c>End()</c>.
    ///  Las texturas se generan por código (sin imágenes en el proyecto).
    /// </summary>
    public static class TrenchGUI
    {
        public static float Scale { get; private set; } = 1f;
        public static float VW => Screen.width / Scale;
        public static float VH => Screen.height / Scale;

        public static GUIStyle TitleStyle, LabelStyle, SmallStyle, ButtonStyle, SelectedStyle, BigButtonStyle, FieldStyle;
        private static GUIStyle shadowStyle;
        private static Texture2D panelTex, plankTex, plankHoverTex, plankDownTex, brassTex, darkTex, fillTex, boltTex, knobTex;
        private static float builtFor = -1f;
        private static Matrix4x4 saved;

        public static readonly Color Gold = new Color(0.98f, 0.80f, 0.36f);
        public static readonly Color Paper = new Color(0.94f, 0.90f, 0.80f);
        public static readonly Color Blood = new Color(0.85f, 0.22f, 0.18f);
        private static readonly Color Ink = new Color(0.10f, 0.07f, 0.04f, 0.85f);

        // ------------------------------------------------------------------
        //  FUENTES (las comparte el menú principal de uGUI vía MenuDressing)
        // ------------------------------------------------------------------
        private static Font stencil, typewriter;
        private static bool fontsLoaded;

        /// <summary>Fuente militar de plantilla (títulos, botones). Puede ser null.</summary>
        public static Font Stencil { get { LoadFonts(); return stencil; } }
        /// <summary>Fuente de máquina de escribir (textos). Puede ser null.</summary>
        public static Font Typewriter { get { LoadFonts(); return typewriter; } }

        private static void LoadFonts()
        {
            if (fontsLoaded) return;
            fontsLoaded = true;
            stencil = Resources.Load<Font>("Fonts/BlackOpsOne-Regular");
            typewriter = Resources.Load<Font>("Fonts/SpecialElite-Regular");
        }

        public static void Begin()
        {
            Scale = Mathf.Max(0.6f, Screen.height / 1080f);
            Ensure();
            saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        }

        public static void End() => GUI.matrix = saved;

        // ------------------------------------------------------------------
        //  PIEZAS
        // ------------------------------------------------------------------

        /// <summary>Panel de caja de munición: sombra, chapa con bisel y tornillos.</summary>
        public static void Panel(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            var c = GUI.color;
            // Sombra suave debajo
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(r.x + 10f, r.y + 14f, r.width, r.height), fillTex);
            GUI.color = c;
            // Marco de latón y chapa (la textura ya trae el bisel: se estira con 9-slice)
            GUI.DrawTexture(new Rect(r.x - 5f, r.y - 5f, r.width + 10f, r.height + 10f), brassTex);
            DrawSliced(r, panelTex, 14);
            // Tornillos en las esquinas
            foreach (var p in new[] { new Vector2(r.x + 9f, r.y + 9f), new Vector2(r.xMax - 23f, r.y + 9f),
                                      new Vector2(r.x + 9f, r.yMax - 23f), new Vector2(r.xMax - 23f, r.yMax - 23f) })
                GUI.DrawTexture(new Rect(p.x, p.y, 14f, 14f), boltTex);
        }

        /// <summary>Oscurece todo lo de detrás (para menús encima de la partida).</summary>
        public static void Dim(float alpha = 0.55f)
        {
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, VW, VH), fillTex);
            GUI.color = c;
        }

        /// <summary>Título de plantilla dorado con sombra y una raya de latón debajo.</summary>
        public static void Title(Rect r, string text)
        {
            text = Loc.T(text);
            Shadowed(r, text, TitleStyle, 3f);
            if (Event.current.type == EventType.Repaint)
            {
                var size = TitleStyle.CalcSize(new GUIContent(text));
                float w = Mathf.Min(r.width - 40f, size.x + 60f);
                var c = GUI.color;
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
                GUI.DrawTexture(new Rect(r.center.x - w * 0.5f, r.yMax + 2f, w, 2f), fillTex);
                GUI.color = c;
            }
        }

        public static void Label(Rect r, string text) => Shadowed(r, Loc.T(text), LabelStyle, 2f);
        public static void Small(Rect r, string text) => GUI.Label(r, Loc.T(text), SmallStyle);

        /// <summary>Campo de texto con un texto de ejemplo en gris cuando está vacío.</summary>
        public static string Field(Rect r, string value, int maxLength, string placeholder)
        {
            string v = GUI.TextField(r, value ?? "", maxLength, FieldStyle);
            if (string.IsNullOrEmpty(v) && Event.current.type == EventType.Repaint)
            {
                var c = SmallStyle.normal.textColor;
                SmallStyle.normal.textColor = new Color(0.75f, 0.68f, 0.5f, 0.45f);
                GUI.Label(new Rect(r.x + 14f, r.y + (r.height - 26f) * 0.5f, r.width - 20f, 28f), Loc.T(placeholder), SmallStyle);
                SmallStyle.normal.textColor = c;
            }
            return v;
        }

        /// <summary>Tablón de madera. <paramref name="selected"/> = de latón (opción elegida).</summary>
        public static bool Button(Rect r, string text, bool selected = false, bool big = false)
        {
            text = Loc.T(text);
            var e = Event.current;
            bool hover = GUI.enabled && r.Contains(e.mousePosition);
            bool down = hover && GUIUtility.hotControl != 0 && e.button == 0 && Input.GetMouseButton(0);
            // Al pasar el ratón el tablón "se levanta" 2 px.
            var rr = hover && !down ? new Rect(r.x, r.y - 2f, r.width, r.height) : r;
            if (e.type == EventType.Repaint)
            {
                var c = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.4f);
                GUI.DrawTexture(new Rect(r.x + 3f, r.y + 4f, r.width, r.height), fillTex);
                GUI.color = c;
                DrawSliced(rr, selected ? brassTex : down ? plankDownTex : hover ? plankHoverTex : plankTex, 8);
                // Clavos en los extremos
                if (rr.width > 90f)
                {
                    float ny = rr.y + rr.height * 0.5f - 4f;
                    GUI.DrawTexture(new Rect(rr.x + 7f, ny, 8f, 8f), boltTex);
                    GUI.DrawTexture(new Rect(rr.xMax - 15f, ny, 8f, 8f), boltTex);
                }
            }
            var style = selected ? SelectedStyle : big ? BigButtonStyle : ButtonStyle;
            var tr = new Rect(rr.x + 14f, rr.y, rr.width - 28f, rr.height);
            Shadowed(tr, text, style, 2f, hover && !selected ? Color.white : style.normal.textColor);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (clicked) Sfx.Play(Sfx.Id.Whack, 0.25f, 0.15f);
            return clicked;
        }

        /// <summary>Fila "Etiqueta: [a] [b] [c]" con la opción elegida resaltada. Devuelve la nueva elección.</summary>
        public static int Choice(Rect r, string label, string[] options, int current, float labelWidth = 230f)
        {
            Label(new Rect(r.x, r.y, labelWidth, r.height), label);
            float bx = r.x + labelWidth, bw = (r.width - labelWidth) / options.Length;
            int result = current;
            for (int i = 0; i < options.Length; i++)
                if (Button(new Rect(bx + i * bw + 3f, r.y, bw - 6f, r.height), options[i], i == current))
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
            Label(new Rect(r.x, r.y, labelWidth, r.height), label);
            var bar = new Rect(r.x + labelWidth, r.y + r.height * 0.5f - 9f, r.width - labelWidth - 90f, 18f);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), brassTex);
                GUI.DrawTexture(bar, darkTex);
                var c = GUI.color;
                GUI.color = Gold;
                GUI.DrawTexture(new Rect(bar.x, bar.y + 3f, bar.width * Mathf.Clamp01(value), bar.height - 6f), fillTex);
                GUI.color = c;
                GUI.DrawTexture(new Rect(bar.x + bar.width * Mathf.Clamp01(value) - 11f, bar.y - 5f, 22f, 28f), knobTex);
            }
            float nv = GUI.HorizontalSlider(new Rect(bar.x, bar.y - 6f, bar.width, bar.height + 12f), value, 0f, 1f, GUIStyle.none, GUIStyle.none);
            Shadowed(new Rect(bar.xMax + 14f, r.y, 80f, r.height), $"{Mathf.RoundToInt(nv * 100f)}%", LabelStyle, 2f);
            return nv;
        }

        /// <summary>Texto con sombra (se lee sobre cualquier fondo).</summary>
        private static void Shadowed(Rect r, string text, GUIStyle style, float off, Color? color = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            shadowStyle.font = style.font;
            shadowStyle.fontSize = style.fontSize;
            shadowStyle.fontStyle = style.fontStyle;
            shadowStyle.alignment = style.alignment;
            shadowStyle.wordWrap = style.wordWrap;
            shadowStyle.clipping = style.clipping;
            shadowStyle.normal.textColor = Ink;
            shadowStyle.Draw(new Rect(r.x + off, r.y + off, r.width, r.height), new GUIContent(text), false, false, false, false);
            var c = style.normal.textColor;
            if (color.HasValue) style.normal.textColor = color.Value;
            style.Draw(r, new GUIContent(text), false, false, false, false);
            style.normal.textColor = c;
        }

        /// <summary>Dibuja una textura en 9 trozos: las esquinas no se deforman al estirar.</summary>
        private static void DrawSliced(Rect r, Texture2D t, int border)
        {
            float b = Mathf.Min(border, r.width * 0.5f, r.height * 0.5f);
            float u = border / (float)t.width, v = border / (float)t.height;
            float[] xs = { r.x, r.x + b, r.xMax - b, r.xMax };
            float[] ys = { r.y, r.y + b, r.yMax - b, r.yMax };
            float[] us = { 0f, u, 1f - u, 1f };
            float[] vs = { 1f, 1f - v, v, 0f };   // la Y de las texturas va al revés que la de la GUI
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    var dst = new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]);
                    if (dst.width <= 0f || dst.height <= 0f) continue;
                    var uv = new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]);
                    GUI.DrawTextureWithTexCoords(dst, t, uv);
                }
        }

        // ------------------------------------------------------------------
        //  ESTILOS Y TEXTURAS
        // ------------------------------------------------------------------
        private static void Ensure()
        {
            if (TitleStyle != null && Mathf.Approximately(builtFor, Scale) && panelTex != null) return;
            builtFor = Scale;
            LoadFonts();
            BuildTextures();

            TitleStyle = new GUIStyle(GUI.skin.label) { font = stencil, fontSize = 44, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            TitleStyle.normal.textColor = Gold;
            LabelStyle = new GUIStyle(GUI.skin.label) { font = stencil, fontSize = 23, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
            LabelStyle.normal.textColor = Paper;
            if (stencil == null) LabelStyle.fontStyle = TitleStyle.fontStyle = FontStyle.Bold;
            SmallStyle = new GUIStyle(GUI.skin.label) { font = typewriter, fontSize = 18, wordWrap = true, alignment = TextAnchor.UpperLeft };
            SmallStyle.normal.textColor = new Color(0.86f, 0.82f, 0.70f);

            // Los botones dibujan su fondo a mano (tablón); el estilo es solo para el texto.
            ButtonStyle = new GUIStyle(GUI.skin.label) { font = stencil, fontSize = 22, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            ButtonStyle.normal.textColor = new Color(0.98f, 0.90f, 0.70f);
            if (stencil == null) ButtonStyle.fontStyle = FontStyle.Bold;
            SelectedStyle = new GUIStyle(ButtonStyle);
            SelectedStyle.normal.textColor = new Color(0.20f, 0.12f, 0.04f);
            BigButtonStyle = new GUIStyle(ButtonStyle) { fontSize = 30 };
            shadowStyle = new GUIStyle(GUI.skin.label);

            FieldStyle = new GUIStyle(GUI.skin.textField) { font = typewriter, fontSize = 24, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 6, 6) };
            FieldStyle.normal.background = FieldStyle.focused.background = FieldStyle.hover.background = darkTex;
            FieldStyle.normal.textColor = FieldStyle.focused.textColor = FieldStyle.hover.textColor = Gold;
        }

        private static void BuildTextures()
        {
            if (panelTex != null) return;
            fillTex = Solid(Color.white);
            darkTex = Solid(new Color(0.07f, 0.06f, 0.05f, 1f));
            panelTex = Crate(160, 160);
            plankTex = Plank(128, 40, 1f, 7);
            plankHoverTex = Plank(128, 40, 1.22f, 7);
            plankDownTex = Plank(128, 40, 0.8f, 7);
            brassTex = Brass(64, 32);
            boltTex = Bolt(16);
            knobTex = Knob(22, 28);
        }

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
        }

        private static Texture2D Solid(Color c)
        {
            var t = NewTex(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Chapa verde oliva de caja de munición: grano, manchas de óxido y bisel.</summary>
        private static Texture2D Crate(int w, int h)
        {
            var t = NewTex(w, h);
            var px = new Color[w * h];
            var baseC = new Color(0.20f, 0.22f, 0.15f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.5f + Mathf.PerlinNoise(x * 0.4f + 50f, y * 0.4f) * 0.25f;
                    float rust = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.05f + 7f, y * 0.05f + 3f) - 0.62f) * 4f);
                    Color c = baseC * (0.8f + n * 0.4f);
                    c = Color.Lerp(c, new Color(0.30f, 0.17f, 0.08f), rust * 0.6f);
                    // Bisel: luz arriba/izquierda, sombra abajo/derecha
                    int edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    if (edge < 4) c *= (x < 4 || y >= h - 4) ? 1.35f : 0.55f;
                    else if (edge < 6) c *= 0.75f;
                    c.a = 0.97f;
                    px[y * w + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Tablón de madera: vetas horizontales, nudos, luz arriba y sombra abajo.</summary>
        private static Texture2D Plank(int w, int h, float bright, int seed)
        {
            var t = NewTex(w, h);
            var px = new Color[w * h];
            var wood = new Color(0.42f, 0.28f, 0.15f) * bright;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float grain = Mathf.PerlinNoise(x * 0.03f + seed, y * 0.45f) ;
                    float lines = Mathf.Abs(Mathf.Sin((y + grain * 9f) * 0.9f));
                    Color c = wood * (0.78f + grain * 0.3f + lines * 0.08f);
                    float knot = Vector2.Distance(new Vector2(x, y), new Vector2(w * 0.68f, h * 0.45f));
                    if (knot < 4f) c *= 0.7f + knot * 0.06f;
                    if (y >= h - 3) c *= 1.3f;          // canto de arriba iluminado
                    if (y < 3) c *= 0.5f;               // sombra de abajo
                    if (x < 2 || x >= w - 2) c *= 0.6f; // cortes de los extremos
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Latón cepillado (marcos y opción elegida).</summary>
        private static Texture2D Brass(int w, int h)
        {
            var t = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float k = 0.85f + Mathf.PerlinNoise(x * 0.5f, y * 0.04f) * 0.25f + (y / (float)h) * 0.2f;
                    Color c = new Color(0.78f, 0.58f, 0.22f) * k;
                    if (y >= h - 2) c *= 1.25f;
                    if (y < 2 || x < 1 || x >= w - 1) c *= 0.55f;
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Tornillo/clavo redondo con brillo.</summary>
        private static Texture2D Bolt(int n)
        {
            var t = NewTex(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
                    float light = Mathf.Clamp01(0.55f + (-dx + dy) / n * 0.9f);
                    Color c = Color.Lerp(new Color(0.25f, 0.2f, 0.12f), new Color(0.95f, 0.85f, 0.6f), light);
                    c.a = Mathf.Clamp01((1f - d) * 4f);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Asa del deslizador: una "chapa" de latón.</summary>
        private static Texture2D Knob(int w, int h)
        {
            var t = NewTex(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    Color c = new Color(0.85f, 0.66f, 0.28f) * (0.8f + y / (float)h * 0.4f);
                    if (edge) c = new Color(0.2f, 0.13f, 0.05f);
                    if (x == w / 2 && y > 5 && y < h - 5) c *= 0.6f;   // ranura
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
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

        /// <summary>Altura que ocupa <see cref="OptionsBlock"/> (para dimensionar la ventana).</summary>
        public const float OptionsBlockHeight = 9 * 52f + 12f;

        /// <summary>Dibuja el bloque de opciones en <paramref name="area"/>. Devuelve la altura usada.</summary>
        public static float OptionsBlock(Rect area)
        {
            float y = area.y, h = 42f, gap = 52f;
            int lang = Choice(new Rect(area.x, y, area.width, h), "Idioma", new[] { "Español", "English" }, (int)Loc.Current);
            if (lang != (int)Loc.Current) Loc.Current = (Loc.Lang)lang;
            y += gap + 6f;
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
            const float tw = 380f;   // etiquetas largas ("Risas y frases de las ratas")
            GoreOn = Toggle(new Rect(area.x, y, area.width, h), "Sangre y vísceras", GoreOn, tw); y += gap;
            LaughsOn = Toggle(new Rect(area.x, y, area.width, h), "Risas y frases de las ratas", LaughsOn, tw); y += gap;
            ShakeOn = Toggle(new Rect(area.x, y, area.width, h), "Temblor de cámara", ShakeOn, tw); y += gap;
            Ratms.Networking.ControlHints.Enabled = Toggle(new Rect(area.x, y, area.width, h), "Ayudas de controles", Ratms.Networking.ControlHints.Enabled, tw); y += gap;
            bool full = Toggle(new Rect(area.x, y, area.width, h), "Pantalla completa", Screen.fullScreen, tw);
            if (full != Screen.fullScreen) Screen.fullScreen = full;
            y += gap;
            return y - area.y;
        }
    }
}
