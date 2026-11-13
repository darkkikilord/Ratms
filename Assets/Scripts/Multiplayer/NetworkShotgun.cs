using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ESCOPETA — arma de disparo instantáneo (tecla 4).
    ///
    ///   • ESPACIO dispara en línea recta hacia donde apuntas (sin parábola,
    ///     sin viento). Hasta <see cref="shotsPerTurn"/> disparos por turno:
    ///     puedes reapuntar entre uno y otro.
    ///   • El SERVIDOR lanza un rayo: si da a una rata le quita vida (mucha
    ///     de cerca, poca de lejos) y la empuja un poco; si da al terreno
    ///     abre un agujerito.
    ///   • El dueño ve una MIRA de puntitos rojos hasta donde llegaría el tiro.
    ///
    ///  Va en la RAÍZ del prefab de la rata (lo añade RatmsPrefabSetup).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class NetworkShotgun : NetworkBehaviour
    {
        [Header("Disparo")]
        [SerializeField] private int shotsPerTurn = 2;
        [SerializeField] private float range = 11f;
        [Tooltip("Daño a quemarropa y a máxima distancia (cae linealmente entre medias).")]
        [SerializeField] private int damageNear = 35;
        [SerializeField] private int damageFar = 10;
        [Tooltip("Empujón del perdigonazo (la rata pesa 1).")]
        [SerializeField] private float knockback = 6f;
        [Tooltip("Radio (u) del agujerito que deja en el terreno.")]
        [SerializeField] private float holeRadius = 0.45f;

        public int ShotsPerTurn => shotsPerTurn;

        private WeaponsMenuUI weaponsMenu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private Transform gun;
        private readonly Transform[] sightDots = new Transform[10];
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        public bool IsEquipped => weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Shotgun;

        private void Awake()
        {
            weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
        }

        // ==============================================================
        // Disparo
        // ==============================================================
        /// <summary>Dispara (solo el dueño). Devuelve true si se ha pedido al servidor.</summary>
        public bool Fire(Vector2 dir)
        {
            if (!IsOwner || !IsEquipped) return false;
            FireServerRpc(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right);
            return true;
        }

        [ServerRpc]
        private void FireServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.IsTurnOf(rpc.Receive.SenderClientId)) return;

            Vector2 origin = Origin();
            Vector2 end = origin + dir * range;
            bool hitRat = false;

            if (FirstHit(origin, dir, range, out var hit))
            {
                end = hit.point;
                Debug.Log($"[NetworkShotgun] Disparo desde ({origin.x:F2},{origin.y:F2}) dir ({dir.x:F2},{dir.y:F2}) → da en '{hit.collider.name}' a {hit.distance:F2} u");
                var nh = hit.collider.GetComponentInParent<NetworkHealth>();
                if (nh != null && !nh.IsDead.Value)
                {
                    hitRat = true;
                    float t = Mathf.Clamp01(hit.distance / range);
                    int dmg = Mathf.RoundToInt(Mathf.Lerp(damageNear, damageFar, t));
                    nh.ApplyDamageServer(dmg, nh.BodyCenter, DamageType.Generic);
                    nh.ApplyKnockbackClientRpc((dir + Vector2.up * 0.25f).normalized * knockback, 0.4f);
                }
                else if (NetworkDestructibleTerrain.Instance != null)
                {
                    NetworkDestructibleTerrain.Instance.ServerCarveAt(hit.point, holeRadius);
                }
            }
            ShotFxClientRpc(origin, end, hitRat);
        }

        /// <summary>Desde el cuerpo, un poco hacia delante (igual en servidor y en la mira).</summary>
        private Vector2 Origin()
        {
            Vector2 body = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            Vector2 fwd = movement != null ? movement.WeaponForward : Vector2.right;
            return body + fwd * 0.15f;
        }

        /// <summary>Primer collider sólido en la línea, ignorando a la propia rata.</summary>
        private bool FirstHit(Vector2 origin, Vector2 dir, float dist, out RaycastHit2D first)
        {
            var filter = new ContactFilter2D();
            filter.NoFilter();
            filter.useTriggers = false;
            int n = Physics2D.Raycast(origin, dir, filter, hits, dist);
            first = default;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || c.transform == transform || c.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; first = hits[i]; }
            }
            return best < float.MaxValue;
        }

        [ClientRpc]
        private void ShotFxClientRpc(Vector2 from, Vector2 to, bool hitRat)
        {
            // Trazo del disparo que se desvanece en un instante
            var go = new GameObject("ShotTracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.05f; lr.endWidth = 0.02f;
            lr.startColor = new Color(1f, 0.95f, 0.6f, 1f);
            lr.endColor = new Color(1f, 0.7f, 0.3f, 0.6f);
            lr.positionCount = 2;
            lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.sortingOrder = 300;
            Destroy(go, 0.09f);

            Sfx.Play(Sfx.Id.Shot);
            Fx.Sparks(from, 6, new Color(1f, 0.9f, 0.5f, 1f), 1.5f, 3.5f);   // fogonazo de la boca
            Fx.Sparks(to, hitRat ? 10 : 7, new Color(1f, 0.85f, 0.4f, 1f), 2f, 5f);
            WorldDamagePopup.SpawnImpact(to, hitRat ? "¡PUM!" : "pum", new Color(1f, 0.85f, 0.35f));
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.08f, 0.12f);
        }

        // ==============================================================
        // Visual: escopeta en la mano + mira (solo el dueño)
        // ==============================================================
        private void LateUpdate()
        {
            bool show = IsEquipped && movement != null;
            if (gun == null && show) gun = MakeGun();
            if (gun != null && gun.gameObject.activeSelf != show) gun.gameObject.SetActive(show);

            bool sight = show && IsOwner && !movement.FiredThisTurn &&
                         (NetworkTurnManager.Instance == null || NetworkTurnManager.Instance.IsTurnOf(OwnerClientId));
            if (sight && sightDots[0] == null)
                for (int i = 0; i < sightDots.Length; i++) sightDots[i] = MakeDot(i);
            for (int i = 0; i < sightDots.Length; i++)
                if (sightDots[i] != null && sightDots[i].gameObject.activeSelf != sight) sightDots[i].gameObject.SetActive(sight);
            if (!show) return;

            Vector2 fwd = movement.WeaponForward;
            Vector3 hand = movement.HandPosition;
            gun.position = new Vector3(hand.x, hand.y, transform.position.z);
            gun.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg);
            gun.GetComponent<SpriteRenderer>().flipY = fwd.x < 0f;

            if (!sight) return;
            Vector2 origin = Origin();
            float reach = FirstHit(origin, fwd, range, out var h) ? h.distance : range;
            for (int i = 0; i < sightDots.Length; i++)
            {
                float d = (i + 1) / (float)sightDots.Length * reach;
                Vector2 p = origin + fwd * d;
                sightDots[i].position = new Vector3(p.x, p.y, 0f);
            }
        }

        private Transform MakeGun()
        {
            const int W = 18, H = 5;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color32 clear = new Color32(0, 0, 0, 0), metal = new Color32(70, 74, 82, 255), metalL = new Color32(120, 126, 136, 255),
                    wood = new Color32(130, 80, 44, 255), woodD = new Color32(96, 58, 30, 255), outline = new Color32(28, 24, 30, 255);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color32 c = clear;
                    if (x < 6 && y >= 0 && y <= 3) c = y == 0 ? woodD : wood;                 // culata
                    else if (x >= 6 && y >= 1 && y <= 3) c = y == 3 ? metalL : metal;         // cañones
                    if (c.a != 0 && (y == 0 && x >= 6)) c = outline;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            var go = new GameObject("ShotgunVisual");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.15f, 0.5f), 48f);
            sr.sortingOrder = 60;
            return go.transform;
        }

        private Transform MakeDot(int i)
        {
            var go = new GameObject("ShotgunSight");
            go.transform.localScale = Vector3.one * 0.06f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WeaponSprites.Dot();
            sr.color = new Color(1f, 0.3f, 0.25f, 0.85f - i * 0.05f);
            sr.sortingOrder = 900;
            return go.transform;
        }

        private void OnDisable()
        {
            if (gun != null) gun.gameObject.SetActive(false);
            foreach (var d in sightDots) if (d != null) d.gameObject.SetActive(false);
        }

        public override void OnDestroy()
        {
            if (gun != null) Destroy(gun.gameObject);
            foreach (var d in sightDots) if (d != null) Destroy(d.gameObject);
            base.OnDestroy();
        }
    }
}
