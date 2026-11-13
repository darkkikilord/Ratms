using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  TELETRANSPORTE — herramienta (tecla 6).
    ///
    ///   • Con él equipado, el dueño ve un MARCADOR donde está el ratón:
    ///     verde si puede aparecer ahí, rojo si no (dentro de la tierra, en
    ///     el agua, demasiado lejos o encima de otra rata).
    ///   • CLIC IZQUIERDO (o ESPACIO) → la rata desaparece y aparece en el
    ///     marcador. Gasta el arma del turno: tras teletransportarte el turno
    ///     acaba enseguida (como en Worms).
    ///
    ///  El servidor vuelve a comprobar el destino (el terreno es igual en
    ///  todos) y avisa a todos para los efectos; la rata la mueve su dueño,
    ///  que es quien simula su física.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class NetworkTeleport : NetworkBehaviour
    {
        [Tooltip("Distancia máxima (u) a la que puedes teletransportarte.")]
        [SerializeField] private float maxDistance = 30f;

        private WeaponsMenuUI weaponsMenu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private Transform marker;
        private SpriteRenderer markerSr;

        /// <summary>Si tiene valor, sustituye a la posición del ratón (solo AutoPlayTest).</summary>
        public Vector2? TestTarget { get; set; }

        public bool IsEquipped => weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Teleport;

        private void Awake()
        {
            weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
        }

        /// <summary>Punto apuntado: ratón (o el de la prueba), en el mundo.</summary>
        public Vector2 AimPoint()
        {
            if (TestTarget.HasValue) return TestTarget.Value;
            var cam = Camera.main;
            if (cam == null) return transform.position;
            Vector3 m = Input.mousePosition;
            m.z = -cam.transform.position.z;
            return cam.ScreenToWorldPoint(m);
        }

        /// <summary>
        ///  ¿Puede aparecer el CUERPO de la rata con su centro en <paramref name="body"/>?
        ///  Miramos varios puntos del cuerpo contra el terreno (píxeles) y que
        ///  no haya otra rata encima ni esté por debajo del agua.
        /// </summary>
        public bool IsValidTarget(Vector2 body, out string why)
        {
            why = null;
            Vector2 me = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            if (Vector2.Distance(me, body) > maxDistance) { why = "demasiado lejos"; return false; }
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            if (terrain != null && body.y < terrain.transform.position.y + 2f) { why = "agua"; return false; }
            for (int ix = -1; ix <= 1; ix++)
                for (int iy = -2; iy <= 2; iy++)
                    if (DestructibleTerrain.IsSolidAtWorld(body + new Vector2(ix * 0.12f, iy * 0.12f))) { why = "dentro de la tierra"; return false; }
            foreach (var c in Physics2D.OverlapCircleAll(body, 0.3f))
                if (c != null && !c.isTrigger && c.GetComponentInParent<NetworkHealth>() != null && !c.transform.IsChildOf(transform))
                { why = "hay otra rata"; return false; }
            return true;
        }

        /// <summary>Intenta teletransportarse al punto apuntado (solo el dueño).</summary>
        public bool TryTeleport()
        {
            if (!IsOwner || !IsEquipped) return false;
            Vector2 target = AimPoint();
            if (!IsValidTarget(target, out _)) return false;
            TeleportServerRpc(target);
            return true;
        }

        [ServerRpc]
        private void TeleportServerRpc(Vector2 body, ServerRpcParams rpc = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpc.Receive.SenderClientId, NetworkObjectId)) return;
            if (!IsValidTarget(body, out _)) return;
            Vector2 from = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            TeleportClientRpc(from, body);
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector2 from, Vector2 body)
        {
            Sfx.Play(Sfx.Id.Teleport);
            Fx.Sparks(from, 22, new Color(0.6f, 0.9f, 1f, 1f), 1f, 3.5f);
            Fx.Sparks(body, 22, new Color(0.7f, 1f, 0.9f, 1f), 1f, 3.5f);
            WorldDamagePopup.SpawnImpact(body + Vector2.up * 0.4f, "¡ZAS!", new Color(0.6f, 0.95f, 1f));

            if (!IsOwner) return;   // solo el dueño mueve su física
            var rb = GetComponent<Rigidbody2D>();
            Vector2 pivotToBody = ownHealth != null ? ownHealth.BodyCenter - (Vector2)transform.position : Vector2.zero;
            rb.position = body - pivotToBody;
            rb.linearVelocity = Vector2.zero;
            transform.position = rb.position;
            if (movement != null) movement.OnTeleported();
        }

        // ==============================================================
        // Marcador (solo el dueño, en su turno)
        // ==============================================================
        private void LateUpdate()
        {
            bool show = IsOwner && IsEquipped && movement != null && !movement.FiredThisTurn &&
                        (NetworkTurnManager.Instance == null || NetworkTurnManager.Instance.IsActiveRat(NetworkObjectId));
            if (marker == null)
            {
                if (!show) return;
                var go = new GameObject("TeleportMarker");
                go.transform.localScale = Vector3.one * 0.5f;
                markerSr = go.AddComponent<SpriteRenderer>();
                markerSr.sprite = WeaponSprites.Ring();
                markerSr.sortingOrder = 900;
                marker = go.transform;
            }
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
            if (!show) return;

            Vector2 p = AimPoint();
            marker.position = new Vector3(p.x, p.y, 0f);
            bool ok = IsValidTarget(p, out _);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 8f);
            markerSr.color = ok ? new Color(0.4f, 1f, 0.6f, pulse) : new Color(1f, 0.35f, 0.3f, pulse);
            marker.Rotate(0f, 0f, 90f * Time.deltaTime);
        }

        private void OnDisable() { if (marker != null) marker.gameObject.SetActive(false); }

        public override void OnDestroy()
        {
            if (marker != null) Destroy(marker.gameObject);
            base.OnDestroy();
        }
    }
}
