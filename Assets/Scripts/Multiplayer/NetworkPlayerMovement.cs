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
        [SerializeField] private float maxSpeed = 2f;

        [Tooltip("Cuánto tarda en acelerar desde 0 hasta maxSpeed al pulsar A/D (unidades/s²). " +
                 "Valores bajos = arranque más suave/gradual (menos brusco). 18 = arranque smooth estilo Worms.")]
        [SerializeField] private float moveAcceleration = 18f;

        [Tooltip("Cuánto tarda en frenar al soltar las teclas (unidades/s²). " +
                 "Ligeramente mayor que la aceleración para que la rata se detenga rápido pero no clavada.")]
        [SerializeField] private float moveDeceleration = 22f;

        // Campos viejos mantenidos por compatibilidad; ya no se usan en la
        // lógica nueva, pero evitan que el Inspector marque valores perdidos
        // si alguien los tenía editados. Suprimimos el warning CS0414 (asignado
        // pero nunca leído) — es intencional.
#pragma warning disable CS0414
        [HideInInspector, SerializeField] private float moveForce = 20f;
        [HideInInspector, SerializeField] private float linearDrag = 4f;
#pragma warning restore CS0414

        [Header("Salto")]
        [Tooltip("Velocidad inicial vertical del salto (unidades/seg). Tunear según tamaño del personaje:\n" +
                 "  · Rata pequeña: 3.5-5 (lo que tenemos ahora)\n" +
                 "  · Personaje humano normal: 8-10\n" +
                 "  · Salto Mario-style alto: 12-15\n" +
                 "Recomendado bajarlo si los saltos parecen poco realistas para el tamaño del personaje.")]
        [SerializeField] private float jumpForce = 2f;

        [Header("Asistencia de cuestas")]
        [Tooltip("Radio de redondeo de las esquinas del BoxCollider2D de la rata. Se aplica EN RUNTIME — no toca el prefab. Las esquinas redondeadas evitan que la rata se enganche en cada bumpcito del polígono procedural y la deja subir cuestas espinadas con suavidad. Valores típicos: 0.04-0.10. Si lo subes mucho, la rata 'flota' un pelín sobre el suelo. Si lo bajas a 0, recuperas el comportamiento original (con enganches).")]
        [SerializeField] private float colliderEdgeRadius = 0.06f;

        [Tooltip("Cuánto BAJAMOS el collider (y el groundCheck) para que su fondo coincida con las PATAS del dibujo (u). " +
                 "Medido con la prueba automática: el dibujo de la rata quedaba ~0.19 u por debajo del collider, así que las " +
                 "patas se metían en la hierba y la rata parecía hundida. Se aplica en runtime; el prefab no se toca. 0 = sin ajuste.")]
        [SerializeField] private float colliderDropToFeet = 0.19f;
        private bool feetAligned;

        [Tooltip("Altura EXTRA (u) que se añade al collider hacia ARRIBA (los pies no se mueven). El dibujo de la rata mide " +
                 "~0.8 u y el collider solo 0.25 u: sin esto, un misil podía atravesar la cabeza sin darle. 0 = sin cambio.")]
        [SerializeField] private float colliderExtraHeight = 0.22f;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundRadius = 0.15f;
        [SerializeField] private LayerMask groundMask;

        [Header("Turnos")]
        [Tooltip("Si está ON, la rata solo responde a input durante SU turno. Desactívalo para modo libre.")]
        [SerializeField] private bool respectTurns = true;

        [Header("Visual")]
        [Tooltip("Si ON, la rata se dibuja con RatAppearance (pixel-art generado por código, contorno, casco del color del equipo, " +
                 "animaciones propias) en vez del sprite .ase antiguo. Desmárcalo para volver a la rata antigua.")]
        [SerializeField] private bool useProceduralRat = true;

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

        [Tooltip("Bate (cuerpo a cuerpo) de la propia rata (se auto-resuelve).")]
        [SerializeField] private NetworkBat bat;

        // Armas/herramientas añadidas por RatmsPrefabSetup (se auto-resuelven).
        private NetworkShotgun shotgun;
        private NetworkTeleport teleport;
        private NetworkNinjaRope rope;
        private int shotsThisTurn;

        [Tooltip("Menú de armas de la propia rata (se auto-resuelve). Sirve para saber qué arma está equipada y disparar la correcta.")]
        [SerializeField] private WeaponsMenuUI weaponsMenu;

        [Tooltip("Mano de la rata que sujeta el arma. Rota con el ángulo de apuntado.")]
        [SerializeField] private Transform weaponHolder;

        [Tooltip("Velocidad de apuntado (grados por segundo).")]
        [SerializeField] private float aimSpeed = 200f;

        [Tooltip("Segundos MÁXIMOS que esperamos entre el disparo y pasar el turno. Es un fallback: si el misil/granada explota antes, el propio proyectil llama a ShortenEndTurnClientRpc() y acorta este timer a ~1s para que el receptor no pueda huir mientras el proyectil vuela hacia él.")]
        [SerializeField] private float endTurnDelay = 3.5f;

        [Header("Feel del salto")]
        [Tooltip("Multiplicador de gravedad cuando la rata está CAYENDO (velocidad Y negativa). " +
                 "1 = sin cambio. 1.5-2 = cae más rápido que sube → salto se siente con más peso y menos 'flotante'. " +
                 "Técnica clásica de platformer para un salto smooth y satisfactorio.")]
        [SerializeField] private float fallGravityMultiplier = 1.8f;

        [Tooltip("Multiplicador de gravedad cuando la rata está SUBIENDO y sueltas la flecha Up antes de " +
                 "llegar arriba (short jump). Permite saltos cortos o largos según cuánto mantengas la tecla. " +
                 "1 = sin efecto. 2 = corta el salto rápido cuando sueltas.")]
        [SerializeField] private float lowJumpMultiplier = 1.6f;

        [Header("Daño por caída")]
        [Tooltip("Altura mínima (unidades) para empezar a recibir daño al aterrizar. " +
                 "Un salto normal es ~3 unidades, así que 5 es un margen seguro para no dañar por saltar.")]
        [SerializeField] private float fallDamageThreshold = 5f;

        [Tooltip("Puntos de daño por unidad de caída por encima del threshold. " +
                 "Ej: threshold=5, damagePerUnit=6 → caer desde 10u = (10-5)*6 = 30 daño.")]
        [SerializeField] private float fallDamagePerUnit = 6f;

        [Tooltip("Daño MÁXIMO por caída de una sola vez. Sin límite = caídas letales instantáneas al vacío.")]
        [SerializeField] private int fallDamageMax = 80;

        [Tooltip("Si está en KNOCKBACK activo (explosión te tira volando), el peakY solo se cuenta " +
                 "desde el peak REAL alcanzado — así explosión + gran caída = mucho daño.")]
#pragma warning disable CS0414   // reservado: hoy el comportamiento ya es siempre este
        [SerializeField] private bool trackFallDuringKnockback = true;
#pragma warning restore CS0414

        // --- Privadas ---
        private Rigidbody2D rb;
        private float inputX;
        private int speedHash;
        private SpriteRenderer[] allSprites;
        private Vector3 weaponHolderDefaultPos;
        private bool weaponHolderPosCached;
        private float endTurnTimer = -1f;

        // Tracking de caída para daño
        private float peakYSinceLastGround = float.MinValue;
        private bool wasGroundedLastFrame = true;

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

        [Tooltip("Si está ON, escupe en consola un Debug.Log cada vez que IsGrounded() detecta o pierde suelo, y otro al pulsar saltar con todas las condiciones. Solo para depurar el salto: llena la consola y cuesta rendimiento.")]
        [SerializeField] private bool debugGroundCheck = false;

        [Tooltip("Distancia (unidades de mundo) hacia abajo del raycast de respaldo del groundcheck. " +
                 "Es independiente del groundMask: detecta CUALQUIER collider que no sea de la rata. " +
                 "Sirve de fallback cuando el groundCheck Transform no está bien colocado o el groundMask está mal configurado.")]
        [SerializeField] private float groundRaycastDistance = 0.40f;

        [Header("Quieta en cuestas")]
        [Tooltip("Si ON, cuando la rata está quieta y apoyada en el suelo (sin pulsar nada) se 'aparca': " +
                 "se anula su gravedad para que NO resbale cuesta abajo. Se desactiva sola en cuanto anda, " +
                 "salta, pierde el suelo o recibe un empujón.")]
        [SerializeField] private bool holdOnSlopes = true;

        [Tooltip("Velocidad por debajo de la cual se considera que la rata está 'quieta' y se puede aparcar (u/s).")]
        [SerializeField] private float parkMaxSpeed = 0.5f;

        [Tooltip("Inclinación mínima de la normal del contacto para contar como 'suelo' (0 = pared vertical, 1 = suelo plano).")]
        [SerializeField] private float footNormalMinY = 0.45f;

        [Header("Subir escalones")]
        [Tooltip("Si ON, al andar contra un bordillo bajo (más bajo que stepHeight) la rata lo sube sola con un saltito, " +
                 "en vez de quedarse empujando contra él. Ayuda mucho a salir de cráteres y a moverse por terreno roto.")]
        [SerializeField] private bool stepAssist = true;

        [Tooltip("Altura máxima (u) de escalón que se sube sola. La rata mide ~0.25 u.")]
        [SerializeField] private float stepHeight = 0.2f;

        [Tooltip("Cuestas más empinadas que esto ya no se suben andando (normal.y mínima del suelo: 0.6 ≈ 53°).")]
        [SerializeField] private float maxWalkSlopeNormalY = 0.6f;

        [Tooltip("Velocidad vertical del saltito de escalón (u/s). Ojo: al no pulsar saltar se aplica la gravedad de 'salto corto' " +
                 "(lowJumpMultiplier), así que 2.7 sube ~0.23 u.")]
        [SerializeField] private float stepUpVelocity = 2.7f;

        private float nextStepTime;
        private readonly RaycastHit2D[] stepHits = new RaycastHit2D[6];

        private bool parked;
        private float defaultGravityScale = -1f;
        private readonly ContactPoint2D[] footContacts = new ContactPoint2D[8];

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
        // ENGANCHES PARA LA PRUEBA AUTOMÁTICA (Ratms.Testing.AutoPlayTest)
        // ==================================================================
        // En partidas normales nadie los toca: valen null/false y todo el
        // input viene del teclado como siempre.

        /// <summary>Si tiene valor, sustituye al eje A/D (-1..1). Solo lo usa AutoPlayTest.</summary>
        public float? TestInputX { get; set; }

        /// <summary>Simula mantener pulsada la flecha arriba (salto largo). Solo AutoPlayTest.</summary>
        public bool TestJumpHeld { get; set; }

        /// <summary>Simula W/S colgando de la cuerda (+1 subir, -1 bajar). Solo AutoPlayTest.</summary>
        public float? TestRopeClimb { get; set; }

        private bool testJumpPressed;

        /// <summary>Simula pulsar la flecha arriba este frame. Solo AutoPlayTest.</summary>
        public void TestPressJump() => testJumpPressed = true;

        private bool ConsumeTestJump()
        {
            if (!testJumpPressed) return false;
            testJumpPressed = false;
            return true;
        }

        private bool testFireDown, testFireUp;

        /// <summary>Simula pulsar ESPACIO este frame. Solo AutoPlayTest.</summary>
        public void TestPressFire() => testFireDown = true;

        /// <summary>Simula soltar ESPACIO este frame. Solo AutoPlayTest.</summary>
        public void TestReleaseFire() => testFireUp = true;

        /// <summary>
        ///  Olvida la altura máxima registrada para el daño por caída. Úsalo
        ///  tras TELETRANSPORTAR la rata (si no, un teletransporte hacia abajo
        ///  cuenta como una caída enorme). Solo AutoPlayTest.
        /// </summary>
        public void TestResetFallTracking()
        {
            peakYSinceLastGround = transform.position.y;
            wasGroundedLastFrame = false;
        }

        /// <summary>Fija el ángulo de apuntado (-90 abajo … +90 arriba). Solo AutoPlayTest.</summary>
        public void TestSetAim(float degrees)
        {
            if (IsOwner) aimAngle.Value = Mathf.Clamp(degrees, -90f, 90f);
        }

        // ==================================================================
        // Lectura pública (armas, HUD, pruebas)
        // ==================================================================
        /// <summary>true = la rata mira a la derecha.</summary>
        public bool FacingRight => facingRight.Value;

        /// <summary>Dirección del arma en el mundo (apuntado + lado al que mira).</summary>
        public Vector2 WeaponForward => GetWeaponForward();

        /// <summary>
        ///  Punto de la MANO en el mundo (donde la rata sujeta las armas: el
        ///  weaponHolder). Léelo en un LateUpdate que corra DESPUÉS del de este
        ///  script (ver [DefaultExecutionOrder] de las armas), porque aquí se
        ///  corrige lo que el Animator pisa cada frame.
        /// </summary>
        public Vector3 HandPosition => weaponHolder != null ? weaponHolder.position : transform.position;

        /// <summary>true si ya has usado un arma en este turno (solo se puede una vez por turno).</summary>
        public bool FiredThisTurn => firedThisTurn;

        // Un arma por turno: tras disparar puedes moverte (huir), pero no
        // volver a disparar. Antes, durante los segundos entre el disparo y
        // el fin de turno, se podía disparar OTRA vez.
        private bool firedThisTurn;
        private bool couldControlLastFrame;

        /// <summary>
        ///  Si se pone a true justo antes de spawnear una rata en el servidor,
        ///  esa rata nace como "DIANA" de pruebas: no lee input, no mueve la
        ///  cámara y su muerte NO cuenta para los turnos ni la victoria.
        ///  Solo lo usa AutoPlayTest.
        /// </summary>
        public static bool SpawnNextAsTestDummy;

        /// <summary>true = rata diana de la prueba automática (ver <see cref="SpawnNextAsTestDummy"/>).</summary>
        public bool IsTestDummy { get; private set; }

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
            if (bat == null) bat = GetComponentInChildren<NetworkBat>();
            shotgun = GetComponent<NetworkShotgun>();
            teleport = GetComponent<NetworkTeleport>();
            rope = GetComponent<NetworkNinjaRope>();
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
            if (IsServer && SpawnNextAsTestDummy)
            {
                IsTestDummy = true;
                SpawnNextAsTestDummy = false;
            }

            // Re-resuelve las refs visuales por si el prefab las tiene mal cacheadas
            ResolveVisualReferences();

            // ASISTENCIA DE CUESTAS: redondea las esquinas del BoxCollider2D
            // de la rata. Esto se aplica en runtime — el prefab queda intacto.
            // Por qué: nuestro polígono procedural tiene micro-bumps en cada
            // vértice del skyline. Una box collider con esquinas cuadradas se
            // engancha en cada bump y la rata "se atasca" subiendo cuestas.
            // Con edgeRadius > 0 las esquinas son curvas → la rata rueda
            // suavemente sobre los bumps, igual que un personaje de Worms.
            ApplyColliderEdgeRadius();
            AlignColliderWithFeet();

            // SUAVIDAD: el dueño es quien simula la física de su rata.
            //  · Interpolate → Unity dibuja la rata entre dos pasos de física,
            //    así no va "a tirones" cuando los FPS no coinciden con los
            //    50 Hz de la física (la cámara la sigue en LateUpdate y se
            //    notaba mucho el temblor).
            //  · Continuous → a las velocidades de un empujón de explosión la
            //    rata ya no puede atravesar paredes finas del terreno.
            if (IsOwner && rb != null)
            {
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

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
            // (la diana de pruebas no participa en los turnos).
            if (IsServer && health != null && !IsTestDummy)
                health.OnDiedServer += HandleDiedServer;

            // Etiqueta en la jerarquía para depurar
            gameObject.name = IsTestDummy ? "Rat_DIANA"
                : IsOwner ? $"Rat_{OwnerClientId}_(yo)"
                : $"Rat_{OwnerClientId}";

            // Rata dibujada por código (más bonita, casco del color del equipo).
            if (useProceduralRat && GetComponent<RatAppearance>() == null)
                gameObject.AddComponent<RatAppearance>().Init(this, OwnerClientId, IsTestDummy);

            // Si SOY yo, que la cámara me siga (y recuerde a quién volver
            // tras seguir un proyectil o tras una pausa de impacto).
            if (IsOwner && !IsTestDummy)
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
        /// <summary>¿Puede controlar la rata AHORA? (dueño + su turno). Lo leen las armas.</summary>
        public bool CanControlNow => CanControl();

        /// <summary>Lo llama NetworkTeleport tras mover la rata: sin "caída" falsa y sin aparcar.</summary>
        public void OnTeleported()
        {
            peakYSinceLastGround = transform.position.y;
            if (parked && rb != null) { parked = false; rb.gravityScale = defaultGravityScale >= 0f ? defaultGravityScale : 1f; }
        }

        /// <summary>Lo llama la cuerda al engancharse o soltarse: reinicia la caída y suelta el aparcado.</summary>
        public void OnRopeAttachChanged()
        {
            peakYSinceLastGround = transform.position.y;
            if (parked && rb != null) { parked = false; rb.gravityScale = defaultGravityScale >= 0f ? defaultGravityScale : 1f; }
        }

        private bool CanControl()
        {
            if (!IsOwner || IsTestDummy) return false;
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

            // Al EMPEZAR un turno nuevo (pasamos de no controlar a controlar),
            // se recupera el derecho a usar un arma.
            bool canControlNow = CanControl();
            if (canControlNow && !couldControlLastFrame) { firedThisTurn = false; shotsThisTurn = 0; }
            couldControlLastFrame = canControlNow;

            if (!canControlNow)
            {
                // Si pierdo el turno mientras me movía, dejo de empujar y cancelo
                // cualquier carga en curso (bazooka o granada).
                inputX = 0f;
                if (bazooka != null && bazooka.IsCharging) bazooka.CancelCharging();
                if (grenadeThrower != null && grenadeThrower.IsCharging) grenadeThrower.CancelCharging();
                return;
            }

            inputX = TestInputX ?? Input.GetAxisRaw("Horizontal");

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

            // ==============================================
            //  DAÑO POR CAÍDA — solo el OWNER trackea y solicita
            // ==============================================
            if (IsOwner) TrackFallDamage(grounded);

            // Salto: SOLO Flecha Arriba. W queda libre para apuntar arriba.
            bool jumpKeyDown = Input.GetKeyDown(KeyCode.UpArrow) || ConsumeTestJump();

            // Colgando de la cuerda: la flecha arriba la SUELTA (no salta).
            if (rope != null && rope.IsAttached)
            {
                if (jumpKeyDown) rope.Release();
                jumpKeyDown = false;
                jumpRequestedTime = -10f;
            }
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
                Sfx.Play(Sfx.Id.Jump, 0.7f);
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
            // Apuntado: W sube el ángulo, S lo baja. Las flechas arriba/abajo
            // ya NO apuntan — la flecha arriba es saltar, y la flecha abajo
            // queda libre por si en el futuro queremos agacharse o algo así.
            float v = 0f;
            if (Input.GetKey(KeyCode.W)) v = 1f;
            else if (Input.GetKey(KeyCode.S)) v = -1f;
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

            bool fireDown = Input.GetKeyDown(KeyCode.Space) || testFireDown;
            bool fireUp   = Input.GetKeyUp(KeyCode.Space)   || testFireUp;
            testFireDown = testFireUp = false;

            // CUERDA NINJA: herramienta de movimiento, NO gasta el arma del turno.
            if (equipped == WeaponsMenuUI.WeaponId.Rope && rope != null)
            {
                if (fireDown)
                {
                    if (rope.IsAttached) rope.Release();
                    else rope.TryAttach(GetWeaponForward());
                }
                return;
            }

            // Ya usaste tu arma este turno → solo puedes moverte (huir).
            if (firedThisTurn) return;

            // TELETRANSPORTE: clic izquierdo o ESPACIO.
            if (equipped == WeaponsMenuUI.WeaponId.Teleport && teleport != null)
            {
                bool click = Input.GetMouseButtonDown(0) && TestInputX == null;
                if ((fireDown || click) && teleport.TryTeleport())
                {
                    firedThisTurn = true;
                    ScheduleEndTurn(1.5f);
                }
                return;
            }

            // ESCOPETA: hasta N disparos por turno, puedes reapuntar entre ellos.
            if (equipped == WeaponsMenuUI.WeaponId.Shotgun && shotgun != null)
            {
                if (fireDown && shotgun.Fire(GetWeaponForward()))
                {
                    shotsThisTurn++;
                    if (shotsThisTurn >= shotgun.ShotsPerTurn)
                    {
                        firedThisTurn = true;
                        ScheduleEndTurn(2f);
                    }
                }
                return;
            }

            // === START: al pulsar ESPACIO, arrancamos la carga del arma correspondiente ===
            if (fireDown)
            {
                if (equipped == WeaponsMenuUI.WeaponId.Bazooka && bazooka != null)
                    bazooka.StartCharging();
                else if (equipped == WeaponsMenuUI.WeaponId.Grenade && grenadeThrower != null)
                    grenadeThrower.StartCharging();
                // Armas SIN carga: actúan al pulsar.
                else if (equipped == WeaponsMenuUI.WeaponId.Dynamite && grenadeThrower != null)
                {
                    if (grenadeThrower.PlaceDynamite(facingRight.Value))
                    {
                        firedThisTurn = true;
                        // El turno dura lo que la mecha + margen: ese es tu
                        // tiempo para huir. La explosión lo acorta a 1 s.
                        ScheduleEndTurn(grenadeThrower.DynamiteFuse + 1.5f);
                    }
                    return;
                }
                else if (equipped == WeaponsMenuUI.WeaponId.Bat && bat != null)
                {
                    if (bat.Swing(GetWeaponForward()))
                    {
                        firedThisTurn = true;
                        ScheduleEndTurn();
                    }
                    return;
                }
            }

            // === TICK: mientras se mantiene pulsado, sube la fuerza ===
            Vector2 dir = GetWeaponForward();

            if (bazooka != null && bazooka.IsCharging)
                bazooka.TickCharging(Time.deltaTime, dir);
            if (grenadeThrower != null && grenadeThrower.IsCharging)
                grenadeThrower.TickCharging(Time.deltaTime, dir);

            // === RELEASE: al soltar ESPACIO, disparamos ===
            if (fireUp)
            {
                bool fired = false;
                if (bazooka != null && bazooka.IsCharging)
                    fired = bazooka.ReleaseShot(dir);
                if (!fired && grenadeThrower != null && grenadeThrower.IsCharging)
                    fired = grenadeThrower.ReleaseShot(dir);

                if (fired)
                {
                    firedThisTurn = true;
                    ScheduleEndTurn();
                }
            }
        }

        private void ScheduleEndTurn() => ScheduleEndTurn(endTurnDelay);

        private void ScheduleEndTurn(float seconds)
        {
            endTurnTimer = seconds;
        }

        /// <summary>
        ///  Trackea la altura máxima que alcanza la rata mientras está en el aire.
        ///  Al aterrizar, si ha caído desde muy alto, solicita daño al servidor.
        ///
        ///  Solo lo llama el OWNER (una vez por frame). El servidor confía en el
        ///  cliente para simplicidad — es un juego pequeño, no vale la pena
        ///  server-authoritative para esto. Si en el futuro hay problemas de
        ///  cheating, se podría mover al servidor observando transform.position.
        /// </summary>
        private void TrackFallDamage(bool grounded)
        {
            // Reset del peak si acabamos de tocar suelo (aterrizaje)
            if (grounded)
            {
                if (!wasGroundedLastFrame && peakYSinceLastGround > float.MinValue)
                {
                    // ATERRIZAJE detectado — calcular caída y solicitar daño
                    float currentY = transform.position.y;
                    float fallHeight = peakYSinceLastGround - currentY;

                    if (fallHeight > fallDamageThreshold)
                    {
                        float rawDmg = (fallHeight - fallDamageThreshold) * fallDamagePerUnit;
                        int dmg = Mathf.Clamp(Mathf.RoundToInt(rawDmg), 1, fallDamageMax);
                        RequestFallDamageServerRpc(dmg);
                    }
                }
                // Reset — próximo salto empieza a trackear desde aquí
                peakYSinceLastGround = transform.position.y;
            }
            else
            {
                // En el aire — actualiza peak si supera el máximo
                if (transform.position.y > peakYSinceLastGround)
                    peakYSinceLastGround = transform.position.y;
            }

            wasGroundedLastFrame = grounded;
        }

        [ServerRpc]
        private void RequestFallDamageServerRpc(int amount)
        {
            var hp = GetComponent<NetworkHealth>();
            if (hp == null || hp.IsDead.Value) return;
            hp.ApplyDamageServer(amount, transform.position, DamageType.Fall);
            Debug.Log($"[NetworkPlayerMovement] Daño por caída aplicado a clientId={OwnerClientId}: {amount}");
        }

        /// <summary>
        ///  Lo llama el proyectil (desde el servidor) al explotar/impactar, para
        ///  acortar el temporizador de fin de turno. La idea: el fallback largo
        ///  (endTurnDelay = 3.5s) cubre los casos raros (misil perdido, granada
        ///  con fusible), pero cuando el proyectil SÍ impacta, no queremos hacer
        ///  esperar más al siguiente jugador — con 1s (para ver la explosión
        ///  y el focus de cámara) es suficiente.
        ///
        ///  Solo tiene efecto en el Owner (que es quien está corriendo el
        ///  temporizador local). Y solo acorta — nunca alarga.
        /// </summary>
        [ClientRpc]
        public void ShortenEndTurnClientRpc(float seconds)
        {
            if (!IsOwner) return;
            if (endTurnTimer < 0f) return; // no había timer activo, nada que hacer
            if (endTurnTimer > seconds) endTurnTimer = seconds;
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

            // RED DE SEGURIDAD: el terreno es de bordes (EdgeCollider2D), que no
            // tienen "dentro". Si por lo que sea el cuerpo acaba DENTRO de la
            // tierra, nada lo empujaría fuera y caería a través del mapa.
            if (IsOwner) UnstuckFromTerrain();

            // APARCADO en cuestas (solo el dueño simula su física).
            if (IsOwner)
            {
                // Con la cuerda enganchada NUNCA se aparca: anularía la gravedad
                // y la velocidad, y la rata ni se columpiaría ni podría trepar.
                bool onRope = rope != null && rope.IsAttached;
                UpdateParking((CanControl() && Mathf.Abs(inputX) > 0.01f) || onRope);
                if (parked) return;   // quieta del todo: nada más que hacer este paso
            }

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

            // COLGANDO DE LA CUERDA: la cuerda manda (balanceo con A/D, subir/
            // bajar con W/S). Nada de frenar en horizontal ni gravedad extra,
            // o mataríamos el columpio.
            if (rope != null && rope.IsAttached)
            {
                float climb = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                if (TestRopeClimb.HasValue) climb = TestRopeClimb.Value;
                rope.FixedTick(CanControl() ? inputX : 0f, CanControl() ? climb : 0f);
                return;
            }

            if (!CanControl())
            {
                // Sin control (turno ajeno, transición, o tras disparar).
                // Antes no tocábamos nada y pasaban dos cosas raras:
                //  · Si se acababa el turno andando, la rata seguía PATINANDO
                //    con la velocidad que llevaba.
                //  · La caída pesaba distinto dentro y fuera de tu turno (la
                //    gravedad extra solo se aplicaba con control).
                // Solo el dueño simula su física, así que solo él corrige.
                if (IsOwner)
                {
                    Vector2 v = rb.linearVelocity;
                    if (IsGrounded())
                        v.x = Mathf.MoveTowards(v.x, 0f, moveDeceleration * Time.fixedDeltaTime);
                    if (v.y < 0f)
                        v.y += Physics2D.gravity.y * rb.gravityScale * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime;
                    rb.linearVelocity = v;
                }
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
            Vector2 walkVel = new Vector2(newVx, rb.linearVelocity.y);

            // ANDAR SIGUIENDO EL SUELO: en una cuesta, empujar en horizontal
            // mete la rata CONTRA la pendiente y la fricción se come el
            // avance (en cuestas de 35-45° se quedaba clavada). Si hay suelo
            // bajo los pies y no es demasiado empinado, la velocidad va
            // PARALELA a la superficie: sube y baja cuestas sin atascarse ni
            // ir dando saltitos al bajar.
            if (Mathf.Abs(inputX) > 0.01f && walkVel.y < 1f && TryGetGroundNormal(Mathf.Sign(inputX), out Vector2 groundN) && groundN.y >= maxWalkSlopeNormalY)
            {
                Vector2 tangent = new Vector2(groundN.y, -groundN.x);   // a lo largo del suelo, hacia +x
                float along = Mathf.Abs(newVx) * Mathf.Sign(inputX);
                walkVel = tangent * along / Mathf.Max(0.5f, tangent.x);  // misma velocidad horizontal que en llano
                walkVel = Vector2.ClampMagnitude(walkVel, maxSpeed * 1.35f);
            }
            rb.linearVelocity = walkVel;

            if (stepAssist && Mathf.Abs(inputX) > 0.01f) TryStepUp(Mathf.Sign(inputX));

            // ==========================================================
            //  ASYMMETRIC GRAVITY — hace el salto MUCHO más satisfactorio
            //  · Cayendo → aplicamos gravedad extra → cae rápido, no flota
            //  · Subiendo Y sueltas la tecla → cortamos el salto (short jump)
            // ==========================================================
            Vector2 vel = rb.linearVelocity;
            float extraGravity = Physics2D.gravity.y * rb.gravityScale;   // negativo

            if (vel.y < 0f)
            {
                // Cae con MÁS gravedad que la de subir
                vel.y += extraGravity * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime;
            }
            else if (vel.y > 0.01f && !(Input.GetKey(KeyCode.UpArrow) || TestJumpHeld))
            {
                // Sube pero no está pulsando salto → cortar el salto
                vel.y += extraGravity * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
            }
            rb.linearVelocity = vel;
        }

        /// <summary>
        ///  Si el CENTRO del cuerpo está dentro de tierra sólida, sube la rata
        ///  hasta el primer hueco libre (máx. 1.5 u) y la para. Lo comprobamos
        ///  cada paso de física: es solo mirar un píxel.
        /// </summary>
        private void UnstuckFromTerrain()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;
            Vector2 center = col.bounds.center;
            float? up = Ratms.World.DestructibleTerrain.EscapeUpDistance(center, 1.5f);
            if (up == null) return;

            float lift = up.Value + col.bounds.extents.y + 0.03f;
            rb.position += Vector2.up * lift;
            rb.linearVelocity = Vector2.zero;
            Debug.LogWarning($"[NetworkPlayerMovement] {name} estaba DENTRO del terreno en ({center.x:F2},{center.y:F2}) → la saco {lift:F2} u hacia arriba.");
        }

        /// <summary>
        ///  "Aparca" la rata cuando está quieta sobre el suelo: velocidad 0 y
        ///  gravedad 0, para que no resbale por las cuestas (con la fricción
        ///  por defecto, en pendientes de ~25° o más se deslizaba sola, y al
        ///  aterrizar de un salto en cuesta seguía bajando sin parar).
        ///
        ///  Se suelta (gravedad normal) en cuanto: hay input de andar, la
        ///  velocidad sube (salto, empujón), entra en vuelo por knockback o
        ///  deja de tocar suelo (p. ej. le vuelan el terreno de debajo).
        ///
        ///  Usamos los CONTACTOS reales del Rigidbody (no el groundCheck por
        ///  radio) para no aparcarla flotando a unos milímetros del suelo.
        /// </summary>
        private void UpdateParking(bool hasMoveInput)
        {
            if (defaultGravityScale < 0f) defaultGravityScale = rb.gravityScale;

            bool shouldPark = holdOnSlopes
                              && !hasMoveInput
                              && !InKnockbackFlight
                              && rb.linearVelocity.sqrMagnitude < parkMaxSpeed * parkMaxSpeed
                              && HasFootContact();

            if (shouldPark)
            {
                if (!parked)
                {
                    parked = true;
                    rb.gravityScale = 0f;
                }
                rb.linearVelocity = Vector2.zero;
                rb.linearDamping = 0f;
            }
            else if (parked)
            {
                parked = false;
                rb.gravityScale = defaultGravityScale;
            }
        }

        /// <summary>
        ///  SUBIR ESCALONES: si al andar hay una pared justo delante de los
        ///  PIES pero no a la altura de un escalón, es un bordillo bajo → damos
        ///  un saltito vertical para subirlo. Dos rayos cortos hacia delante:
        ///  uno a ras de suelo y otro a <see cref="stepHeight"/> por encima.
        /// </summary>
        private void TryStepUp(float dir)
        {
            if (Time.time < nextStepTime) return;
            if (rb.linearVelocity.y > stepUpVelocity * 0.8f) return;   // ya está saltando (subir una cuesta andando no cuenta)
            if (!HasFootContact()) return;                      // solo apoyada en el suelo

            var col = GetComponent<Collider2D>();
            if (col == null) return;
            Bounds b = col.bounds;
            float frontX = dir > 0f ? b.max.x : b.min.x;
            const float probe = 0.07f;

            bool lowBlocked = WallAhead(new Vector2(frontX - dir * 0.01f, b.min.y + 0.04f), dir, probe);
            if (!lowBlocked) return;
            bool highBlocked = WallAhead(new Vector2(frontX - dir * 0.01f, b.min.y + stepHeight), dir, probe);
            if (highBlocked) return;                            // es una pared de verdad, no un escalón

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stepUpVelocity);
            nextStepTime = Time.time + 0.18f;
        }

        /// <summary>¿Hay una superficie casi vertical (no una cuesta suave) delante, que no sea de esta rata?</summary>
        private bool WallAhead(Vector2 origin, float dir, float dist)
        {
#pragma warning disable CS0618
            int n = Physics2D.RaycastNonAlloc(origin, new Vector2(dir, 0f), stepHits, dist);
#pragma warning restore CS0618
            for (int i = 0; i < n; i++)
            {
                var c = stepHits[i].collider;
                if (c == null || c.isTrigger) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
                if (Mathf.Abs(stepHits[i].normal.x) > 0.6f) return true;   // pared/escalón, no rampa
            }
            return false;
        }

        /// <summary>
        ///  Normal del suelo que toca ahora mismo, eligiendo la del contacto que
        ///  más se OPONE al avance (la cuesta de delante, no la de detrás: en
        ///  una "V" la media salía casi horizontal y la rata se clavaba).
        /// </summary>
        private bool TryGetGroundNormal(float dir, out Vector2 normal)
        {
            normal = Vector2.zero;
            float best = float.MaxValue;
            int n = rb.GetContacts(footContacts);
            for (int i = 0; i < n; i++)
            {
                var c = footContacts[i];
                if (c.collider == null || c.collider.isTrigger) continue;
                if (c.normal.y < footNormalMinY) continue;
                float against = c.normal.x * dir;   // más negativo = cuesta arriba delante
                if (against < best) { best = against; normal = c.normal; }
            }
            return best < float.MaxValue;
        }

        /// <summary>¿Está tocando físicamente algo que hace de suelo (normal hacia arriba)?</summary>
        private bool HasFootContact()
        {
            int n = rb.GetContacts(footContacts);
            for (int i = 0; i < n; i++)
                if (footContacts[i].normal.y >= footNormalMinY) return true;
            return false;
        }

        /// <summary>
        ///  Lo llama <see cref="NetworkHealth.ApplyKnockbackClientRpc"/> justo
        ///  después de aplicar el impulso. Activa una ventana en la que la rata
        ///  "vuela libre" (sin fricción aérea ni tope de velocidad).
        /// </summary>
        public void BeginKnockbackFlight(float seconds)
        {
            knockbackFlightUntil = Time.time + Mathf.Max(0.05f, seconds);
            // Si estaba aparcada, la soltamos YA (gravedad normal) para que
            // el empujón la lance en parábola desde el primer paso de física.
            if (parked && rb != null)
            {
                parked = false;
                rb.gravityScale = defaultGravityScale >= 0f ? defaultGravityScale : 1f;
            }
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

                // Unity 6 marcó esta API como deprecated pero funciona perfectamente
                // y hacer el cambio a la nueva (que usa List<T> gestionada) implicaría
                // allocations extra en cada frame. Suprimimos el warning.
#pragma warning disable CS0618
                int count = Physics2D.OverlapCircleNonAlloc(
                    groundCheck.position, groundRadius, groundHits, maskValue);
#pragma warning restore CS0618

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

#pragma warning disable CS0618
            int rayCount = Physics2D.RaycastNonAlloc(origin, Vector2.down, groundRayHits, groundRaycastDistance);
#pragma warning restore CS0618
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
        // ASISTENCIA DE CUESTAS — redondeo de esquinas del BoxCollider2D
        // ==================================================================
        /// <summary>
        ///  Pone un edgeRadius en el BoxCollider2D de la rata. Esto convierte
        ///  las esquinas duras en curvas, evitando que la rata se enganche
        ///  con los micro-vértices del polígono del terreno procedural.
        ///
        ///  Aplica también una pequeña reducción al tamaño del box para
        ///  COMPENSAR el grosor que añade el edgeRadius — así la rata
        ///  conserva las mismas dimensiones efectivas y no flota encima
        ///  del suelo. (Unity infla la caja por edgeRadius en cada lado.)
        /// </summary>
        private void ApplyColliderEdgeRadius()
        {
            if (colliderEdgeRadius <= 0f) return;

            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            // Si ya tiene un edgeRadius (porque ya lo aplicamos antes en
            // este ciclo de vida), no hacemos nada para no encoger el box
            // dos veces y quedarnos con un collider minúsculo.
            if (box.edgeRadius > 0.001f) return;

            float r = colliderEdgeRadius;
            // Encogemos el size original por 2*r (uno por cada lado) para
            // que el collider efectivo (size + 2r de halo) coincida con el
            // size original. min size 0.05 para no degenerarlo si la box
            // era muy pequeña.
            Vector2 newSize = box.size - new Vector2(r * 2f, r * 2f);
            newSize.x = Mathf.Max(0.05f, newSize.x);
            newSize.y = Mathf.Max(0.05f, newSize.y);
            box.size = newSize;
            box.edgeRadius = r;
        }

        /// <summary>
        ///  Baja el collider (y el groundCheck) <see cref="colliderDropToFeet"/>
        ///  para que la rata se APOYE con las patas del dibujo y no con un
        ///  punto más alto. Solo una vez por rata.
        /// </summary>
        private void AlignColliderWithFeet()
        {
            if (feetAligned || Mathf.Abs(colliderDropToFeet) < 0.0001f) return;
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            float sy = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
            box.offset -= new Vector2(0f, colliderDropToFeet / sy);

            // Más alto hacia arriba (el fondo no se mueve): que la cabeza también reciba.
            if (colliderExtraHeight > 0f)
            {
                float extra = colliderExtraHeight / sy;
                box.size += new Vector2(0f, extra);
                box.offset += new Vector2(0f, extra * 0.5f);
            }

            if (groundCheck == null)
            {
                var found = transform.Find("groundcheck") ?? transform.Find("GroundCheck") ?? transform.Find("groundCheck");
                if (found != null) groundCheck = found;
            }
            if (groundCheck != null)
                groundCheck.position -= new Vector3(0f, colliderDropToFeet, 0f);

            feetAligned = true;
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
