using Ratms.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ratms.UI
{
    /// <summary>
    ///  ASPECTO del menú principal (0.19), todo por código y en PIXEL-ART como
    ///  el logo:
    ///   · POSTE DE TRINCHERA a la derecha con los botones como TABLONES en
    ///     forma de flecha, clavados y un poco torcidos. Al pasar el ratón el
    ///     tablón se ilumina, crece y se balancea (y cruje).
    ///   · SELECTOR DE IDIOMA (ES / EN) arriba a la derecha.
    ///   · Paneles del multijugador ordenados en una caja de munición, con
    ///     botones de tablón y la fuente de plantilla.
    ///  Las fuentes son las de <see cref="TrenchGUI"/> (Resources/Fonts),
    ///  convertidas a TextMeshPro al vuelo.
    /// </summary>
    public static class MainMenuLook
    {
        private const int PixelScale = 4;   // 1 píxel del dibujo = 4 px de pantalla (1080p)

        private static TMP_FontAsset stencilTmp, typewriterTmp;

        // Fuentes de TextMeshPro ESTÁTICAS (Resources/Fonts/*.asset), con todas las
        // letras ya dibujadas en su atlas (incluidas á, ñ, ¿, ¡...). Crearlas al
        // vuelo con CreateFontAsset salía con letras basura en la build.
        /// <summary>Fuente de plantilla para TextMeshPro (null si no se pudo cargar).</summary>
        public static TMP_FontAsset StencilTMP
        {
            get
            {
                if (stencilTmp == null) stencilTmp = Resources.Load<TMP_FontAsset>("Fonts/BlackOpsOne SDF");
                return stencilTmp;
            }
        }

        public static TMP_FontAsset TypewriterTMP
        {
            get
            {
                if (typewriterTmp == null) typewriterTmp = Resources.Load<TMP_FontAsset>("Fonts/SpecialElite SDF");
                return typewriterTmp;
            }
        }

        // ==================================================================
        //  POSTE CON TABLONES
        // ==================================================================
        /// <summary>
        ///  Coloca los botones del menú (Jugar, Multijugador, Opciones, Salir)
        ///  como tablones en un poste a la derecha de la pantalla.
        /// </summary>
        public static void BuildSignpost(Transform canvas)
        {
            var buttons = canvas.Find("SafeArea/MainPanel/Buttons") as RectTransform;
            if (buttons == null) return;

            // Fuera el LayoutGroup de la escena: colocamos nosotros.
            foreach (var lg in buttons.GetComponents<LayoutGroup>()) lg.enabled = false;
            var fitter = buttons.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            buttons.anchorMin = buttons.anchorMax = new Vector2(0.5f, 0.5f);
            buttons.pivot = new Vector2(0.5f, 0.5f);
            buttons.sizeDelta = new Vector2(480f, 700f);
            // El panel de la escena es más ancho que la pantalla: colocamos el
            // poste respecto al CANVAS (78 % del ancho), y se recoloca solo si
            // cambia la resolución.
            var pin = buttons.gameObject.AddComponent<PinToCanvas>();
            pin.canvas = canvas as RectTransform;
            pin.normalized = new Vector2(0.80f, 0.47f);
            pin.LateUpdate();

            // El poste (detrás de los tablones), hincado en la hierba.
            var post = NewImage("Poste", buttons, PostSprite(), Color.white);
            post.rectTransform.SetAsFirstSibling();
            post.rectTransform.anchorMin = post.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            post.rectTransform.sizeDelta = new Vector2(12 * PixelScale, 190 * PixelScale);
            post.rectTransform.anchoredPosition = new Vector2(0f, -150f);
            // Cartel pequeño en lo alto: "CUARTEL GENERAL"
            var hq = NewImage("CartelHQ", buttons, PlankSprite(48, 14, 0, 33), Color.white);
            hq.rectTransform.anchorMin = hq.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hq.rectTransform.sizeDelta = new Vector2(48 * PixelScale, 14 * PixelScale);
            hq.rectTransform.anchoredPosition = new Vector2(0f, 330f);
            hq.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -3f);
            var hqText = NewText("Texto", hq.transform, "CUARTEL GENERAL", 24f, new Color(0.15f, 0.09f, 0.04f), StencilTMP, outline: false);
            Stretch(hqText.rectTransform);
            hqText.enableAutoSizing = true;          // "HEADQUARTERS" es más largo
            hqText.fontSizeMax = 24f;
            hqText.fontSizeMin = 10f;
            hqText.margin = new Vector4(10f, 2f, 10f, 2f);
            hqText.gameObject.AddComponent<LocText>();

            string[] order = { "Jugar", "Multijugador", "Opciones", "Salir" };
            float[] tilt = { 2.5f, -2f, 1.5f, -3f };
            int[] point = { -1, 1, -1, 1 };   // hacia dónde apunta la flecha
            for (int i = 0; i < order.Length; i++)
            {
                var b = buttons.Find(order[i]) as RectTransform;
                if (b == null) continue;
                b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f);
                b.pivot = new Vector2(0.5f, 0.5f);
                b.sizeDelta = new Vector2(104 * PixelScale, 22 * PixelScale);
                b.anchoredPosition = new Vector2(point[i] * 14f, 200f - i * 125f);
                b.localRotation = Quaternion.Euler(0f, 0f, tilt[i]);
                StylePlankButton(b.GetComponent<Button>(), PlankSprite(104, 22, point[i], i * 17 + 3), 48f);
                var wob = b.gameObject.AddComponent<PlankHover>();
                wob.baseTilt = tilt[i];
            }
        }

        /// <summary>Viste un botón de uGUI como tablón (sprite, fuente, colores).</summary>
        public static void StylePlankButton(Button b, Sprite sprite, float fontSize)
        {
            if (b == null) return;
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = Color.white;
            }
            var cb = b.colors;
            cb.normalColor = new Color(0.92f, 0.92f, 0.92f);
            cb.highlightedColor = new Color(1.15f, 1.08f, 0.95f);
            cb.pressedColor = new Color(0.7f, 0.62f, 0.5f);
            cb.selectedColor = new Color(0.92f, 0.92f, 0.92f);
            cb.colorMultiplier = 1.25f;
            b.colors = cb;
            foreach (var o in b.GetComponents<Outline>()) Object.Destroy(o);
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
            {
                SetFont(t, StencilTMP);
                t.fontStyle = FontStyles.UpperCase;
                t.color = new Color(0.99f, 0.92f, 0.72f);
                t.characterSpacing = 3f;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.enableAutoSizing = true;
                t.fontSizeMax = fontSize;
                t.fontSizeMin = 12f;
                t.alignment = TextAlignmentOptions.Center;
                if (t.gameObject.activeInHierarchy) { t.outlineWidth = 0.22f; t.outlineColor = new Color32(40, 22, 10, 255); }
                else if (t.GetComponent<OutlineWhenShown>() == null) t.gameObject.AddComponent<OutlineWhenShown>();
                t.margin = new Vector4(18f, 4f, 18f, 4f);
            }
        }

        // ==================================================================
        //  IDIOMA (ES / EN)
        // ==================================================================
        public static void BuildLanguageSelector(Transform canvas)
        {
            var root = NewRect("Idioma", canvas);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-24f, -20f);
            root.sizeDelta = new Vector2(260f, 70f);

            MakeLangButton(root, "ES", Loc.Lang.Es, new Vector2(-195f, -35f), -2f);
            MakeLangButton(root, "EN", Loc.Lang.En, new Vector2(-65f, -35f), 2f);
        }

        private static void MakeLangButton(RectTransform parent, string label, Loc.Lang lang, Vector2 pos, float tilt)
        {
            var img = NewImage("Idioma_" + label, parent, PlankSprite(30, 15, 0, label.Length * 11 + (int)lang), Color.white);
            img.raycastTarget = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(30 * PixelScale, 15 * PixelScale);
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
            var btn = img.gameObject.AddComponent<Button>();
            var txt = NewText("Texto", rt, label, 34f, Color.white, StencilTMP);
            Stretch(txt.rectTransform);
            btn.onClick.AddListener(() => { Loc.Current = lang; Sfx.Play(Sfx.Id.Whack, 0.3f, 0.1f); });
            var mark = img.gameObject.AddComponent<LangMark>();
            mark.lang = lang;
            mark.text = txt;
            mark.image = img;
            mark.Refresh();   // (su OnEnable saltó al añadirlo, antes de tener los campos)
            img.gameObject.AddComponent<PlankHover>().baseTilt = tilt;
        }

        /// <summary>Resalta (latón) el idioma activo.</summary>
        private class LangMark : MonoBehaviour
        {
            public Loc.Lang lang;
            public TMP_Text text;
            public Image image;
            private void OnEnable() { Loc.Changed += Refresh; Refresh(); }
            private void OnDisable() => Loc.Changed -= Refresh;
            public void Refresh()
            {
                if (text == null || image == null) return;
                bool on = Loc.Current == lang;
                image.color = on ? new Color(1.25f, 1.05f, 0.6f) : new Color(0.7f, 0.7f, 0.7f);
                text.color = on ? new Color(1f, 0.92f, 0.55f) : new Color(0.75f, 0.7f, 0.6f);
            }
        }

        // ==================================================================
        //  PANELES DEL MULTIJUGADOR (uGUI de la escena)
        // ==================================================================
        /// <summary>
        ///  El panel "Multijugador" venía con botones de 200×30 sueltos y el
        ///  nombre en una esquina. Lo ordenamos en columna dentro de una caja de
        ///  munición y lo ampliamos.
        /// </summary>
        public static void RestyleMultiplayerPanels(Transform canvas)
        {
            var mp = canvas.Find("MultiplayerPanel") as RectTransform;
            if (mp != null)
            {
                const float cx = -75f;
                var box = CrateBehind(mp, new Vector2(cx, 18f), new Vector2(330f, 470f));
                Title(box, "MULTIJUGADOR", 0f, 200f);
                PlaceLabel(mp, "TuNombre", "Tu nombre", new Vector2(cx, 165f));
                Place(mp, "Player name input", new Vector2(cx, 135f), new Vector2(240f, 34f));
                Place(mp, "Btn_CreateRoom", new Vector2(cx, 88f), new Vector2(240f, 40f));
                PlaceLabel(mp, "O", "— o —", new Vector2(cx, 52f));
                Place(mp, "JoinCodeInput", new Vector2(cx, 18f), new Vector2(240f, 38f));
                Place(mp, "Btn_JoinRoom", new Vector2(cx, -28f), new Vector2(240f, 40f));
                Place(mp, "StatusText", new Vector2(cx, -78f), new Vector2(290f, 50f));
                Place(mp, "Btn_Back", new Vector2(cx, -150f), new Vector2(200f, 40f));
                mp.localScale = Vector3.one * 1.7f;
            }

            var lobby = canvas.Find("LobbyPanel") as RectTransform;
            if (lobby != null)
            {
                CrateBehind(lobby, new Vector2(15f, -60f), new Vector2(560f, 230f));
                lobby.localScale = Vector3.one * 1.25f;
            }

            foreach (var panel in new[] { mp, lobby })
            {
                if (panel == null) continue;
                foreach (var b in panel.GetComponentsInChildren<Button>(true))
                    StylePlankButton(b, SlicedPlankSprite(), 30f);
                foreach (var f in panel.GetComponentsInChildren<TMP_InputField>(true)) StyleField(f);
                foreach (var t in panel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.GetComponentInParent<Button>(true) != null || t.GetComponentInParent<TMP_InputField>(true) != null) continue;
                    if (t.name == "Titulo") continue;   // el título de la caja ya va con su estilo
                    SetFont(t, TypewriterTMP);
                    t.color = new Color(0.95f, 0.9f, 0.78f);
                    if (t.GetComponent<OutlineWhenShown>() == null) t.gameObject.AddComponent<OutlineWhenShown>();
                }
            }
        }

        private static RectTransform CrateBehind(RectTransform panel, Vector2 center, Vector2 size)
        {
            var img = NewImage("CajaMunicion", panel, CrateSprite(), Color.white);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.rectTransform.SetAsFirstSibling();
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.anchoredPosition = center;
            img.rectTransform.sizeDelta = size;
            return img.rectTransform;
        }

        private static void Title(RectTransform box, string text, float x, float y)
        {
            var t = NewText("Titulo", box, text, 34f, TrenchGUI.Gold, StencilTMP);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchoredPosition = new Vector2(x, y);
            t.rectTransform.sizeDelta = new Vector2(320f, 50f);
            t.gameObject.AddComponent<LocText>();
        }

        private static void PlaceLabel(RectTransform panel, string name, string text, Vector2 pos)
        {
            var t = NewText(name, panel, text, 16f, new Color(0.95f, 0.9f, 0.78f));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchoredPosition = pos;
            t.rectTransform.sizeDelta = new Vector2(260f, 24f);
            t.gameObject.AddComponent<LocText>();
        }

        private static void Place(RectTransform panel, string child, Vector2 pos, Vector2 size)
        {
            var c = panel.Find(child) as RectTransform;
            if (c == null) return;
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.5f);
            c.pivot = new Vector2(0.5f, 0.5f);
            c.anchoredPosition = pos;
            c.sizeDelta = size;
        }

        private static void StyleField(TMP_InputField f)
        {
            if (f.GetComponent<Image>() is Image img)
            {
                img.sprite = null;
                img.color = new Color(0.08f, 0.06f, 0.04f, 0.95f);
            }
            foreach (var t in f.GetComponentsInChildren<TMP_Text>(true))
            {
                SetFont(t, TypewriterTMP);
                bool placeholder = t != f.textComponent;
                t.color = placeholder ? new Color(0.7f, 0.62f, 0.45f, 0.7f) : TrenchGUI.Gold;
                t.enableAutoSizing = true;
                t.fontSizeMax = 22f;
                t.fontSizeMin = 10f;
                t.alignment = TextAlignmentOptions.Center;
            }
        }

        // ==================================================================
        //  SPRITES PIXEL-ART (generados)
        // ==================================================================
        private static readonly Color32 WoodOutline = new(42, 24, 12, 255);
        private static readonly Color32 WoodDark = new(104, 66, 34, 255);
        private static readonly Color32 WoodMid = new(140, 92, 48, 255);
        private static readonly Color32 WoodLight = new(176, 122, 66, 255);
        private static readonly Color32 NailHead = new(150, 150, 140, 255);
        private static readonly Color32 NailShade = new(70, 70, 66, 255);

        /// <summary>
        ///  Tablón de <paramref name="w"/>×<paramref name="h"/> píxeles con vetas y
        ///  clavos. <paramref name="point"/>: -1 = punta de flecha a la izquierda,
        ///  1 = a la derecha, 0 = rectangular.
        /// </summary>
        public static Sprite PlankSprite(int w, int h, int point, int seed)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rng = new System.Random(seed);
            int tip = point != 0 ? h / 2 : 0;
            for (int y = 0; y < h; y++)
            {
                // Vetas: cada fila tiene su tono y alguna raya oscura a trozos.
                int rowTone = rng.Next(3);
                for (int x = 0; x < w; x++)
                {
                    // Punta de flecha a 45°: a una distancia dy del centro, la
                    // madera empieza a dy píxeles de la punta.
                    int fromTipEdge = point < 0 ? x : point > 0 ? w - 1 - x : 99;
                    int dy = Mathf.Abs(y * 2 + 1 - h) / 2;   // 0 en el centro
                    if (fromTipEdge < dy) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    bool edge = y == 0 || y == h - 1 || fromTipEdge == dy ||
                                (point >= 0 && x == 0) || (point <= 0 && x == w - 1);
                    Color32 c;
                    if (edge) c = WoodOutline;
                    else if (y == h - 2) c = WoodLight;                 // canto de arriba con luz
                    else if (y == 1) c = WoodDark;                      // sombra de abajo
                    else
                    {
                        float n = Mathf.PerlinNoise(x * 0.12f + seed, y * 0.9f);
                        c = n < 0.33f ? WoodDark : n < 0.66f ? WoodMid : (rowTone == 0 ? WoodLight : WoodMid);
                        if ((x * 7 + y * 13 + seed) % 23 == 0) c = WoodDark;
                    }
                    px[y * w + x] = c;
                }
            }
            // Clavos cerca de los extremos (no en la punta)
            int nailY = h / 2;
            foreach (int nx in new[] { point < 0 ? tip + 4 : 4, point > 0 ? w - tip - 5 : w - 5 })
            {
                if (nx <= 1 || nx >= w - 2) continue;
                px[nailY * w + nx] = NailHead;
                px[nailY * w + nx + 1] = NailShade;
                px[(nailY - 1) * w + nx] = NailShade;
            }
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite slicedPlank;
        /// <summary>Tablón rectangular que se puede estirar (9-slice) a cualquier tamaño.</summary>
        public static Sprite SlicedPlankSprite()
        {
            if (slicedPlank != null) return slicedPlank;
            var p = PlankSprite(40, 12, 0, 91);
            slicedPlank = Sprite.Create(p.texture, new Rect(0, 0, 40, 12), new Vector2(0.5f, 0.5f), 50f, 0,
                                        SpriteMeshType.FullRect, new Vector4(6, 4, 6, 4));
            return slicedPlank;
        }

        private static Sprite PostSprite()
        {
            const int w = 12, h = 190;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = x == 0 || x == w - 1 ? WoodOutline
                              : x <= 2 ? WoodLight
                              : x >= w - 3 ? new Color32(70, 44, 22, 255)
                              : Mathf.PerlinNoise(x * 0.5f, y * 0.08f) < 0.45f ? WoodDark : new Color32(118, 76, 40, 255);
                    if (y == h - 1) c = WoodOutline;
                    px[y * w + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite crate;
        /// <summary>Chapa de caja de munición (verde oliva, remaches, letras de carga).</summary>
        private static Sprite CrateSprite()
        {
            if (crate != null) return crate;
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                    Color32 c = edge == 0 ? new Color32(20, 22, 14, 255)
                              : edge == 1 ? new Color32(150, 116, 52, 255)        // ribete de latón
                              : edge == 2 ? new Color32(96, 74, 32, 255)
                              : Mathf.PerlinNoise(x * 0.3f, y * 0.3f) < 0.5f ? new Color32(52, 58, 38, 245) : new Color32(60, 66, 44, 245);
                    bool rivet = (x == 5 || x == n - 6) && (y == 5 || y == n - 6);
                    if (rivet) c = new Color32(190, 160, 90, 255);
                    px[y * n + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            crate = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 25f, 0,
                                  SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
            return crate;
        }

        // ==================================================================
        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <remarks>
        ///  OJO con TextMeshPro: la fuente SIEMPRE antes que el contorno. Poner el
        ///  contorno crea una copia del material con el atlas de la fuente que
        ///  haya en ese momento; si luego cambias de fuente, las letras salen de
        ///  ese atlas viejo y se ven como basura.
        /// </remarks>
        public static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color,
                                              TMP_FontAsset font = null, bool outline = true)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font != null ? font : TypewriterTMP != null ? TypewriterTMP : TMP_Settings.defaultFontAsset;
            if (t.font != null) t.fontSharedMaterial = t.font.material;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // El contorno necesita el texto ya "despierto": si el panel está
            // oculto, se pone al mostrarse (OutlineWhenShown).
            if (!outline) return t;
            if (t.gameObject.activeInHierarchy) { t.outlineWidth = 0.2f; t.outlineColor = new Color32(30, 20, 12, 255); }
            else t.gameObject.AddComponent<OutlineWhenShown>();
            return t;
        }

        /// <summary>
        ///  Cambia la fuente de un texto que AÚN NO tiene contorno (ver la nota de
        ///  <see cref="NewText"/>): el contorno se pone después.
        /// </summary>
        public static void SetFont(TMP_Text t, TMP_FontAsset font)
        {
            if (t == null || font == null) return;
            t.font = font;
            t.fontSharedMaterial = font.material;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    ///  Pone el contorno oscuro a un texto la primera vez que se ve (TMP no
    ///  deja ponerlo mientras el panel está oculto: aún no tiene material).
    /// </summary>
    public class OutlineWhenShown : MonoBehaviour
    {
        private void Start()
        {
            if (TryGetComponent<TMP_Text>(out var t) && t.fontSharedMaterial != null)
            {
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(20, 12, 6, 255);
            }
            Destroy(this);
        }
    }

    /// <summary>
    ///  Mantiene un RectTransform en un punto relativo del canvas (0..1), sea
    ///  cual sea el panel en el que esté metido.
    /// </summary>
    public class PinToCanvas : MonoBehaviour
    {
        public RectTransform canvas;
        public Vector2 normalized = new(0.5f, 0.5f);

        public void LateUpdate()
        {
            if (canvas == null) return;
            var r = canvas.rect;
            var local = new Vector3(r.xMin + r.width * normalized.x, r.yMin + r.height * normalized.y, 0f);
            transform.position = canvas.TransformPoint(local);
        }
    }

    /// <summary>
    ///  Tablón que reacciona al ratón: crece un poco, se endereza con un
    ///  balanceo de muelle y cruje. Todo con tiempo sin escalar (vale en pausa).
    /// </summary>
    public class PlankHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public float baseTilt;
        private bool over;
        private float angle, angVel, scale = 1f;

        private void Start() => angle = baseTilt;

        public void OnPointerEnter(PointerEventData e)
        {
            over = true;
            angVel += baseTilt >= 0f ? -60f : 60f;   // empujoncito: se balancea
            Sfx.Play(Sfx.Id.Squeak, 0.12f, 0.25f);
        }

        public void OnPointerExit(PointerEventData e) => over = false;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float target = over ? baseTilt * 0.3f : baseTilt;
            // Muelle amortiguado hacia el ángulo objetivo.
            angVel += (target - angle) * 120f * dt;
            angVel *= Mathf.Exp(-7f * dt);
            angle += angVel * dt;
            scale = Mathf.MoveTowards(scale, over ? 1.07f : 1f, dt * 0.8f);
            transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = Vector3.one * scale;
        }
    }
}
