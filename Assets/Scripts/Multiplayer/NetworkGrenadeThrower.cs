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
            bool myTurn = (tm == null) || tm.IsTurnOf(OwnerClientId);
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
        // SERVIDOR: spawn de la granada
        // ==============================================================
        [ServerRpc]
        private void ThrowServerRpc(Vector3 spawnPos, Vector2 direction, float force, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || grenadePrefab == null) return;

            // Valida turno
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.IsTurnOf(rpcParams.Receive.SenderClientId))
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
