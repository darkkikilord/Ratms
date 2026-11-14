using TMPro;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Popup flotante de DAÑO en mundo (NO usa Canvas).
    ///
    ///  Diferencia clave con el viejo DamagePopup.cs:
    ///   · Aquel necesitaba un prefab con TextMeshProUGUI dentro de un
    ///     Canvas. Si el prefab perdía la referencia o tenía texto basura,
    ///     se veía mal (de ahí la "T" fantasma).
    ///   · Este se construye programáticamente con un TextMeshPro de mundo
    ///     (3D, sin Canvas). No hace falta prefab. Lo crea NetworkHealth
    ///     directamente cuando hay daño.
    ///
    ///  Cómo se usa:
    ///     WorldDamagePopup.Spawn(worldPos, amount, DamageType.Explosion);
    ///
    ///  El método Spawn es estático, así que no hay que asignar nada en el
    ///  inspector — invocas y listo.
    /// </summary>
    public class WorldDamagePopup : MonoBehaviour
    {
        // -----------------------
        // Configuración del look
        // -----------------------
        // Cuánto sube el texto en su segundo de vida (unidades de mundo).
        private const float FloatDistance = 0.8f;
        // Cuánto vive (segundos) — luego se autodestruye.
        private const float Lifetime = 1.0f;
        // Tamaño base del texto.
        private const float FontSize = 4f;
        // Z fija para que se vea por delante del terreno y la rata.
        private const float ZPosition = -4f;

        private TextMeshPro tmp;
        private float startTime;
        private Vector3 startPos;

        /// <summary>
        ///  Crea un popup en worldPos mostrando "amount" con el color/prefijo
        ///  correspondiente al tipo de daño. Se autodestruye solo.
        /// </summary>
        /// <summary>
        ///  Popup genérico de impacto — para FX que NO son daño a una rata
        ///  (p. ej. el "BOOM!" cuando un misil cae en el terreno sin pegar a
        ///  nadie). Útil para que el jugador siempre tenga feedback visual
        ///  de sus disparos, aunque haya fallado.
        /// </summary>
        public static WorldDamagePopup SpawnImpact(Vector3 worldPos, string text, Color color)
        {
            text = Ratms.UI.Loc.Show(text);   // "¡PAM!", "¡Sin munición!"... en el idioma de este PC
            // Z FIJA (-4): el popup es 2D ortográfico y el sortingOrder=32000
            // ya garantiza que se pinte por DELANTE de todo. Antes ponía la Z
            // basándome en Camera.main al spawnear, pero si la cámara se movía
            // luego (sigue al misil, focus de impacto…) el popup se quedaba en
            // una Z que pasaba a estar DETRÁS de la cámara → invisible.
            float z = ZPosition;

            var go = new GameObject("WorldImpactPopup");
            go.SetActive(false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y + 0.4f, z);

            var pop = go.AddComponent<WorldDamagePopup>();
            pop.BuildRaw(text, color);

            go.SetActive(true);
            return pop;
        }

        private void BuildRaw(string text, Color color)
        {
            tmp = gameObject.AddComponent<TextMeshPro>();
            // Apagamos el componente TMP de inmediato — así no puede pintar
            // ningún placeholder mientras configuramos el text de verdad.
            tmp.enabled = false;

            if (tmp.font == null)
            {
                var defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (defaultFont != null) tmp.font = defaultFont;
            }
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = FontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = false;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);

            var mr = GetComponent<MeshRenderer>();
            if (mr != null) { mr.sortingOrder = 32000; mr.sortingLayerName = "Default"; }

            // Volvemos a encender TMP — ya con el text correcto puesto.
            tmp.enabled = true;

            startTime = Time.time;
            startPos = transform.position;
        }

        public static WorldDamagePopup Spawn(Vector3 worldPos, int amount, DamageType type)
        {
            // Z FIJA (-4): el popup vive en 2D ortográfico y el sortingOrder=32000
            // ya garantiza el orden de pintado. Si pongo la Z relativa a la cámara
            // en el momento del spawn, cuando la cámara vuelve a su sitio (tras
            // seguir al misil) el popup se queda colgado en una Z DETRÁS de la
            // cámara nueva — y desaparece. Con Z fija siempre se ve.
            float z = ZPosition;

            // CLAVE PARA MATAR LA "T" FANTASMA:
            // Creamos el GameObject DESACTIVADO. Así, cuando hagamos
            // AddComponent<TextMeshPro>() dentro de Build, el Awake/OnEnable
            // de TMP NO corre todavía y no genera la malla con el placeholder
            // por defecto que aparecía como "T". Cuando todo esté configurado,
            // activamos el GameObject de golpe y TMP arranca con el texto YA
            // bien puesto.
            var go = new GameObject("WorldDamagePopup");
            go.SetActive(false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y + 0.4f, z);

            var pop = go.AddComponent<WorldDamagePopup>();
            pop.Build(amount, type);

            // Ahora sí, "encendemos" el popup con el texto correcto desde el
            // primer frame.
            go.SetActive(true);

            Debug.Log($"[WorldDamagePopup] Spawn at {go.transform.position} → '{pop.tmp?.text}' ({type}, {amount} dmg)");
            return pop;
        }

        private void Build(int amount, DamageType type)
        {
            // 1) TextMeshPro de mundo (no UI). Esto crea internamente
            //    MeshRenderer + MeshFilter — no necesitamos hacerlo a mano.
            //    Como el GameObject está desactivado, TMP no se inicializa
            //    todavía: configuramos en frío y al activar todo arranca limpio.
            tmp = gameObject.AddComponent<TextMeshPro>();
            // Doble salvavidas: lo apagamos por componente. Así, aunque alguien
            // active el GO antes de tiempo, TMP no pinta nada hasta que nos
            // dé la gana. Lo encenderemos al final con el text ya en su sitio.
            tmp.enabled = false;

            // 1.1) Aseguramos que el TMP tiene FUENTE asignada. Cuando creamos
            //      un TMP por código, en algunos proyectos (Unity 6 incluido)
            //      el font asset default queda nulo y el texto sale invisible.
            //      Forzamos el LiberationSans SDF de los TMP Essentials.
            if (tmp.font == null)
            {
                var defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (defaultFont != null) tmp.font = defaultFont;
                else Debug.LogWarning("[WorldDamagePopup] No se encontró LiberationSans SDF. " +
                                       "Importa los TMP Essentials desde Window > TextMeshPro.");
            }

            // 2) Texto y estilo según el tipo de daño.
            //    Importante: el texto se asigna ANTES de cualquier otra cosa
            //    para que cuando TMP arranque (al activar el GameObject) ya
            //    sepa qué pintar y no use placeholder.
            tmp.text = FormatText(amount, type);
            tmp.color = GetColor(type);
            tmp.fontSize = FontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = false; // queremos tamaño fijo, sin sorpresas
            // Outline para que se lea sobre cualquier fondo
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;

            // 3) Tamaño del rect del TMP — suficientemente ancho para "9999 💧"
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            // Sorting order MUY alto para que esté por delante de TODO
            // (terreno, ratas, agua, partículas...).
            var mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sortingOrder = 32000;
                mr.sortingLayerName = "Default";
            }

            // Reencendemos TMP ahora que el text correcto ya está puesto.
            tmp.enabled = true;

            startTime = Time.time;
            startPos = transform.position;
        }

        private void OnEnable()
        {
            // Al activar el GameObject (después de Build) forzamos un refresh
            // de la malla para que TMP no se quede con residuos del estado
            // por defecto que tenía antes de configurar.
            if (tmp != null) tmp.ForceMeshUpdate(true, true);
        }

        private void Update()
        {
            float t = (Time.time - startTime) / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Sube con curva ease-out (más rápido al principio, frena al final)
            float ease = 1f - Mathf.Pow(1f - t, 2f);
            transform.position = startPos + Vector3.up * FloatDistance * ease;

            // Fade alpha hacia 0 en la segunda mitad de la vida
            if (tmp != null)
            {
                float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
                Color c = tmp.color;
                c.a = alpha;
                tmp.color = c;
            }
        }

        /// <summary>
        ///  Formatea "amount" + prefijo/símbolo según el tipo de daño.
        ///  Usamos texto ASCII en lugar de emojis porque la fuente por
        ///  defecto de TextMeshPro (LiberationSans SDF) no tiene atlas de
        ///  emojis — saldrían cuadrados vacíos.
        /// </summary>
        private static string FormatText(int amount, DamageType type)
        {
            // Daño "muerte instantánea" del agua: no muestres "9999",
            // queda más limpio un símbolo claro.
            if (type == DamageType.Water) return Ratms.UI.Loc.T("¡AGUA!");

            string prefix = type switch
            {
                DamageType.Explosion => "*",  // estallido
                DamageType.Fire      => "~",  // fuego
                DamageType.Fall      => "v",  // caída
                DamageType.Melee     => "x",  // golpe
                _                    => "-",
            };
            return $"{prefix}{amount}";
        }

        private static Color GetColor(DamageType type)
        {
            return type switch
            {
                DamageType.Explosion => new Color(1f, 0.35f, 0.15f), // naranja-rojo
                DamageType.Fire      => new Color(1f, 0.6f, 0.1f),   // naranja
                DamageType.Fall      => new Color(0.85f, 0.85f, 0.4f), // amarillo
                DamageType.Water     => new Color(0.3f, 0.7f, 1f),   // azul
                DamageType.Melee     => new Color(1f, 0.9f, 0.3f),   // amarillo cálido
                _                    => new Color(1f, 0.2f, 0.2f),   // rojo genérico
            };
        }
    }
}
