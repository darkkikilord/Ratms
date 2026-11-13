using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  GRANADA en MULTIJUGADOR.
    ///
    ///  Diferencias con el misil (<see cref="NetworkMissile"/>):
    ///   • NO explota al chocar. Al impactar REBOTA (Physics2D + PhysicsMaterial2D).
    ///   • Tiene un FUSIBLE (3s por defecto). Al llegar a 0, explota donde esté,
    ///     en el suelo, en el aire, como sea.
    ///   • Radio de explosión MAYOR que el misil (rompe más mapa — diseño).
    ///   • Muestra un contador de tiempo restante encima, para que el dueño
    ///     sepa cuándo explotará.
    ///
    ///  Autoridad: EL SERVIDOR. Los clientes solo ven (el Rigidbody2D está
    ///  deshabilitado en ellos, el NetworkTransform interpola posición).
    ///
    ///  Tras <see cref="maxBounces"/> rebotes subimos el drag para frenarla
    ///  en seco donde cayó, y esperamos a que se acabe el fusible.
    ///
    ///  A diferencia del misil, la granada SÍ puede dañar al lanzador.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class NetworkGrenade : NetworkBehaviour
    {
        [Header("Daño")]
        [SerializeField] private int maxDamage = 70;
        [SerializeField] private float explosionRadius = 3.5f;
        [Tooltip("Layers que recibirán daño por la explosión. A diferencia del misil, la granada PUEDE dañar al lanzador.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Fusible")]
        [Tooltip("Segundos tras el lanzamiento en los que explota sí o sí.")]
        [SerializeField] private float fuseSeconds = 3f;

        [Header("Rebote")]
        [Tooltip("Número máximo de rebotes antes de que la granada se frene. Tras este número, la granada se queda quieta hasta que se acabe el fusible.")]
        [SerializeField] private int maxBounces = 3;

        [Tooltip("Rozamiento lineal que aplicamos tras el último rebote para frenar la granada en seco.")]
        [SerializeField] private float stopDrag = 8f;

        [Header("Impacto — empujón y cámara")]
        [Tooltip("Fuerza máxima del empujón en el centro de la explosión. Cae con la distancia igual que el daño.")]
        [SerializeField] private float knockbackForce = 9.4f;

        [Tooltip("Amplitud del temblor de cámara (unidades de mundo).")]
        [SerializeField] private float shakeAmplitude = 0.35f;

        [Tooltip("Duración del temblor de cámara (segundos).")]
        [SerializeField] private float shakeDuration = 0.5f;

        [Tooltip("Segundos que la cámara se queda mirando el punto de impacto antes de volver a la rata del dueño.")]
        [SerializeField] private float impactPauseSeconds = 1f;

        [Header("FX (opcional)")]
        [SerializeField] private GameObject explosionVfxPrefab;
        [SerializeField] private AudioClip explosionSfx;
        [SerializeField] private float explosionSfxVolume = 0.8f;
        [Tooltip("Clip opcional para el bote (si es null, silencio).")]
        [SerializeField] private AudioClip bounceSfx;
        [SerializeField] private float bounceSfxVolume = 0.35f;

        [Header("Contador visible")]
        [Tooltip("Si está ON, todos ven encima de la granada/dinamita el tiempo que le queda.")]
        [SerializeField] private bool showCountdown = true;
        [SerializeField] private Color countdownColor = new Color(1f, 0.9f, 0.2f);

        [Header("Viento")]
        [Tooltip("Cuánto le afecta el viento a la GRANADA (0-1). Es pesada: poco. La dinamita ignora el viento.")]
        [SerializeField] private float windFactor = 0.25f;

        // ==============================================================
        // Estado sincronizado
        // ==============================================================
        /// <summary>
        ///  true = este proyectil es una DINAMITA (mismo prefab que la
        ///  granada, distinta configuración y dibujo). Lo pone el servidor
        ///  ANTES de spawnear, así llega a todos con el estado inicial.
        /// </summary>
        public readonly NetworkVariable<bool> IsDynamite = new(
            value: false,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>Duración de la mecha en segundos (para que los CLIENTES pinten bien la cuenta atrás).</summary>
        private readonly NetworkVariable<float> fuseNet = new(
            value: 3f,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        private Rigidbody2D rb;
        private NetworkObject ownerNetObj;
        private float spawnTime;
        private float timeRemaining;
        private bool exploded;
        private int bounceCount;
        private GameObject dynamiteVisual;
        private bool pendingDynamite;   // solo servidor: PrepareAsDynamite antes del Spawn
        private float nextFuseSpark;

        // ==============================================================
        /// <summary>
        ///  SOLO SERVIDOR, ANTES de <c>NetworkObject.Spawn()</c>: convierte
        ///  esta granada en DINAMITA. No rebota, no la empuja el viento, la
        ///  cámara no la sigue (el jugador tiene que huir) y explota más fuerte.
        /// </summary>
        public void PrepareAsDynamite(float fuse, int damage, float radius, float knockback)
        {
            // La NetworkVariable NO se puede escribir antes del Spawn (NGO avisa):
            // lo apuntamos y se escribe en OnNetworkSpawn del servidor, que es
            // justo antes de mandar el estado inicial a los clientes.
            pendingDynamite = true;
            fuseSeconds = fuse;
            maxDamage = damage;
            explosionRadius = radius;
            knockbackForce = knockback;
            windFactor = 0f;

            rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearDamping = 3f;
                rb.angularDamping = 10f;
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;   // se queda de pie
            }
            // Sin rebote: material "pegajoso" en todos los colliders.
            var sticky = new PhysicsMaterial2D("DynamiteNoBounce") { bounciness = 0f, friction = 1f };
            if (rb != null) rb.sharedMaterial = sticky;
            foreach (var c in GetComponentsInChildren<Collider2D>()) c.sharedMaterial = sticky;
        }

        public override void OnNetworkSpawn()
        {
            rb = GetComponent<Rigidbody2D>();
            spawnTime = Time.time;

            if (IsServer)
            {
                fuseNet.Value = fuseSeconds;
                if (pendingDynamite) IsDynamite.Value = true;
            }
            timeRemaining = fuseNet.Value;

            // En los clientes NO simulamos física (la autoridad es el servidor).
            // El NetworkTransform se encarga de interpolar la posición.
            if (!IsServer && rb != null)
                rb.simulated = false;

            // Colisión CONTINUA: el terreno son líneas finas (EdgeCollider2D).
            if (IsServer && rb != null)
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (IsDynamite.Value) ApplyDynamiteVisual();
            IsDynamite.OnValueChanged += OnDynamiteChanged;
            Sfx.Play(Sfx.Id.Throw, IsDynamite.Value ? 0.5f : 1f);

            // Pedimos a todas las cámaras que sigan a la granada mientras vuela
            // y rebota. La DINAMITA no: el jugador tiene que ver a su rata para
            // huir mientras arde la mecha.
            if (IsServer && !IsDynamite.Value)
                FollowWithCameraClientRpc(fuseSeconds + 0.5f);
        }

        public override void OnNetworkDespawn()
        {
            IsDynamite.OnValueChanged -= OnDynamiteChanged;
        }

        private void OnDynamiteChanged(bool _, bool now)
        {
            if (now) ApplyDynamiteVisual();
        }

        /// <summary>
        ///  Dibujo de la dinamita, generado por código (cero assets): cartucho
        ///  rojo con dos bandas, mecha marrón y chispa amarilla. Oculta el
        ///  sprite de granada del prefab.
        /// </summary>
        private void ApplyDynamiteVisual()
        {
            if (dynamiteVisual != null) return;

            foreach (var sr in GetComponentsInChildren<SpriteRenderer>()) sr.enabled = false;

            var sprite = WeaponSprites.Dynamite();   // mismo dibujo que la que llevas en la mano

            dynamiteVisual = new GameObject("DynamiteVisual");
            dynamiteVisual.transform.SetParent(transform, worldPositionStays: false);
            // Compensamos la escala del prefab para que mida lo mismo siempre.
            float s = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
            dynamiteVisual.transform.localScale = Vector3.one / s;
            var dsr = dynamiteVisual.AddComponent<SpriteRenderer>();
            dsr.sprite = sprite;
            dsr.sortingOrder = 50;
        }

        [ClientRpc]
        private void FollowWithCameraClientRpc(float maxSeconds)
        {
            if (CameraFollow.Instance != null)
                CameraFollow.Instance.FollowTransform(transform, maxSeconds);
        }

        /// <summary>
        ///  Llamado por <see cref="NetworkGrenadeThrower.ThrowServerRpc"/>
        ///  tras spawnear la granada en el servidor. Aplica el impulso inicial
        ///  y guarda quién la lanzó para no autoexplotarle encima.
        /// </summary>
        public void ServerInit(NetworkObject owner, Vector2 impulse)
        {
            if (!IsServer) return;
            ownerNetObj = owner;
            if (rb != null)
                rb.AddForce(impulse, ForceMode2D.Impulse);
        }

        private void Update()
        {
            // Todos descuentan (para pintar la cuenta atrás); solo el
            // servidor decide la explosión.
            timeRemaining = fuseNet.Value - (Time.time - spawnTime);

            // Mecha encendida de la dinamita: chispitas en la punta (local).
            if (IsDynamite.Value && Time.time >= nextFuseSpark)
            {
                nextFuseSpark = Time.time + 0.07f;
                Fx.Sparks((Vector2)transform.position + Vector2.up * 0.25f, 2, new Color(1f, 0.85f, 0.3f, 1f), 0.6f, 1.6f);
            }

            if (!IsServer || exploded) return;
            if (timeRemaining <= 0f)
                Explode(transform.position);
        }

        /// <summary>VIENTO (solo servidor, que es quien simula). Ver NetworkMissile.</summary>
        private void FixedUpdate()
        {
            if (!IsServer || exploded || rb == null || !rb.simulated) return;
            float accel = NetworkTurnManager.WindAcceleration * windFactor;
            if (Mathf.Abs(accel) > 0.0001f)
                rb.AddForce(new Vector2(accel * rb.mass, 0f), ForceMode2D.Force);
        }

        // ==============================================================
        // COLISIÓN: la granada REBOTA, no explota
        // ==============================================================
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!IsServer || exploded) return;

            // El PhysicsMaterial2D del Rigidbody se encarga del bote físico.
            // Nosotros solo contamos cuántos rebotes llevamos para frenarla
            // en seco tras el último, y hacer un FX de bote en los clientes.
            bounceCount++;
            if (IsDynamite.Value) return;   // la dinamita no "rebota": ni FX ni conteo

            Vector3 at = collision.contactCount > 0
                ? (Vector3)collision.GetContact(0).point
                : transform.position;

            BounceFxClientRpc(at);

            // Tras el último rebote, subimos drag para que se quede quieta
            // hasta que se acabe el fusible y explote donde ha caído.
            if (bounceCount >= maxBounces && rb != null)
            {
                rb.linearDamping = stopDrag;
                rb.angularDamping = stopDrag;
            }
        }

        [ClientRpc]
        private void BounceFxClientRpc(Vector3 at)
        {
            if (bounceSfx != null)
                AudioSource.PlayClipAtPoint(bounceSfx, at, bounceSfxVolume);
        }

        // ==============================================================
        // EXPLOSIÓN (igual que NetworkMissile pero con radio mayor)
        // ==============================================================
        private void Explode(Vector3 at)
        {
            if (!IsServer || exploded) return;
            exploded = true;

            // Daño en área con falloff lineal.
            // OJO: a diferencia del misil, la granada SÍ puede dañar al
            // lanzador. Si se te queda pegada a los pies, te revienta tú
            // mismo — es parte de la gracia del arma.
            var hits = Physics2D.OverlapCircleAll(at, explosionRadius, damageMask);
            var alreadyDamaged = new HashSet<NetworkHealth>();
            Debug.Log($"[NetworkGrenade] {(IsDynamite.Value ? "Dinamita" : "Granada")} explota en ({at.x:F2},{at.y:F2}) r={explosionRadius} → {hits.Length} colliders");

            foreach (var col in hits)
            {
                if (col == null) continue;
                var nh = col.GetComponentInParent<NetworkHealth>();
                if (nh == null || alreadyDamaged.Contains(nh)) continue;
                alreadyDamaged.Add(nh);

                // Distancia al CUERPO (no al pivote del prefab, que está bajo el suelo).
                Vector2 body = nh.BodyCenter;
                float dist = Vector2.Distance((Vector2)at, body);
                float t = Mathf.Clamp01(1f - (dist / explosionRadius));
                int dmg = Mathf.CeilToInt(maxDamage * t);
                Debug.Log($"[NetworkGrenade] {(IsDynamite.Value ? "Dinamita" : "Granada")} → {nh.gameObject.name} dist={dist:F2} dmg={dmg}");
                if (dmg <= 0) continue;

                nh.ApplyDamageServer(dmg, body, DamageType.Explosion);

                // KNOCKBACK con falloff y componente vertical para "saltar".
                // La granada empuja más que el misil — es un arma de control de zona.
                Vector2 away = body - (Vector2)at;
                if (away.sqrMagnitude < 0.0001f) away = Vector2.up;

                // KNOCKBACK PROPORCIONAL AL DAÑO (sqrt para que la curva sea suave):
                //   · 25 dmg → 1.0× | · 50 dmg → 1.41× | · 100 dmg → 2.0×
                float damageScale = Mathf.Sqrt(Mathf.Max(1, dmg) / 25f);
                Vector2 push = (away.normalized + Vector2.up * 0.8f).normalized
                               * (knockbackForce * t * damageScale);
                nh.ApplyKnockbackClientRpc(push);
            }

            // Carvea el TERRENO DESTRUCTIBLE en red. La granada rompe más
            // mapa que el misil — parte de su diseño como arma de control de zona.
            if (NetworkDestructibleTerrain.Instance != null)
                NetworkDestructibleTerrain.Instance.ServerCarveAt(at, explosionRadius);

            // FX a todos los clientes
            ExplosionFxClientRpc(at);

            // Cámara: temblor + pausa sobre el punto de impacto.
            ExplosionCameraFxClientRpc(at, shakeAmplitude, shakeDuration, impactPauseSeconds);

            // Aviso al dueño: "ya he explotado, acorta tu timer de fin de
            // turno". Con la granada esto importa MUCHO porque el fusible es
            // de 3s + rebotes: si no lo acortamos, el turno cambiaría antes
            // de la explosión y el receptor podría escapar.
            if (ownerNetObj != null)
            {
                var ownerMove = ownerNetObj.GetComponent<NetworkPlayerMovement>();
                if (ownerMove != null) ownerMove.ShortenEndTurnClientRpc(1f);
            }

            // Fuera del mundo
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(destroy: true);
            else
                Destroy(gameObject);
        }

        [ClientRpc]
        private void ExplosionFxClientRpc(Vector3 at)
        {
            if (explosionVfxPrefab != null)
                Instantiate(explosionVfxPrefab, at, Quaternion.identity);

            if (explosionSfx != null)
                AudioSource.PlayClipAtPoint(explosionSfx, at, explosionSfxVolume);

            Fx.Explosion(at, IsDynamite.Value ? Mathf.Max(explosionRadius, 4f) : explosionRadius);
            Sfx.Play(IsDynamite.Value ? Sfx.Id.BigExplosion : Sfx.Id.Explosion);

            // POPUP DE IMPACTO: feedback visual fijo aunque la granada
            // no le pegue a ninguna rata.
            if (IsDynamite.Value)
                WorldDamagePopup.SpawnImpact(at, "KABOOM!", new Color(1f, 0.25f, 0.1f));
            else
                WorldDamagePopup.SpawnImpact(at, "POW!", new Color(0.9f, 0.4f, 0.2f));
        }

        [ClientRpc]
        private void ExplosionCameraFxClientRpc(Vector3 at, float amp, float seconds, float pause)
        {
            if (CameraFollow.Instance == null) return;
            CameraFollow.Instance.Shake(amp, seconds);
            CameraFollow.Instance.FocusOnImpact(at, pause);
        }

        // ==============================================================
        // CONTADOR VISIBLE (OnGUI del dueño)
        // ==============================================================
        private void OnGUI()
        {
            // Antes solo lo veía el que la lanzaba, y además solo en el HOST
            // (ownerNetObj solo existe en el servidor y el fusible solo se
            // descontaba allí). Ahora lo ven TODOS: es información de la
            // partida (¿me da tiempo a huir?), como en Worms.
            if (!showCountdown || exploded) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;
            if (Camera.main == null) return;

            float t = Mathf.Max(0f, timeRemaining);
            Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 0.4f);
            if (screenPos.z < 0) return; // detrás de la cámara

            var prev = GUI.color;
            GUI.color = countdownColor;
            GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            Rect r = new Rect(screenPos.x - 30f, Screen.height - screenPos.y - 20f, 60f, 20f);
            GUI.Label(r, t.ToString("0.0"), style);
            GUI.color = prev;
        }

        // ==============================================================
        // Gizmos: ver el radio de explosión en el Editor
        // ==============================================================
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
