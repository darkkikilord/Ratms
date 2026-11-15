using System.Collections.Generic;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  PROYECTIL DEL ARSENAL DE TRINCHERA (0.21). Un solo prefab de red
    ///  (Gadget.network) que se comporta según su <see cref="Kind"/>:
    ///
    ///   · RatBomb    — rata kamikaze con dinamita: corre sola, salta lo que se
    ///                  le cruza; el dueño la revienta con ESPACIO (o a los 9 s).
    ///   · Mine       — se arma a los 2 s y explota si alguien se acerca (o si le
    ///                  llega otra explosión). Se queda en el mapa entre turnos.
    ///   · Pigeon     — vuela hacia el punto elegido esquivando el terreno por arriba.
    ///   · MortarShell— cae en picado y al chocar suelta metralla (Shrapnel).
    ///   · Arrow      — flecha: a la rata que toca le hace un EFECTO al azar
    ///                  (quemadura, veneno, ceguera o amputación); si falla, se clava.
    ///   · GasCan     — bidón: al reventar suelta una nube venenosa (GasSystem).
    ///   · Cheese     — queso apestoso: atrae a las ratas cercanas y luego revienta.
    ///   · HolyCheese — bomba bíblica de queso: ¡aleluya! y explosión enorme.
    ///
    ///  Como el resto de proyectiles: SOLO el servidor simula y decide (daño,
    ///  explosiones); los demás PCs ven la posición por el NetworkTransform y
    ///  dibujan el aspecto según el tipo (que viaja en <see cref="KindNet"/>).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class NetworkGadget : NetworkBehaviour
    {
        public enum Kind : byte
        {
            None = 0, RatBomb = 1, Mine = 2, Pigeon = 3, MortarShell = 4, Shrapnel = 5, Arrow = 6, GasCan = 7, Cheese = 8, HolyCheese = 9,
            // 0.28
            Banana = 10, BananaShard = 11, Molotov = 12, Fireball = 13, Homing = 14, Anvil = 15, Meteor = 16,
            RabidRat = 17, GrannyRat = 18, StinkRat = 19, Mole = 20,
        }

        /// <summary>¿Es una rata que anda sola?</summary>
        public static bool IsWalker(Kind k) => k == Kind.RatBomb || k == Kind.RabidRat || k == Kind.GrannyRat || k == Kind.StinkRat;

        /// <summary>0.28: aspecto de las ratas-arma (rabiosa, abuela, apestosa, topo).</summary>
        public static RatLook WalkerLook(Kind k)
        {
            var l = RatLook.Random(new System.Random((int)k * 97));
            switch (k)
            {
                case Kind.RabidRat: l.body = RatLook.BodyType.Flaca; l.fur = RatLook.FurType.Negra; l.eye = RatLook.EyeType.Demonio; l.hat = RatLook.HatType.Ninguno; l.mouth = RatLook.MouthType.Lengua; l.acc = RatLook.AccessoryType.Collar; break;
                case Kind.GrannyRat: l.body = RatLook.BodyType.Enana; l.fur = RatLook.FurType.Canosa; l.hat = RatLook.HatType.Lana; l.eye = RatLook.EyeType.Monoculo; l.acc = RatLook.AccessoryType.Mochila; l.mouth = RatLook.MouthType.Nada; break;
                case Kind.StinkRat: l.body = RatLook.BodyType.Gorda; l.fur = RatLook.FurType.Zombi; l.hat = RatLook.HatType.MascaraGas; l.acc = RatLook.AccessoryType.Ninguno; break;
                case Kind.Mole: l.body = RatLook.BodyType.Topo; l.hat = RatLook.HatType.Minero; l.acc = RatLook.AccessoryType.Ninguno; break;
            }
            l.oneArm = false; l.pegLeg = false;
            return l;
        }

        public readonly NetworkVariable<byte> KindNet = new(0);
        /// <summary>Equipo del que lo lanzó (para el color de la rata-bomba).</summary>
        public readonly NetworkVariable<int> TeamNet = new(0);
        /// <summary>Mina armada (parpadea en rojo).</summary>
        public readonly NetworkVariable<bool> Armed = new(false);
        private readonly NetworkVariable<float> fuseNet = new(0f);

        public Kind Type => (Kind)KindNet.Value;

        // ---------- Servidor ----------
        private Kind pendingKind;
        /// <summary>(Servidor) no mover la cámara al nacer (estampida, lluvias).</summary>
        public bool quietSpawn;
        private int anvilHits;
        private float nextAnvilHit, nextDig;
        private NetworkObject ownerRat;
        private Vector2 target;
        private int walkDir = 1;
        private float spawnTime;
        private bool exploded;
        private float lastX, stuckTime, nextHop;
        private int hops;
        private float triggerAt = -1f;
        private bool lured, choirDone;
        private Rigidbody2D rb;
        private Collider2D[] myCols;
        private Collider2D[] ownerCols;
        private bool ownerIgnored;
        private GameObject gadgetPrefab;   // para soltar metralla

        // ---------- Todos ----------
        private SpriteRenderer art;
        private Sprite[] walkFrames;
        private SpriteRenderer backpack;
        private float animT, nextSpark;
        private Vector3 lastPos;
        private static readonly List<NetworkGadget> live = new();

        /// <summary>Rata-bombas vivas (para que el dueño pueda reventar la suya con ESPACIO).</summary>
        public static NetworkGadget ActiveRatBombOf(ulong ownerRatId)
        {
            foreach (var g in live)
                if (g != null && g.IsSpawned && (g.Type == Kind.RatBomb || g.Type == Kind.RabidRat) && g.ownerRatIdNet.Value == ownerRatId) return g;
            return null;
        }
        private readonly NetworkVariable<ulong> ownerRatIdNet = new(ulong.MaxValue);

        // ==============================================================
        //  PREPARACIÓN (servidor, ANTES de Spawn)
        // ==============================================================
        public void Prepare(Kind kind, NetworkObject owner, GameObject prefabForShrapnel, Vector2 targetPoint = default, int dir = 1)
        {
            pendingKind = kind;
            ownerRat = owner;
            gadgetPrefab = prefabForShrapnel;
            target = targetPoint;
            walkDir = dir >= 0 ? 1 : -1;
        }

        public override void OnNetworkSpawn()
        {
            rb = GetComponent<Rigidbody2D>();
            myCols = GetComponentsInChildren<Collider2D>();
            spawnTime = Time.time;
            live.Add(this);
            if (IsServer)
            {
                KindNet.Value = (byte)pendingKind;
                var mv = ownerRat != null ? ownerRat.GetComponent<NetworkPlayerMovement>() : null;
                TeamNet.Value = mv != null ? mv.TeamIndex.Value : 0;
                ownerRatIdNet.Value = ownerRat != null ? ownerRat.NetworkObjectId : ulong.MaxValue;
                fuseNet.Value = FuseOf(pendingKind);
                SetupPhysics(pendingKind);
                IgnoreOwner(true);
                if (pendingKind != Kind.Shrapnel && pendingKind != Kind.Mine && pendingKind != Kind.BananaShard && pendingKind != Kind.Meteor && !quietSpawn)
                    FollowWithCameraClientRpc(IsWalker(pendingKind) ? 10f : 8f);
            }
            else if (rb != null) rb.simulated = false;
            KindNet.OnValueChanged += (_, __) => BuildLook();
            BuildLook();
            lastPos = transform.position;
            Sfx.Play(Type == Kind.Arrow ? Sfx.Id.Twang : Type == Kind.Pigeon ? Sfx.Id.Coo : Sfx.Id.Throw, 0.8f);
        }

        public override void OnNetworkDespawn() => live.Remove(this);

        private static float FuseOf(Kind k) => k switch
        {
            Kind.RatBomb => 9f,
            Kind.GasCan => 2.5f,
            Kind.Cheese => 6f,
            Kind.HolyCheese => 4f,
            Kind.Shrapnel => 2.5f,
            Kind.Pigeon => 9f,
            Kind.MortarShell => 8f,
            Kind.Arrow => 8f,
            Kind.Banana => 3.5f,
            Kind.BananaShard => 1.4f,
            Kind.Molotov => 8f,
            Kind.Fireball => 6f,
            Kind.Homing => 9f,
            Kind.Anvil => 7f,
            Kind.Meteor => 9f,
            Kind.RabidRat => 8f,
            Kind.GrannyRat => 7f,
            Kind.StinkRat => 6f,
            Kind.Mole => 3.2f,
            _ => 0f,   // mina: sin mecha
        };

        private void SetupPhysics(Kind k)
        {
            if (rb == null) return;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.gravityScale = 1f;
            rb.mass = 1f;
            PhysicsMaterial2D mat;
            switch (k)
            {
                case Kind.RatBomb:
                case Kind.RabidRat:
                case Kind.GrannyRat:
                case Kind.StinkRat:
                    mat = new PhysicsMaterial2D("RataBomba") { friction = 0f, bounciness = 0f };
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    break;
                case Kind.Banana:
                case Kind.BananaShard:
                    mat = new PhysicsMaterial2D("Platano") { friction = 0.4f, bounciness = 0.55f };
                    break;
                case Kind.Fireball:
                case Kind.Homing:
                    mat = new PhysicsMaterial2D("Vuela") { friction = 0f, bounciness = 0f };
                    rb.gravityScale = 0f;
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    break;
                case Kind.Anvil:
                    mat = new PhysicsMaterial2D("Yunque") { friction = 1f, bounciness = 0.2f };
                    rb.mass = 3f; rb.gravityScale = 2.2f;
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    break;
                case Kind.Meteor:
                    mat = new PhysicsMaterial2D("Meteorito") { friction = 0f, bounciness = 0f };
                    rb.gravityScale = 1.4f;
                    break;
                case Kind.Mole:
                    mat = new PhysicsMaterial2D("Topo") { friction = 0f, bounciness = 0f };
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    break;
                case Kind.Mine:
                    mat = new PhysicsMaterial2D("Mina") { friction = 1f, bounciness = 0f };
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    rb.linearDamping = 1.5f;
                    break;
                case Kind.Pigeon:
                    mat = new PhysicsMaterial2D("Paloma") { friction = 0f, bounciness = 0f };
                    rb.gravityScale = 0f;
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    break;
                case Kind.GasCan:
                case Kind.Cheese:
                case Kind.HolyCheese:
                    mat = new PhysicsMaterial2D("Botón") { friction = 0.6f, bounciness = 0.35f };
                    rb.mass = k == Kind.HolyCheese ? 1.4f : 1f;
                    break;
                default:
                    mat = new PhysicsMaterial2D("Seco") { friction = 0.8f, bounciness = 0f };
                    break;
            }
            rb.sharedMaterial = mat;
            if (myCols != null) foreach (var c in myCols) c.sharedMaterial = mat;
        }

        /// <summary>Que no choque con quien lo lanza al salir (se quita a los 0.4 s).</summary>
        private void IgnoreOwner(bool ignore)
        {
            if (ownerRat == null || myCols == null) return;
            if (ownerCols == null) ownerCols = ownerRat.GetComponentsInChildren<Collider2D>();
            foreach (var a in myCols)
                foreach (var b in ownerCols)
                    if (a != null && b != null) Physics2D.IgnoreCollision(a, b, ignore);
            ownerIgnored = ignore;
        }

        /// <summary>(Servidor) Impulso inicial (velocidad = impulso, masa 1).</summary>
        public void ServerInit(Vector2 impulse)
        {
            if (!IsServer || rb == null) return;
            rb.linearVelocity = impulse / Mathf.Max(0.01f, rb.mass);
        }

        // ==============================================================
        //  SIMULACIÓN (servidor)
        // ==============================================================
        private void FixedUpdate()
        {
            if (!IsServer || exploded || rb == null) return;
            float age = Time.time - spawnTime;
            if (ownerIgnored && age > 0.4f && !IsWalker(Type)) IgnoreOwner(false);

            // Al agua: se pierde ("glub")
            var water = FindFirstObjectByType<WaterDeathZone>();
            if (water != null && rb.position.y < water.SurfaceY - 0.2f)
            {
                SplashClientRpc(rb.position);
                Shorten(1.2f);
                Despawn();
                return;
            }

            switch (Type)
            {
                case Kind.RatBomb: WalkTick(); break;
                case Kind.RabidRat: WalkTick(4.4f, 9f); break;
                case Kind.GrannyRat: WalkTick(1.1f, 5.5f); break;
                case Kind.StinkRat: WalkTick(2.2f, 7f); break;
                case Kind.Pigeon: FlyTick(); break;
                case Kind.Homing: HomeTick(); break;
                case Kind.Mole: MoleTick(); break;
                case Kind.Meteor: case Kind.Banana: case Kind.Molotov:
                    rb.AddForce(new Vector2(NetworkTurnManager.WindAcceleration * 0.25f, 0f));
                    break;
                case Kind.GasCan: case Kind.MortarShell: case Kind.Shrapnel: case Kind.HolyCheese: case Kind.Cheese:
                    rb.AddForce(new Vector2(NetworkTurnManager.WindAcceleration * (Type == Kind.MortarShell ? 0f : 0.2f), 0f));
                    break;
                case Kind.Arrow:
                    rb.AddForce(new Vector2(NetworkTurnManager.WindAcceleration * 0.5f, 0f));
                    break;
            }
        }

        private void Update()
        {
            DrawTick();
            if (!IsServer || exploded) return;
            float age = Time.time - spawnTime;
            float fuse = fuseNet.Value;

            switch (Type)
            {
                case Kind.Mine:
                    if (!Armed.Value && age > 2f) { Armed.Value = true; BeepClientRpc(); }
                    // 0.22: antes hacía ~3 de daño (en 0,6 s la rata ya se había alejado
                    // casi al borde del radio). Ahora: mecha más corta, radio mayor y un
                    // mínimo de daño para quien la pisa (como la mina de Worms: duele).
                    if (Armed.Value && triggerAt < 0f && RatNear(0.95f)) { triggerAt = Time.time + 0.45f; BeepClientRpc(); }
                    if (triggerAt > 0f && Time.time >= triggerAt) Explode(transform.position, 2.4f, 45, 9f, minFrac: 0.45f);
                    return;
                case Kind.Cheese:
                    if (!lured && age > 1.4f)
                    {
                        lured = true;
                        NetworkTurnManager.Instance?.ServerLure(transform.position, 8f, fuse - age - 0.3f);
                        StinkClientRpc();
                    }
                    break;
                case Kind.HolyCheese:
                    if (!choirDone && age > fuse - 1.5f) { choirDone = true; ChoirClientRpc(); }
                    break;
            }

            // 0.28: la abuela y la apestosa revientan solas al llegar junto a una rata enemiga
            if ((Type == Kind.GrannyRat || Type == Kind.StinkRat) && age > 1f && EnemyNear(0.9f)) { OnFuseEnd(); return; }
            if (fuse > 0f && age >= fuse) OnFuseEnd();
        }

        private void OnFuseEnd()
        {
            Vector2 at = transform.position;
            switch (Type)
            {
                case Kind.RatBomb: Explode(at, 2.6f, 50, 10f); break;
                case Kind.RabidRat: Explode(at, 3.0f, 60, 12f); break;
                case Kind.GrannyRat: Explode(at, 3.2f, 60, 13f, big: true); break;
                case Kind.StinkRat:
                    GasSystem.ServerRelease(at + Vector2.up * 0.1f, 26);
                    Explode(at, 1.4f, 15, 5f);
                    break;
                case Kind.Mole: Explode(at, 2.4f, 50, 10f); break;
                case Kind.Banana:
                    SpawnChildren(Kind.BananaShard, at, 5, 5.5f);
                    Explode(at, 2.2f, 40, 9f);
                    break;
                case Kind.BananaShard: Explode(at, 1.5f, 25, 6f); break;
                case Kind.Molotov: MolotovBurst(at); break;
                case Kind.Fireball: Explode(at, 1.5f, 30, 7f); break;
                case Kind.Anvil: Shorten(1f); Despawn(); break;
                case Kind.GasCan:
                    GasSystem.ServerRelease(at + Vector2.up * 0.3f, 12);
                    Explode(at, 1.2f, 10, 4f);
                    break;
                case Kind.Cheese: Explode(at, 2.2f, 35, 8f); break;
                case Kind.HolyCheese: Explode(at, 6f, 100, 18f, big: true); break;
                case Kind.Shrapnel: Explode(at, 1.2f, 15, 4f, quiet: true); break;
                default: Explode(at, 1.6f, 25, 6f); break;   // paloma / proyectil que se ha perdido
            }
        }

        /// <summary>RATA-BOMBA: corre en su dirección; si se atasca, salta; si no puede, da la vuelta.</summary>
        private void WalkTick(float speed = 2.6f, float jump = 7f)
        {
            bool grounded = Physics2D.Raycast(rb.position + Vector2.down * 0.05f, Vector2.down, 0.3f, ~0).collider != null;
            Vector2 v = rb.linearVelocity;
            if (grounded) v.x = walkDir * speed;
            rb.linearVelocity = v;
            if (Mathf.Abs(rb.position.x - lastX) < 0.01f) stuckTime += Time.fixedDeltaTime; else stuckTime = 0f;
            lastX = rb.position.x;
            if (grounded && stuckTime > 0.18f && Time.time >= nextHop)
            {
                nextHop = Time.time + 0.5f;
                hops++;
                if (hops > 3) { walkDir = -walkDir; hops = 0; }   // una pared imposible: media vuelta
                rb.linearVelocity = new Vector2(walkDir * speed, jump);
                HopClientRpc();
            }
            else if (grounded && stuckTime == 0f) hops = 0;   // ya avanza: se olvidan los saltos fallidos
        }

        /// <summary>PALOMA: hacia el objetivo; si tiene terreno delante, sube.</summary>
        private void FlyTick()
        {
            Vector2 to = target - rb.position;
            if (to.magnitude < 0.45f) { Explode(rb.position, 2.2f, 40, 9f); return; }
            Vector2 dir = to.normalized;
            Vector2 v = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : dir;
            if (DestructibleTerrain.IsSolidAtWorld(rb.position + v * 1.3f) || DestructibleTerrain.IsSolidAtWorld(rb.position + dir * 1.0f))
                dir = (dir + Vector2.up * 2.2f).normalized;
            Vector2 wanted = dir * 6.5f;
            rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, wanted, 1f - Mathf.Exp(-4f * Time.fixedDeltaTime));
        }

        /// <summary>0.28 MISIL TELEDIRIGIDO: gira hacia el objetivo (no esquiva el terreno).</summary>
        private void HomeTick()
        {
            float age = Time.time - spawnTime;
            Vector2 to = target - rb.position;
            if (to.magnitude < 0.4f) { Explode(rb.position, 2.0f, 45, 9f); return; }
            Vector2 cur = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : Vector2.up;
            float turn = age < 0.4f ? 0f : 5f;   // primero sube un poco
            Vector2 dir = Vector2.Lerp(cur, to.normalized, 1f - Mathf.Exp(-turn * Time.fixedDeltaTime)).normalized;
            rb.linearVelocity = dir * Mathf.Min(10f, 4f + age * 6f);
        }

        /// <summary>0.28 TOPO: avanza por DENTRO del terreno cavando, hacia abajo y adelante.</summary>
        private void MoleTick()
        {
            Vector2 step = new Vector2(walkDir * 1.5f, -1.1f) * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + step);
            if (Time.time >= nextDig)
            {
                nextDig = Time.time + 0.12f;
                NetworkDestructibleTerrain.Instance?.ServerDig(rb.position, 0.42f);
            }
        }

        /// <summary>0.28 MOLOTOV: al romperse, lo llena todo de fuego.</summary>
        private void MolotovBurst(Vector2 at)
        {
            if (exploded) return;
            FireSystem.ServerIgnite(at + Vector2.up * 0.2f, Vector2.up * 1.8f, 3.2f, 16, 3.5f, 6.5f);
            Explode(at, 0.9f, 8, 3f, quiet: true);
        }

        private void SpawnChildren(Kind kind, Vector2 at, int n, float speed)
        {
            if (gadgetPrefab == null) return;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(-70f, 70f, n > 1 ? i / (float)(n - 1) : 0.5f) + Random.Range(-8f, 8f);
                Vector2 v = (Vector2)(Quaternion.Euler(0f, 0f, a) * Vector2.up) * speed * Random.Range(0.8f, 1.1f);
                var go = Instantiate(gadgetPrefab, (Vector3)(at + Vector2.up * 0.4f), Quaternion.identity);
                var g = go.GetComponent<NetworkGadget>();
                var no = go.GetComponent<NetworkObject>();
                if (g == null || no == null) { Destroy(go); continue; }
                g.Prepare(kind, ownerRat, gadgetPrefab);
                g.quietSpawn = true;
                no.Spawn(destroyWithScene: true);
                g.ServerInit(v);
            }
        }

        private bool EnemyNear(float r)
        {
            var ownH = ownerRat != null ? ownerRat.GetComponent<NetworkHealth>() : null;
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
                if (h != null && h != ownH && h.IsSpawned && !h.IsDead.Value && Vector2.Distance(h.BodyCenter, transform.position) < r)
                    return true;
            return false;
        }

        private bool RatNear(float r)
        {
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
                if (h != null && h.IsSpawned && !h.IsDead.Value && Vector2.Distance(h.BodyCenter, transform.position) < r)
                    return true;
            return false;
        }

        // ==============================================================
        //  CHOQUES (servidor)
        // ==============================================================
        private void OnCollisionEnter2D(Collision2D c)
        {
            if (!IsServer || exploded) return;
            Vector2 at = c.contactCount > 0 ? c.GetContact(0).point : (Vector2)transform.position;
            var nh = c.collider.GetComponentInParent<NetworkHealth>();
            switch (Type)
            {
                case Kind.MortarShell:
                    Explode(at, 2.0f, 35, 8f);
                    SpawnShrapnel(at);
                    break;
                case Kind.Shrapnel:
                    if (Time.time - spawnTime > 0.15f) Explode(at, 1.2f, 15, 4f, quiet: true);
                    break;
                case Kind.Pigeon:
                    Explode(at, 2.2f, 40, 9f);
                    break;
                // 0.28
                case Kind.Molotov:
                    if (Time.time - spawnTime > 0.1f) MolotovBurst(at);
                    break;
                case Kind.Fireball:
                    FireSystem.ServerIgnite(at, Vector2.up * 1.2f, 1f, 6, 3f, 5f);
                    Explode(at, 1.5f, 30, 7f);
                    break;
                case Kind.Homing:
                    Explode(at, 2.0f, 45, 9f);
                    break;
                case Kind.Meteor:
                    Explode(at, 1.6f, 25, 6f);
                    break;
                case Kind.Banana:
                case Kind.BananaShard:
                    BounceClientRpc();
                    break;
                case Kind.Anvil:
                    if (Time.time < nextAnvilHit) break;
                    nextAnvilHit = Time.time + 0.25f;
                    anvilHits++;
                    // Aplasta: daño a quien esté debajo y hunde el suelo (rebota y vuelve a caer)
                    foreach (var col in Physics2D.OverlapCircleAll(at, 1.2f))
                    {
                        var h = col != null ? col.GetComponentInParent<NetworkHealth>() : null;
                        if (h == null || h.IsDead.Value) continue;
                        h.ApplyDamageServer(anvilHits == 1 ? 40 : 20, h.BodyCenter, DamageType.Explosion, at + Vector2.up);
                        h.ApplyKnockbackClientRpc(new Vector2(Mathf.Sign(h.BodyCenter.x - at.x) * 2f, 1.5f), 0.2f);
                    }
                    NetworkDestructibleTerrain.Instance?.ServerCarveAt(at, 0.9f);
                    AnvilClientRpc(at);
                    if (anvilHits >= 4) { Shorten(1.2f); Despawn(); }
                    break;
                case Kind.Arrow:
                    if (nh != null && !nh.IsDead.Value) ArrowHit(nh, at);
                    else StickArrow();
                    break;
                case Kind.GasCan:
                case Kind.Cheese:
                case Kind.HolyCheese:
                    BounceClientRpc();
                    break;
            }
        }

        /// <summary>FLECHA en el cuerpo: daño y un efecto al azar.</summary>
        private void ArrowHit(NetworkHealth nh, Vector2 at)
        {
            exploded = true;
            Vector2 v = rb != null ? rb.linearVelocity : Vector2.right;
            nh.ApplyDamageServer(16, nh.BodyCenter, DamageType.Bullet, at);
            nh.ApplyKnockbackClientRpc(v.normalized * 2.5f + Vector2.up * 1.5f, 0.15f);
            int effect = Random.Range(0, 4);
            bool mutil = MatchRules.EffectiveMutilations;
            switch (effect)
            {
                case 0:   // quemadura
                    FireSystem.ServerIgnite(nh.BodyCenter, Vector2.up * 1.2f, 0.7f, 4, 2.5f, 4f);
                    nh.StatusClientRpc(1);
                    break;
                case 1:   // veneno
                    nh.PoisonServer(3);
                    break;
                case 2:   // ceguera (un ojo)
                    if (!mutil || !nh.InjureServer(NetworkHealth.ZoneHead)) nh.ApplyDamageServer(8, nh.BodyCenter, DamageType.Bullet);
                    nh.StatusClientRpc(2);
                    break;
                default:  // amputación
                    bool done = mutil && (nh.InjureServer(Random.value < 0.5f ? NetworkHealth.ZoneArms : NetworkHealth.ZoneLegs)
                                          || nh.InjureServer(NetworkHealth.ZoneArms) || nh.InjureServer(NetworkHealth.ZoneLegs));
                    if (!done) nh.ApplyDamageServer(10, nh.BodyCenter, DamageType.Bullet);
                    nh.StatusClientRpc(3);
                    break;
            }
            Shorten(1.5f);
            Despawn();
        }

        /// <summary>Flecha que falla: se queda clavada un momento y desaparece.</summary>
        private void StickArrow()
        {
            exploded = true;
            if (rb != null) { rb.linearVelocity = Vector2.zero; rb.simulated = false; }
            ThudClientRpc();
            Shorten(1f);
            Invoke(nameof(Despawn), 2f);
        }

        private void SpawnShrapnel(Vector2 at)
        {
            if (gadgetPrefab == null) return;
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Lerp(-60f, 60f, i / 3f) + Random.Range(-10f, 10f);
                Vector2 v = (Vector2)(Quaternion.Euler(0f, 0f, a) * Vector2.up) * Random.Range(4.5f, 6.5f);
                var go = Instantiate(gadgetPrefab, (Vector3)(at + Vector2.up * 0.35f), Quaternion.identity);
                var g = go.GetComponent<NetworkGadget>();
                var no = go.GetComponent<NetworkObject>();
                if (g == null || no == null) { Destroy(go); continue; }
                g.Prepare(Kind.Shrapnel, ownerRat, null);
                no.Spawn(destroyWithScene: true);
                g.ServerInit(v);
            }
        }

        // ==============================================================
        //  EXPLOSIÓN (servidor)
        // ==============================================================
        /// <summary>(Servidor) El dueño de la rata-bomba pide reventarla (ESPACIO otra vez).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void DetonateServerRpc(ServerRpcParams rpc = default)
        {
            if (exploded || (Type != Kind.RatBomb && Type != Kind.RabidRat) || ownerRat == null) return;
            if (ownerRat.OwnerClientId != rpc.Receive.SenderClientId) return;
            if (Type == Kind.RabidRat) Explode(transform.position, 3.0f, 60, 12f);
            else Explode(transform.position, 2.6f, 50, 10f);
        }

        private void OnEnable() => NetworkDestructibleTerrain.OnServerExplosion += OnOtherExplosion;
        private void OnDisable() => NetworkDestructibleTerrain.OnServerExplosion -= OnOtherExplosion;

        /// <summary>Una explosión cercana revienta las minas (en cadena, como los barriles).</summary>
        private void OnOtherExplosion(Vector2 at, float radius)
        {
            if (!IsServer || exploded || Type != Kind.Mine) return;
            if (Vector2.Distance(at, transform.position) > radius + 0.3f) return;
            if (triggerAt < 0f) triggerAt = Time.time + 0.25f;
        }

        private void Explode(Vector2 at, float radius, int maxDamage, float knockback, bool big = false, bool quiet = false, float minFrac = 0f)
        {
            if (!IsServer || exploded) return;
            exploded = true;
            var done = new HashSet<NetworkHealth>();
            foreach (var col in Physics2D.OverlapCircleAll(at, radius))
            {
                var nh = col != null ? col.GetComponentInParent<NetworkHealth>() : null;
                if (nh == null || done.Contains(nh) || nh.IsDead.Value) continue;
                done.Add(nh);
                Vector2 body = nh.BodyCenter;
                float t = Mathf.Clamp01(1f - nh.BlastDistance(at) / radius);
                if (t <= 0f) continue;
                int dmg = Mathf.CeilToInt(maxDamage * Mathf.Lerp(minFrac, 1f, t));
                if (dmg <= 0) continue;
                nh.ApplyDamageServer(dmg, body, DamageType.Explosion, at);
                Vector2 away = body - at;
                if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
                float scale = Mathf.Sqrt(Mathf.Max(1, dmg) / 25f);
                nh.ApplyKnockbackClientRpc((away.normalized + Vector2.up * 0.8f).normalized * knockback * Mathf.Max(0.35f, t) * scale, 0.5f);
            }
            if (NetworkDestructibleTerrain.Instance != null)
                NetworkDestructibleTerrain.Instance.ServerCarveAt(at, radius);
            ExplosionFxClientRpc(at, radius, big, quiet);
            Shorten(big ? 2.5f : Type == Kind.MortarShell ? 2.5f : 1.2f);
            Despawn();
        }

        private void Shorten(float seconds)
        {
            if (Type == Kind.Shrapnel || ownerRat == null) return;
            var mv = ownerRat.GetComponent<NetworkPlayerMovement>();
            if (mv != null) mv.ShortenEndTurnClientRpc(seconds);
        }

        private void Despawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(destroy: true);
            else Destroy(gameObject);
        }

        // ==============================================================
        //  EFECTOS (todos los PCs)
        // ==============================================================
        [ClientRpc]
        private void FollowWithCameraClientRpc(float seconds)
        {
            if (CameraFollow.Instance != null) CameraFollow.Instance.FollowTransform(transform, seconds);
        }

        [ClientRpc]
        private void ExplosionFxClientRpc(Vector2 at, float radius, bool big, bool quiet)
        {
            Fx.Explosion(at, radius);
            Sfx.Play(big ? Sfx.Id.BigExplosion : Sfx.Id.Explosion, quiet ? 0.5f : 1f);
            if (!quiet) WorldDamagePopup.SpawnImpact(at, big ? "¡ALELUYA!" : "¡BUM!", big ? new Color(1f, 0.92f, 0.4f) : new Color(1f, 0.45f, 0.15f));
            if (CameraFollow.Instance != null)
            {
                CameraFollow.Instance.Shake(big ? 0.9f : quiet ? 0.12f : 0.35f, big ? 1.1f : 0.45f);
                if (!quiet) CameraFollow.Instance.FocusOnImpact(at, big ? 2f : 1f);
            }
        }

        [ClientRpc] private void BeepClientRpc() => Sfx.Play(Sfx.Id.Beep, 0.6f, 0f);
        [ClientRpc]
        private void AnvilClientRpc(Vector2 at)
        {
            Sfx.Play(Sfx.Id.Whack, 1f, 0.05f);
            Fx.Dust(at, Vector2.up, 10);
            WorldDamagePopup.SpawnImpact(at + Vector2.up * 0.6f, "¡CLONK!", new Color(0.85f, 0.85f, 0.95f));
            CameraFollow.Instance?.Shake(0.3f, 0.3f);
        }
        [ClientRpc] private void BounceClientRpc() => Sfx.Play(Sfx.Id.Whack, 0.25f, 0.2f);
        [ClientRpc] private void HopClientRpc() => Sfx.Play(Sfx.Id.Squeak, 0.35f, 0.25f);
        [ClientRpc] private void ThudClientRpc() => Sfx.Play(Sfx.Id.Whack, 0.4f, 0.1f);

        [ClientRpc]
        private void SplashClientRpc(Vector2 at)
        {
            Fx.Splash(at);
            Sfx.Play(Sfx.Id.Splash, 0.6f);
            WorldDamagePopup.SpawnImpact(at + Vector2.up * 0.5f, "glub", Color.cyan);
        }

        [ClientRpc]
        private void StinkClientRpc()
        {
            WorldDamagePopup.SpawnImpact((Vector2)transform.position + Vector2.up * 0.9f, "¡QUÉ PESTE!", new Color(0.8f, 0.9f, 0.3f));
            Sfx.Play(Sfx.Id.Squeak, 0.6f, 0.3f);
        }

        [ClientRpc]
        private void ChoirClientRpc()
        {
            Sfx.Play(Sfx.Id.Choir, 1f, 0f);
            WorldDamagePopup.SpawnImpact((Vector2)transform.position + Vector2.up * 1.2f, "¡AAAAAAH!", new Color(1f, 0.95f, 0.6f));
        }

        // ==============================================================
        //  ASPECTO (todos los PCs)
        // ==============================================================
        private Kind builtKind;

        private void BuildLook()
        {
            if (Type == Kind.None || Type == builtKind) return;
            builtKind = Type;
            foreach (var old in GetComponents<ProjectileSkin>()) Destroy(old);
            if (art != null) Destroy(art.gameObject);
            if (backpack != null) Destroy(backpack.gameObject);
            art = null;

            Sprite s; float size = 0.8f; bool face = false; float spin = 0f;
            switch (Type)
            {
                case Kind.RatBomb:
                    var look = RatLook.Random(new System.Random((int)(NetworkObjectId * 7919)));
                    walkFrames = RatAppearance.WalkFrames(look.Pack(), RatAppearance.ColorForTeam(TeamNet.Value));
                    s = walkFrames[0]; size = 1f;
                    break;
                case Kind.Mine: s = WeaponArt.Mine(); size = 0.8f; break;
                case Kind.Pigeon: s = WeaponArt.Pigeon(0); size = 0.9f; break;
                case Kind.MortarShell: s = WeaponArt.AirBomb(); size = 0.8f; face = true; break;
                case Kind.Shrapnel: s = WeaponArt.Bomblet(); size = 0.6f; spin = 260f; break;
                case Kind.Arrow: s = WeaponArt.Arrow(); size = 0.9f; face = true; break;
                case Kind.GasCan: s = WeaponArt.GasCan(); spin = 120f; break;
                case Kind.Cheese: s = WeaponArt.Cheese(); spin = 140f; break;
                case Kind.HolyCheese: s = WeaponArt.HolyCheese(); size = 1f; spin = 90f; break;
                // 0.28
                case Kind.RabidRat: case Kind.GrannyRat: case Kind.StinkRat: case Kind.Mole:
                    walkFrames = RatAppearance.WalkFrames(WalkerLook(Type).Pack(), RatAppearance.ColorForTeam(TeamNet.Value));
                    s = walkFrames[0]; size = Type == Kind.GrannyRat ? 0.9f : 1f;
                    break;
                case Kind.Banana: s = WeaponArt.Banana(); size = 0.9f; spin = 300f; break;
                case Kind.BananaShard: s = WeaponArt.Banana(); size = 0.6f; spin = 420f; break;
                case Kind.Molotov: s = WeaponArt.Molotov(); size = 0.8f; spin = 260f; break;
                case Kind.Fireball: s = WeaponArt.Fireball(); size = 0.9f; spin = 200f; break;
                case Kind.Homing: s = WeaponArt.Missile(); size = 0.8f; face = true; break;
                case Kind.Anvil: s = WeaponArt.Anvil(); size = 1.1f; break;
                case Kind.Meteor: s = WeaponArt.Meteor(); size = 1f; spin = 90f; break;
                default: return;
            }
            ProjectileSkin.Apply(gameObject, s, size, face, spin);
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.name == "Arte" && sr.sprite == s) art = sr;

            if ((Type == Kind.RatBomb || Type == Kind.RabidRat) && art != null)
            {
                // Dinamita a la espalda
                var go = new GameObject("Dinamita");
                go.transform.SetParent(art.transform, false);
                go.transform.localPosition = new Vector3(-0.05f, 0.42f, 0f);
                go.transform.localScale = Vector3.one * 0.55f;
                backpack = go.AddComponent<SpriteRenderer>();
                backpack.sprite = WeaponArt.Dynamite();
                backpack.sortingOrder = art.sortingOrder + 1;
            }
        }

        private void DrawTick()
        {
            if (art == null) return;
            Vector3 p = transform.position;
            Vector3 d = p - lastPos;
            lastPos = p;
            animT += Time.deltaTime;
            switch (Type)
            {
                case Kind.GrannyRat: case Kind.StinkRat: case Kind.Mole:
                    if (walkFrames != null && walkFrames.Length > 0)
                    {
                        art.sprite = walkFrames[(int)(animT * (Type == Kind.GrannyRat ? 5f : 10f)) % walkFrames.Length];
                        if (Mathf.Abs(d.x) > 0.0005f) art.flipX = d.x < 0f;
                    }
                    if (Type == Kind.StinkRat && Time.time >= nextSpark) { nextSpark = Time.time + 0.15f; Fx.Sparks(art.transform.position + Vector3.up * 0.4f, 1, new Color(0.6f, 0.85f, 0.3f, 0.8f), 0.3f, 0.8f); }
                    if (Type == Kind.Mole && Time.time >= nextSpark) { nextSpark = Time.time + 0.1f; Fx.Dust(art.transform.position, Vector2.up, 2); }
                    break;
                case Kind.Fireball: case Kind.Meteor:
                    if (Time.time >= nextSpark) { nextSpark = Time.time + 0.04f; Fx.Sparks(art.transform.position, 2, new Color(1f, 0.6f, 0.15f, 1f), 0.4f, 1.4f); }
                    break;
                case Kind.Homing:
                    if (Time.time >= nextSpark) { nextSpark = Time.time + 0.05f; Fx.Sparks(art.transform.position, 1, new Color(0.75f, 0.75f, 0.75f, 0.8f), 0.2f, 0.8f); }
                    break;
                case Kind.RatBomb:
                case Kind.RabidRat:
                    if (walkFrames != null && walkFrames.Length > 0)
                    {
                        art.sprite = walkFrames[(int)(animT * 10f) % walkFrames.Length];
                        if (Mathf.Abs(d.x) > 0.0005f) art.flipX = d.x < 0f;
                        if (backpack != null) backpack.transform.localPosition = new Vector3(art.flipX ? 0.05f : -0.05f, 0.42f, 0f);
                    }
                    if (Time.time >= nextSpark)
                    {
                        nextSpark = Time.time + 0.08f;
                        Fx.Sparks(art.transform.position + Vector3.up * 0.75f, 2, new Color(1f, 0.85f, 0.3f, 1f), 0.6f, 1.6f);
                    }
                    break;
                case Kind.Pigeon:
                    art.sprite = WeaponArt.Pigeon((int)(animT * 9f));
                    if (Mathf.Abs(d.x) > 0.0005f) art.flipX = d.x < 0f;
                    break;
                case Kind.Mine:
                    // Piloto: parpadea en rojo cuando está armada
                    art.color = Armed.Value && ((int)(Time.time * (triggerAt > 0f ? 10f : 2.5f)) % 2 == 0) ? new Color(1f, 0.6f, 0.6f) : Color.white;
                    break;
            }
        }

        /// <summary>Cuenta atrás encima de lo que tiene mecha.</summary>
        private void OnGUI()
        {
            if (exploded || Camera.main == null || Event.current.type != EventType.Repaint) return;
            if (!IsWalker(Type) && Type != Kind.GasCan && Type != Kind.Cheese && Type != Kind.HolyCheese && Type != Kind.Banana && Type != Kind.Mole) return;
            float left = fuseNet.Value - (Time.time - spawnTime);
            if (left < 0f) return;
            Vector3 sp = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 0.7f);
            if (sp.z < 0f) return;
            var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(16 * Mathf.Max(0.6f, Screen.height / 1080f)), fontStyle = FontStyle.Bold };
            st.normal.textColor = new Color(1f, 0.9f, 0.2f);
            GUI.Label(new Rect(sp.x - 30f, Screen.height - sp.y - 12f, 60f, 24f), left.ToString("0.0"), st);
        }
    }
}
