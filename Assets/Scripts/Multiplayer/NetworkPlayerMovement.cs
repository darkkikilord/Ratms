using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Movimiento + disparo de la rata en MULTIJUGADOR.
    ///
    ///  Lo que hace:
    ///   • Movimiento horizontal y salto (solo el dueño).
    ///   • Flip y velocidad sincronizados por red (todos los clientes ven al
    ///     dueño girarse y correr igual).
    ///   • Apuntado ↑↓ con ángulo sincronizado por red.
    ///   • Carga de disparo con ESPACIO y disparo del NetworkBazooka.
    ///   • Tras disparar, pide al NetworkTurnManager que acabe el turno.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class NetworkPlayerMovement : NetworkBehaviour
    {
        [Header("Movimiento")]
        [SerializeField] private float moveForce = 20f;
        [SerializeField] private float maxSpeed = 6f;
        [SerializeField] private float linearDrag = 4f;

        [Header("Salto")]
        [SerializeField] private float jumpForce = 10f;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundRadius = 0.15f;
        [SerializeField] private LayerMask groundMask;

        [Header("Turnos")]
        [Tooltip("Si está ON, la rata solo responde a input durante SU turno. Desactívalo para modo libre.")]
        [SerializeField] private bool respectTurns = true;

        [Header("Visual")]
        [Tooltip("Sprite de la rata (para hacer flip). Si no se asigna se buscará automáticamente.")]
        [SerializeField] private SpriteRenderer bodySprite;

        [Tooltip("Animator de la rata. Si no se asigna se buscará automáticamente.")]
        [SerializeField] private Animator animator;

        [Tooltip("Nombre del parámetro float del Animator que decide idle/agachada/corriendo.")]
        [SerializeField] private string speedParam = "Speed";

        [Header("Vida / Armas")]
        [Tooltip("Referencia al NetworkHealth de esta rata (se auto-resuelve).")]
        [SerializeField] private NetworkHealth health;

        [Tooltip("Referencia al NetworkBazooka en la propia rata (se auto-resuelve si está vacío).")]
        [SerializeField] private NetworkBazooka bazooka;

        [Tooltip("Mano de la rata que sujeta el arma. Rota con el ángulo de apuntado.")]
        [SerializeField] private Transform weaponHolder;

        [Tooltip("Velocidad de apuntado (grados por segundo).")]
        [SerializeField] private float aimSpeed = 200f;

        [Tooltip("Segundos de retardo entre el disparo y pasar el turno al siguiente jugador.")]
        [SerializeField] private float endTurnDelay = 0.6f;

        // --- Privadas ---
        private Rigidbody2D rb;
        private float inputX;
        private int speedHash;
        private SpriteRenderer[] allSprites;
        private Vector3 weaponHolderDefaultPos;
        private bool weaponHolderPosCached;
        private float endTurnTimer = -1f;

        /// <summary>
        ///  Variable sincronizada por red: true = mira a la derecha, false = izquierda.
        ///  Así los demás clientes ven al dueño girarse correctamente.
        ///  Escritura permitida solo por el dueño.
        /// </summary>
        private readonly NetworkVariable<bool> facingRight = new(
            value: true,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Owner);

        /// <summary>
        ///  Velocidad horizontal sincronizada por red para alimentar la animación.
        ///  El dueño la escribe; todos los clientes la leen y la pasan al Animator.
        ///  Necesario porque en los clientes no-dueños el Rigidbody2D no simula física,
        ///  así que su linearVelocity local sería 0 y la animación no arrancaría.
        /// </summary>
        private readonly NetworkVariable<float> syncedSpeed = new(
            value: 0f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Owner);

        /// <summary>
        ///  Ángulo de apuntado en grados (0 = horizontal, 90 = arriba).
        ///  Lo escribe el dueño, lo leen todos los clientes para aplicar la
        ///  rotación del weaponHolder. Así todos ven tu bazooka apuntando al mismo sitio.
        /// </summary>
        private readonly NetworkVariable<float> aimAngle = new(
            value: 45f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Owner);

        // ==================================================================
        // CICLO DE VIDA
        // ==================================================================
        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            ResolveVisualReferences();
            speedHash = Animator.StringToHash(speedParam);

            // Auto-resolución del bazooka y la vida si están en la misma rata
            if (bazooka == null) bazooka = GetComponentInChildren<NetworkBazooka>();
            if (health == null) health = GetComponent<NetworkHealth>();

            // Guardamos la pos local por defecto del weaponHolder para poder
            // reflejarla cuando la rata mire al otro lado.
            if (weaponHolder != null)
            {
                weaponHolderDefaultPos = weaponHolder.localPosition;
                weaponHolderPosCached = true;
            }
        }

        /// <summary>
        ///  Resuelve bodySprite y animator. Lo forzamos también en OnNetworkSpawn
        ///  porque a veces las referencias arrastradas en el prefab apuntan al
        ///  asset del source prefab y no a la instancia — y en runtime se quedan
        ///  sin aplicar. Buscarlos por código es más fiable.
        /// </summary>
        private void ResolveVisualReferences()
        {
            // SpriteRenderer "de verdad" de la rata:
            // primero el propio objeto, luego en hijos EXCLUYENDO el WeaponHolder
            if (bodySprite == null) bodySprite = GetComponent<SpriteRenderer>();
            if (bodySprite == null)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // Ignoramos sprites que cuelguen del WeaponHolder (armas equipadas)
                    if (sr.transform.IsChildOf(transform) &&
                        !sr.transform.name.ToLower().Contains("weapon"))
                    {
                        bodySprite = sr;
                        break;
                    }
                }
            }

            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Cacheamos TODOS los sprites que NO cuelguen del weaponHolder.
            // Así el flip del cuerpo no toca a las armas (bazooka, granada, etc.),
            // y el flip del arma se gestiona por separado en ApplyAimRotation.
            var found = GetComponentsInChildren<SpriteRenderer>(true);
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (var sr in found)
            {
                if (weaponHolder != null && sr.transform.IsChildOf(weaponHolder))
                    continue; // ignoramos sprites que cuelguen del weaponHolder
                list.Add(sr);
            }
            allSprites = list.ToArray();

            if (bodySprite == null)
                Debug.LogWarning($"[NetworkPlayerMovement] {name}: bodySprite no encontrado, el flip no se verá.");
        }

        public override void OnNetworkSpawn()
        {
            // Re-resuelve las refs visuales por si el prefab las tiene mal cacheadas
            ResolveVisualReferences();

            // Aplica el flip inicial al spawnear (importante para clientes que
            // entran después y ven al jugador ya girado)
            ApplyFlipVisual(facingRight.Value);
            ApplyAnimationSpeed(syncedSpeed.Value);
            ApplyAimRotation(aimAngle.Value, facingRight.Value);

            // Cuando los valores cambien por red, actualizamos visuales
            facingRight.OnValueChanged += OnFacingChanged;
            syncedSpeed.OnValueChanged += OnSpeedChanged;
            aimAngle.OnValueChanged += OnAimChanged;

            // Cuando esta rata muera EN EL SERVIDOR, sacarla de los turnos
            if (IsServer && health != null)
                health.OnDiedServer += HandleDiedServer;

            // Etiqueta en la jerarquía para depurar
            gameObject.name = IsOwner
                ? $"Rat_{OwnerClientId}_(yo)"
                : $"Rat_{OwnerClientId}";

            // Si SOY yo, que la cámara me siga
            if (IsOwner)
            {
                var cam = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
                if (cam != null) cam.target = transform;
            }
        }

        public override void OnNetworkDespawn()
        {
            facingRight.OnValueChanged -= OnFacingChanged;
            syncedSpeed.OnValueChanged -= OnSpeedChanged;
            aimAngle.OnValueChanged -= OnAimChanged;
            if (health != null) health.OnDiedServer -= HandleDiedServer;
        }

        /// <summary>
        ///  Server-side: cuando esta rata muere, la quitamos del orden de turnos
        ///  para que no se le espere eternamente.
        /// </summary>
        private void HandleDiedServer(NetworkHealth h)
        {
            if (!IsServer) return;
            var tm = NetworkTurnManager.Instance;
            if (tm != null) tm.RemovePlayerServer(OwnerClientId);
        }

        // ==================================================================
        // LOOP
        // ==================================================================
        /// <summary>
        ///  ¿Tengo permiso para controlar la rata ahora mismo?
        ///  - Debo ser el dueño (IsOwner).
        ///  - Si hay TurnManager activo y respectTurns=true, debe ser mi turno.
        /// </summary>
        private bool CanControl()
        {
            if (!IsOwner) return false;
            if (!respectTurns) return true;

            var tm = NetworkTurnManager.Instance;
            if (tm == null) return true; // si no hay manager, jugamos libre
            return tm.IsTurnOf(OwnerClientId);
        }

        private void Update()
        {
            // El timer de "fin de turno tras disparar" corre aunque ya no podamos controlar,
            // para que el servidor efectivamente avance el turno.
            TickEndTurnTimer();

            if (!CanControl())
            {
                // Si pierdo el turno mientras me movía, dejo de empujar y cancelo carga.
                inputX = 0f;
                if (bazooka != null && bazooka.IsCharging) bazooka.CancelCharging();
                return;
            }

            inputX = Input.GetAxisRaw("Horizontal");

            // Salto — ahora SOLO con W para no chocar con ESPACIO (que ahora es disparo)
            bool grounded = groundCheck != null
                && Physics2D.OverlapCircle(groundCheck.position, groundRadius, groundMask);

            if (grounded && Input.GetKeyDown(KeyCode.W))
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            }

            // Flip: actualiza la NetworkVariable → se propaga a todos
            if (inputX > 0.01f && !facingRight.Value) facingRight.Value = true;
            else if (inputX < -0.01f && facingRight.Value) facingRight.Value = false;

            // Apuntado ↑ ↓
            HandleAimInput();

            // Disparo (ESPACIO): carga mientras se mantiene, dispara al soltar
            HandleFireInput();

            // Publica la velocidad para que todos los clientes animen correctamente.
            // Sólo actualizamos si cambia "notablemente" para no saturar la red.
            float currentSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (Mathf.Abs(currentSpeed - syncedSpeed.Value) > 0.1f)
                syncedSpeed.Value = currentSpeed;
        }

        // ==================================================================
        // APUNTADO
        // ==================================================================
        private void HandleAimInput()
        {
            float v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(v) < 0.1f) return;

            float next = aimAngle.Value + v * aimSpeed * Time.deltaTime;
            // Limito a un rango cómodo para jugar (0° horizontal adelante, 90° arriba, 180° atrás)
            next = Mathf.Clamp(next, 0f, 180f);
            aimAngle.Value = next;
        }

        private void OnAimChanged(float oldValue, float newValue)
        {
            ApplyAimRotation(newValue, facingRight.Value);
        }

        private void ApplyAimRotation(float angle, bool right)
        {
            if (weaponHolder == null) return;

            // Rotación Z pura: ángulo de apuntado.
            // (Antes usábamos también Y=180 al mirar a la izquierda, pero eso
            //  al combinarse con la Z dejaba el apuntado al revés — ponías 90°
            //  pensando "arriba" y el motor calculaba "abajo". Ya no.)
            weaponHolder.localEulerAngles = new Vector3(0f, 0f, angle);

            // Flip horizontal REAL con escala negativa en X.
            // Esto refleja el weaponHolder y TODOS sus hijos (bazooka visual,
            // firepoint, trayectoria) como un espejo. Así el arma apunta hacia
            // la misma dirección que la rata y el ángulo de apuntado responde
            // bien en ambos lados.
            Vector3 scale = weaponHolder.localScale;
            float abs = Mathf.Abs(scale.x);
            scale.x = right ? abs : -abs;
            weaponHolder.localScale = scale;

            // Reflejamos la posición local en X por si el pivote del
            // weaponHolder no está justo en el centro de la rata.
            if (weaponHolderPosCached)
            {
                var pos = weaponHolderDefaultPos;
                pos.x *= right ? 1f : -1f;
                weaponHolder.localPosition = pos;
            }
        }

        // ==================================================================
        // DISPARO
        // ==================================================================
        private void HandleFireInput()
        {
            if (bazooka == null) return;

            if (Input.GetKeyDown(KeyCode.Space))
                bazooka.StartCharging();

            if (bazooka.IsCharging)
            {
                Vector2 dir = weaponHolder != null ? (Vector2)weaponHolder.right : Vector2.right;
                bazooka.TickCharging(Time.deltaTime, dir);
            }

            if (Input.GetKeyUp(KeyCode.Space) && bazooka.IsCharging)
            {
                Vector2 dir = weaponHolder != null ? (Vector2)weaponHolder.right : Vector2.right;
                bool fired = bazooka.ReleaseShot(dir);
                if (fired) ScheduleEndTurn();
            }
        }

        private void ScheduleEndTurn()
        {
            endTurnTimer = endTurnDelay;
        }

        private void TickEndTurnTimer()
        {
            if (endTurnTimer < 0f) return;
            endTurnTimer -= Time.deltaTime;
            if (endTurnTimer <= 0f)
            {
                endTurnTimer = -1f;
                var tm = NetworkTurnManager.Instance;
                if (tm != null && IsOwner) tm.EndTurnServerRpc();
            }
        }

        private void FixedUpdate()
        {
            if (!CanControl()) return;

            if (Mathf.Abs(inputX) > 0.1f)
                rb.AddForce(Vector2.right * inputX * moveForce);

            if (Mathf.Abs(rb.linearVelocity.x) > maxSpeed)
                rb.linearVelocity = new Vector2(Mathf.Sign(rb.linearVelocity.x) * maxSpeed,
                                                rb.linearVelocity.y);

            rb.linearDamping = linearDrag;
        }

        /// <summary>
        ///  LateUpdate se ejecuta DESPUÉS del Animator. Reaplicamos el flip y
        ///  el ángulo de apuntado aquí para que no los pise ninguna animación.
        /// </summary>
        private void LateUpdate()
        {
            ApplyFlipVisual(facingRight.Value);
            ApplyAimRotation(aimAngle.Value, facingRight.Value);
        }

        // ==================================================================
        // FLIP
        // ==================================================================
        private void OnFacingChanged(bool oldValue, bool newValue)
        {
            ApplyFlipVisual(newValue);
        }

        private void ApplyFlipVisual(bool right)
        {
            // Voltamos la rata cambiando el signo del scale.x del transform del sprite.
            // Hemos comprobado con logs que es este método el que realmente se aplica
            // al SpriteRenderer visible (no el flipX), así que usamos SOLO este para
            // no cancelarnos a nosotros mismos.
            if (allSprites != null)
            {
                foreach (var sr in allSprites)
                {
                    if (sr == null) continue;

                    // Aseguramos flipX = false para que no interfiera
                    sr.flipX = false;

                    Transform t = sr.transform;
                    Vector3 scale = t.localScale;
                    float abs = Mathf.Abs(scale.x);
                    scale.x = right ? abs : -abs;
                    t.localScale = scale;
                }
            }
        }

        // ==================================================================
        // ANIMACIÓN
        // ==================================================================
        private void OnSpeedChanged(float oldValue, float newValue)
        {
            ApplyAnimationSpeed(newValue);
        }

        private void ApplyAnimationSpeed(float speed)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat(speedHash, speed);
        }

        // ==================================================================
        // GIZMOS (útil para ver el groundCheck en el Editor)
        // ==================================================================
        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
            }
        }
    }
}
