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
    ///   • El feedback (popup de daño flotante + numerito de vida) lo gestionan
    ///     <see cref="WorldDamagePopup"/> y <see cref="RatHealthLabel"/>. Ambos
    ///     son scripts programáticos — NO necesitan prefab — así nos quitamos
    ///     de encima los problemas que dieron los TMP_UI dentro de Canvas.
    ///
    ///  Flujo normal de daño:
    ///   1. Un misil/granada (en el servidor) detecta colisión/explosión.
    ///   2. Llama a <see cref="ApplyDamageServer"/> con el tipo de daño.
    ///   3. El servidor actualiza CurrentHealth y dispara el popup en todos
    ///      los clientes vía <see cref="ShowDamagePopupClientRpc"/>.
    ///   4. Si la vida llega a 0, el servidor marca IsDead=true.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour
    {
        [Header("Vida")]
        [SerializeField] private int maxHealth = 100;

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

        private Collider2D bodyCollider;

        /// <summary>
        ///  Centro REAL del cuerpo de la rata (centro de su collider).
        ///
        ///  ¡OJO! El pivote del prefab (transform.position) está ~0.8 u POR
        ///  DEBAJO del cuerpo, dentro del suelo. Para distancias de explosión,
        ///  popups, golpes o "¿ha tocado el agua?" hay que usar esto, no
        ///  transform.position.
        /// </summary>
        public Vector2 BodyCenter
        {
            get
            {
                if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
                return bodyCollider != null && bodyCollider.enabled
                    ? (Vector2)bodyCollider.bounds.center
                    : (Vector2)transform.position;
            }
        }

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

            // Auto-añadir el numerito de vida flotante en TODOS los clientes
            // (host incluido). Es solo visual, no toca lógica.
            EnsureHealthLabel();

            // Late-join safety: si un cliente entra y esta rata ya estaba muerta,
            // que no aparezca viva por un frame.
            if (IsDead.Value) ApplyDeathVisual();
        }

        // Rata malherida (< 35 % de vida): va dejando gotas de sangre (local).
        private float nextDrip;
        private void Update()
        {
            if (!IsSpawned || IsDead.Value || maxHealth <= 0) return;
            float hp = CurrentHealth.Value / (float)maxHealth;
            if (hp >= 0.35f || Time.time < nextDrip) return;
            nextDrip = Time.time + Mathf.Lerp(0.15f, 0.7f, hp / 0.35f) * Random.Range(0.7f, 1.3f);
            Fx.Drip(BodyCenter + Vector2.down * 0.15f);
        }

        public override void OnNetworkDespawn()
        {
            IsDead.OnValueChanged -= OnDeathStateChanged;
        }

        /// <summary>
        ///  Añade el componente RatHealthLabel si no estaba ya. Así no hay
        ///  que tocar el prefab de la rata para tener el numerito de vida.
        /// </summary>
        private void EnsureHealthLabel()
        {
            var label = GetComponent<RatHealthLabel>();
            if (label == null) label = gameObject.AddComponent<RatHealthLabel>();
            label.Init(this);
        }

        // ==============================================================
        // DAÑO
        // ==============================================================
        /// <summary>
        ///  Aplica daño. SOLO el servidor puede llamarlo.
        ///  Versión por defecto (compatible hacia atrás): tipo = Generic.
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint)
        {
            ApplyDamageServer(amount, hitPoint, DamageType.Generic);
        }

        /// <summary>
        ///  Aplica daño con un tipo específico — explosión, agua, fuego...
        ///  El popup pintará el color/símbolo correspondiente.
        /// </summary>
        public void ApplyDamageServer(int amount, Vector3 hitPoint, DamageType type)
        {
            if (!IsServer) return;
            if (IsDead.Value || amount <= 0) return;

            int newHp = Mathf.Max(0, CurrentHealth.Value - amount);
            CurrentHealth.Value = newHp;

            // Propagamos el popup a todos los clientes (incluido el dueño).
            // Mandamos el tipo como byte para minimizar el tamaño del paquete.
            ShowDamagePopupClientRpc(amount, hitPoint, (byte)type, newHp <= 0);

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
            ApplyDamageServer(amount, hitPoint, DamageType.Generic);
        }

        // ==============================================================
        // KNOCKBACK (empujón de explosión)
        // ==============================================================
        /// <summary>
        ///  Empuja a esta rata aplicando un impulso a su Rigidbody2D.
        ///
        ///  Se llama desde el servidor (misil/granada al explotar) y lo aplica
        ///  SOLO el Owner de la rata — es quien controla la física de su
        ///  personaje; el NetworkTransform sincroniza el "vuelo" al resto de
        ///  clientes como cualquier otro movimiento normal.
        ///
        ///  Si la rata acaba de morir, no aplicamos empuje (para no mandar el
        ///  cadáver volando por toda la pantalla — opinión de diseño;
        ///  cámbialo si prefieres ragdoll).
        ///
        ///  <paramref name="freeFlightSeconds"/>: durante este tiempo el
        ///  <see cref="NetworkPlayerMovement"/> ignora su linearDamping y el
        ///  maxSpeed. Así el empujón "vuela" de verdad en lugar de frenar en
        ///  medio segundo por la fricción aérea que la rata usa para caminar.
        /// </summary>
        [ClientRpc]
        public void ApplyKnockbackClientRpc(Vector2 force, float freeFlightSeconds = 0.5f)
        {
            if (!IsOwner) return;
            if (IsDead.Value) return;

            var rb = GetComponent<Rigidbody2D>();
            if (rb == null) return;

            // Reseteamos velocidad vertical para que el empujón "suba" de verdad
            // aunque la rata estuviera ya cayendo. Si la rata se movía horizontal,
            // respetamos ese impulso sumándolo en vez de pisar.
            Vector2 v = rb.linearVelocity;
            v.y = 0f;
            rb.linearVelocity = v;

            rb.AddForce(force, ForceMode2D.Impulse);

            // Pedimos al script de movimiento que desactive fricción y tope de
            // velocidad durante el "vuelo" del empujón.
            var move = GetComponent<NetworkPlayerMovement>();
            if (move != null) move.BeginKnockbackFlight(freeFlightSeconds);
        }

        // ==============================================================
        // POPUPS
        // ==============================================================
        /// <summary>
        ///  Spawnea el popup flotante en TODOS los clientes (cada uno lo crea
        ///  en su escena, no es un objeto en red). El "type" llega como byte
        ///  por la red — lo casteamos a DamageType al llegar.
        /// </summary>
        [ClientRpc]
        private void ShowDamagePopupClientRpc(int amount, Vector3 hitPoint, byte typeByte, bool lethal)
        {
            DamageType type = (DamageType)typeByte;
            lastDamageType = type;
            WorldDamagePopup.Spawn(hitPoint, amount, type);
            if (type == DamageType.Water) { Fx.Splash(hitPoint); Sfx.Play(Sfx.Id.Splash); return; }

            // GORE: salpicón de sangre hacia fuera del golpe (mancha el suelo).
            Vector2 away = BodyCenter - (Vector2)hitPoint;
            Fx.Blood(BodyCenter, away.sqrMagnitude > 0.0004f ? away : Vector2.up, amount);
            var look = GetComponent<RatAppearance>();
            if (look != null) look.HitFlash();
            if (!lethal)
            {
                Sfx.Play(Sfx.Id.Ouch, 0.7f, 0.15f);
                if (Random.value < 0.35f)
                    WorldDamagePopup.SpawnImpact(BodyCenter + Vector2.up * 0.9f, Pick(HurtQuips), new Color(1f, 0.75f, 0.6f));
            }

            // RISA: la rata que tiene el turno se ríe de su víctima (siempre si
            // la mata, a veces si solo la hiere). Humor negro de trinchera.
            var attacker = CurrentTurnRat();
            if (attacker != null && attacker.gameObject != gameObject && (lethal || Random.value < 0.45f))
            {
                var ah = attacker.GetComponent<NetworkHealth>();
                Vector2 at = (ah != null ? ah.BodyCenter : (Vector2)attacker.transform.position) + Vector2.up * 0.95f;
                WorldDamagePopup.SpawnImpact(at, Pick(Laughs), new Color(1f, 0.9f, 0.35f));
                Sfx.Play(Sfx.Id.Laugh, lethal ? 1f : 0.75f, 0.1f);
            }
        }

        private static readonly string[] HurtQuips =
        {
            "¡Mi cola!", "¡Eso escuece!", "¡Mis bigotes!", "¡Que me desangro!", "¡Auch, cabrón!",
            "¡Ratas!", "¡Mamáaa!", "¡Me has dejado tuerta!", "¡Eso no se hace!", "¡Uy, la víscera!",
        };
        private static readonly string[] Laughs =
        {
            "¡JA, JA, JA!", "¡JIJIJIJI!", "¡Chúpate esa!", "¡Toma ya!", "¡Al hoyo!", "¡Ñiec, ñiec!", "¡JE, JE!",
        };
        private static readonly string[] DeathQuips =
        {
            "¡Al queso eterno!", "Fiambre de trinchera", "Otra para el foso", "Ratatouille", "Se acabó la guerra para esta",
            "Tanto queso para esto...", "Directa al hoyo", "Requiescat in pizza", "Carne de cañón", "¡Adiós, mundo cruel!",
        };

        private static string Pick(string[] arr) => arr[Random.Range(0, arr.Length)];

        /// <summary>Rata (no diana) del jugador que tiene el turno ahora mismo.</summary>
        private static NetworkPlayerMovement CurrentTurnRat()
        {
            var tm = NetworkTurnManager.Instance;
            if (tm == null || tm.CurrentTurnClientId.Value == ulong.MaxValue) return null;
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                if (m.OwnerClientId == tm.CurrentTurnClientId.Value && !m.IsTestDummy && m.isActiveAndEnabled) return m;
            return null;
        }

        // Último tipo de daño recibido (en cada cliente): para saber si la
        // muerte fue en el agua (salpicadura, sin lápida) o en tierra (lápida).
        private DamageType lastDamageType = DamageType.Generic;

        // ==============================================================
        // MUERTE
        // ==============================================================
        private void OnDeathStateChanged(bool oldValue, bool newValue)
        {
            if (!newValue) return;
            // Muerte en tierra: REVIENTA (gore) + lápida + frase de humor negro.
            // En el agua no: ya hay salpicadura.
            if (lastDamageType != DamageType.Water)
            {
                Vector2 b = BodyCenter;
                var look = GetComponent<RatAppearance>();
                Fx.Gibs(b, look != null ? look.Team : (Color32?)null);
                if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.25f, 0.35f);
                Sfx.Play(Sfx.Id.Splat);
                Fx.Gravestone(b);
                WorldDamagePopup.SpawnImpact(b + Vector2.up * 1.3f, Pick(DeathQuips), new Color(0.95f, 0.25f, 0.2f));
            }
            Sfx.Play(Sfx.Id.Squeak, 0.8f);
            ApplyDeathVisual();
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
