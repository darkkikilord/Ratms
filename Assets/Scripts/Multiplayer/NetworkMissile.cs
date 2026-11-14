using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Misil en MULTIJUGADOR. Es un NetworkObject que vive en la escena tras
    ///  ser disparado por un NetworkBazooka.
    ///
    ///  Autoridad: EL SERVIDOR.
    ///   • El servidor simula la física (gravedad, fuerza inicial, colisiones).
    ///   • Los clientes lo ven volar gracias al NetworkTransform. Por eso
    ///     desactivamos el Rigidbody2D en clientes: nos interesa que solo
    ///     el servidor lo mueva de verdad.
    ///
    ///  Explosión con daño en área:
    ///   • En vez de dañar solo al collider que toca (como el missile single),
    ///     al impactar hacemos un OverlapCircle para encontrar TODOS los
    ///     NetworkHealth en el radio y a cada uno le aplicamos daño con
    ///     falloff lineal (más cerca = más daño).
    ///   • Esto hace que las granadas y futuras armas de área funcionen igual.
    ///
    ///  Safety:
    ///   • Autodestrucción a los X segundos por si se va al limbo.
    ///   • Ignora al propio lanzador durante el primer medio segundo.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class NetworkMissile : NetworkBehaviour
    {
        [Header("Daño")]
        [SerializeField] private int maxDamage = 50;
        [SerializeField] private float explosionRadius = 2.5f;
        [Tooltip("Layers que recibirán daño por la explosión.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Viento")]
        [Tooltip("Cuánto le afecta el viento del turno (0 = nada, 1 = todo). El misil es ligero y vuela mucho rato: 1.")]
        [SerializeField] private float windFactor = 1f;

        [Header("Safety")]
        [SerializeField] private float selfDestructSeconds = 8f;
        [SerializeField] private float ownerIgnoreTime = 0.5f;


        [Header("Impacto — empujón y cámara")]
        [Tooltip("Fuerza máxima del empujón a una rata pegada al centro de la explosión. Cae con la distancia con falloff sqrt (más 'kick' a media distancia).")]
        [SerializeField] private float knockbackForce = 32f;

        [Tooltip("Empujón MÍNIMO que recibe una rata alcanzada por la explosión, aunque esté en el borde. 0 = solo falloff. Sube esto si quieres que cualquier impacto se note.")]
        [SerializeField] private float minKnockback = 8f;

        [Tooltip("Segundos durante los cuales la rata ignora su linearDamping tras un empujón, para que vuele de verdad en lugar de frenarse en medio segundo.")]
        [SerializeField] private float knockbackFreeFlight = 0.5f;

        [Tooltip("Amplitud del temblor de cámara al explotar (unidades de mundo).")]
        [SerializeField] private float shakeAmplitude = 0.28f;

        [Tooltip("Duración del temblor de cámara (segundos).")]
        [SerializeField] private float shakeDuration = 0.4f;

        [Tooltip("Tiempo que la cámara se queda mirando el punto de impacto antes de volver al dueño.")]
        [SerializeField] private float impactPauseSeconds = 1f;

        [Header("FX (opcional)")]
        [SerializeField] private GameObject explosionVfxPrefab;
        [SerializeField] private AudioClip explosionSfx;
        [SerializeField] private float explosionSfxVolume = 0.7f;

        /// <summary>
        ///  Tipo de proyectil: 0 = obús de bazuca, 1 = bomba de AVIÓN,
        ///  2 = bomba de NAPALM (explota poco y lo llena todo de fuego).
        ///  Lo decide el servidor ANTES del Spawn (ver <see cref="PrepareAsAirBomb"/>).
        /// </summary>
        public readonly NetworkVariable<byte> Variant = new(0);

        private byte pendingVariant;
        private bool shortenTurnOnImpact = true;

        private Rigidbody2D rb;
        private NetworkObject ownerNetObj;
        private float spawnTime;
        private bool exploded;

        // ==============================================================
        /// <summary>
        ///  (Servidor, ANTES de Spawn) Convierte el misil en bomba del ataque
        ///  aéreo. Las bombas de avión SÍ dañan a quien las pidió (cuidado
        ///  con pedirlas encima de tu rata) y no acortan el turno: caen varias.
        /// </summary>
        public void PrepareAsAirBomb(bool napalm)
        {
            pendingVariant = (byte)(napalm ? 2 : 1);
            shortenTurnOnImpact = false;
            ownerIgnoreTime = 0f;
            windFactor = 0.3f;
            if (napalm) { maxDamage = 12; explosionRadius = 1.1f; knockbackForce = 10f; minKnockback = 4f; }
            else { maxDamage = 32; explosionRadius = 1.9f; knockbackForce = 22f; }
            impactPauseSeconds = 0.35f;
            shakeAmplitude *= 0.8f;
        }

        private void ApplySkin()
        {
            var sprite = Variant.Value == 1 ? WeaponArt.AirBomb() : Variant.Value == 2 ? WeaponArt.NapalmBomb() : WeaponArt.Missile();
            ProjectileSkin.Apply(gameObject, sprite, 0.85f, faceVelocity: true);
        }

        public override void OnNetworkSpawn()
        {
            rb = GetComponent<Rigidbody2D>();
            spawnTime = Time.time;
            if (IsServer && pendingVariant != 0) Variant.Value = pendingVariant;
            Variant.OnValueChanged += (_, __) => ApplySkin();

            // Los clientes no simulan física: dejamos que NetworkTransform
            // interpole la posición. Evita que el misil "rebote" distinto
            // en cada máquina.
            if (!IsServer && rb != null)
                rb.simulated = false;

            Sfx.Play(Variant.Value == 0 ? Sfx.Id.Launch : Sfx.Id.Whistle, Variant.Value == 0 ? 1f : 0.6f);
            // Obús de trinchera (o bomba de avión) que apunta a su trayectoria (local, en todos los clientes).
            ApplySkin();

            // Colisión CONTINUA: el terreno es una línea fina (EdgeCollider2D)
            // y un misil rápido podría saltársela entre dos pasos de física.
            if (IsServer && rb != null)
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // Avisamos a TODOS los clientes para que sus cámaras sigan al
            // proyectil mientras vuela (como en Worms). Cada cámara volverá
            // sola a su rata cuando el misil se destruya o caduque el seguimiento.
            // (Las bombas del avión no: caen cinco a la vez y la cámara iría loca.)
            if (IsServer && Variant.Value == 0)
                FollowWithCameraClientRpc(selfDestructSeconds);
        }

        [ClientRpc]
        private void FollowWithCameraClientRpc(float maxSeconds)
        {
            if (CameraFollow.Instance != null)
                CameraFollow.Instance.FollowTransform(transform, maxSeconds);
        }

        /// <summary>
        ///  Llamado por <see cref="NetworkBazooka.FireServerRpc"/> justo después
        ///  de spawnear el misil, en el servidor. Aplica la fuerza inicial y
        ///  guarda quién lo disparó para no autoexplotarle en la cara.
        /// </summary>
        public void ServerInit(NetworkObject owner, Vector2 impulse)
        {
            if (!IsServer) return;
            ownerNetObj = owner;
            if (rb != null)
                rb.AddForce(impulse, ForceMode2D.Impulse);
        }

        private float nextTrail;

        private void Update()
        {
            // Estela de humo (local, en todos los clientes).
            if (IsSpawned && Time.time >= nextTrail)
            {
                nextTrail = Time.time + 0.035f;
                Fx.TrailPuff(transform.position);
            }

            if (!IsServer || exploded) return;

            if (Time.time - spawnTime > selfDestructSeconds)
                Explode(transform.position);
        }

        /// <summary>
        ///  VIENTO: solo el servidor simula el misil, así que solo él empuja.
        ///  Multiplicamos por la masa para que sea una ACELERACIÓN (igual
        ///  para todos los proyectiles, pesen lo que pesen).
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsServer || exploded || rb == null || !rb.simulated) return;
            float accel = NetworkTurnManager.WindAcceleration * windFactor;
            if (Mathf.Abs(accel) > 0.0001f)
                rb.AddForce(new Vector2(accel * rb.mass, 0f), ForceMode2D.Force);
        }

        // ==============================================================
        // COLISIÓN
        // ==============================================================
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!IsServer || exploded) return;

            // Durante el primer medio segundo, ignoramos al propio lanzador
            if (ownerNetObj != null && (Time.time - spawnTime) < ownerIgnoreTime)
            {
                if (collision.transform.IsChildOf(ownerNetObj.transform)) return;
            }

            Vector3 pt = collision.contacts.Length > 0
                ? (Vector3)collision.contacts[0].point
                : transform.position;

            Explode(pt);
        }

        // ==============================================================
        // EXPLOSIÓN
        // ==============================================================
        private void Explode(Vector3 at)
        {
            if (!IsServer || exploded) return;
            exploded = true;

            // 1) Daño en área con falloff lineal.
            var hits = Physics2D.OverlapCircleAll(at, explosionRadius, damageMask);
            var alreadyDamaged = new HashSet<NetworkHealth>();

            // DEBUG: te dice en consola si la explosión está alcanzando o no
            // a alguna rata. Si ves "0 colliders" o "0 ratas en radio", tu
            // misil cayó lejos de las ratas y por eso no aparece popup.
            Debug.Log($"[NetworkMissile] Explosión en {at} — {hits.Length} colliders en radio {explosionRadius}");

            // AUTO-DAÑO (como en Worms): si te disparas a los pies, te comes
            // la explosión. Antes el lanzador quedaba excluido y los amigos
            // veían que un bazucazo a bocajarro no te hacía nada. Lo único que
            // se ignora es el CHOQUE con el lanzador en el primer medio
            // segundo (ownerIgnoreTime), para que el misil no reviente al salir.

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
                if (dmg <= 0) continue;

                Debug.Log($"[NetworkMissile] → Aplicando {dmg} de daño a {nh.gameObject.name} (dist={dist:F2}, t={t:F2})");
                nh.ApplyDamageServer(dmg, body, DamageType.Explosion, (Vector2)at);

                // KNOCKBACK: empuja a la rata alejándola del centro de la explosión.
                // Falloff SQRT (mucho más notorio a media distancia que el lineal):
                //   · centro: t ≈ 1        → empujón completo
                //   · mitad:  t ≈ 0.71     → con lineal habría sido 0.5
                //   · borde:  t ≈ 0        → solo queda minKnockback
                // Sumamos componente hacia arriba (+0.8) para que "salga volando"
                // en parábola en vez de raspar por el suelo — estilo Worms.
                Vector2 away = body - (Vector2)at;
                if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
                float kbT = Mathf.Sqrt(t);
                float magnitude = Mathf.Max(minKnockback, knockbackForce * kbT);

                // KNOCKBACK PROPORCIONAL AL DAÑO:
                //   · 25 dmg →  multiplicador 1.0 (impulso normal)
                //   · 50 dmg →  multiplicador ~1.41
                //   · 100 dmg →  multiplicador 2.0 (te manda el doble de lejos)
                //   · 9 dmg →   multiplicador 0.6
                // Usamos sqrt para que la curva no sea brutal — un golpe de
                // 100 no debería mandarte 4× más lejos que uno de 25.
                float damageScale = Mathf.Sqrt(Mathf.Max(1, dmg) / 25f);
                magnitude *= damageScale;

                Vector2 push = (away.normalized + Vector2.up * 0.8f).normalized * magnitude;
                nh.ApplyKnockbackClientRpc(push, knockbackFreeFlight);
            }

            // 2) Carvea el TERRENO DESTRUCTIBLE en red. El NetworkDestructibleTerrain
            //    propaga la orden a todos los clientes (incluido el host-as-client).
            if (NetworkDestructibleTerrain.Instance != null)
                NetworkDestructibleTerrain.Instance.ServerCarveAt(at, explosionRadius);

            // 2b) NAPALM: revienta en un charco de fuego.
            if (Variant.Value == 2)
                FireSystem.ServerIgnite((Vector2)at + Vector2.up * 0.15f, new Vector2(0f, 3.5f), 3.2f, 11, 4.5f, 7.5f);

            // 3) Notificamos VFX/SFX a todos los clientes
            ExplosionFxClientRpc(at, explosionRadius);

            // 4) Cámara: temblor + pausa dramática sobre el punto de impacto.
            //    Al caducar la pausa, la cámara vuelve sola a la rata del dueño.
            ExplosionCameraFxClientRpc(at, shakeAmplitude, shakeDuration, impactPauseSeconds);

            // 5) Aviso al dueño: "ya he impactado, puedes acortar tu timer de
            //    fin de turno". Así el siguiente jugador NO puede moverse
            //    mientras el misil está en vuelo (endTurnDelay es 3.5s por
            //    defecto), pero en cuanto explotamos, dejamos 1s de margen
            //    para ver la pausa dramática y cambiamos de turno.
            if (ownerNetObj != null && shortenTurnOnImpact)
            {
                var ownerMove = ownerNetObj.GetComponent<NetworkPlayerMovement>();
                if (ownerMove != null) ownerMove.ShortenEndTurnClientRpc(1f);
            }

            // 6) Fuera del mundo
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(destroy: true);
            else
                Destroy(gameObject);
        }

        [ClientRpc]
        private void ExplosionFxClientRpc(Vector3 at, float radius)
        {
            if (explosionVfxPrefab != null)
                Instantiate(explosionVfxPrefab, at, Quaternion.identity);

            if (explosionSfx != null)
                AudioSource.PlayClipAtPoint(explosionSfx, at, explosionSfxVolume);

            Fx.Explosion(at, radius);
            if (Variant.Value == 2) Fx.FireBurst(at, 1.2f);
            Sfx.Play(Sfx.Id.Explosion, Variant.Value == 0 ? 1f : 0.75f);

            // POPUP DE IMPACTO: SIEMPRE pintamos un "BOOM!" en el punto de la
            // explosión, aunque no haya rata dañada. Así el jugador siempre
            // ve feedback de su disparo (especialmente útil en testing solo).
            WorldDamagePopup.SpawnImpact(at, Variant.Value == 2 ? "¡FUSH!" : "BOOM!", new Color(1f, 0.6f, 0.1f));
        }

        [ClientRpc]
        private void ExplosionCameraFxClientRpc(Vector3 at, float amp, float seconds, float pause)
        {
            if (CameraFollow.Instance == null) return;
            CameraFollow.Instance.Shake(amp, seconds);
            CameraFollow.Instance.FocusOnImpact(at, pause);
        }

        // ==============================================================
        // Gizmos: ver el radio de explosión en el Editor
        // ==============================================================
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
