using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  CUERDA NINJA — herramienta de movimiento (tecla 7), muy Worms.
    ///
    ///   • ESPACIO: lanza la cuerda hacia donde apuntas (W/S). Si engancha en
    ///     el terreno (hasta <see cref="maxLength"/> u), la rata queda colgando.
    ///   • Colgando: A/D te balancean, W/S suben/bajan por la cuerda.
    ///   • ESPACIO o FLECHA ARRIBA: sueltas la cuerda y sales disparado con
    ///     el impulso que llevaras.
    ///   • NO gasta el arma del turno: puedes moverte con la cuerda y luego
    ///     disparar. Se suelta sola al acabar tu turno.
    ///
    ///  Física: la simula el DUEÑO con un DistanceJoint2D hacia el punto de
    ///  enganche. El enganche y si está colgando se publican por red
    ///  (NetworkVariables del dueño) para que todos dibujen la cuerda.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class NetworkNinjaRope : NetworkBehaviour
    {
        [Header("Cuerda")]
        [SerializeField] private float maxLength = 8f;
        [SerializeField] private float minLength = 0.6f;
        [Tooltip("Fuerza con la que A/D te balancean.")]
        [SerializeField] private float swingForce = 9f;
        [Tooltip("Velocidad (u/s) al subir/bajar por la cuerda con W/S.")]
        [SerializeField] private float climbSpeed = 2.5f;
        [Tooltip("Velocidad máxima colgando (para que no salga disparada a lo loco).")]
        [SerializeField] private float maxSwingSpeed = 14f;

        private readonly NetworkVariable<bool> attachedNet = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector2> anchorNet = new(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private WeaponsMenuUI weaponsMenu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private Rigidbody2D rb;
        private DistanceJoint2D joint;
        private LineRenderer line;
        private readonly Transform[] previewDots = new Transform[6];
        private readonly RaycastHit2D[] hits = new RaycastHit2D[12];

        public bool IsEquipped => weaponsMenu != null && weaponsMenu.SelectedWeapon == WeaponsMenuUI.WeaponId.Rope;
        /// <summary>true mientras la rata cuelga de la cuerda (en el dueño).</summary>
        public bool IsAttached => joint != null && joint.enabled;

        /// <summary>Punto de enganche actual (sincronizado).</summary>
        public Vector2 Anchor => anchorNet.Value;

        /// <summary>Longitud actual de la cuerda (en el dueño).</summary>
        public float Length => joint != null ? joint.distance : 0f;

        public float MaxLength => maxLength;

        private void Awake()
        {
            weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
            rb = GetComponent<Rigidbody2D>();
        }

        // ==============================================================
        // Enganchar / soltar (dueño)
        // ==============================================================
        /// <summary>Lanza la cuerda en <paramref name="dir"/>. true si ha enganchado.</summary>
        public bool TryAttach(Vector2 dir)
        {
            if (!IsOwner || !IsEquipped || IsAttached) return false;
            Vector2 origin = Body();
            if (!FirstTerrainHit(origin, dir.normalized, maxLength, out var hit)) return false;

            if (joint == null)
            {
                joint = gameObject.AddComponent<DistanceJoint2D>();
                joint.autoConfigureConnectedAnchor = false;
                joint.autoConfigureDistance = false;
                joint.enableCollision = true;
                joint.maxDistanceOnly = false;
            }
            // El ancla del cuerpo: el centro del collider en coordenadas locales.
            joint.anchor = transform.InverseTransformPoint(origin);
            joint.connectedBody = null;
            joint.connectedAnchor = hit.point;
            joint.distance = Mathf.Clamp(Vector2.Distance(origin, hit.point), minLength, maxLength);
            joint.enabled = true;

            attachedNet.Value = true;
            anchorNet.Value = hit.point;
            Fx.Sparks(hit.point, 6, new Color(0.9f, 0.85f, 0.7f, 1f), 1f, 2.5f);
            if (movement != null) movement.OnRopeAttachChanged();
            return true;
        }

        /// <summary>Suelta la cuerda conservando la velocidad.</summary>
        public void Release()
        {
            if (!IsOwner) return;
            if (joint != null) joint.enabled = false;
            if (attachedNet.Value) attachedNet.Value = false;
            if (movement != null) movement.OnRopeAttachChanged();
        }

        /// <summary>
        ///  Física mientras cuelga (lo llama NetworkPlayerMovement en su
        ///  FixedUpdate, en vez del control normal de andar).
        /// </summary>
        public void FixedTick(float inputX, float inputY)
        {
            if (!IsAttached) return;
            Vector2 anchor = joint.connectedAnchor;
            Vector2 toAnchor = anchor - Body();
            // Perpendicular a la cuerda, orientada hacia la DERECHA cuando el
            // ancla está encima: así D (+1) empuja a la derecha y A a la izquierda.
            Vector2 tangent = new Vector2(toAnchor.y, -toAnchor.x).normalized;
            if (Mathf.Abs(inputX) > 0.01f)
                rb.AddForce(tangent * inputX * swingForce * rb.mass, ForceMode2D.Force);
            if (Mathf.Abs(inputY) > 0.01f)
                joint.distance = Mathf.Clamp(joint.distance - inputY * climbSpeed * Time.fixedDeltaTime, minLength, maxLength);
            if (rb.linearVelocity.sqrMagnitude > maxSwingSpeed * maxSwingSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * maxSwingSpeed;
        }

        private Vector2 Body() => ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;

        /// <summary>Primer TERRENO en la línea (las ratas no sirven de enganche).</summary>
        private bool FirstTerrainHit(Vector2 origin, Vector2 dir, float dist, out RaycastHit2D first)
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
                if (c == null || c.GetComponentInParent<NetworkHealth>() != null) continue;
                if (c.attachedRigidbody != null && c.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic) continue;   // proyectiles
                if (hits[i].distance < best) { best = hits[i].distance; first = hits[i]; }
            }
            return best < float.MaxValue;
        }

        // ==============================================================
        // Visual: cuerda (todos) + previsualización (dueño)
        // ==============================================================
        private void LateUpdate()
        {
            // Si se acaba el turno (o muere), soltamos.
            if (IsOwner && IsAttached && movement != null && !movement.CanControlNow) Release();

            bool drawRope = attachedNet.Value;
            if (drawRope && line == null)
            {
                var go = new GameObject("NinjaRope");
                line = go.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startWidth = line.endWidth = 0.035f;
                line.startColor = line.endColor = new Color(0.82f, 0.72f, 0.5f, 1f);
                line.positionCount = 2;
                line.sortingOrder = 55;
            }
            if (line != null)
            {
                if (line.gameObject.activeSelf != drawRope) line.gameObject.SetActive(drawRope);
                if (drawRope)
                {
                    Vector3 hand = movement != null ? movement.HandPosition : transform.position;
                    line.SetPosition(0, new Vector3(hand.x, hand.y, 0f));
                    line.SetPosition(1, new Vector3(anchorNet.Value.x, anchorNet.Value.y, 0f));
                }
            }

            // Previsualización: puntos hasta donde engancharía (o hasta el máximo, en gris)
            bool preview = IsOwner && IsEquipped && !IsAttached && movement != null && movement.CanControlNow;
            if (preview && previewDots[0] == null)
                for (int i = 0; i < previewDots.Length; i++) previewDots[i] = MakeDot();
            Vector2 dir = movement != null ? movement.WeaponForward : Vector2.right;
            RaycastHit2D h = default;
            bool canHook = preview && FirstTerrainHit(Body(), dir, maxLength, out h);
            float reach = canHook ? h.distance : maxLength;
            for (int i = 0; i < previewDots.Length; i++)
            {
                var d = previewDots[i];
                if (d == null) continue;
                if (d.gameObject.activeSelf != preview) d.gameObject.SetActive(preview);
                if (!preview) continue;
                Vector2 p = Body() + dir * reach * (i + 1) / previewDots.Length;
                d.position = new Vector3(p.x, p.y, 0f);
                d.GetComponent<SpriteRenderer>().color = canHook ? new Color(0.95f, 0.85f, 0.55f, 0.9f) : new Color(0.7f, 0.7f, 0.7f, 0.45f);
            }
        }

        private Transform MakeDot()
        {
            var go = new GameObject("RopePreview");
            go.transform.localScale = Vector3.one * 0.07f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WeaponSprites.Dot();
            sr.sortingOrder = 900;
            return go.transform;
        }

        private void OnDisable()
        {
            if (joint != null) joint.enabled = false;
            if (line != null) line.gameObject.SetActive(false);
            foreach (var d in previewDots) if (d != null) d.gameObject.SetActive(false);
        }

        public override void OnDestroy()
        {
            if (line != null) Destroy(line.gameObject);
            foreach (var d in previewDots) if (d != null) Destroy(d.gameObject);
            base.OnDestroy();
        }
    }
}
