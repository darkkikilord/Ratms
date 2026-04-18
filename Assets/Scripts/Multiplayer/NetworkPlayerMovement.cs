using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Movimiento de la rata en MULTIJUGADOR.
    ///  Versión simplificada: movimiento, salto, flip y animación. SIN disparos todavía.
    ///  El disparo de red lo meteremos en una sesión futura.
    ///
    ///  Diferencias clave con el PlayerMovement de single-player:
    ///   • Hereda de NetworkBehaviour (no MonoBehaviour).
    ///   • Solo el dueño (IsOwner) procesa input y física.
    ///   • El flip (izquierda/derecha) y la velocidad para animación
    ///     se sincronizan con NetworkVariables, así los otros clientes
    ///     ven la rata girarse y correr igual que su dueño.
    ///   • No usa el TurnManager — en esta fase cada jugador se mueve libre.
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

        // --- Privadas ---
        private Rigidbody2D rb;
        private float inputX;
        private int speedHash;
        private SpriteRenderer[] allSprites;

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

        // ==================================================================
        // CICLO DE VIDA
        // ==================================================================
        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            ResolveVisualReferences();
            speedHash = Animator.StringToHash(speedParam);
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

            // Cacheamos TODOS los sprites NO-weapon para poder flipear todo a la vez.
            // Así no importa si es en el root o en un hijo, ni si hay varios.
            var found = GetComponentsInChildren<SpriteRenderer>(true);
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (var sr in found)
            {
                if (!sr.transform.name.ToLower().Contains("weapon"))
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

            // Cuando los valores cambien por red, actualizamos visuales
            facingRight.OnValueChanged += OnFacingChanged;
            syncedSpeed.OnValueChanged += OnSpeedChanged;

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
            if (!CanControl())
            {
                // Si pierdo el turno mientras me movía, dejo de empujar.
                inputX = 0f;
                return;
            }

            inputX = Input.GetAxisRaw("Horizontal");

            // Salto (W o Espacio)
            bool grounded = groundCheck != null
                && Physics2D.OverlapCircle(groundCheck.position, groundRadius, groundMask);

            if (grounded && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space)))
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            }

            // Flip: actualiza la NetworkVariable → se propaga a todos
            if (inputX > 0.01f && !facingRight.Value) facingRight.Value = true;
            else if (inputX < -0.01f && facingRight.Value) facingRight.Value = false;

            // Publica la velocidad para que todos los clientes animen correctamente.
            // Sólo actualizamos si cambia "notablemente" para no saturar la red.
            float currentSpeed = Mathf.Abs(rb.linearVelocity.x);
            if (Mathf.Abs(currentSpeed - syncedSpeed.Value) > 0.1f)
                syncedSpeed.Value = currentSpeed;
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
        ///  LateUpdate se ejecuta DESPUÉS del Animator. Reaplicamos el flip aquí
        ///  para que no nos lo pise ninguna animación que toque al SpriteRenderer.
        /// </summary>
        private void LateUpdate()
        {
            ApplyFlipVisual(facingRight.Value);
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
