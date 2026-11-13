using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Indicador de trayectoria tipo Worms: una serie de PUNTITOS que van
    ///  describiendo el arco parabólico que seguiría un proyectil lanzado
    ///  desde <see cref="firePoint"/> con la fuerza y dirección dadas.
    ///
    ///  IMPORTANTE — por qué los dots viven en WORLD SPACE:
    ///   Este script suele ir como hijo del arma, que a su vez es hijo de un
    ///   Weaponholder con escala muy pequeña (el bazooka se ve chiquitito).
    ///   Si creásemos los dots como hijos, heredarían esa escala y no se verían.
    ///   Por eso al nacer los desparentamos al root de la escena: mantienen su
    ///   tamaño absoluto y los colocamos cada frame leyendo firePoint.position.
    ///   Al destruirse el arma los limpiamos (OnDestroy) para no dejar basura.
    ///
    ///  API pública:
    ///    • Show(force, dir) → enseña la trayectoria.
    ///    • Preview(dir)     → enseña con fuerza fija (arma equipada sin cargar).
    ///    • Hide()           → la apaga.
    /// </summary>
    public class TrajectoryDots : MonoBehaviour
    {
        [Header("Origen")]
        [Tooltip("Desde qué Transform calcular la parábola (normalmente la punta del arma).")]
        public Transform firePoint;

        [Header("Puntitos")]
        [Tooltip("Cuántos puntitos se dibujan a lo largo de la trayectoria.")]
        [Range(4, 60)] public int dotCount = 18;

        [Tooltip("Separación temporal entre puntitos (s). Más alto = trayectoria más larga.")]
        public float timeStep = 0.06f;

        [Tooltip("Tamaño en el MUNDO de cada puntito (unidades Unity).")]
        public float dotWorldSize = 0.2f;

        [Tooltip("Color de los puntitos.")]
        public Color dotColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("Sprite del puntito. Si lo dejas vacío, se genera un círculo blanco suave por código.")]
        public Sprite dotSprite;

        [Tooltip("Sorting layer de los puntitos (por nombre). Déjalo en Default si no sabes.")]
        public string sortingLayerName = "Default";

        [Tooltip("Sorting order: alto = por delante del escenario y de la rata.")]
        public int sortingOrder = 1000;

        [Header("Preview (cuando no está cargando)")]
        [Tooltip("Fuerza que se usa al llamar a Preview().")]
        public float previewForce = 7f;

        // Estado interno
        private readonly List<Transform> dots = new();
        private float gravity;
        private bool visible;
        private float cachedForce;
        private Vector2 cachedDir;

        // ==============================================================
        private void Awake()
        {
            gravity = Mathf.Abs(Physics2D.gravity.y);
            if (dotSprite == null) dotSprite = BuildCircleSprite();

            // Auto-resolución defensiva: si olvidas asignar firePoint en el
            // Inspector, buscamos el del bazooka (todas las armas comparten la
            // misma salida de disparo en la mano de la rata).
            if (firePoint == null)
            {
                var bazooka = GetComponentInParent<NetworkBazooka>();
                if (bazooka == null)
                    bazooka = GetComponentInChildren<NetworkBazooka>(includeInactive: true);
                if (bazooka != null)
                    firePoint = bazooka.FirePoint;
            }

            BuildPool();
            HideImmediate();
        }

        private void OnDestroy()
        {
            // Limpieza: los dots viven en world space así que hay que destruirlos a mano.
            foreach (var d in dots)
            {
                if (d != null) Destroy(d.gameObject);
            }
            dots.Clear();
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        private void BuildPool()
        {
            for (int i = 0; i < dotCount; i++)
            {
                GameObject go = new GameObject($"TrajectoryDot_{i}");
                // ¡Importante! No lo metemos como hijo nuestro: vive en world space
                // para ignorar la escala del weaponholder.
                go.transform.SetParent(null, worldPositionStays: true);

                // Tamaño en el mundo: el dot es independiente así que simplemente
                // le ponemos el tamaño deseado.
                go.transform.localScale = Vector3.one * dotWorldSize;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dotSprite;
                sr.color = dotColor;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;

                // Ligero efecto "hormigueo": los puntos lejanos se ven un poco más pequeños.
                float shrink = Mathf.Lerp(1f, 0.55f, (float)i / Mathf.Max(1, dotCount - 1));
                go.transform.localScale = Vector3.one * dotWorldSize * shrink;

                go.SetActive(false);
                dots.Add(go.transform);
            }
        }

        /// <summary>
        ///  Genera un sprite de círculo 32x32 por código: centro blanco (se
        ///  tiñe con dotColor) y un BORDE OSCURO que no se tiñe del todo, para
        ///  que los puntos se lean igual sobre cielo claro que sobre tierra.
        ///
        ///  OJO con el PPU: lo creamos con PPU = tamaño en píxeles, así el
        ///  sprite mide EXACTAMENTE 1 unidad y <see cref="dotWorldSize"/> es
        ///  de verdad el tamaño en el mundo. Antes usaba PPU 100 → el sprite
        ///  medía 0.32 u y los puntos salían 3 veces más pequeños de lo
        ///  configurado (casi invisibles).
        /// </summary>
        private Sprite BuildCircleSprite()
        {
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float r = size * 0.5f;
            const float border = 4.5f;   // grosor del borde oscuro en píxeles
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((r - d) / 1.5f);
                    // 1 = interior blanco, 0 = borde oscuro (con transición suave)
                    float inner = Mathf.Clamp01((r - border - d) / 1.5f);
                    float shade = Mathf.Lerp(0.12f, 1f, inner);
                    tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // ==============================================================
        // API PÚBLICA
        // ==============================================================
        public void Show(float force, Vector2 dir)
        {
            // LateUpdate decide qué puntos se ven (se cortan contra el terreno),
            // así que aquí solo activamos al pasar de oculto a visible.
            if (!visible) SetAllActive(true);
            visible = true;
            cachedForce = Mathf.Max(0.01f, force);
            cachedDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        }

        public void Preview(Vector2 dir) => Show(previewForce, dir);

        public void Hide() => HideImmediate();

        private void HideImmediate()
        {
            visible = false;
            foreach (var d in dots)
                if (d != null) d.gameObject.SetActive(false);
        }

        [Header("Corte contra el terreno")]
        [Tooltip("Si ON, la trayectoria se corta donde choca con algo (terreno, otra rata...) en vez de atravesarlo.")]
        public bool stopAtObstacles = true;

        private readonly RaycastHit2D[] segmentHits = new RaycastHit2D[6];
        private Transform ownerRoot;
        private NetworkHealth ownerHealth;

        // ==============================================================
        private void LateUpdate()
        {
            if (!visible || firePoint == null) return;
            if (ownerRoot == null)
            {
                var no = GetComponentInParent<Unity.Netcode.NetworkObject>();
                ownerRoot = no != null ? no.transform : transform.root;
            }

            // TUERTA: solo ve el principio de la trayectoria.
            if (ownerHealth == null && ownerRoot != null) ownerHealth = ownerRoot.GetComponent<NetworkHealth>();
            int maxVisible = ownerHealth != null && ownerHealth.HeadHurt ? 3 : int.MaxValue;

            Vector2 start = firePoint.position;
            Vector2 vel = cachedDir * cachedForce;
            Vector2 prev = start;
            bool blocked = false;

            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i] == null) continue;

                float t = i * timeStep;
                float dx = vel.x * t;
                float dy = vel.y * t - 0.5f * gravity * t * t;
                Vector2 p = new Vector2(start.x + dx, start.y + dy);

                // ¿El tramo desde el punto anterior atraviesa algo? Entonces
                // ese es el final de la trayectoria: los puntos que quedan
                // se ocultan (estilo Worms: ves dónde va a pegar).
                if (stopAtObstacles && !blocked && i > 0 && SegmentBlocked(prev, p))
                    blocked = true;

                bool show = !blocked && i < maxVisible;
                if (dots[i].gameObject.activeSelf != show) dots[i].gameObject.SetActive(show);
                if (show) dots[i].position = new Vector3(p.x, p.y, firePoint.position.z);
                prev = p;
            }
        }

        /// <summary>¿Hay algún collider (que no sea de nuestra propia rata) entre a y b?</summary>
        private bool SegmentBlocked(Vector2 a, Vector2 b)
        {
            var filter = new ContactFilter2D();
            filter.NoFilter();            // todas las capas...
            filter.useTriggers = false;   // ...pero sin triggers
            int n = Physics2D.Linecast(a, b, filter, segmentHits);
            for (int k = 0; k < n; k++)
            {
                var c = segmentHits[k].collider;
                if (c == null || c.isTrigger) continue;
                if (ownerRoot != null && c.transform.IsChildOf(ownerRoot)) continue;
                return true;
            }
            return false;
        }

        private void SetAllActive(bool active)
        {
            foreach (var d in dots)
                if (d != null) d.gameObject.SetActive(active);
        }
    }
}
