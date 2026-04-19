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

        [Header("Safety")]
        [SerializeField] private float selfDestructSeconds = 8f;
        [SerializeField] private float ownerIgnoreTime = 0.5f;

        [Header("FX (opcional)")]
        [SerializeField] private GameObject explosionVfxPrefab;
        [SerializeField] private AudioClip explosionSfx;
        [SerializeField] private float explosionSfxVolume = 0.7f;

        private Rigidbody2D rb;
        private NetworkObject ownerNetObj;
        private float spawnTime;
        private bool exploded;

        // ==============================================================
        public override void OnNetworkSpawn()
        {
            rb = GetComponent<Rigidbody2D>();
            spawnTime = Time.time;

            // Los clientes no simulan física: dejamos que NetworkTransform
            // interpole la posición. Evita que el misil "rebote" distinto
            // en cada máquina.
            if (!IsServer && rb != null)
                rb.simulated = false;
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

        private void Update()
        {
            if (!IsServer || exploded) return;

            if (Time.time - spawnTime > selfDestructSeconds)
                Explode(transform.position);
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

            // 1) Daño en área con falloff lineal
            var hits = Physics2D.OverlapCircleAll(at, explosionRadius, damageMask);
            var alreadyDamaged = new HashSet<NetworkHealth>();

            // NetworkHealth del dueño del misil: lo EXCLUIMOS del daño en área
            // para evitar que un disparo hacia atrás te explote en la cara.
            // (Worms-style: nunca te daña TU propio misil.)
            NetworkHealth ownerHealth = null;
            if (ownerNetObj != null)
                ownerHealth = ownerNetObj.GetComponent<NetworkHealth>();

            foreach (var col in hits)
            {
                if (col == null) continue;
                var nh = col.GetComponentInParent<NetworkHealth>();
                if (nh == null || alreadyDamaged.Contains(nh)) continue;
                if (ownerHealth != null && nh == ownerHealth) continue; // no auto-daño
                alreadyDamaged.Add(nh);

                float dist = Vector2.Distance((Vector2)at, (Vector2)col.transform.position);
                float t = Mathf.Clamp01(1f - (dist / explosionRadius));
                int dmg = Mathf.CeilToInt(maxDamage * t);
                if (dmg <= 0) continue;

                nh.ApplyDamageServer(dmg, col.transform.position);
            }

            // 2) Notificamos VFX/SFX a todos los clientes
            ExplosionFxClientRpc(at);

            // 3) Fuera del mundo
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
