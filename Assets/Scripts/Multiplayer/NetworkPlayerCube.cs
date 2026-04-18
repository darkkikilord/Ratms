using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Cubo de prueba controlado por red (Fase 2).
    ///  Cada jugador mueve el SUYO con A/D (o flechas) y salta con W/Espacio.
    ///  Cuando esto funcione en dos ventanas, reemplazaremos este script por
    ///  el de la rata real en la Fase 2.5.
    ///
    ///  Claves de NGO que aparecen aquí:
    ///   - Hereda de NetworkBehaviour (no MonoBehaviour) para poder usar la red.
    ///   - IsOwner: true solo en el PC del jugador que controla este cubo.
    ///   - OnNetworkSpawn: se llama cuando el objeto aparece ya conectado a la red.
    ///   - OwnerClientId: identificador único del jugador dueño (0 = host, 1, 2...).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class NetworkPlayerCube : NetworkBehaviour
    {
        [Header("Movimiento")]
        [Tooltip("Velocidad horizontal del cubo.")]
        [SerializeField] private float moveSpeed = 6f;

        [Tooltip("Fuerza vertical al saltar.")]
        [SerializeField] private float jumpForce = 9f;

        [Header("Visual")]
        [Tooltip("Colores que se asignan a cada jugador por orden de conexión.")]
        [SerializeField] private Color[] playerColors = new Color[]
        {
            new Color(1.00f, 0.35f, 0.35f),  // rojo
            new Color(0.35f, 0.60f, 1.00f),  // azul
            new Color(0.40f, 1.00f, 0.45f),  // verde
            new Color(1.00f, 0.90f, 0.35f),  // amarillo
        };

        private Rigidbody2D rb;
        private SpriteRenderer sr;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            sr = GetComponent<SpriteRenderer>();
        }

        public override void OnNetworkSpawn()
        {
            // Colorea el cubo de cada jugador de forma distinta para distinguirlos
            if (sr != null && playerColors.Length > 0)
            {
                int idx = (int)(OwnerClientId % (uint)playerColors.Length);
                sr.color = playerColors[idx];
            }

            // Nombre útil en la jerarquía para saber quién es quién al depurar
            gameObject.name = IsOwner
                ? $"Cube_{OwnerClientId}_(yo)"
                : $"Cube_{OwnerClientId}";
        }

        private void Update()
        {
            // 🔑 Solo el DUEÑO del cubo procesa el input.
            //    Los otros clientes verán el cubo moverse gracias al NetworkTransform.
            if (!IsOwner) return;

            // Movimiento horizontal
            float x = Input.GetAxisRaw("Horizontal");
            rb.linearVelocity = new Vector2(x * moveSpeed, rb.linearVelocity.y);

            // Salto sencillo (sin ground check para mantenerlo mínimo en la Fase 2)
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space))
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            }
        }
    }
}
