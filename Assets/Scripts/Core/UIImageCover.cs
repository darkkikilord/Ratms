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
        private Image fill;

        private void EnsureFill(Vector2 size, RectTransform canvasRect)
        {
            if (fill == null)
            {
                var go = new GameObject("FondoRelleno", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform.parent, false);
                fill = go.GetComponent<Image>();
                fill.sprite = image.sprite;
                fill.raycastTarget = false;
                fill.color = new Color(0.82f, 0.82f, 0.82f, 1f);   // un pelín más oscuro: el ojo va al centro
            }
            fill.gameObject.SetActive(true);
            fill.transform.SetSiblingIndex(transform.GetSiblingIndex());   // justo detrás
            var r = (RectTransform)fill.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.position = canvasRect.position;
        }

        private void Awake()
        {
            image = GetComponent<Image>();
            rect = (RectTransform)transform;
        }

        private void OnEnable() => lastScreen = Vector2Int.zero;   // fuerza recálculo

        private void LateUpdate()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen == lastScreen) return;
            lastScreen = screen;
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
            const float maxAspect = 16f / 9f;
            if (areaAspect > maxAspect && spriteAspect < maxAspect)
            {
                float h = area.y * maxAspect / spriteAspect;
                size = new Vector2(h * spriteAspect, h);
                EnsureFill(new Vector2(area.x, h), canvasRect);
            }
            else if (fill != null) fill.gameObject.SetActive(false);

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
