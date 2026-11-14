using System.Collections.Generic;
using Ratms.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ratms.UI
{
    /// <summary>
    ///  AMBIENTE DE GUERRA en el menú principal (todo por código, encima del
    ///  fondo y debajo de los botones):
    ///   • Tinte de atardecer de guerra y viñeta oscura.
    ///   • Columnas de humo en el horizonte y fogonazos de obuses lejanos.
    ///   • DESFILE de ratas de trinchera al azar marchando por la hierba
    ///     (cada vez distintas: gordas, flacas, topos, mancas...).
    ///   • Botones de madera de caja de munición y lema bajo el título.
    /// </summary>
    public class MenuDressing : MonoBehaviour
    {
        public const string Version = "v0.20";

        private RectTransform canvasRect, layer;
        private readonly List<Marcher> marchers = new();
        private readonly List<(RectTransform rt, float speed, float phase)> smokes = new();
        private readonly System.Random rng = new();
        private float nextShell;
        private Sprite softSprite;

        private class Marcher
        {
            public RectTransform rt;
            public Image img;
            public Sprite[] frames;
            public float x, speed, anim;
            public int dir;
        }

        public static void Install(Canvas canvas, Transform background)
        {
            if (canvas == null || canvas.GetComponent<MenuDressing>() != null) return;
            var d = canvas.gameObject.AddComponent<MenuDressing>();
            d.Build(canvas, background);
        }

        private void Build(Canvas canvas, Transform background)
        {
            canvasRect = canvas.GetComponent<RectTransform>();
            softSprite = SoftCircle();

            // Capa propia justo encima del fondo (por debajo de los paneles/botones)
            // (Hermana del fondo, justo encima de él: así queda por debajo de los botones.)
            Transform parent = background != null ? background.parent : canvas.transform;
            layer = NewRect("AmbienteGuerra", parent);
            Stretch(layer);
            layer.SetSiblingIndex(background != null ? background.GetSiblingIndex() + 1 : 0);

            // Tinte de atardecer de guerra + viñeta
            var tint = NewImage("Tinte", layer, null, new Color(0.34f, 0.17f, 0.07f, 0.5f));
            Stretch(tint.rectTransform);
            // Cielo de humo: oscuro arriba, se aclara hacia el horizonte
            var sky = NewImage("CieloHumo", layer, SkyGradient(), new Color(0.12f, 0.09f, 0.08f, 0.8f));
            Stretch(sky.rectTransform);
            var vig = NewImage("Viñeta", layer, Vignette(), new Color(0f, 0f, 0f, 0.9f));
            Stretch(vig.rectTransform);

            // Humo en el horizonte
            for (int i = 0; i < 7; i++)
            {
                var s = NewImage("Humo", layer, softSprite, new Color(0.18f, 0.16f, 0.15f, 0.35f));
                s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2((float)rng.NextDouble(), 0.2f);
                float size = 180f + (float)rng.NextDouble() * 220f;
                s.rectTransform.sizeDelta = new Vector2(size * 1.5f, size);
                smokes.Add((s.rectTransform, 6f + (float)rng.NextDouble() * 10f, (float)rng.NextDouble() * 10f));
            }

            // Desfile de ratas por la hierba
            for (int i = 0; i < 6; i++) marchers.Add(NewMarcher(i * 0.18f));

            // Lema y versión
            var tag = MainMenuLook.NewText("Lema", canvas.transform, "~ GUERRA DE TRINCHERAS ROEDORA ~", 34, new Color(1f, 0.86f, 0.55f), MainMenuLook.TypewriterTMP);
            tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = new Vector2(0.45f, 0.655f);
            tag.rectTransform.sizeDelta = new Vector2(1200f, 60f);
            tag.characterSpacing = 4f;
            tag.gameObject.AddComponent<LocText>();
            tagline = tag;
            mainPanel = canvas.transform.Find("SafeArea/MainPanel")?.gameObject;
            var ver = NewText("Version", canvas.transform, $"Ratms {Version}", 22, new Color(1f, 1f, 1f, 0.55f));
            ver.rectTransform.anchorMin = ver.rectTransform.anchorMax = new Vector2(1f, 0f);
            ver.rectTransform.pivot = new Vector2(1f, 0f);
            ver.rectTransform.anchoredPosition = new Vector2(-16f, 10f);
            ver.alignment = TextAlignmentOptions.BottomRight;

            // 0.19: poste de tablones, selector de idioma y paneles en caja de munición.
            MainMenuLook.BuildSignpost(canvas.transform);
            MainMenuLook.BuildLanguageSelector(canvas.transform);
            MainMenuLook.RestyleMultiplayerPanels(canvas.transform);
            RestyleButtons(canvas.transform);   // los que queden (panel de opciones antiguo...)
            nextShell = Time.time + 1f;
        }

        private Marcher NewMarcher(float startFrac)
        {
            var look = RatLook.Random(rng);
            int team = rng.Next(4);
            var img = NewImage("RataDesfile", layer, null, Color.white);
            img.preserveAspect = true;
            var m = new Marcher
            {
                rt = img.rectTransform, img = img,
                frames = RatAppearance.WalkFrames(look.Pack(), RatAppearance.ColorForTeam(team)),
                speed = 70f + (float)rng.NextDouble() * 60f,
                dir = 1,
            };
            m.rt.anchorMin = m.rt.anchorMax = new Vector2(0f, 0f);
            m.rt.pivot = new Vector2(0.5f, 0f);
            float k = 3.4f + (float)rng.NextDouble() * 0.6f;
            m.rt.sizeDelta = new Vector2(32f * k, 32f * k);
            m.x = startFrac * Width() - 200f;
            return m;
        }

        // El lema va en la raíz del canvas (por encima de todo): lo escondemos
        // cuando no está el menú principal (si no, se veía sobre el panel
        // multijugador).
        private TMP_Text tagline;
        private GameObject mainPanel;

        private float Width() => canvasRect != null ? canvasRect.rect.width : 1920f;
        private float Height() => canvasRect != null ? canvasRect.rect.height : 1080f;

        private void Update()
        {
            if (tagline != null && mainPanel != null) tagline.enabled = mainPanel.activeInHierarchy;
            float w = Width(), h = Height();
            // Desfile
            for (int i = 0; i < marchers.Count; i++)
            {
                var m = marchers[i];
                m.x += m.speed * Time.deltaTime;
                m.anim += Time.deltaTime * m.speed / 9f;
                if (m.x > w + 150f)   // sale por la derecha → entra otra rata nueva por la izquierda
                {
                    Destroy(m.rt.gameObject);
                    marchers[i] = m = NewMarcher(0f);
                    m.x = -150f - (float)rng.NextDouble() * 400f;
                }
                m.img.sprite = m.frames[(int)m.anim % m.frames.Length];
                m.rt.anchoredPosition = new Vector2(m.x, h * 0.135f);
            }
            // Humo que sube y se mece
            foreach (var (rt, speed, phase) in smokes)
            {
                var a = rt.anchorMin;
                a.x += Time.deltaTime * 0.004f;
                if (a.x > 1.1f) a.x = -0.1f;
                rt.anchorMin = rt.anchorMax = a;
                rt.anchoredPosition = new Vector2(Mathf.Sin(Time.time * 0.3f + phase) * 30f, Mathf.Repeat(Time.time * speed + phase * 40f, 160f));
            }
            // Obuses lejanos: fogonazo en el horizonte
            if (Time.time >= nextShell)
            {
                nextShell = Time.time + 1.5f + (float)rng.NextDouble() * 3.5f;
                StartCoroutine(Shell(new Vector2((float)rng.NextDouble() * w, h * (0.16f + (float)rng.NextDouble() * 0.06f))));
            }
        }

        private System.Collections.IEnumerator Shell(Vector2 at)
        {
            var flash = NewImage("Obus", layer, softSprite, new Color(1f, 0.75f, 0.35f, 0.9f));
            flash.rectTransform.anchorMin = flash.rectTransform.anchorMax = Vector2.zero;
            flash.rectTransform.anchoredPosition = at;
            var smoke = NewImage("HumoObus", layer, softSprite, new Color(0.2f, 0.18f, 0.16f, 0f));
            smoke.rectTransform.anchorMin = smoke.rectTransform.anchorMax = Vector2.zero;
            flash.rectTransform.SetSiblingIndex(2);
            smoke.rectTransform.SetSiblingIndex(2);
            Sfx.Play(Sfx.Id.Explosion, 0.12f, 0.2f);
            for (float t = 0f; t < 2.2f; t += Time.deltaTime)
            {
                float f = Mathf.Clamp01(t / 0.35f);
                flash.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 260f, f);
                flash.color = new Color(1f, 0.75f, 0.35f, 0.9f * (1f - f));
                float s = Mathf.Clamp01(t / 2.2f);
                smoke.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(80f, 320f, s);
                smoke.rectTransform.anchoredPosition = at + Vector2.up * s * 120f;
                smoke.color = new Color(0.2f, 0.18f, 0.16f, 0.5f * Mathf.Sin(s * Mathf.PI));
                yield return null;
            }
            Destroy(flash.gameObject);
            Destroy(smoke.gameObject);
        }

        /// <summary>Botones de madera de caja de munición con letras de plantilla
        /// (los que no ha vestido ya <see cref="MainMenuLook"/>).</summary>
        private static void RestyleButtons(Transform root)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (b.GetComponent<Image>() is Image already && already.sprite != null && already.sprite.texture != null &&
                    already.sprite.texture.filterMode == FilterMode.Point) continue;   // ya es un tablón pixel-art
                if (b.GetComponent<Image>() is Image img)
                {
                    img.color = new Color(0.30f, 0.23f, 0.14f, 0.95f);
                    var cb = b.colors;
                    cb.normalColor = Color.white;
                    cb.highlightedColor = new Color(1.35f, 1.25f, 1.05f);
                    cb.pressedColor = new Color(0.8f, 0.7f, 0.5f);
                    cb.selectedColor = Color.white;
                    b.colors = cb;
                }
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.color = new Color(0.96f, 0.86f, 0.62f);
                    t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
                    t.characterSpacing = 2f;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.enableAutoSizing = true;
                    t.fontSizeMax = t.fontSize;
                    t.fontSizeMin = 10f;
                }
                if (b.GetComponent<Outline>() == null)
                {
                    var o = b.gameObject.AddComponent<Outline>();
                    o.effectColor = new Color(0.62f, 0.46f, 0.18f, 1f);
                    o.effectDistance = new Vector2(2f, -2f);
                }
            }
        }

        // ============================================================
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

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(30, 20, 12, 255);
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Sprite SoftCircle()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        private static Sprite SkyGradient()
        {
            const int N = 64;
            var t = new Texture2D(4, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
            {
                float k = Mathf.Clamp01((y / (float)N - 0.45f) / 0.55f);   // 0 por debajo del 45 %, 1 arriba
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, k * k));
            }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, N), new Vector2(0.5f, 0.5f));
        }

        private static Sprite Vignette()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;
                    float a = Mathf.Clamp01((d - 0.55f) / 0.45f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }
    }
}
