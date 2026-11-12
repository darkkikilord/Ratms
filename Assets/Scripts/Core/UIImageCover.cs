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
