using UnityEngine;
using UnityEngine.UI;

namespace Ratms.UI
{
    /// <summary>
    ///  Hace que una Image de UI CUBRA toda la pantalla sin deformarse
    ///  (como "background-size: cover" en web): escala la imagen manteniendo
    ///  su proporción hasta que no quede ningún hueco, recortando lo que
    ///  sobre por arriba/abajo o por los lados.
    ///
    ///  Por qué existe: el fondo del menú usaba "Preserve Aspect" dentro de
    ///  una pantalla 16:9. La imagen es 3:2, así que encajaba por altura y
    ///  dejaba BANDAS AZULES (el color de fondo de la cámara) a los lados.
    ///
    ///  Uso: añádelo a la Image (o deja que MainMenuController lo añada solo
    ///  al fondo llamado "Background"). Se recalcula si cambia la resolución.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class UIImageCover : MonoBehaviour
    {
        private Image image;
        private RectTransform rect;
        private Vector2Int lastScreen;
        private GameObject fill;
        private RawImage fillL, fillR;

        /// <summary>
        ///  0.30.1 — RELLENO DE LOS LADOS en pantallas más anchas que la imagen:
        ///  dos tiras que prolongan la última columna de píxeles de cada borde
        ///  (cielo con cielo, hierba con hierba). Antes era la imagen entera
        ///  estirada y se veía un trozo del título repetido.
        /// </summary>
        private void EnsureFill(Vector2 imageSize, RectTransform canvasRect, Vector2 area)
        {
            if (fill == null)
            {
                fill = new GameObject("FondoRelleno", typeof(RectTransform));
                fill.transform.SetParent(transform.parent, false);
                fillL = MakeStrip("Izq");
                fillR = MakeStrip("Der");
            }
            fill.SetActive(true);
            fill.transform.SetSiblingIndex(transform.GetSiblingIndex());   // justo detrás
            var fr = (RectTransform)fill.transform;
            fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0.5f);
            fr.sizeDelta = area;
            fr.position = canvasRect.position;

            var sp = image.sprite;
            var tex = sp.texture;
            Rect tr = sp.textureRect;
            float u0 = tr.xMin / tex.width, u1 = tr.xMax / tex.width, v0 = tr.yMin / tex.height, v1 = tr.yMax / tex.height;
            float px = 1f / tex.width;   // una columna de píxeles
            float side = Mathf.Max(0f, (area.x - imageSize.x) * 0.5f) + 2f;
            Place(fillL, -1f, side, imageSize.y, new Rect(u0 + px * 0.5f, v0, px * 0.01f, v1 - v0), tex);
            Place(fillR, 1f, side, imageSize.y, new Rect(u1 - px * 0.5f, v0, px * 0.01f, v1 - v0), tex);
        }

        private RawImage MakeStrip(string n)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(fill.transform, false);
            var ri = go.GetComponent<RawImage>();
            ri.raycastTarget = false;
            ri.color = new Color(0.85f, 0.85f, 0.85f, 1f);   // un pelín más oscuro: el ojo va al centro
            return ri;
        }

        private static void Place(RawImage ri, float dir, float width, float height, Rect uv, Texture tex)
        {
            ri.texture = tex;
            ri.uvRect = uv;
            var r = ri.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(dir < 0f ? 0f : 1f, 0.5f);
            r.pivot = new Vector2(dir < 0f ? 0f : 1f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(width, height);
        }

        private void Awake()
        {
            image = GetComponent<Image>();
            rect = (RectTransform)transform;
        }

        private void OnEnable() => lastScreen = Vector2Int.zero;   // fuerza recálculo

        private Vector2 lastArea;

        private void LateUpdate()
        {
            // 0.36.1: también si cambia el tamaño del CANVAS, no solo el de la
            // pantalla. Al volver de una partida (o al cambiar de resolución) el
            // primer cálculo podía hacerse ANTES de que el CanvasScaler ajustase
            // la escala: el fondo se quedaba enorme (título cortado arriba y la
            // rata gigante abajo) y, como la pantalla ya no cambiaba, nunca se corregía.
            var screen = new Vector2Int(Screen.width, Screen.height);
            var canvas = image != null && image.canvas != null ? image.canvas.rootCanvas : null;
            Vector2 area = canvas != null ? ((RectTransform)canvas.transform).rect.size : Vector2.zero;
            if (screen == lastScreen && (area - lastArea).sqrMagnitude < 1f) return;
            lastScreen = screen;
            lastArea = area;
            Apply();
        }

        /// <summary>Recalcula el tamaño para cubrir el Canvas raíz entero.</summary>
        public void Apply()
        {
            if (image == null || image.sprite == null) return;
            var canvas = image.canvas != null ? image.canvas.rootCanvas : null;
            if (canvas == null) return;

            var canvasRect = (RectTransform)canvas.transform;
            Vector2 area = canvasRect.rect.size;          // tamaño de la pantalla en unidades del Canvas
            if (area.x <= 0f || area.y <= 0f) return;

            Vector2 spriteSize = image.sprite.rect.size;
            float spriteAspect = spriteSize.x / spriteSize.y;
            float areaAspect = area.x / area.y;

            // Cubrir: si la pantalla es más ancha que la imagen, mandamos por
            // ancho (se recorta arriba/abajo); si no, por alto (se recortan lados).
            Vector2 size = areaAspect > spriteAspect
                ? new Vector2(area.x, area.x / spriteAspect)
                : new Vector2(area.y * spriteAspect, area.y);

            // 0.22: en pantallas MÁS ANCHAS que 16:9 (ultrapanorámicas) el recorte
            // de arriba/abajo se comía el título RATMS (está dibujado en el fondo).
            // Ahora la imagen se recorta como en 16:9 y los lados se rellenan con
            // una copia estirada por detrás (cielo con cielo, suelo con suelo).
            // 0.30.1: SIEMPRE que la pantalla sea más ancha que la imagen (aunque sea
            // un poco), se ajusta por ALTO: nunca se corta el título de arriba ni
            // el suelo de abajo. Los lados se rellenan con la copia estirada.
            if (areaAspect > spriteAspect + 0.01f)
            {
                size = new Vector2(area.y * spriteAspect, area.y);
                EnsureFill(size, canvasRect, area);
            }
            else if (fill != null) fill.SetActive(false);

            image.preserveAspect = false;   // ya la proporción la llevamos nosotros

            // Anclado al centro con tamaño fijo, centrado sobre el Canvas raíz
            // (el padre "SafeArea" puede ser más ancho que la pantalla).
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.position = canvasRect.position;
        }
    }
}
