using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  BATE — arma cuerpo a cuerpo en MULTIJUGADOR (tecla 5).
    ///
    ///  Cómo funciona:
    ///   • Con el bate equipado, ESPACIO da un golpe (sin cargar).
    ///   • El SERVIDOR busca ratas en un círculo pequeño delante de la tuya,
    ///     en la dirección a la que apuntas (W/S cambian el ángulo).
    ///   • A cada una le hace daño (<see cref="damage"/>, tipo Melee) y le da
    ///     un EMPUJÓN muy fuerte en esa dirección: es el arma para tirar
    ///     ratas al agua. No rompe terreno.
    ///
    ///  Visual: un bate dibujado por código que la rata sujeta cuando está
    ///  equipado y que hace un barrido al golpear (lo ven todos los clientes).
    ///  No necesita sprites ni prefabs propios.
    ///
    ///  Va en la RAÍZ del prefab de la rata (junto a NetworkPlayerMovement).
    /// </summary>
    // Orden 100: nuestro LateUpdate corre DESPUÉS del de NetworkPlayerMovement
    // (que corrige giro y apuntado tras el Animator). Si no, leíamos la
    // dirección del arma "a medio corregir" y el bate apuntaba al revés.
    [DefaultExecutionOrder(100)]
    public class NetworkBat : NetworkBehaviour
    {
        [Header("Golpe")]
        [Tooltip("Daño a cada rata alcanzada.")]
        [SerializeField] private int damage = 50;

        [Tooltip("Distancia desde el centro de la rata hasta el centro de la zona de golpe (u).")]
        [SerializeField] private float reach = 0.32f;

        [Tooltip("Radio de la zona de golpe (u). La rata mide ~0.25 u.")]
        [SerializeField] private float hitRadius = 0.3f;

        [Tooltip("Fuerza del empujón (impulso; la rata pesa 1). Referencia: un misil en el centro de la explosión da ~18. " +
                 "Medido en la prueba automática: 18 → la diana voló ~30 u (medio mapa); la distancia crece con el CUADRADO de la fuerza.")]
        [SerializeField] private float knockbackForce = 14f;

        [Tooltip("Cuánto 'hacia arriba' sale la víctima además de en la dirección del golpe (0 = raso).")]
        [SerializeField] private float upwardBias = 0.45f;

        [Tooltip("Segundos de vuelo libre (sin fricción ni tope de velocidad) de la víctima.")]
        [SerializeField] private float freeFlightSeconds = 1.0f;

        [Header("Visual")]
        [SerializeField] private float swingSeconds = 0.22f;
        [Tooltip("Grados del barrido (de +arco/2 a -arco/2 respecto a donde apuntas).")]
        [SerializeField] private float swingArc = 120f;

        private WeaponsMenuUI weaponsMenu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private Transform visual;
        private SpriteRenderer visualRenderer;
        private float swingOffset;           // ángulo extra mientras dura el barrido
        private Coroutine swingRoutine;

        public bool IsBatEquipped =>
            weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Bat;

        // ==============================================================
        private void Awake()
        {
            weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
            BuildVisual();
        }

        /// <summary>Bate pixel-art generado por código: mango oscuro, cuerpo de madera.</summary>
        private void BuildVisual()
        {
            const int W = 24, H = 5;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            var clear = new Color32(0, 0, 0, 0);
            var grip = new Color32(60, 35, 20, 255);
            var wood = new Color32(196, 140, 80, 255);
            var woodDark = new Color32(150, 100, 55, 255);
            for (int x = 0; x < W; x++)
            {
                // Grosor: mango fino (1 px de alto en el centro) que engorda hacia la punta.
                int half = x < 7 ? 0 : (x < 14 ? 1 : 2);
                for (int y = 0; y < H; y++)
                {
                    int dy = Mathf.Abs(y - 2);
                    Color32 c = clear;
                    if (dy <= half) c = x < 7 ? grip : (dy == half && half > 0 ? woodDark : wood);
                    px[y * W + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            // Pivot en el mango (izquierda): así rota desde la mano.
            var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), 64f);   // 24 px → 0.375 u

            // SIN padre: vive en el mundo, así no hereda escalas ni volteos de
            // la rata. Lo colocamos y giramos nosotros cada frame.
            var go = new GameObject("BatVisual");
            visualRenderer = go.AddComponent<SpriteRenderer>();
            visualRenderer.sprite = sprite;
            visualRenderer.sortingOrder = 60;
            visual = go.transform;
            go.SetActive(false);
        }

        // ==============================================================
        // MIRA DEL BATE (solo la ve el dueño)
        // ==============================================================
        [Header("Mira")]
        [Tooltip("Si ON, el dueño ve un círculo donde golpeará el bate y unos puntos hacia donde saldrá volando la víctima.")]
        [SerializeField] private bool showAimIndicator = true;
        [SerializeField] private Color aimColor = new Color(1f, 0.85f, 0.25f, 0.9f);

        private Transform aimRing;
        private readonly Transform[] aimDots = new Transform[4];

        private void UpdateAimIndicator()
        {
            bool show = showAimIndicator && IsOwner && IsBatEquipped && movement != null && !movement.FiredThisTurn
                        && (NetworkTurnManager.Instance == null || NetworkTurnManager.Instance.IsTurnOf(OwnerClientId));
            if (aimRing == null)
            {
                if (!show) return;
                aimRing = MakeMarker("BatAimRing", WeaponSprites.Ring(), hitRadius * 2f, 0.85f);
                for (int i = 0; i < aimDots.Length; i++)
                    aimDots[i] = MakeMarker($"BatAimDot{i}", WeaponSprites.Dot(), Mathf.Lerp(0.085f, 0.05f, i / 3f), 1f - i * 0.18f);
            }
            if (aimRing.gameObject.activeSelf != show)
            {
                aimRing.gameObject.SetActive(show);
                foreach (var d in aimDots) d.gameObject.SetActive(show);
            }
            if (!show) return;

            // Misma cuenta que el golpe del servidor (centro del cuerpo + alcance).
            Vector2 dir = movement.WeaponForward;
            Vector2 origin = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            Vector2 center = origin + dir * reach;
            aimRing.position = new Vector3(center.x, center.y, transform.position.z);

            // Puntos: hacia dónde saldrá volando la víctima (misma fórmula que el golpe).
            Vector2 push = (dir + Vector2.up * upwardBias).normalized;
            for (int i = 0; i < aimDots.Length; i++)
            {
                Vector2 p = center + push * (hitRadius + 0.12f + i * 0.14f);
                aimDots[i].position = new Vector3(p.x, p.y, transform.position.z);
            }
        }

        /// <summary>Crea un marcador en el mundo (sin padre: así no hereda escalas raras de la rata).</summary>
        private Transform MakeMarker(string name, Sprite sprite, float worldSize, float alpha)
        {
            var go = new GameObject(name);
            go.transform.localScale = Vector3.one * worldSize;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var c = aimColor; c.a *= alpha;
            sr.color = c;
            sr.sortingOrder = 900;
            return go.transform;
        }

        public override void OnDestroy()
        {
            if (aimRing != null) Destroy(aimRing.gameObject);
            foreach (var d in aimDots) if (d != null) Destroy(d.gameObject);
            if (visual != null) Destroy(visual.gameObject);
            base.OnDestroy();
        }

        private void OnDisable()
        {
            // Rata muerta (se desactiva) → que no se quede nada flotando
            // (el bate y la mira viven en el mundo, no cuelgan de la rata).
            if (aimRing != null) aimRing.gameObject.SetActive(false);
            foreach (var d in aimDots) if (d != null) d.gameObject.SetActive(false);
            if (visual != null) visual.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            UpdateAimIndicator();
            if (visual == null) return;
            bool show = IsBatEquipped || swingRoutine != null;
            if (visual.gameObject.activeSelf != show) visual.gameObject.SetActive(show);
            if (!show || movement == null) return;

            // Seguimos la dirección del arma (apuntado + lado al que mira).
            Vector2 fwd = movement.WeaponForward;
            bool right = fwd.x >= 0f;
            float angle = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg + (right ? swingOffset : -swingOffset);
            // El mango en la MANO de la rata (donde sujeta el bazooka).
            Vector3 hand = movement.HandPosition;
            visual.position = new Vector3(hand.x, hand.y, transform.position.z);
            visual.rotation = Quaternion.Euler(0f, 0f, angle);
            // Si mira a la izquierda, volteamos en Y para que el bate no quede "boca abajo".
            visualRenderer.flipY = !right;
        }

        // ==============================================================
        // API que llama NetworkPlayerMovement (solo en el dueño)
        // ==============================================================
        /// <summary>Golpe. Devuelve true si se ha pedido al servidor.</summary>
        public bool Swing(Vector2 aimDirection)
        {
            if (!IsOwner || !IsBatEquipped) return false;
            Vector2 dir = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : Vector2.right;
            SwingServerRpc(dir);
            return true;
        }

        [ServerRpc]
        private void SwingServerRpc(Vector2 dir, ServerRpcParams rpcParams = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.IsTurnOf(rpcParams.Receive.SenderClientId)) return;

            // Desde el CUERPO de la rata (el pivote del prefab está bajo el suelo).
            Vector2 origin = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            Vector2 center = origin + dir * reach;
            var hits = Physics2D.OverlapCircleAll(center, hitRadius);
            var done = new HashSet<NetworkHealth>();
            int victims = 0;

            foreach (var col in hits)
            {
                if (col == null) continue;
                var nh = col.GetComponentInParent<NetworkHealth>();
                if (nh == null || nh == ownHealth || done.Contains(nh) || nh.IsDead.Value) continue;
                done.Add(nh);
                victims++;

                nh.ApplyDamageServer(damage, nh.BodyCenter, DamageType.Melee);

                // Empujón en la dirección del golpe, con algo de altura para
                // que "salga volando" en parábola (si no, raspa por el suelo).
                Vector2 push = (dir + Vector2.up * upwardBias).normalized * knockbackForce;
                nh.ApplyKnockbackClientRpc(push, freeFlightSeconds);
            }

            Debug.Log($"[NetworkBat] Golpe en ({center.x:0.00},{center.y:0.00}) r={hitRadius} → {hits.Length} colliders, {victims} ratas alcanzadas");
            SwingFxClientRpc(center, victims > 0);

            // Si has dado a alguien, acortamos tu fin de turno a lo justo para
            // ver volar a la víctima (el fallback largo es para cuando fallas).
            if (victims > 0 && movement != null)
                movement.ShortenEndTurnClientRpc(2f);
        }

        [ClientRpc]
        private void SwingFxClientRpc(Vector2 at, bool hitSomething)
        {
            if (swingRoutine != null) StopCoroutine(swingRoutine);
            swingRoutine = StartCoroutine(SwingAnim());

            if (hitSomething)
            {
                Fx.Sparks(at, 14, new Color(1f, 0.95f, 0.6f, 1f), 3f, 7f);
                WorldDamagePopup.SpawnImpact(at, "¡PAM!", new Color(1f, 0.85f, 0.2f));
                if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.2f, 0.25f);
            }
            else
            {
                WorldDamagePopup.SpawnImpact(at, "fiuu", new Color(0.85f, 0.85f, 0.85f));
            }
        }

        private IEnumerator SwingAnim()
        {
            float t = 0f;
            while (t < swingSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / swingSeconds);
                swingOffset = Mathf.Lerp(swingArc * 0.5f, -swingArc * 0.5f, 1f - (1f - k) * (1f - k));
                yield return null;
            }
            swingOffset = 0f;
            swingRoutine = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            Vector2 fwd = movement != null ? movement.WeaponForward : Vector2.right;
            Vector2 origin = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            Gizmos.DrawWireSphere(origin + fwd * reach, hitRadius);
        }
    }
}
