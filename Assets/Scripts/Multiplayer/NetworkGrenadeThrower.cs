using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Lanza-granadas en MULTIJUGADOR. Prima hermana del <see cref="NetworkBazooka"/>
    ///  pero para granadas: carga con ESPACIO, suelta y ¡bum!
    ///
    ///  Diferencias clave con el bazooka:
    ///   • NO spawnea un misil que explota al tocar, sino una <see cref="NetworkGrenade"/>
    ///     que rebota y tiene fusible.
    ///   • La preview de trayectoria se enseña cuando está equipada la Granada
    ///     (leído de <see cref="WeaponsMenuUI.SelectedWeapon"/>), no el Bazooka.
    ///   • Visual opcional en la mano (si tu prefab tiene un sprite de granada
    ///     sostenida, se muestra igual que el bazookaVisual).
    ///
    ///  Comparte con el bazooka:
    ///   • firePoint, weaponsMenu, TrajectoryDots (puede reutilizar los mismos
    ///     de la rata — solo uno se activa a la vez según el arma seleccionada).
    ///   • El mismo API: StartCharging / TickCharging / ReleaseShot / CancelCharging.
    /// </summary>
    // Orden 100: el LateUpdate de la dinamita-en-la-mano corre DESPUÉS del de
    // NetworkPlayerMovement (que coloca bien la mano tras el Animator).
    [DefaultExecutionOrder(100)]
    public class NetworkGrenadeThrower : NetworkBehaviour
    {
        [Header("Referencias — lanzamiento")]
        [Tooltip("Prefab de la granada en red (debe tener NetworkObject + NetworkGrenade + Rigidbody2D).")]
        [SerializeField] private GameObject grenadePrefab;

        [Tooltip("Punto desde el que sale la granada. Suele ser el mismo firePoint que usa el bazooka (punta del cañón o mano).")]
        [SerializeField] private Transform firePoint;

        [Header("Referencias — visual y menú")]
        [Tooltip("GameObject del sprite de granada que sujeta la rata (opcional — puede ser null).")]
        [SerializeField] private GameObject grenadeVisual;

        [Tooltip("Menú de armas de la misma rata (se auto-resuelve).")]
        [SerializeField] private WeaponsMenuUI weaponsMenu;

        [Tooltip("Indicador de trayectoria con puntitos (lo compartimos con el bazooka).")]
        [SerializeField] private TrajectoryDots trajectoryDots;

        [Header("Carga")]
        [Tooltip("Cuánta fuerza se acumula por segundo mientras mantienes ESPACIO.")]
        [SerializeField] private float chargeRate = 9f;

        [Tooltip("Fuerza máxima del lanzamiento. Si sigues manteniendo ESPACIO pasada ésta, se queda en el máximo.")]
        [SerializeField] private float maxForce = 14f;

        [Header("Preview de trayectoria (sin cargar)")]
        [Tooltip("Fuerza de previsualización cuando tienes la granada equipada pero aún no has pulsado ESPACIO.")]
        [SerializeField] private float previewForce = 6.5f;

        [Header("Dinamita (usa el mismo prefab que la granada)")]
        [Tooltip("Segundos de mecha. Es también tu tiempo para HUIR: el turno no acaba hasta que explota.")]
        [SerializeField] private float dynamiteFuse = 5f;
        [SerializeField] private int dynamiteDamage = 90;
        [Tooltip("Radio de la explosión (daño y cráter). La granada usa 3.5, el misil 2.5. " +
                 "Ojo: la rata anda a ~1.8 u/s, así que con 5 s de mecha puede alejarse ~4-6 u si no hay obstáculos.")]
        [SerializeField] private float dynamiteRadius = 4f;

        /// <summary>Radio de la explosión de la dinamita (lo usa la prueba automática).</summary>
        public float DynamiteRadius => dynamiteRadius;
        [Tooltip("Empujón en el centro de la explosión (la granada usa 9.4).")]
        [SerializeField] private float dynamiteKnockback = 14f;

        /// <summary>Segundos de mecha de la dinamita (NetworkPlayerMovement lo usa para alargar el turno).</summary>
        public float DynamiteFuse => dynamiteFuse;

        public bool IsDynamiteEquipped =>
            weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Dynamite;

        // Estado local del dueño
        private bool charging;
        private float currentForce;

        public bool IsCharging => charging;
        public float CurrentForce => currentForce;
        public float MaxForce => maxForce;
        public bool IsGrenadeEquipped =>
            weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Grenade;

        // ==============================================================
        private void Awake()
        {
            if (weaponsMenu == null) weaponsMenu = GetComponentInParent<WeaponsMenuUI>();
            if (weaponsMenu == null) weaponsMenu = GetComponent<WeaponsMenuUI>();

            // Auto-resolución defensiva: si olvidas arrastrar estas referencias
            // en el Inspector, las buscamos automáticamente por la jerarquía de
            // la rata. Así evitamos los típicos "no se ve la trayectoria" por
            // un campo vacío.
            NetworkBazooka bazooka = GetComponentInParent<NetworkBazooka>();
            if (bazooka == null)
                bazooka = GetComponentInChildren<NetworkBazooka>(includeInactive: true);

            if (trajectoryDots == null)
            {
                if (bazooka != null) trajectoryDots = bazooka.TrajectoryDots;
                if (trajectoryDots == null)
                    trajectoryDots = GetComponentInParent<TrajectoryDots>();
                if (trajectoryDots == null)
                    trajectoryDots = GetComponentInChildren<TrajectoryDots>(includeInactive: true);
            }

            if (firePoint == null && bazooka != null)
                firePoint = bazooka.FirePoint;
        }

        public override void OnNetworkSpawn()
        {
            ApplyVisibility();
        }

        private void Update()
        {
            ApplyVisibility();

            // La trayectoria-preview solo la renderiza el dueño.
            if (IsOwner) UpdateTrajectoryPreview();
        }

        // ==============================================================
        // DINAMITA EN LA MANO (visual, todos los clientes)
        // ==============================================================
        private Transform heldDynamite;
        private NetworkPlayerMovement ownerMovement;

        /// <summary>
        ///  Con la dinamita equipada, la rata la SUJETA delante (inclinada hacia
        ///  donde mira). Antes no se veía nada en la mano y no quedaba claro
        ///  qué arma llevabas. La ven todos los jugadores.
        /// </summary>
        private void LateUpdate()
        {
            if (ownerMovement == null) ownerMovement = GetComponentInParent<NetworkPlayerMovement>();
            // Tras soltarla ya no la llevas en la mano (FiredThisTurn solo lo
            // conoce el dueño; en los demás clientes se ve hasta el cambio de arma).
            bool show = IsDynamiteEquipped && !(ownerMovement != null && ownerMovement.FiredThisTurn);
            if (heldDynamite == null)
            {
                if (!show) return;
                // SIN padre (vive en el mundo): no hereda escalas/volteos de la rata.
                var go = new GameObject("HeldDynamite");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = WeaponSprites.Dynamite();
                sr.sortingOrder = 60;
                heldDynamite = go.transform;
            }
            if (heldDynamite.gameObject.activeSelf != show) heldDynamite.gameObject.SetActive(show);
            if (!show) return;

            bool right = ownerMovement == null || ownerMovement.FacingRight;
            // En la MANO (el mismo punto donde sujeta el bazooka), un poco inclinada.
            Vector3 hand = ownerMovement != null ? ownerMovement.HandPosition : transform.position;
            heldDynamite.position = new Vector3(hand.x, hand.y - 0.05f, transform.position.z);
            heldDynamite.rotation = Quaternion.Euler(0f, 0f, right ? -20f : 20f);
        }

        private void OnDisable()
        {
            if (heldDynamite != null) heldDynamite.gameObject.SetActive(false);   // rata muerta
        }

        public override void OnDestroy()
        {
            if (heldDynamite != null) Destroy(heldDynamite.gameObject);
            base.OnDestroy();
        }

        /// <summary>Enciende/apaga el sprite de la granada en la mano según arma equipada.</summary>
        private void ApplyVisibility()
        {
            if (grenadeVisual == null) return;
            bool shouldShow = IsGrenadeEquipped;
            if (grenadeVisual.activeSelf != shouldShow)
                grenadeVisual.SetActive(shouldShow);
        }

        /// <summary>
        ///  Solo para el DUEÑO: si tiene la granada equipada Y es su turno,
        ///  dibujamos la trayectoria-fantasma. Usa la fuerza real si está cargando,
        ///  y una preview fija si solo apunta.
        /// </summary>
        private void UpdateTrajectoryPreview()
        {
            if (trajectoryDots == null) return;

            if (!IsGrenadeEquipped)
            {
                // Si NO está equipada la granada, el bazooka o "nada" se encargan
                // de pintar / ocultar la trayectoria; no la pisamos aquí.
                return;
            }

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
            if (!IsGrenadeEquipped) return;
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
        ///  Suelta el lanzamiento. Devuelve true si la granada se ha lanzado
        ///  realmente (false si no cargábamos o no tocaba granada).
        /// </summary>
        public bool ReleaseShot(Vector2 aimDirection)
        {
            if (!IsOwner || !charging) return false;
            charging = false;

            if (!IsGrenadeEquipped)
            {
                currentForce = 0f;
                return false;
            }

            Vector2 dir = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : Vector2.right;
            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
            float force = Mathf.Max(0.1f, currentForce);

            ThrowServerRpc(spawnPos, dir, force);

            currentForce = 0f;
            return true;
        }

        /// <summary>
        ///  Dirección de apuntado en world space. Mismo truco que en NetworkBazooka:
        ///  usamos <c>localToWorldMatrix.MultiplyVector</c> para que la escala
        ///  negativa (rata mirando izquierda) se aplique y el vector apunte al
        ///  lado correcto.
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
        // DINAMITA: se deja caer a los pies, sin cargar
        // ==============================================================
        /// <summary>
        ///  Deja la dinamita en el suelo, un pelín por delante de la rata.
        ///  Devuelve true si se ha pedido al servidor (para que el movimiento
        ///  programe el fin de turno con tiempo de huida).
        /// </summary>
        public bool PlaceDynamite(bool facingRight)
        {
            if (!IsOwner || !IsDynamiteEquipped) return false;
            // A los PIES de la rata, un pelín por delante. Usamos el collider
            // del cuerpo: el pivote del prefab está ~0.8 u bajo el suelo y
            // la dinamita aparecía enterrada.
            var body = GetComponentInParent<NetworkHealth>();
            var col = body != null ? body.GetComponent<Collider2D>() : null;
            Vector3 feet = col != null
                ? new Vector3(col.bounds.center.x, col.bounds.min.y, 0f)
                : transform.position;
            Vector3 pos = feet + new Vector3(facingRight ? 0.18f : -0.18f, 0.15f, 0f);
            DropDynamiteServerRpc(pos);
            return true;
        }

        [ServerRpc]
        private void DropDynamiteServerRpc(Vector3 pos, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || grenadePrefab == null) return;

            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpcParams.Receive.SenderClientId, NetworkObjectId))
                return;

            GameObject go = Instantiate(grenadePrefab, pos, Quaternion.identity);
            var no = go.GetComponent<NetworkObject>();
            var grenade = go.GetComponent<NetworkGrenade>();
            if (no == null || grenade == null)
            {
                Debug.LogError("[NetworkGrenadeThrower] grenadePrefab no sirve para dinamita (falta NetworkObject o NetworkGrenade).");
                Destroy(go);
                return;
            }

            // Configurar ANTES de Spawn → los clientes la reciben ya como dinamita.
            grenade.PrepareAsDynamite(dynamiteFuse, dynamiteDamage, dynamiteRadius, dynamiteKnockback);
            no.Spawn(destroyWithScene: true);
            grenade.ServerInit(NetworkObject, Vector2.zero);   // sin impulso: cae donde la dejas
        }

        // ==============================================================
        // SERVIDOR: spawn de la granada
        // ==============================================================
        [ServerRpc]
        private void ThrowServerRpc(Vector3 spawnPos, Vector2 direction, float force, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || grenadePrefab == null) return;

            // Valida turno
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpcParams.Receive.SenderClientId, NetworkObjectId))
                return;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);

            GameObject go = Instantiate(grenadePrefab, spawnPos, rot);
            var no = go.GetComponent<NetworkObject>();
            if (no == null)
            {
                Debug.LogError("[NetworkGrenadeThrower] grenadePrefab no tiene NetworkObject — no se puede spawnear.");
                Destroy(go);
                return;
            }

            no.Spawn(destroyWithScene: true);

            var grenade = go.GetComponent<NetworkGrenade>();
            if (grenade != null)
                grenade.ServerInit(NetworkObject, direction * force);
        }
    }
}
