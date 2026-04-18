using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Vida de la rata en MULTIJUGADOR.
    ///
    ///  Claves de diseño:
    ///   • El SERVIDOR es la única autoridad sobre la vida. Nadie más puede
    ///     bajársela (así nadie puede hacer trampa aunque modifique su cliente).
    ///   • <see cref="CurrentHealth"/> e <see cref="IsDead"/> son NetworkVariables:
    ///     el servidor las escribe, todos los clientes las leen y reaccionan.
    ///   • El feedback (popup de daño flotante) se propaga con un ClientRpc
    ///     para que todos lo vean, no solo quien recibió el golpe.
    ///
    ///  Flujo normal de daño:
    ///   1. Un misil (ya en el servidor) detecta colisión/explosión.
    ///   2. Llama a <see cref="ApplyDamageServer"/> sobre cada NetworkHealth
    ///      dentro del radio.
    ///   3. El servidor actualiza CurrentHealth y dispara el popup en todos
    ///      los clientes vía <see cref="ShowDamagePopupClientRpc"/>.
    ///   4. Si la vida llega a 0, el servidor marca IsDead=true y el
    ///      callback deja KO a la rata en TODOS los clientes.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour
    {
        [Header("Vida")]
        [SerializeField] private int maxHealth = 100;

        [Header("Feedback de daño")]
        [Tooltip("Prefab del DamagePopup del mundo (TextMeshPro flotante).")]
        [SerializeField] private GameObject damagePopupPrefab;
        [Tooltip("Offset sobre el punto de impacto donde aparece el popup.")]
        [SerializeField] private Vector3 popupOffset = new Vector3(0f, 0.5f, -4f);

        [Header("Muerte")]
        [Tooltip("Si está ON, al morir desactiva el GameObject (como el single-player). Si está OFF, solo oculta visuales.")]
        [SerializeField] private bool disableOnDeath = true;

        // ==============================================================
        // Estado sincronizado
        // ==============================================================
        /// <summary>Vida actual. Solo escribible por el servidor.</summary>
        public readonly NetworkVariable<int> CurrentHealth = new(
            value: 100,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>true = rata KO. Al cambiar, todos los clientes la apagan.</summary>
        public readonly NetworkVariable<bool> IsDead = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        public int MaxHealth => maxHealth;

        /// <summary>
        ///  Evento que se dispara EN EL SERVIDOR cuando la rata muere.
        ///  El NetworkTurnManager lo escucha para sacar al jugador de la rotación
        ///  de turnos sin esperar al siguiente timeout.
        /// </summary>
        public event System.Action<NetworkHealth> OnDiedServer;

        // ==============================================================
        // CICLO DE VIDA
        // ==============================================================
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHealth.Value = maxHealth;
                IsDead.Value = false;
            }

            IsDead.OnValueChanged += OnDeathStateChanged;

            // Late-join safety: si un cliente entra y esta rata ya estaba muerta,
            // que no aparezca viva por un frame.
            if (IsDead.Value) ApplyDeathVisual();
        }

        public override void OnNetworkDespawn()
        {
            IsDead.OnValueChanged -= OnDeathStateChanged;
        }

        // ==============================================================
        // DAÑO
        // ==============================================================
        /// <summary>
        ///  Aplica daño. SOLO el servidor puede llamarlo (lo usa el misil).
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint)
        {
            if (!IsServer) return;
            if (IsDead.Value || amount <= 0) return;

            int newHp = Mathf.Max(0, CurrentHealth.Value - amount);
            CurrentHealth.Value = newHp;

            // Propagamos el popup a todos los clientes (incluido el dueño)
            ShowDamagePopupClientRpc(amount, hitPoint);

            if (newHp <= 0)
            {
                IsDead.Value = true;
                OnDiedServer?.Invoke(this);
            }
        }

        /// <summary>
        ///  ServerRpc para que un cliente pueda pedir daño (no se usa hoy pero
        ///  está disponible por si en el futuro hay armas cliente-autoritativas).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void TakeDamageServerRpc(int amount, Vector3 hitPoint)
        {
            ApplyDamageServer(amount, hitPoint);
        }

        // ==============================================================
        // POPUPS
        // ==============================================================
        [ClientRpc]
        private void ShowDamagePopupClientRpc(int amount, Vector3 hitPoint)
        {
            if (damagePopupPrefab == null) return;

            Vector3 spawnPos = hitPoint + popupOffset;
            GameObject popup = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity);

            // Mismo truco que el missile single: reparentar al WorldCanvas si existe
            GameObject canvasGO = GameObject.FindWithTag("WorldCanvas");
            if (canvasGO != null)
                popup.transform.SetParent(canvasGO.transform, worldPositionStays: true);

            var script = popup.GetComponent<DamagePopup>();
            if (script != null) script.Setup(amount);
        }

        // ==============================================================
        // MUERTE
        // ==============================================================
        private void OnDeathStateChanged(bool oldValue, bool newValue)
        {
            if (newValue) ApplyDeathVisual();
        }

        private void ApplyDeathVisual()
        {
            Debug.Log($"[NetworkHealth] {gameObject.name} ha muerto.");
            if (disableOnDeath)
            {
                gameObject.SetActive(false);
            }
            else
            {
                // Modo "solo oculta visuales" — útil si más adelante queremos
                // animación de muerte antes de desaparecer.
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                    sr.enabled = false;
                foreach (var col in GetComponentsInChildren<Collider2D>(true))
                    col.enabled = false;
            }
        }
    }
}
