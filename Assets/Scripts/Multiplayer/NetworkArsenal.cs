using System.Collections;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ARSENAL AMPLIADO (0.18): las armas nuevas que no lanzan granadas.
    ///
    ///   • LANZALLAMAS (9): mantén ESPACIO. Escupe fuego líquido mientras
    ///     quede gasolina (<see cref="flameFuelSeconds"/>). Las llamas caen,
    ///     se quedan ardiendo y queman (ver <see cref="FireSystem"/>).
    ///   • METRALLETA (Q): ESPACIO suelta una ráfaga de balas que se abre un
    ///     poco; cada bala quita poco, pero si entran todas, duele.
    ///   • ATAQUE AÉREO (R) y NAPALM (F): clic en el mapa. Un biplano cruza el
    ///     cielo y suelta cinco bombas (o cinco bidones de napalm) sobre ese
    ///     punto. No hacen falta manos: se pide por radio.
    ///
    ///  Igual que el resto de armas: el DUEÑO pulsa, el SERVIDOR valida el
    ///  turno (<see cref="NetworkTurnManager.CanAct"/>) y hace el daño, y los
    ///  efectos se mandan a todos por ClientRpc.
    ///
    ///  Va en la RAÍZ del prefab de la rata (lo añade RatmsPrefabSetup).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class NetworkArsenal : NetworkBehaviour
    {
        [Header("Lanzallamas")]
        [Tooltip("Segundos de gasolina por uso (lo que dura el chorro manteniendo ESPACIO).")]
        [SerializeField] private float flameFuelSeconds = 2.2f;
        [Tooltip("Cada cuánto se manda un chorro de llamas al servidor (s).")]
        [SerializeField] private float flameInterval = 0.07f;
        [Tooltip("Velocidad de salida del fuego (u/s). Más = llega más lejos.")]
        [SerializeField] private float flameSpeed = 10.5f;

        [Header("Metralleta")]
        [SerializeField] private int burstBullets = 14;
        [SerializeField] private float burstInterval = 0.065f;
        [SerializeField] private float bulletRange = 15f;
        [Tooltip("Daño de cada bala (de cerca / de lejos).")]
        [SerializeField] private int bulletDamageNear = 4;
        [SerializeField] private int bulletDamageFar = 2;
        [Tooltip("Apertura de la ráfaga (grados a cada lado).")]
        [SerializeField] private float burstSpreadDeg = 5f;

        [Header("Ataque aéreo")]
        [SerializeField] private int strikeBombs = 5;
        [Tooltip("Separación entre bombas (u).")]
        [SerializeField] private float strikeSpacing = 0.8f;
        [SerializeField] private float planeSpeed = 10f;

        private WeaponsMenuUI weaponsMenu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        // Estado del dueño
        private bool flaming;
        private float fuelLeft, nextFlameSend;

        /// <summary>¿Está escupiendo fuego ahora mismo? (dueño)</summary>
        public bool Flaming => flaming;

        /// <summary>Gasolina que queda (0-1) para la barrita del HUD.</summary>
        public float Fuel01 => flaming ? Mathf.Clamp01(fuelLeft / flameFuelSeconds) : 1f;

        /// <summary>PRUEBA AUTOMÁTICA: punto del mapa donde "hace clic" el ataque aéreo.</summary>
        public Vector2? TestStrikeTarget { get; set; }

        private void Awake()
        {
            weaponsMenu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
        }

        private WeaponsMenuUI.WeaponId Equipped =>
            weaponsMenu != null ? weaponsMenu.SelectedWeapon : WeaponsMenuUI.WeaponId.None;

        /// <summary>¿Es un arma de este componente?</summary>
        public static bool Handles(WeaponsMenuUI.WeaponId id) =>
            id == WeaponsMenuUI.WeaponId.Flamethrower || id == WeaponsMenuUI.WeaponId.MachineGun ||
            id == WeaponsMenuUI.WeaponId.AirStrike || id == WeaponsMenuUI.WeaponId.Napalm;

        // ==============================================================
        //  ENTRADA (solo el dueño, lo llama NetworkPlayerMovement)
        // ==============================================================
        /// <summary>
        ///  Procesa ESPACIO / clic para el arma equipada. Devuelve true cuando
        ///  el arma se ha USADO (gasta el turno); <paramref name="endTurnIn"/>
        ///  dice cuántos segundos dejar antes de pasar el turno.
        /// </summary>
        public bool HandleInput(WeaponsMenuUI.WeaponId id, bool fireDown, bool fireUp, Vector2 dir, out float endTurnIn)
        {
            endTurnIn = 3.5f;
            if (!IsOwner) return false;
            switch (id)
            {
                case WeaponsMenuUI.WeaponId.Flamethrower:
                    if (fireDown && !flaming) { flaming = true; fuelLeft = flameFuelSeconds; nextFlameSend = 0f; }
                    if (!flaming) return false;
                    fuelLeft -= Time.deltaTime;
                    if (Time.time >= nextFlameSend)
                    {
                        nextFlameSend = Time.time + flameInterval;
                        FlameServerRpc(dir);
                    }
                    if (fireUp || fuelLeft <= 0f)
                    {
                        flaming = false;
                        endTurnIn = 3.5f;   // tiempo para ver arder (y huir)
                        return true;
                    }
                    return false;

                case WeaponsMenuUI.WeaponId.MachineGun:
                    if (!fireDown) return false;
                    BurstServerRpc(dir);
                    endTurnIn = burstBullets * burstInterval + 2f;
                    return true;

                case WeaponsMenuUI.WeaponId.AirStrike:
                case WeaponsMenuUI.WeaponId.Napalm:
                {
                    Vector2? target = TestStrikeTarget;
                    TestStrikeTarget = null;
                    if (target == null && (weaponsMenu == null || !weaponsMenu.IsVisible) && Input.GetMouseButtonDown(0) && Camera.main != null)
                        target = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    if (target == null) return false;
                    StrikeServerRpc(target.Value.x, movement == null || movement.FacingRight, id == WeaponsMenuUI.WeaponId.Napalm);
                    endTurnIn = 18f / planeSpeed + 5f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Se acabó el turno a medias: deja de echar fuego.</summary>
        public void CancelAll() => flaming = false;

        // ==============================================================
        //  LANZALLAMAS
        // ==============================================================
        private float lastFlameServer;

        [ServerRpc]
        private void FlameServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpc.Receive.SenderClientId, NetworkObjectId)) return;
            if (Time.time - lastFlameServer < flameInterval * 0.6f) return;   // anti-spam
            lastFlameServer = Time.time;

            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            Vector2 nozzle = Nozzle(dir);
            // Hereda un poco la velocidad de la rata (si corre, el chorro va con ella).
            FireSystem.ServerIgnite(nozzle, dir * flameSpeed + Vector2.up * 1.2f, 1.3f, 3, 2.2f, 4f);
            FlameFxClientRpc(nozzle, dir);
        }

        private Vector2 Nozzle(Vector2 dir)
        {
            Vector2 hand = movement != null ? (Vector2)movement.HandPosition : (ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position);
            return hand + dir * 0.6f;
        }

        private float nextFlameSound;

        [ClientRpc]
        private void FlameFxClientRpc(Vector2 at, Vector2 dir)
        {
            Fx.MuzzleFlash(at, dir);
            if (Time.time >= nextFlameSound)
            {
                nextFlameSound = Time.time + 0.22f;
                Sfx.Play(Sfx.Id.Flame, 0.8f);
            }
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.03f, 0.08f);
        }

        // ==============================================================
        //  METRALLETA
        // ==============================================================
        [ServerRpc]
        private void BurstServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpc.Receive.SenderClientId, NetworkObjectId)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            StartCoroutine(BurstRoutine(dir));
        }

        private IEnumerator BurstRoutine(Vector2 dir)
        {
            for (int i = 0; i < burstBullets; i++)
            {
                if (ownHealth != null && ownHealth.IsDead.Value) yield break;
                float spread = Random.Range(-burstSpreadDeg, burstSpreadDeg) * (0.5f + i / (float)burstBullets);   // se abre al final
                Vector2 d = Quaternion.Euler(0f, 0f, spread) * dir;
                Vector2 origin = (ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position) + dir * 0.2f;
                Vector2 end = origin + d * bulletRange;
                bool hitRat = false;
                if (FirstHit(origin, d, bulletRange, out var hit))
                {
                    end = hit.point;
                    var nh = hit.collider.GetComponentInParent<NetworkHealth>();
                    if (nh != null && !nh.IsDead.Value)
                    {
                        hitRat = true;
                        int dmg = Mathf.RoundToInt(Mathf.Lerp(bulletDamageNear, bulletDamageFar, hit.distance / bulletRange));
                        nh.ApplyDamageServer(dmg, nh.BodyCenter, DamageType.Generic, origin);
                        nh.ApplyKnockbackClientRpc((d + Vector2.up * 0.35f).normalized * 1.8f, 0.12f);
                    }
                    else if (i % 3 == 0 && NetworkDestructibleTerrain.Instance != null)
                    {
                        // Agujeritos (uno de cada tres balas: reconstruir el collider cuesta).
                        NetworkDestructibleTerrain.Instance.ServerCarveAt(hit.point, 0.28f);
                    }
                }
                BulletFxClientRpc(origin, end, hitRat);
                yield return new WaitForSeconds(burstInterval);
            }
        }

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

        private float recoil;

        [ClientRpc]
        private void BulletFxClientRpc(Vector2 from, Vector2 to, bool hitRat)
        {
            recoil = 1f;
            var go = new GameObject("Bala");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.035f; lr.endWidth = 0.015f;
            lr.startColor = new Color(1f, 0.95f, 0.6f, 1f);
            lr.endColor = new Color(1f, 0.75f, 0.3f, 0.5f);
            lr.positionCount = 2;
            lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.sortingOrder = 300;
            Destroy(go, 0.05f);

            Sfx.Play(Sfx.Id.MachineGun, 0.7f, 0.12f);
            Vector2 dir = (to - from).normalized;
            if (Random.value < 0.5f) Fx.MuzzleFlash(from, dir);
            Fx.Sparks(to, hitRat ? 4 : 3, new Color(1f, 0.85f, 0.4f, 1f), 1.5f, 4f);
            if (!hitRat) Fx.Dust(to, -dir, 3);
            else if (Fx.GoreOn) Fx.Blood(to, dir, 0.4f);
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.04f, 0.06f);
        }

        // ==============================================================
        //  ATAQUE AÉREO / NAPALM
        // ==============================================================
        [ServerRpc]
        private void StrikeServerRpc(float targetX, bool fromLeft, bool napalm, ServerRpcParams rpc = default)
        {
            var tm = NetworkTurnManager.Instance;
            if (tm != null && !tm.CanAct(rpc.Receive.SenderClientId, NetworkObjectId)) return;
            var bazooka = GetComponent<NetworkBazooka>();
            if (bazooka == null || bazooka.MissilePrefab == null) return;
            StartCoroutine(StrikeRoutine(targetX, fromLeft ? 1 : -1, napalm, bazooka.MissilePrefab));
        }

        /// <summary>
        ///  Altura del vuelo: 3.5 u por encima de lo MÁS ALTO que haya cerca del
        ///  objetivo (±6 u, islas incluidas). Así el avión sale en pantalla
        ///  junto al objetivo y las bombas no chocan con nada antes de tiempo.
        /// </summary>
        private static float SkyY(float targetX)
        {
            var t = FindFirstObjectByType<DestructibleTerrain>();
            if (t == null) return 12f;
            float top = t.transform.position.y + t.WorldSize.y;
            float highest = t.transform.position.y;
            for (float x = targetX - 6f; x <= targetX + 6f; x += 0.5f)
                for (float y = top; y > highest; y -= 0.25f)
                    if (DestructibleTerrain.IsSolidAtWorld(new Vector2(x, y))) { highest = y; break; }
            return Mathf.Min(top + 1.2f, highest + 3.5f);
        }

        private IEnumerator StrikeRoutine(float targetX, int dir, bool napalm, GameObject prefab)
        {
            float planeY = SkyY(targetX);
            float startX = targetX - dir * 18f;
            PlaneClientRpc(startX, planeY, dir, planeSpeed, napalm, targetX);

            // Las bombas caen RECTAS (solo el viento las desvía un poco): así
            // caen donde has hecho clic, que es lo que importa al jugar.
            const float bombVx = 0f;
            const float lead = 0f;

            float t0 = Time.time;
            for (int i = 0; i < strikeBombs; i++)
            {
                float x = targetX - dir * lead + dir * (i - (strikeBombs - 1) * 0.5f) * strikeSpacing;
                float when = (x - startX) * dir / planeSpeed;
                while (Time.time - t0 < when) yield return null;

                var go = Instantiate(prefab, new Vector3(x, planeY - 0.4f, 0f), Quaternion.Euler(0f, 0f, -90f));
                var no = go.GetComponent<NetworkObject>();
                var m = go.GetComponent<NetworkMissile>();
                if (no == null || m == null) { Destroy(go); continue; }
                m.PrepareAsAirBomb(napalm);
                no.Spawn(destroyWithScene: true);
                var rb = go.GetComponent<Rigidbody2D>();
                float mass = rb != null ? rb.mass : 1f;
                m.ServerInit(NetworkObject, new Vector2(dir * bombVx, -1.5f) * mass);
            }
        }

        [ClientRpc]
        private void PlaneClientRpc(float startX, float y, int dir, float speed, bool napalm, float targetX)
        {
            var go = new GameObject(napalm ? "AvionNapalm" : "AvionBombardero");
            var fly = go.AddComponent<PlaneFlyby>();
            fly.Init(new Vector2(startX, y), dir, speed, 36f / speed);
            Sfx.Play(Sfx.Id.Plane, 0.9f, 0f);
            // La cámara mira la ZONA del objetivo (no al avión): se le ve entrar
            // por arriba, soltar la carga y las explosiones.
            if (CameraFollow.Instance != null) CameraFollow.Instance.FocusOnImpact(new Vector3(targetX, y - 3.5f, 0f), 18f / speed + 0.8f);
            WorldDamagePopup.SpawnImpact(new Vector2(startX + dir * 18f, y - 1.5f), napalm ? "¡NAPALM!" : "¡ATAQUE AÉREO!", new Color(1f, 0.8f, 0.3f));
        }

        // ==============================================================
        //  DIBUJO: arma en la mano + mira del ataque aéreo (dueño)
        // ==============================================================
        private Transform held, reticle;
        private SpriteRenderer heldSr;
        private WeaponsMenuUI.WeaponId heldId = WeaponsMenuUI.WeaponId.None;

        private void LateUpdate()
        {
            var id = Equipped;
            bool inHand = (id == WeaponsMenuUI.WeaponId.Flamethrower || id == WeaponsMenuUI.WeaponId.MachineGun)
                          && movement != null && (ownHealth == null || !ownHealth.IsDead.Value);
            if (inHand && held == null)
            {
                var go = new GameObject("ArmaEnLaMano");
                heldSr = go.AddComponent<SpriteRenderer>();
                heldSr.sortingOrder = 60;
                held = go.transform;
                held.localScale = new Vector3(0.75f, 0.75f, 1f);
            }
            if (held != null)
            {
                if (held.gameObject.activeSelf != inHand) held.gameObject.SetActive(inHand);
                if (inHand)
                {
                    if (heldId != id)
                    {
                        heldId = id;
                        heldSr.sprite = id == WeaponsMenuUI.WeaponId.Flamethrower ? WeaponArt.Flamethrower() : WeaponArt.MachineGun();
                    }
                    Vector2 fwd = movement.WeaponForward;
                    Vector3 hand = movement.HandPosition;
                    recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 10f);
                    Vector2 back = -fwd * 0.06f * recoil;
                    held.position = new Vector3(hand.x + back.x, hand.y + back.y, 0f);
                    held.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg + (fwd.x >= 0f ? 1f : -1f) * 8f * recoil);
                    heldSr.flipY = fwd.x < 0f;
                    // Piloto encendido del lanzallamas.
                    if (id == WeaponsMenuUI.WeaponId.Flamethrower && Random.value < 0.25f)
                        Fx.Sparks((Vector2)hand + fwd * 0.6f, 1, new Color(1f, 0.6f, 0.15f, 1f), 0.2f, 0.6f);
                }
            }

            // Mira del ataque aéreo bajo el ratón (solo el dueño, en su turno).
            bool aiming = IsOwner && (id == WeaponsMenuUI.WeaponId.AirStrike || id == WeaponsMenuUI.WeaponId.Napalm)
                          && movement != null && movement.IsActiveTurnRat && !movement.FiredThisTurn && Camera.main != null;
            if (aiming && reticle == null)
            {
                var go = new GameObject("MiraAtaqueAereo");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = WeaponSprites.Ring();
                sr.sortingOrder = 900;
                reticle = go.transform;
            }
            if (reticle != null)
            {
                if (reticle.gameObject.activeSelf != aiming) reticle.gameObject.SetActive(aiming);
                if (aiming)
                {
                    Vector2 m = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    float pulse = 1.2f + 0.15f * Mathf.Sin(Time.time * 7f);
                    reticle.position = new Vector3(m.x, m.y, 0f);
                    reticle.localScale = new Vector3(pulse * 1.6f, pulse, 1f);
                    reticle.GetComponent<SpriteRenderer>().color = id == WeaponsMenuUI.WeaponId.Napalm
                        ? new Color(1f, 0.55f, 0.15f, 0.9f) : new Color(1f, 0.3f, 0.25f, 0.9f);
                }
            }
        }

        private void OnDisable()
        {
            if (held != null) held.gameObject.SetActive(false);
            if (reticle != null) reticle.gameObject.SetActive(false);
        }

        public override void OnDestroy()
        {
            if (held != null) Destroy(held.gameObject);
            if (reticle != null) Destroy(reticle.gameObject);
            base.OnDestroy();
        }
    }

    /// <summary>
    ///  El biplano que cruza la pantalla (solo visual, local). Deja estela de
    ///  humo y hace ruido de motor; se destruye solo al salir.
    /// </summary>
    public class PlaneFlyby : MonoBehaviour
    {
        private Vector2 pos;
        private int dir;
        private float speed, dieAt, nextPuff, nextDrone;

        public void Init(Vector2 start, int direction, float spd, float lifetime)
        {
            pos = start;
            dir = direction;
            speed = spd;
            dieAt = Time.time + lifetime;
            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = WeaponArt.Plane();
            sr.flipX = dir < 0;
            sr.sortingOrder = 80;
            transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            transform.position = pos;
        }

        private void Update()
        {
            pos.x += dir * speed * Time.deltaTime;
            // Cabeceo suave: así parece que vuela y no que se desliza.
            transform.position = new Vector3(pos.x, pos.y + Mathf.Sin(Time.time * 3f) * 0.08f, 0f);
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.3f) * 2.5f);
            if (Time.time >= nextPuff)
            {
                nextPuff = Time.time + 0.06f;
                Fx.TrailPuff((Vector2)transform.position - new Vector2(dir * 0.9f, 0f));
            }
            if (Time.time >= nextDrone)
            {
                nextDrone = Time.time + 1.6f;
                Sfx.Play(Sfx.Id.Plane, 0.5f, 0f);
            }
            if (Time.time >= dieAt) Destroy(gameObject);
        }
    }
}
