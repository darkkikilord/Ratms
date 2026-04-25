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
        [Tooltip("Velocidad tope al andar (unidades/s).")]
        [SerializeField] private float maxSpeed = 6f;

        [Tooltip("Cuánto tarda en acelerar desde 0 hasta maxSpeed al pulsar A/D (unidades/s²). 40 = llega a 6 u/s en 0.15s — se nota responsivo pero no brusco.")]
        [SerializeField] private float moveAcceleration = 40f;

        [Tooltip("Cuánto tarda en frenar al soltar las teclas (unidades/s²). Ligeramente mayor que la aceleración para que la rata se detenga rápido pero no clavada.")]
        [SerializeField] private float moveDeceleration = 55f;

        // Campos viejos mantenidos por compatibilidad; ya no se usan en la
        // lógica nueva, pero evitan que el Inspector marque valores perdidos
        // si alguien los tenía editados.
        [HideInInspector, SerializeField] private float moveForce = 20f;
        [HideInInspector, SerializeField] private float linearDrag = 4f;

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

        [Tooltip("Referencia al NetworkGrenadeThrower en la propia rata (se auto-resuelve si está vacío).")]
        [SerializeField] private NetworkGrenadeThrower grenadeThrower;

        [Tooltip("Menú de armas de la propia rata (se auto-resuelve). Sirve para saber qué arma está equipada y disparar la correcta.")]
        [SerializeField] private WeaponsMenuUI weaponsMenu;

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

        // Cooldown para evitar saltos dobles: tras saltar, no se puede volver
        // a saltar hasta que pasa un poquito Y se vuelva a tocar suelo de verdad.
        [Header("Salto — afinado")]
        [Tooltip("Segundos mínimos entre dos saltos consecutivos, por si el groundCheck detecta suelo durante un frame del salto inicial.")]
        [SerializeField] private float jumpCooldown = 0.25f;

        [Tooltip("Colliders con estos tags NO cuentan como 'suelo' aunque estén en la groundMask. Úsalo si la rata tiene algún collider hijo que se superpone al groundCheck.")]
        [SerializeField] private string[] groundCheckIgnoreTags = new string[0];

        [Tooltip("Coyote time: segundos de gracia DESPUÉS de dejar el suelo durante los que aún se puede saltar. Soluciona el bug clásico de pulsar W justo cuando empiezas a caer del borde de un saliente y no salta.")]
        [SerializeField] private float coyoteTime = 0.10f;

        [Tooltip("Jump buffer: si pulsas W/Up hasta este número de segundos ANTES de tocar el suelo, el salto se ejecuta automáticamente al aterrizar. Da una sensación de salto muy responsiva.")]
        [SerializeField] private float jumpBufferTime = 0.10f;

        [Tooltip("Si la velocidad vertical de la rata supera este valor (positivo = subiendo), no se considera 'en el suelo'. Antes era 0.5 — demasiado bajo, los rebotes diminutos del collider procedural anulaban el groundcheck. 2 perdona los micro-rebotes pero detecta de verdad un salto en marcha.")]
        [SerializeField] private float groundCheckMaxRiseVy = 2f;

        [Tooltip("Si está ON, escupe en consola un Debug.Log cada vez que IsGrounded() detecta o pierde suelo, y otro al pulsar saltar con todas las condiciones. PUESTO ON POR DEFECTO mientras afinamos el salto — quítalo cuando funcione.")]
        [SerializeField] private bool debugGroundCheck = true;

        [Tooltip("Distancia (unidades de mundo) hacia abajo del raycast de respaldo del groundcheck. " +
                 "Es independiente del groundMask: detecta CUALQUIER collider que no sea de la rata. " +
                 "Sirve de fallback cuando el groundCheck Transform no está bien colocado o el groundMask está mal configurado.")]
        [SerializeField] private float groundRaycastDistance = 0.40f;

        private float nextJumpAllowedTime = -1f;
        // Buffer reutilizable para el OverlapCircleNonAlloc (evita allocs por frame).
        private readonly Collider2D[] groundHits = new Collider2D[8];
        private readonly RaycastHit2D[] groundRayHits = new RaycastHit2D[8];

        // Estado interno del coyote/buffer.
        private float lastGroundedTime = -10f; // último instante en el que SÍ había suelo
        private float jumpRequestedTime = -10f; // último instante en el que se pulsó saltar
        private bool wasGrounded; // estado anterior, solo para el log de debug

        // Ventana de "vuelo libre" tras un empujón de explosión: durante este
        // tiempo ignoramos linearDamping y maxSpeed para que la rata vuele
        // bonito en vez de frenarse en medio segundo.
        private float knockbackFlightUntil = -1f;
        private bool InKnockbackFlight => Time.time < knockbackFlightUntil;

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
        ///  Ángulo de apuntado en grados, RELATIVO al lado al que mira la rata:
        ///      0°  = recto hacia adelante (en la dirección que mira la rata)
        ///     +90° = recto arriba
        ///     -90° = recto abajo
        ///  Lo escribe el dueño, lo leen todos los clientes para aplicar la
        ///  rotación del weaponHolder. Así todos ven tu bazooka apuntando al mismo sitio.
        ///
        ///  El ángulo se guarda SIEMPRE en el sistema local del weaponHolder —
        ///  el flip del lado lo hace ApplyFlipVisual invirtiendo la escala local
        ///  del weaponHolder. Con ese truco, "0° adelante" apunta a la derecha
        ///  cuando facingRight=true y a la izquierda cuando facingRight=false,
        ///  sin tocar la variable.
        /// </summary>
        private readonly NetworkVariable<float> aimAngle = new(
            value: 0f,
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

            // Auto-resolución de referencias en la propia rata
            if (bazooka == null) bazooka = GetComponentInChildren<NetworkBazooka>();
            if (grenadeThrower == null) grenadeThrower = GetComponentInChildren<NetworkGrenadeThrower>();
            if (weaponsMenu == null) weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
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
            // primero el propio objeto, luego en hijos EXCLUYENDO el WeaponHolder.
            //
            // OJO: si el prefab tiene bodySprite arrastrado desde el source-prefab
            // externo, esa referencia puede apuntar a un SpriteRenderer que NO
            // pertenece a esta instancia (no es hijo de este transform). Entonces
            // no se renderiza nada cuando lo flipeamos. Lo detectamos y descartamos.
            if (bodySprite != null &&
                bodySprite.transform != transform &&
                !bodySprite.transform.IsChildOf(transform))
            {
                bodySprite = null;
            }

            if (bodySprite == null) bodySprite = GetComponent<SpriteRenderer>();
            if (bodySprite == null)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // Ignoramos sprites que cuelguen del WeaponHolder (armas equipadas)
                    if (weaponHolder != null && sr.transform.IsChildOf(weaponHolder))
                        continue;
                    if (sr.transform.IsChildOf(transform))
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

            // Si SOY yo, que la cámara me siga (y recuerde a quién volver
            // tras seguir un proyectil o tras una pausa de impacto).
            if (IsOwner)
            {
                var cam = CameraFollow.Instance != null
                    ? CameraFollow.Instance
                    : (Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null);
                if (cam != null) cam.SetTarget(transform);
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
                // Si pierdo el turno mientras me movía, dejo de empujar y cancelo
                // cualquier carga en curso (bazooka o granada).
                inputX = 0f;
                if (bazooka != null && bazooka.IsCharging) bazooka.CancelCharging();
                if (grenadeThrower != null && grenadeThrower.IsCharging) grenadeThrower.CancelCharging();
                return;
            }

            inputX = Input.GetAxisRaw("Horizontal");

            // Salto — acepta W o la flecha arriba (lo que cada quien prefiera).
            // ESPACIO se queda reservado para disparar.
            //
            // Lógica con COYOTE + BUFFER (estándar plataformas modernas):
            //
            //   · Cada frame en el suelo, recordamos lastGroundedTime = ahora.
            //     Estamos "permitidos a saltar" hasta ahora + coyoteTime, aunque
            //     ya no toquemos suelo. Esto cubre dos casos:
            //       (a) Saltas justo cuando caes del borde de una colina.
            //       (b) El collider procedural rebota un milímetro: aunque
            //           IsGrounded devuelva false un frame, lo perdonamos.
            //
            //   · Cuando pulsas W/Up, anotamos jumpRequestedTime = ahora.
            //     El "jump" salta si jumpRequestedTime está dentro de
            //     jumpBufferTime → es decir, si pulsaste hace poco. Cubre:
            //       (a) Pulsas un pelín antes de aterrizar.
            //       (b) Frame raro donde el groundCheck no corre.
            //
            //   · El jumpCooldown sigue ahí para que dos pulsaciones muy
            //     seguidas (incluso por chattering del teclado) no doblen.
            bool grounded = IsGrounded();
            if (grounded) lastGroundedTime = Time.time;

            if (debugGroundCheck && grounded != wasGrounded)
            {
                Debug.Log($"[NetworkPlayerMovement] grounded={grounded} vy={rb.linearVelocity.y:F2}");
                wasGrounded = grounded;
            }

            bool jumpKeyDown = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            if (jumpKeyDown) jumpRequestedTime = Time.time;

            bool canCoyoteJump = (Time.time - lastGroundedTime) <= coyoteTime;
            bool jumpBuffered  = (Time.time - jumpRequestedTime) <= jumpBufferTime;
            bool cooldownOk    = Time.time >= nextJumpAllowedTime;

            // Log diagnóstico al pulsar saltar — así sabemos exactamente
            // qué condición falla si la rata no salta.
            if (jumpKeyDown && debugGroundCheck)
            {
                Debug.Log($"[Salto] pressed | grounded={grounded} vy={rb.linearVelocity.y:F2} " +
                          $"| coyoteOk={canCoyoteJump} bufferOk={jumpBuffered} cooldownOk={cooldownOk} " +
                          $"| groundCheck={(groundCheck != null ? groundCheck.name : "NULL")} " +
                          $"| groundMask={(int)groundMask.value}");
            }

            if (canCoyoteJump && jumpBuffered && cooldownOk)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                nextJumpAllowedTime = Time.time + jumpCooldown;
                // "Consumimos" la petición y el coyote para que no se ejecuten
                // dos saltos seguidos por el mismo input ni por el mismo
                // contacto de suelo.
                jumpRequestedTime = -10f;
                lastGroundedTime  = -10f;

                if (debugGroundCheck) Debug.Log("[Salto] EJECUTADO ✓");
            }

            // Flip: actualiza la NetworkVariable → se propaga a todos
            if (inputX > 0.01f && !facingRight.Value) facingRight.Value = true;
            else if (inputX < -0.01f && facingRight.Value) facingRight.Value = false;

            // Apuntado ↑ ↓
            HandleAimInput();

            // Disparo (ESPACIO): carga mientras se mantiene, dispara al soltar
            HandleFireInput();

            // Publica la velocidad para que todos los clientes animen correctamente.
            // Sólo actualizamos si cambia "notablemente" para no saturar la red
            // (la NetworkVariable lanzaría un mensaje en cada frame con 0.001
            // de diferencia, cosa que no aporta nada y satura ancho de banda).
            float currentSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (Mathf.Abs(currentSpeed - syncedSpeed.Value) > 0.1f)
                syncedSpeed.Value = currentSpeed;

            // EN EL DUEÑO: alimentamos el Animator local con la velocidad real
            // CADA frame, sin esperar al callback de la NetworkVariable. Así la
            // transición idle → corriendo / agachada se nota instantánea al
            // empezar a andar (antes había un lag hasta que el umbral de 0.1
            // hacía saltar el mensaje de red y volvía a nosotros).
            ApplyAnimationSpeed(currentSpeed);
        }

        // ==================================================================
        // APUNTADO
        // ==================================================================
        private void HandleAimInput()
        {
            float v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(v) < 0.1f) return;

            // Rango SEMIcírculo del lado al que mira la rata:
            //    -90° = recto abajo
            //      0° = horizontal hacia adelante (en el lado al que mira la rata)
            //    +90° = recto arriba
            // Así NUNCA puede apuntar hacia atrás (y por tanto no se autodaña).
            // Para disparar al otro lado, la rata tiene que girarse moviéndose.
            float next = aimAngle.Value + v * aimSpeed * Time.deltaTime;
            next = Mathf.Clamp(next, -90f, 90f);
            aimAngle.Value = next;
        }

        private void OnAimChanged(float oldValue, float newValue)
        {
            ApplyAimRotation(newValue, facingRight.Value);
        }

        private void ApplyAimRotation(float angle, bool right)
        {
            if (weaponHolder == null) return;

            // El flip de la rata (mirar izquierda) se hace poniendo scale.x=-1
            // en el weaponHolder. Ese flip INVIERTE el eje X local, por lo que
            // la rotación Z acaba invertida visualmente:
            //   · scale.x=+1, Z=+90° → arma apunta arriba (✓ lo esperado).
            //   · scale.x=-1, Z=+90° → arma apunta ABAJO (✗ controles invertidos).
            // Para que ↑ y ↓ se sientan igual en ambos lados, cuando la rata
            // mira a la izquierda APLICAMOS el ángulo negado. Así se cancela
            // el efecto del scale negativo y arriba sigue siendo arriba.
            //
            // Ojo: la variable `aimAngle` sigue siendo la misma en red — la
            // corrección se hace solo al pintar. Si no lo hiciéramos así, al
            // girarte darías tumbos raros en el ángulo sincronizado.
            float applied = right ? angle : -angle;
            weaponHolder.localEulerAngles = new Vector3(0f, 0f, applied);
        }

        // ==================================================================
        // DISPARO
        // ==================================================================
        /// <summary>
        ///  ¿Qué arma está equipada AHORA? Se lee del WeaponsMenuUI sincronizado.
        ///  Si no hay menú o no se puede leer, asumimos bazooka como fallback.
        /// </summary>
        private WeaponsMenuUI.WeaponId CurrentWeapon()
        {
            if (weaponsMenu == null) return WeaponsMenuUI.WeaponId.Bazooka;
            return weaponsMenu.SelectedWeapon;
        }

        /// <summary>
        ///  Enruta el input de carga/disparo al arma correcta según el menú.
        ///  Si tienes bazooka equipada, carga/dispara el bazooka. Si tienes
        ///  granada, carga/dispara el GrenadeThrower. Si no tienes arma
        ///  equipada, ESPACIO no hace nada.
        /// </summary>
        private void HandleFireInput()
        {
            WeaponsMenuUI.WeaponId equipped = CurrentWeapon();

            // === START: al pulsar ESPACIO, arrancamos la carga del arma correspondiente ===
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (equipped == WeaponsMenuUI.WeaponId.Bazooka && bazooka != null)
                    bazooka.StartCharging();
                else if (equipped == WeaponsMenuUI.WeaponId.Grenade && grenadeThrower != null)
                    grenadeThrower.StartCharging();
            }

            // === TICK: mientras se mantiene pulsado, sube la fuerza ===
            Vector2 dir = GetWeaponForward();

            if (bazooka != null && bazooka.IsCharging)
                bazooka.TickCharging(Time.deltaTime, dir);
            if (grenadeThrower != null && grenadeThrower.IsCharging)
                grenadeThrower.TickCharging(Time.deltaTime, dir);

            // === RELEASE: al soltar ESPACIO, disparamos ===
            if (Input.GetKeyUp(KeyCode.Space))
            {
                bool fired = false;
                if (bazooka != null && bazooka.IsCharging)
                    fired = bazooka.ReleaseShot(dir);
                if (!fired && grenadeThrower != null && grenadeThrower.IsCharging)
                    fired = grenadeThrower.ReleaseShot(dir);

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
            if (rb == null) return;

            // Durante la ventana de "vuelo libre" tras una explosión, quitamos
            // la fricción y NO clampamos la velocidad. Así el empujón se siente
            // de verdad en vez de frenarse en medio segundo.
            if (InKnockbackFlight)
            {
                rb.linearDamping = 0f;
                return;
            }

            // Gestionamos la fricción horizontal manualmente con MoveTowards —
            // así NO queremos linearDamping, o iría en contra y se notaría
            // "lento/pegajoso". Lo dejamos a 0 siempre que la rata controla.
            rb.linearDamping = 0f;

            if (!CanControl())
            {
                // No controlamos → no tocamos la velocidad. Dejamos que la
                // física (gravedad, colisiones) haga lo suyo.
                return;
            }

            // ----------------------------------------------------------------
            // MOVIMIENTO SMOOTH:
            // En vez de AddForce + clamp duro (brusco al arrancar/parar),
            // usamos Mathf.MoveTowards para interpolar la velocidad horizontal
            // hasta la velocidad objetivo. La aceleración y deceleración son
            // configurables desde el Inspector:
            //   · A/D pulsados → objetivo = ±maxSpeed, interpolamos con
            //     moveAcceleration (u/s²).
            //   · A/D soltados → objetivo = 0, interpolamos con
            //     moveDeceleration (u/s²), un pelín más alto para que no
            //     patine.
            // Este enfoque da un arranque y frenado lineales suaves, se
            // siente mucho más "jugoso" que el impulso+drag.
            // ----------------------------------------------------------------
            float targetVx = inputX * maxSpeed;
            float accel = Mathf.Abs(targetVx) > 0.01f ? moveAcceleration : moveDeceleration;
            float newVx = Mathf.MoveTowards(rb.linearVelocity.x, targetVx, accel * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
        }

        /// <summary>
        ///  Lo llama <see cref="NetworkHealth.ApplyKnockbackClientRpc"/> justo
        ///  después de aplicar el impulso. Activa una ventana en la que la rata
        ///  "vuela libre" (sin fricción aérea ni tope de velocidad).
        /// </summary>
        public void BeginKnockbackFlight(float seconds)
        {
            knockbackFlightUntil = Time.time + Mathf.Max(0.05f, seconds);
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
            // El lado ha cambiado → el signo visual del ángulo también, así que
            // repintamos el apuntado inmediatamente para no esperar al LateUpdate.
            ApplyAimRotation(aimAngle.Value, newValue);
        }

        private void ApplyFlipVisual(bool right)
        {
            // Estrategia: root SIEMPRE con escala positiva (no pisamos la escala
            // que NetworkTransform sincroniza, y evitamos efectos raros en
            // Rigidbody2D/Colliders). En su lugar:
            //   1) El cuerpo se flipea con bodySprite.flipX.
            //   2) El weaponHolder se flipea con su PROPIA escala local, así
            //      el lanzamisiles mira al mismo lado que la rata.
            //   3) La posición local del weaponHolder se refleja en X, para
            //      que el arma no aparezca "cruzando" el cuerpo al girarse.
            Vector3 rs = transform.localScale;
            rs.x = Mathf.Abs(rs.x);
            transform.localScale = rs;

            // 1) Cuerpo: flipX en TODOS los sprites que NO cuelgan del weaponHolder.
            //    Así no depende de que la referencia "bodySprite" del inspector
            //    apunte al sprite correcto (a veces queda rota al heredar del
            //    source prefab).
            if (allSprites != null)
            {
                foreach (var sr in allSprites)
                    if (sr != null) sr.flipX = !right;
            }

            // Seguridad: también el bodySprite explícito, por si apunta a uno
            // no incluido en allSprites (p.ej. si quedó fuera del filtro).
            if (bodySprite != null)
                bodySprite.flipX = !right;

            // 2) Weapon holder: escala local en X según lado
            if (weaponHolder != null)
            {
                Vector3 ws = weaponHolder.localScale;
                float absX = Mathf.Abs(ws.x);
                ws.x = right ? absX : -absX;
                weaponHolder.localScale = ws;

                // 3) Posición local reflejada para que no quede "detrás" del cuerpo
                if (weaponHolderPosCached)
                {
                    Vector3 p = weaponHolderDefaultPos;
                    p.x = right ? Mathf.Abs(p.x) : -Mathf.Abs(p.x);
                    weaponHolder.localPosition = p;
                }
            }
        }

        // ==================================================================
        // DIRECCIÓN DE DISPARO / TRAYECTORIA
        // ==================================================================
        /// <summary>
        ///  Dirección "forward" del weaponHolder en world space, TENIENDO EN
        ///  CUENTA la escala local negativa (flip a la izquierda).
        ///  <see cref="Transform.right"/> ignora la escala — solo usa la
        ///  rotación — así que tras flipear scale.x=-1 devolvería el vector
        ///  hacia la derecha, incorrecto. Con localToWorldMatrix sí aplicamos
        ///  rotación + escala y obtenemos el vector que realmente apunta hacia
        ///  el frente del arma flipeada.
        /// </summary>
        private Vector2 GetWeaponForward()
        {
            if (weaponHolder == null) return Vector2.right;
            Vector3 v = weaponHolder.localToWorldMatrix.MultiplyVector(Vector3.right);
            Vector2 r = new Vector2(v.x, v.y);
            if (r.sqrMagnitude < 0.0001f) return Vector2.right;
            return r.normalized;
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
        // GROUND CHECK
        // ==================================================================
        /// <summary>
        ///  ¿La rata está pegada a algo que cuenta como suelo?
        ///  Ver comentarios en Update() para la lógica completa.
        /// </summary>
        /// <summary>
        ///  ¿La rata está pegada a algo que cuenta como suelo?
        ///
        ///  Implementación a prueba de balas con DOS estrategias en cascada:
        ///   1) OverlapCircle desde groundCheck (preferido, lo que había antes).
        ///   2) Raycast hacia abajo desde la base del propio collider de la rata
        ///      (FALLBACK). No depende de groundMask ni del groundCheck Transform.
        ///      Acepta cualquier collider que no cuelgue de la rata.
        ///
        ///  Razón del fallback: si el groundMask no incluye la layer del
        ///  terreno procedural, o el groundCheck Transform no está colocado
        ///  exactamente en los pies, el OverlapCircle devuelve "nada" y la
        ///  rata cree que está volando. El raycast es independiente y
        ///  rescata el detect.
        /// </summary>
        private bool IsGrounded()
        {
            // Si va subiendo claramente, no es suelo (anti salto infinito).
            if (rb != null && rb.linearVelocity.y > groundCheckMaxRiseVy) return false;

            // ---------- Estrategia 1: OverlapCircle desde groundCheck ----------
            // Auto-resolución por si el prefab no tiene el groundCheck arrastrado.
            if (groundCheck == null)
            {
                var found = transform.Find("groundcheck");
                if (found == null) found = transform.Find("GroundCheck");
                if (found == null) found = transform.Find("groundCheck");
                if (found != null) groundCheck = found;
            }

            if (groundCheck != null)
            {
                // BULLETPROOF: ignoramos el groundMask del Inspector y miramos
                // TODAS las layers. El filtro de self más abajo descarta los
                // colliders de la propia rata. Así da igual en qué layer esté
                // el terreno procedural — siempre lo detectamos.
                int maskValue = ~0;

                int count = Physics2D.OverlapCircleNonAlloc(
                    groundCheck.position, groundRadius, groundHits, maskValue);

                for (int i = 0; i < count; i++)
                {
                    var c = groundHits[i];
                    if (c == null) continue;
                    if (c.transform == transform) continue;
                    if (c.transform.IsChildOf(transform)) continue;
                    if (groundCheckIgnoreTags != null && groundCheckIgnoreTags.Length > 0)
                    {
                        bool ignored = false;
                        foreach (var tag in groundCheckIgnoreTags)
                            if (!string.IsNullOrEmpty(tag) && c.CompareTag(tag)) { ignored = true; break; }
                        if (ignored) continue;
                    }
                    return true;
                }
            }

            // ---------- Estrategia 2 (FALLBACK): raycast hacia abajo ----------
            // Salimos desde justo por encima de la base del collider de la rata
            // (o de su transform si no tiene). Cualquier collider que no sea
            // suyo cuenta como suelo. No usamos máscara: queremos máxima fiabilidad.
            Vector2 origin;
            float startInside = 0.05f; // 5 cm por encima de la base, para no salir DENTRO del suelo si está hundida
            var selfCol = GetComponent<Collider2D>();
            if (selfCol != null)
            {
                Bounds b = selfCol.bounds;
                origin = new Vector2(b.center.x, b.min.y + startInside);
            }
            else
            {
                origin = (Vector2)transform.position + Vector2.up * startInside;
            }

            int rayCount = Physics2D.RaycastNonAlloc(origin, Vector2.down, groundRayHits, groundRaycastDistance);
            for (int i = 0; i < rayCount; i++)
            {
                var c = groundRayHits[i].collider;
                if (c == null) continue;
                if (c.transform == transform) continue;
                if (c.transform.IsChildOf(transform)) continue;
                return true;
            }

            return false;
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
