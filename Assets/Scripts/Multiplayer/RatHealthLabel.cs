using TMPro;
using UnityEngine;
using Unity.Netcode;

namespace Ratms.Networking
{
    /// <summary>
    ///  Etiqueta de VIDA encima de cada rata (estilo Worms).
    ///
    ///  Crea un TextMeshPro de mundo (no UI, no Canvas) como hijo de la rata
    ///  y muestra la vida actual. Cambia de color según porcentaje:
    ///    · verde   > 60%
    ///    · amarillo 30-60%
    ///    · rojo    < 30%
    ///
    ///  Se auto-instancia desde NetworkHealth.OnNetworkSpawn() — no hace falta
    ///  añadir nada al prefab de la rata. Si quieres ajustar la posición o el
    ///  tamaño, edita las constantes de aquí abajo.
    /// </summary>
    public class RatHealthLabel : MonoBehaviour
    {
        // Constantes de presentación (en unidades de mundo)
        private const float HeightAboveRat = 1.7f;   // a qué altura sale el número sobre la rata
        private const float FontSize = 3.5f;
        private const float ZPosition = -4f;

        private NetworkHealth health;
        private TextMeshPro tmp;
        private GameObject labelGO;

        /// <summary>
        ///  Llamado por NetworkHealth justo después de OnNetworkSpawn.
        ///  Crea el GameObject hijo con el TextMeshPro y se suscribe a los
        ///  cambios de vida.
        /// </summary>
        public void Init(NetworkHealth nh)
        {
            health = nh;

            // Construimos el hijo "HealthLabel" con TextMeshPro de mundo.
            //
            // CLAVE PARA NO PINTAR LA "T" FANTASMA:
            // Lo creamos DESACTIVADO antes de añadir TMP. Si añadiésemos TMP
            // en un GameObject activo, su Awake/OnEnable correría inmediatamente
            // y generaría una malla con el placeholder por defecto (la "T" que
            // estabas viendo). Con el GO desactivado, TMP no arranca hasta que
            // termine la configuración y le pongamos el texto bueno.
            labelGO = new GameObject("HealthLabel");
            labelGO.SetActive(false);
            // OJO: NO lo hacemos hijo de la rata. Si lo fuésemos, cuando la
            // rata se flipea (scale.x = -1 para mirar a la izquierda) el label
            // se voltea también y sale al revés o descolocado. Lo dejamos
            // suelto en la escena y en LateUpdate() lo pegamos a la rata.
            labelGO.transform.position = new Vector3(
                transform.position.x,
                transform.position.y + HeightAboveRat,
                ZPosition);

            tmp = labelGO.AddComponent<TextMeshPro>();
            // Lo apagamos como componente: defensa adicional contra el placeholder
            // "T" que TMP a veces pinta antes de que terminemos de configurar.
            tmp.enabled = false;

            // Aseguramos fuente (igual que en WorldDamagePopup): TMP creado
            // por código a veces queda con font = null en proyectos nuevos,
            // y entonces no se renderiza nada o sale un glifo random.
            if (tmp.font == null)
            {
                var defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (defaultFont != null) tmp.font = defaultFont;
            }

            // Asignamos el texto inicial ANTES de activar para que TMP arranque
            // ya con el nombre + número correctos y no con placeholder.
            tmp.text = BuildLabelText(health.CurrentHealth.Value);
            tmp.color = GetColorForHealth(health.CurrentHealth.Value, health.MaxHealth);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = FontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = false;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 2.5f);

            var mr = labelGO.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 90; // por delante de la rata

            // Suscripción al NetworkVariable de vida
            health.CurrentHealth.OnValueChanged += OnHealthChanged;
            health.IsDead.OnValueChanged += OnDeathChanged;

            // Encendemos la etiqueta — TMP arranca con el texto bueno.
            labelGO.SetActive(true);
            tmp.enabled = true;
            tmp.ForceMeshUpdate(true, true);
        }

        /// <summary>
        ///  Pegamos el label a la posición de la rata cada frame. Al no ser
        ///  hijo, no hereda el flip ni la rotación — sale siempre bien puesto
        ///  encima de la cabeza. Usamos LateUpdate para que se sincronice DESPUÉS
        ///  de que el movimiento/NetworkTransform haya actualizado la rata.
        /// </summary>
        // Cacheamos el nombre para no rebuild el texto cada frame
        private string cachedName;

        private void LateUpdate()
        {
            if (labelGO == null) return;
            // Si la rata muere (transform desactivado), ocultamos el label.
            if (transform == null || !gameObject.activeInHierarchy)
            {
                if (labelGO.activeSelf) labelGO.SetActive(false);
                return;
            }
            labelGO.transform.position = new Vector3(
                transform.position.x,
                transform.position.y + HeightAboveRat,
                ZPosition);

            // Si el nombre del jugador cambia (por ejemplo llegó por red DESPUÉS
            // de crear el label), refrescamos el texto. Muy poco coste.
            string currentName = DisplayName();
            if (currentName != cachedName)
            {
                cachedName = currentName;
                if (health != null) Refresh(health.CurrentHealth.Value);
            }
        }

        private void OnDestroy()
        {
            // Al destruir la rata (o desconectar el cliente), tenemos que
            // destruir también el label porque ya no es hijo suyo y se quedaría
            // huérfano en la escena.
            if (labelGO != null) Destroy(labelGO);

            if (health != null)
            {
                health.CurrentHealth.OnValueChanged -= OnHealthChanged;
                health.IsDead.OnValueChanged -= OnDeathChanged;
            }
        }

        private void OnHealthChanged(int oldValue, int newValue)
        {
            Refresh(newValue);
        }

        private void OnDeathChanged(bool oldValue, bool newValue)
        {
            // Al morir ocultamos el número (queda feo "0" en una rata muerta).
            if (newValue && labelGO != null) labelGO.SetActive(false);
        }

        private void Refresh(int hp)
        {
            if (tmp == null || health == null) return;

            tmp.text = BuildLabelText(hp);
            tmp.color = GetColorForHealth(hp, health.MaxHealth);
        }

        /// <summary>
        ///  Construye el texto del label con NOMBRE del jugador arriba (más
        ///  pequeño) y HP debajo (más grande). El nombre viene del
        ///  PlayerNameRegistry, que se sincroniza automáticamente por el
        ///  PlayerNameSync de cada rata.
        /// </summary>
        private string BuildLabelText(int hp)
        {
            // Nombre de la RATA en el color de su equipo (más pequeño) y la vida debajo.
            var move = GetComponent<NetworkPlayerMovement>();
            string name = DisplayName();
            string hex = "FFFFFF";
            if (move != null && !move.IsTestDummy && move.TeamIndex.Value >= 0)
                hex = ColorUtility.ToHtmlStringRGB(RatAppearance.ColorForTeam(move.TeamIndex.Value));
            return $"<size=65%><color=#{hex}>{name}</color></size>\n{hp}";
        }

        /// <summary>Nombre que se muestra: el de la rata; si no tiene, el del jugador.</summary>
        private string DisplayName()
        {
            var move = GetComponent<NetworkPlayerMovement>();
            if (move != null && move.IsTestDummy) return "Diana";
            if (move != null && !move.RatName.Value.IsEmpty) return move.RatName.Value.ToString();
            var netObj = GetComponent<NetworkObject>();
            return PlayerNameRegistry.GetName(netObj != null ? netObj.OwnerClientId : 0);
        }

        private static Color GetColorForHealth(int hp, int maxHp)
        {
            if (maxHp <= 0) return Color.white;
            float pct = (float)hp / maxHp;

            if (pct > 0.6f) return new Color(0.4f, 1f, 0.4f);   // verde
            if (pct > 0.3f) return new Color(1f, 0.95f, 0.3f);  // amarillo
            return new Color(1f, 0.3f, 0.3f);                   // rojo
        }
    }
}
