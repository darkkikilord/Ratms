using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Arma BAZOOKA en MULTIJUGADOR.
    ///
    ///  Responsabilidades:
    ///    1. Mostrar/ocultar el VISUAL del bazooka según el arma que el dueño
    ///       tenga seleccionada en el menú TAB (<see cref="WeaponsMenuUI"/>).
    ///    2. Mientras la bazooka esté equipada Y sea el turno del dueño,
    ///       mostrar una TRAYECTORIA FANTASMA (puntitos estilo Worms) que
    ///       indica por dónde saldría el disparo.
    ///    3. Gestionar la carga (mantener ESPACIO) y convertirla en un disparo
    ///       real que se spawnea en el servidor (<see cref="NetworkMissile"/>).
    ///
    ///  Quién ve qué:
    ///    • El visual del arma se ve en todos los clientes (sincronizado vía
    ///      NetworkVariable del WeaponsMenuUI).
    ///    • La trayectoria fantasma solo la ve el dueño (es un ayuda-apuntado
    ///      local — a nadie le importa la previsión del enemigo).
    /// </summary>
    public class NetworkBazooka : NetworkBehaviour
    {
        [Header("Referencias — disparo")]
        [Tooltip("Prefab del misil en red (debe tener NetworkObject + NetworkMissile).")]
        [SerializeField] private GameObject missilePrefab;

        [Tooltip("Punto del que sale el misil. IDEAL: colócalo en la PUNTA del cañón del bazooka visual, como hijo suyo, para que al rotar el arma la salida del disparo acompañe.")]
        [SerializeField] private Transform firePoint;

        [Header("Referencias — visual y menú")]
        [Tooltip("GameObject del modelo del bazooka (se mostrará/ocultará según el arma seleccionada).")]
        [SerializeField] private GameObject bazookaVisual;

        [Tooltip("Menú de armas de la misma rata (se auto-resuelve).")]
        [SerializeField] private WeaponsMenuUI weaponsMenu;

        [Tooltip("Indicador de trayectoria con puntitos (solo lo ve el dueño).")]
        [SerializeField] private TrajectoryDots trajectoryDots;

        [Header("Carga")]
        [SerializeField] private float chargeRate = 10f;   // fuerza/segundo
        [SerializeField] private float maxForce = 15f;

        [Header("Preview de trayectoria (sin cargar)")]
        [Tooltip("Fuerza usada para mostrar la trayectoria cuando el arma está equipada pero aún no se ha cargado.")]
        [SerializeField] private float previewForce = 7f;

        // Estado local del dueño
        private bool charging;
        private float currentForce;

        public bool IsCharging => charging;
        public float CurrentForce => currentForce;
        public float MaxForce => maxForce;
        public bool IsBazookaEquipped => weaponsMenu == null || weaponsMenu.IsBazookaEquipped;

        /// <summary>Expone el firePoint para que otras armas (granada, dinamita...) puedan reutilizar la misma salida.</summary>
        public Transform FirePoint => firePoint;

        /// <summary>Expone el TrajectoryDots para que otras armas puedan reutilizar el mismo indicador.</summary>
        public TrajectoryDots TrajectoryDots => trajectoryDots;

        // ==============================================================
        private void Awake()
        {
            if (weaponsMenu == null) weaponsMenu = GetComponentInParent<WeaponsMenuUI>();
            if (weaponsMenu == null) weaponsMenu = GetComponent<WeaponsMenuUI>();
        }

        public override void OnNetworkSpawn()
        {
            // Estado inicial: aplicamos visibilidad según el arma ya guardada en red
            ApplyWeaponVisibility();
        }

        // ==============================================================
        // LOOP: controla visual y trayectoria preview
        // ==============================================================
        private void Update()
        {
            // Visual del bazooka: se gestiona en TODOS los clientes (sincronizado).
            ApplyWeaponVisibility();

            // La trayectoria-preview solo la renderiza el dueño.
            if (IsOwner) UpdateTrajectoryPreview();
        }

        /// <summary>Enciende/apaga el sprite del bazooka según arma equipada.</summary>
        private void ApplyWeaponVisibility()
        {
            if (bazookaVisual == null) return;
            bool shouldShow = IsBazookaEquipped;
            if (bazookaVisual.activeSelf != shouldShow)
                bazookaVisual.SetActive(shouldShow);
        }

        /// <summary>
        ///  Solo para el DUEÑO: si tiene el bazooka equipado Y es su turno,
        ///  mostramos una trayectoria fantasma con puntitos. Si está cargando,
        ///  la trayectoria se actualiza con la fuerza real. Si no, usa la fuerza
        ///  de preview (fija) para que sirva solo como referencia visual.
        /// </summary>
        private void UpdateTrajectoryPreview()
        {
            if (trajectoryDots == null) return;

            // Si está otra arma equipada (granada, dinamita...), NO pisamos la
            // trayectoria: deja que el script de esa arma la pinte. Solo la
            // ocultamos nosotros cuando el arma es None (desenfundado).
            if (!IsBazookaEquipped)
            {
                // La granada pinta su propia trayectoria. Cualquier OTRA arma
                // (ninguna, dinamita, bate...) no tiene parábola → ocultamos,
                // o se quedarían los puntos congelados en pantalla.
                if (weaponsMenu != null &&
                    weaponsMenu.SelectedWeapon != WeaponsMenuUI.WeaponId.Grenade)
                {
                    trajectoryDots.Hide();
                }
                return;
            }

            // Solo enseñamos la trayectoria cuando es TU turno (si no, hay
            // que esperar — no tiene sentido apuntar fuera de turno).
            var tm = NetworkTurnManager.Instance;
            bool myTurn = (tm == null) || tm.IsActiveRat(NetworkObjectId);
            if (!myTurn)
            {
                trajectoryDots.Hide();
                return;
            }

            Vector2 dir = AimDirection();
            float force = charging ? Mathf.Max(0.01f, currentForce) : previewForce;
            trajectoryDots.Show(force, dir);
        }

        // ==============================================================
        // API que llama NetworkPlayerMovement (solo en el dueño)
        // ==============================================================
        public void StartCharging()
        {
            if (!IsOwner) return;
            if (!IsBazookaEquipped) return; // con otra arma no cargamos bazooka
            charging = true;
            currentForce = 0f;
        }

        public void TickCharging(float deltaTime, Vector2 aimDirection)
        {
            if (!charging) return;
            currentForce = Mathf.Min(currentForce + chargeRate * deltaTime, maxForce);
        }

        public void CancelCharging()
        {
            if (!charging) return;
            charging = false;
            currentForce = 0f;
        }

        /// <summary>
        ///  Suelta el disparo. Devuelve true si de verdad ha disparado
        ///  (falso si no estábamos cargando o no tenemos bazooka equipada).
        /// </summary>
        public bool ReleaseShot(Vector2 aimDirection)
        {
            if (!IsOwner || !charging) return false;
            charging = false;

            if (!IsBazookaEquipped)
            {
                currentForce = 0f;
                return false;
            }

            Vector2 dir = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : Vector2.right;
            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
            float force = Mathf.Max(0.1f, currentForce);

            FireServerRpc(spawnPos, dir, force);

            currentForce = 0f;
            return true;
        }

        /// <summary>
        ///  Dirección de apuntado en world space (punta del cañón).
        ///
        ///  OJO: NO usamos <c>firePoint.right</c> porque ese vector IGNORA la
        ///  escala del transform (solo usa la rotación). Cuando la rata mira
        ///  a la izquierda, el weaponHolder tiene scale.x = -1 y eso NO afecta
        ///  a <c>Transform.right</c> — seguiría devolviendo el vector derecho.
        ///  Con <c>localToWorldMatrix.MultiplyVector</c> sí se aplica rotación
        ///  y escala, así obtenemos el vector que realmente apunta al frente
        ///  del cañón incluso con la rata flipeada.
        /// </summary>
        private Vector2 AimDirection()
        {
            if (firePoint == null) return Vector2.right;
            Vector3 v = firePoint.localToWorldMatrix.MultiplyVector(Vector3.right);
            Vector2 r = new Vector2(v.x, v.y);
            if (r.sqrMagnitude < 0.0001f) return Vector2.right;
            return r.normalized;
        }

        // ==============================================================
        // SERVIDOR: spawn del misil
        // ==============================================================
        [ServerRpc]
        private void FireServerRpc(Vector3 spawnPos, Vector2 direction, float force, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || missilePrefab == null) return;

            // Solo permitimos disparar si es el turno del cliente que lo pide
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpcParams.Receive.SenderClientId, NetworkObjectId))
                return;

            // Instanciamos el misil en la posición indicada
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);

            GameObject go = Instantiate(missilePrefab, spawnPos, rot);
            var no = go.GetComponent<NetworkObject>();
            if (no == null)
            {
                Debug.LogError("[NetworkBazooka] missilePrefab no tiene NetworkObject — no se puede spawnear.");
                Destroy(go);
                return;
            }

            // Spawn en la red: ahora TODOS los clientes lo ven
            no.Spawn(destroyWithScene: true);

            // Inicializa la fuerza y el owner (para ignorarle los primeros ms)
            var missile = go.GetComponent<NetworkMissile>();
            if (missile != null)
                missile.ServerInit(NetworkObject, direction * force);
        }
    }
}
