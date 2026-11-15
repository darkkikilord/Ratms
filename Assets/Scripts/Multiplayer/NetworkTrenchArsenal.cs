using System.Collections;
using System.Collections.Generic;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ARSENAL DE TRINCHERA (0.21), en la raíz de la rata (lo añade
    ///  RatmsPrefabSetup). Recoge la entrada del DUEÑO para las 12 armas nuevas
    ///  y pide al servidor que actúe:
    ///
    ///   · Con CARGA (mantener ESPACIO, se ve la parábola): mortero, bidón de
    ///     gas, queso apestoso, bomba bíblica de queso y arco.
    ///   · Al pulsar: rata-bomba (otra vez ESPACIO = ¡bum!), mina, francotirador
    ///     (mira láser), bayoneta y pala.
    ///   · Con CLIC en el mapa: paloma bomba y sacos terreros (con fantasma).
    ///
    ///  Los proyectiles son <see cref="NetworkGadget"/> (prefab Gadget.network,
    ///  lo busca en la lista de prefabs de red). El servidor valida siempre que
    ///  sea tu turno (<c>tm.CanAct</c>).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class NetworkTrenchArsenal : NetworkBehaviour
    {
        [Header("Carga (mantener ESPACIO)")]
        [SerializeField] private float chargeRate = 12f;
        [Tooltip("Fuerza máxima: arco / mortero / lanzables.")]
        [SerializeField] private float maxBow = 22f, maxMortar = 18f, maxThrow = 14f;

        [Header("Francotirador")]
        [SerializeField] private float sniperRange = 60f;
        [SerializeField] private int sniperDamage = 45;

        [Header("Bayoneta")]
        [SerializeField] private int bayonetDamage = 22;
        [SerializeField] private float bayonetReach = 0.7f, bayonetRadius = 0.6f, bayonetPush = 9f;

        [Header("Sacos terreros")]
        [SerializeField] private float sandbagW = 2.6f, sandbagH = 0.75f;

        private WeaponsMenuUI menu;
        private NetworkPlayerMovement movement;
        private NetworkHealth ownHealth;
        private TrajectoryDots dots;
        private bool charging;
        private float force;
        private WeaponsMenuUI.WeaponId chargingWeapon;
        private LineRenderer laser;
        private SpriteRenderer sandbagGhost;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        private void Awake()
        {
            menu = GetComponentInChildren<WeaponsMenuUI>();
            movement = GetComponent<NetworkPlayerMovement>();
            ownHealth = GetComponent<NetworkHealth>();
        }

        public static bool Handles(WeaponsMenuUI.WeaponId id) => id >= WeaponsMenuUI.WeaponId.RatBomb && id <= WeaponsMenuUI.WeaponId.Girder;

        /// <summary>¿Esta arma usa la parábola de puntos al apuntar?</summary>
        public static bool UsesTrajectory(WeaponsMenuUI.WeaponId id) =>
            id == WeaponsMenuUI.WeaponId.Mortar || id == WeaponsMenuUI.WeaponId.GasCan || id == WeaponsMenuUI.WeaponId.Cheese ||
            id == WeaponsMenuUI.WeaponId.HolyCheese || id == WeaponsMenuUI.WeaponId.Bow ||
            id == WeaponsMenuUI.WeaponId.Banana || id == WeaponsMenuUI.WeaponId.Molotov;

        private float MaxForce(WeaponsMenuUI.WeaponId id) =>
            id == WeaponsMenuUI.WeaponId.Bow ? maxBow : id == WeaponsMenuUI.WeaponId.Mortar ? maxMortar : maxThrow;

        private WeaponsMenuUI.WeaponId Equipped => menu != null ? menu.SelectedWeapon : WeaponsMenuUI.WeaponId.None;

        public void CancelAll() { charging = false; force = 0f; }

        /// <summary>Si tienes una rata-bomba corriendo, ESPACIO la revienta (aunque ya hayas "disparado").</summary>
        public bool TryDetonateRatBomb()
        {
            if (!IsOwner) return false;
            var g = NetworkGadget.ActiveRatBombOf(NetworkObjectId);
            if (g == null) return false;
            g.DetonateServerRpc();
            return true;
        }

        // ==============================================================
        //  ENTRADA DEL DUEÑO (la llama NetworkPlayerMovement)
        // ==============================================================
        /// <summary>Devuelve true cuando el arma se ha USADO (gasta munición y acaba el turno en <paramref name="endTurnIn"/> s).</summary>
        public bool HandleInput(WeaponsMenuUI.WeaponId id, bool fireDown, bool fireUp, Vector2 dir, out float endTurnIn)
        {
            endTurnIn = 3f;
            if (!IsOwner) return false;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;

            // ---- Con carga ----
            if (UsesTrajectory(id))
            {
                if (fireDown) { charging = true; force = 0f; chargingWeapon = id; }
                if (charging && chargingWeapon == id)
                {
                    force = Mathf.Min(force + chargeRate * Time.deltaTime, MaxForce(id));
                    if (fireUp)
                    {
                        charging = false;
                        LaunchServerRpc((byte)id, dir, Mathf.Max(1.5f, force));
                        endTurnIn = id == WeaponsMenuUI.WeaponId.HolyCheese || id == WeaponsMenuUI.WeaponId.Banana ? 7f : id == WeaponsMenuUI.WeaponId.Cheese ? 9f
                                  : id == WeaponsMenuUI.WeaponId.GasCan ? 6f : id == WeaponsMenuUI.WeaponId.Mortar ? 7f : 5f;
                        return true;
                    }
                }
                return false;
            }

            switch (id)
            {
                case WeaponsMenuUI.WeaponId.RatBomb:
                    if (!fireDown) return false;
                    DropServerRpc((byte)id, movement == null || movement.FacingRight);
                    endTurnIn = 11f;
                    return true;
                case WeaponsMenuUI.WeaponId.Mine:
                    if (!fireDown) return false;
                    DropServerRpc((byte)id, movement == null || movement.FacingRight);
                    endTurnIn = 3f;
                    return true;
                case WeaponsMenuUI.WeaponId.Sniper:
                    if (!fireDown) return false;
                    SniperServerRpc(dir);
                    endTurnIn = 2.5f;
                    return true;
                case WeaponsMenuUI.WeaponId.Bayonet:
                    if (!fireDown) return false;
                    BayonetServerRpc(dir);
                    endTurnIn = 2.5f;
                    return true;
                case WeaponsMenuUI.WeaponId.Shovel:
                    if (!fireDown) return false;
                    ShovelServerRpc(dir);
                    endTurnIn = 3f;
                    return true;
                // ---- 0.28: ratas que andan solas ----
                case WeaponsMenuUI.WeaponId.RabidRat:
                case WeaponsMenuUI.WeaponId.GrannyRat:
                case WeaponsMenuUI.WeaponId.StinkRat:
                case WeaponsMenuUI.WeaponId.Mole:
                case WeaponsMenuUI.WeaponId.Stampede:
                    if (!fireDown) return false;
                    DropServerRpc((byte)id, movement == null || movement.FacingRight);
                    endTurnIn = id == WeaponsMenuUI.WeaponId.Mole ? 6f : id == WeaponsMenuUI.WeaponId.Stampede ? 12f : 10f;
                    return true;
                // ---- 0.28: al instante con ESPACIO ----
                case WeaponsMenuUI.WeaponId.Pistol:
                case WeaponsMenuUI.WeaponId.Fireball:
                case WeaponsMenuUI.WeaponId.FirePunch:
                case WeaponsMenuUI.WeaponId.Prod:
                case WeaponsMenuUI.WeaponId.Kamikaze:
                case WeaponsMenuUI.WeaponId.Medkit:
                case WeaponsMenuUI.WeaponId.Parachute:
                case WeaponsMenuUI.WeaponId.Scales:
                case WeaponsMenuUI.WeaponId.Blowtorch:
                case WeaponsMenuUI.WeaponId.Drill:
                case WeaponsMenuUI.WeaponId.Earthquake:
                case WeaponsMenuUI.WeaponId.Meteors:
                    if (!fireDown) return false;
                    InstantServerRpc((byte)id, dir);
                    endTurnIn = id switch
                    {
                        WeaponsMenuUI.WeaponId.Parachute => 9f, WeaponsMenuUI.WeaponId.Meteors => 8f, WeaponsMenuUI.WeaponId.Fireball => 4f,
                        WeaponsMenuUI.WeaponId.Blowtorch => 5f, WeaponsMenuUI.WeaponId.Drill => 5f, WeaponsMenuUI.WeaponId.Earthquake => 5f,
                        WeaponsMenuUI.WeaponId.Pistol => 3f, _ => 3f,
                    };
                    return true;
                case WeaponsMenuUI.WeaponId.SuperJump:
                    if (!fireDown) return false;
                    {
                        // La física de la rata la lleva su dueño: el salto es aquí mismo.
                        var rb = GetComponent<Rigidbody2D>();
                        if (rb != null) rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.5f + (movement != null && movement.FacingRight ? 1.5f : -1.5f), 13f);
                        movement?.TestResetFallTracking();
                        InstantServerRpc((byte)id, dir);
                    }
                    endTurnIn = 6f;
                    return true;
                case WeaponsMenuUI.WeaponId.Homing:
                case WeaponsMenuUI.WeaponId.Anvil:
                case WeaponsMenuUI.WeaponId.MineStrike:
                case WeaponsMenuUI.WeaponId.Girder:
                case WeaponsMenuUI.WeaponId.Pigeon:
                case WeaponsMenuUI.WeaponId.Sandbags:
                {
                    Vector2? p = TestClickTarget;
                    TestClickTarget = null;
                    if (p == null && (menu == null || !menu.IsVisible) && !WeaponsMenuUI.MouseOverUI && Input.GetMouseButtonDown(0) && Camera.main != null)
                        p = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    if (p == null) return false;
                    if (id == WeaponsMenuUI.WeaponId.Sandbags || id == WeaponsMenuUI.WeaponId.Girder)
                    {
                        bool girder = id == WeaponsMenuUI.WeaponId.Girder;
                        if (!SandbagSpotOk(p.Value, girder)) { WorldDamagePopup.SpawnImpact(p.Value, "¡Ahí no cabe!", new Color(1f, 0.6f, 0.4f)); return false; }
                        SandbagsServerRpc(p.Value, girder);
                        endTurnIn = 1.5f;
                    }
                    else if (id != WeaponsMenuUI.WeaponId.Pigeon)
                    {
                        ClickServerRpc((byte)id, p.Value);
                        endTurnIn = id == WeaponsMenuUI.WeaponId.Homing ? 10f : id == WeaponsMenuUI.WeaponId.Anvil ? 7f : 6f;
                    }
                    else
                    {
                        PigeonServerRpc(p.Value);
                        endTurnIn = 11f;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Para la prueba automática: "clic" en este punto del mapa.</summary>
        public Vector2? TestClickTarget { get; set; }

        // ==============================================================
        //  AYUDAS VISUALES DEL DUEÑO: parábola, láser y fantasma de sacos
        // ==============================================================
        // Arma en la mano (la ven todos: el arma elegida viaja por la red)
        private Transform held, reticle;
        private SpriteRenderer heldSr;
        private WeaponsMenuUI.WeaponId heldId = WeaponsMenuUI.WeaponId.None;

        private static Sprite HeldSprite(WeaponsMenuUI.WeaponId id) => id switch
        {
            WeaponsMenuUI.WeaponId.RatBomb => WeaponArt.Dynamite(),
            WeaponsMenuUI.WeaponId.Mine => WeaponArt.Mine(),
            WeaponsMenuUI.WeaponId.Pigeon => WeaponArt.Pigeon(0),
            WeaponsMenuUI.WeaponId.Mortar => WeaponArt.Mortar(),
            WeaponsMenuUI.WeaponId.GasCan => WeaponArt.GasCan(),
            WeaponsMenuUI.WeaponId.Cheese => WeaponArt.Cheese(),
            WeaponsMenuUI.WeaponId.Bayonet => WeaponArt.Bayonet(),
            WeaponsMenuUI.WeaponId.Shovel => WeaponArt.Shovel(),
            WeaponsMenuUI.WeaponId.Sandbags => WeaponArt.Sandbags(),
            WeaponsMenuUI.WeaponId.HolyCheese => WeaponArt.HolyCheese(),
            WeaponsMenuUI.WeaponId.Sniper => WeaponArt.Sniper(),
            WeaponsMenuUI.WeaponId.Bow => WeaponArt.Bow(),
            WeaponsMenuUI.WeaponId.Banana => WeaponArt.Banana(),
            WeaponsMenuUI.WeaponId.Molotov => WeaponArt.Molotov(),
            WeaponsMenuUI.WeaponId.Fireball => WeaponArt.Fireball(),
            WeaponsMenuUI.WeaponId.Homing => WeaponArt.Missile(),
            WeaponsMenuUI.WeaponId.Pistol => WeaponArt.Pistol(),
            WeaponsMenuUI.WeaponId.FirePunch => WeaponArt.Fist(),
            WeaponsMenuUI.WeaponId.Prod => WeaponArt.Prod(),
            WeaponsMenuUI.WeaponId.Kamikaze => WeaponArt.Kamikaze(),
            WeaponsMenuUI.WeaponId.Medkit => WeaponArt.HealthCrate(),
            WeaponsMenuUI.WeaponId.SuperJump => WeaponArt.Spring(),
            WeaponsMenuUI.WeaponId.Parachute => WeaponArt.ParachuteIcon(),
            WeaponsMenuUI.WeaponId.Scales => WeaponArt.Scales(),
            WeaponsMenuUI.WeaponId.Blowtorch => WeaponArt.Blowtorch(),
            WeaponsMenuUI.WeaponId.Drill => WeaponArt.Drill(),
            WeaponsMenuUI.WeaponId.Girder => WeaponArt.Sandbags(),
            WeaponsMenuUI.WeaponId.Stampede => WeaponArt.Dynamite(),
            _ => null,
        };

        private void UpdateHeld(WeaponsMenuUI.WeaponId id)
        {
            bool inHand = Handles(id) && movement != null && (ownHealth == null || !ownHealth.IsDead.Value)
                          && (ownHealth == null || ownHealth.CanUseHands || !WeaponsMenuUI.NeedsHands(id));
            if (inHand && held == null)
            {
                var go = new GameObject("ArmaTrinchera");
                heldSr = go.AddComponent<SpriteRenderer>();
                heldSr.sortingOrder = 60;
                held = go.transform;
            }
            if (held == null) return;
            if (held.gameObject.activeSelf != inHand) held.gameObject.SetActive(inHand);
            if (!inHand) return;
            if (heldId != id) { heldId = id; heldSr.sprite = HeldSprite(id); }
            // Lo que se lanza o se deja en el suelo va derecho; lo que se empuña, apunta.
            bool aims = id == WeaponsMenuUI.WeaponId.Sniper || id == WeaponsMenuUI.WeaponId.Bow || id == WeaponsMenuUI.WeaponId.Bayonet
                        || id == WeaponsMenuUI.WeaponId.Mortar || id == WeaponsMenuUI.WeaponId.Shovel
                        || id == WeaponsMenuUI.WeaponId.Pistol || id == WeaponsMenuUI.WeaponId.Blowtorch || id == WeaponsMenuUI.WeaponId.Prod;
            Vector2 fwd = movement.WeaponForward;
            Vector3 hand = movement.HandPosition;
            held.position = new Vector3(hand.x, hand.y, 0f);
            held.localScale = Vector3.one * (aims ? 0.75f : 0.6f);
            if (aims)
            {
                held.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg);
                heldSr.flipY = fwd.x < 0f;
                heldSr.flipX = false;
            }
            else
            {
                held.rotation = Quaternion.identity;
                heldSr.flipY = false;
                heldSr.flipX = fwd.x < 0f;
            }
        }

        private void UpdateReticle(bool show)
        {
            if (show && reticle == null)
            {
                var go = new GameObject("MiraPaloma");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = WeaponSprites.Ring();
                sr.sortingOrder = 900;
                sr.color = new Color(1f, 0.9f, 0.6f, 0.9f);
                reticle = go.transform;
            }
            if (reticle == null) return;
            if (reticle.gameObject.activeSelf != show) reticle.gameObject.SetActive(show);
            if (!show) return;
            Vector2 m = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            reticle.position = new Vector3(m.x, m.y, 0f);
            reticle.localScale = Vector3.one * (0.6f + 0.08f * Mathf.Sin(Time.time * 6f));
        }

        public override void OnDestroy()
        {
            if (held != null) Destroy(held.gameObject);
            if (reticle != null) Destroy(reticle.gameObject);
            if (laser != null) Destroy(laser.gameObject);
            if (sandbagGhost != null) Destroy(sandbagGhost.gameObject);
            if (canopy != null) Destroy(canopy.gameObject);
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            bool mine = IsOwner && movement != null && movement.CanControlNow && !movement.FiredThisTurn;
            var id = Equipped;
            UpdateHeld(id);
            UpdateReticle(mine && (id == WeaponsMenuUI.WeaponId.Pigeon || id == WeaponsMenuUI.WeaponId.Homing || id == WeaponsMenuUI.WeaponId.Anvil
                                   || id == WeaponsMenuUI.WeaponId.MineStrike) && Camera.main != null);
            UpdateParachute();

            // Parábola (comparte los puntos del bazuca)
            if (dots == null)
            {
                var bz = GetComponent<NetworkBazooka>();
                dots = bz != null ? bz.TrajectoryDots : GetComponentInChildren<TrajectoryDots>(true);
            }
            if (dots != null && mine && UsesTrajectory(id))
            {
                Vector2 dir = movement.WeaponForwardPublic;
                dots.Show(charging ? Mathf.Max(0.5f, force) : MaxForce(id) * 0.5f, dir);
            }
            else if (dots != null && IsOwner && UsesTrajectory(id)) dots.Hide();   // ya disparada o fuera de turno

            // Mira láser del francotirador
            bool showLaser = mine && id == WeaponsMenuUI.WeaponId.Sniper;
            if (showLaser)
            {
                if (laser == null) laser = MakeLaser();
                Vector2 dir = movement.WeaponForwardPublic;
                Vector2 o = Origin(dir);
                Vector2 end = o + dir * sniperRange;
                if (FirstHit(o, dir, sniperRange, out var h)) end = h.point;
                laser.enabled = true;
                laser.SetPosition(0, o);
                laser.SetPosition(1, end);
            }
            else if (laser != null) laser.enabled = false;

            // Fantasma de los sacos terreros bajo el ratón
            bool girderG = id == WeaponsMenuUI.WeaponId.Girder;
            bool ghost = mine && (id == WeaponsMenuUI.WeaponId.Sandbags || girderG) && Camera.main != null;
            if (ghost)
            {
                if (sandbagGhost == null) sandbagGhost = MakeGhost();
                Vector2 p = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                sandbagGhost.enabled = true;
                sandbagGhost.transform.position = new Vector3(p.x, p.y, -1f);
                sandbagGhost.transform.localScale = girderG ? new Vector3(GirderW, GirderH, 1f) : new Vector3(sandbagW, sandbagH, 1f);
                sandbagGhost.color = SandbagSpotOk(p, girderG) ? new Color(0.85f, 0.75f, 0.5f, 0.5f) : new Color(1f, 0.3f, 0.25f, 0.45f);
            }
            else if (sandbagGhost != null) sandbagGhost.enabled = false;
        }

        private LineRenderer MakeLaser()
        {
            var go = new GameObject("MiraLaser");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.025f; lr.endWidth = 0.02f;
            lr.startColor = new Color(1f, 0.15f, 0.1f, 0.75f);
            lr.endColor = new Color(1f, 0.15f, 0.1f, 0.25f);
            lr.positionCount = 2;
            lr.sortingOrder = 400;
            return lr;
        }

        private static SpriteRenderer MakeGhost()
        {
            var go = new GameObject("FantasmaSacos");
            var sr = go.AddComponent<SpriteRenderer>();
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            sr.sprite = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            sr.sortingOrder = 400;
            return sr;
        }

        private const float GirderW = 4.6f, GirderH = 0.4f;

        private bool SandbagSpotOk(Vector2 p, bool girder = false)
        {
            float sw = girder ? GirderW : sandbagW, sh = girder ? GirderH : sandbagH;
            var t = FindFirstObjectByType<DestructibleTerrain>();
            if (t != null)
            {
                Vector2 o = t.transform.position, s = t.WorldSize;
                if (p.x < o.x + 1f || p.x > o.x + s.x - 1f || p.y < o.y + 1.5f || p.y > o.y + s.y) return false;
            }
            // Que no tape a ninguna rata
            foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
            {
                if (h == null || !h.IsSpawned || h.IsDead.Value) continue;
                Vector2 b = h.BodyCenter;
                if (Mathf.Abs(b.x - p.x) < sw * 0.5f + 0.25f && Mathf.Abs(b.y - p.y) < sh * 0.5f + 0.55f) return false;
            }
            return true;
        }

        // ==============================================================
        //  SERVIDOR
        // ==============================================================
        private bool CanAct(ServerRpcParams rpc)
        {
            var tm = NetworkTurnManager.Instance;
            return tm == null || tm.CanAct(rpc.Receive.SenderClientId, NetworkObjectId);
        }

        private Vector2 Origin(Vector2 dir) =>
            (ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position) + dir * 0.25f;

        private Vector2 Hand(Vector2 dir) =>
            movement != null ? (Vector2)movement.HandPosition + dir * 0.35f : Origin(dir);

        private static GameObject gadgetPrefab;
        private static GameObject GadgetPrefab()
        {
            if (gadgetPrefab != null) return gadgetPrefab;
            var nm = NetworkManager.Singleton;
            if (nm == null) return null;
            foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
                if (p.Prefab != null && p.Prefab.GetComponent<NetworkGadget>() != null) { gadgetPrefab = p.Prefab; break; }
            if (gadgetPrefab == null) Debug.LogError("[Trinchera] No encuentro el prefab Gadget.network en la lista de prefabs de red.");
            return gadgetPrefab;
        }

        /// <summary>0.25: (servidor) un artefacto sin dueño, p. ej. las minas del modo Minas.</summary>
        public static NetworkGadget SpawnLooseServer(NetworkGadget.Kind kind, Vector2 at)
        {
            var prefab = GadgetPrefab();
            if (prefab == null) return null;
            var go = Instantiate(prefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
            var g = go.GetComponent<NetworkGadget>();
            g.Prepare(kind, null, prefab);
            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            return g;
        }

        private NetworkGadget SpawnGadget(NetworkGadget.Kind kind, Vector2 at, Vector2 target = default, int dir = 1)
        {
            var prefab = GadgetPrefab();
            if (prefab == null) return null;
            var go = Instantiate(prefab, new Vector3(at.x, at.y, 0f), Quaternion.identity);
            var g = go.GetComponent<NetworkGadget>();
            var no = go.GetComponent<NetworkObject>();
            g.Prepare(kind, NetworkObject, prefab, target, dir);
            no.Spawn(destroyWithScene: true);
            return g;
        }

        [ServerRpc]
        private void LaunchServerRpc(byte weapon, Vector2 dir, float power, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            power = Mathf.Clamp(power, 0f, MaxForce((WeaponsMenuUI.WeaponId)weapon));
            var kind = (WeaponsMenuUI.WeaponId)weapon switch
            {
                WeaponsMenuUI.WeaponId.Banana => NetworkGadget.Kind.Banana,
                WeaponsMenuUI.WeaponId.Molotov => NetworkGadget.Kind.Molotov,
                WeaponsMenuUI.WeaponId.Mortar => NetworkGadget.Kind.MortarShell,
                WeaponsMenuUI.WeaponId.GasCan => NetworkGadget.Kind.GasCan,
                WeaponsMenuUI.WeaponId.Cheese => NetworkGadget.Kind.Cheese,
                WeaponsMenuUI.WeaponId.HolyCheese => NetworkGadget.Kind.HolyCheese,
                _ => NetworkGadget.Kind.Arrow,
            };
            var g = SpawnGadget(kind, Hand(dir));
            if (g != null) g.ServerInit(dir * power);
            if (kind == NetworkGadget.Kind.MortarShell) FxClientRpc(0, Hand(dir), dir);
        }

        [ServerRpc]
        private void DropServerRpc(byte weapon, bool facingRight, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            int d = facingRight ? 1 : -1;
            Vector2 at = (ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position) + new Vector2(d * 0.45f, 0.1f);
            var w = (WeaponsMenuUI.WeaponId)weapon;
            if (w == WeaponsMenuUI.WeaponId.Stampede) { StartCoroutine(StampedeRoutine(at, d)); return; }
            var kind = w switch
            {
                WeaponsMenuUI.WeaponId.RatBomb => NetworkGadget.Kind.RatBomb,
                WeaponsMenuUI.WeaponId.RabidRat => NetworkGadget.Kind.RabidRat,
                WeaponsMenuUI.WeaponId.GrannyRat => NetworkGadget.Kind.GrannyRat,
                WeaponsMenuUI.WeaponId.StinkRat => NetworkGadget.Kind.StinkRat,
                WeaponsMenuUI.WeaponId.Mole => NetworkGadget.Kind.Mole,
                _ => NetworkGadget.Kind.Mine,
            };
            if (kind == NetworkGadget.Kind.Mole) at += Vector2.down * 0.3f;
            var g = SpawnGadget(kind, at, default, d);
            if (g != null) g.ServerInit(new Vector2(d * 1.2f, 1f));
        }

        private IEnumerator StampedeRoutine(Vector2 at, int d)
        {
            for (int i = 0; i < 3; i++)
            {
                var g = SpawnGadget(NetworkGadget.Kind.RatBomb, at + Vector2.up * 0.2f * i, default, d);
                if (g != null) { g.ServerInit(new Vector2(d * (1.2f + i * 0.5f), 1.5f + i)); }
                yield return new WaitForSeconds(0.45f);
            }
        }

        [ServerRpc]
        private void PigeonServerRpc(Vector2 target, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            Vector2 start = (ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position) + Vector2.up * 0.8f;
            var g = SpawnGadget(NetworkGadget.Kind.Pigeon, start, target);
            if (g != null) g.ServerInit(Vector2.up * 3f);
        }

        [ServerRpc]
        private void SandbagsServerRpc(Vector2 at, bool girder = false, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc) || !SandbagSpotOk(at, girder)) return;
            NetworkDestructibleTerrain.Instance?.ServerSandbags(at, girder ? GirderW : sandbagW, girder ? GirderH : sandbagH);
            FxClientRpc(3, at, Vector2.up);
        }

        [ServerRpc]
        private void SniperServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            Vector2 o = Origin(dir);
            Vector2 end = o + dir * sniperRange;
            bool hitRat = false;
            if (FirstHit(o, dir, sniperRange, out var h))
            {
                end = h.point;
                var nh = h.collider.GetComponentInParent<NetworkHealth>();
                if (nh != null && !nh.IsDead.Value)
                {
                    hitRat = true;
                    nh.ApplyDamageServer(sniperDamage, nh.BodyCenter, DamageType.Bullet, o);
                    nh.ApplyKnockbackClientRpc((dir + Vector2.up * 0.25f).normalized * 4.5f, 0.2f);
                }
                else NetworkDestructibleTerrain.Instance?.ServerCarveAt(h.point, 0.35f);
            }
            TracerClientRpc(o, end, hitRat);
        }

        [ServerRpc]
        private void BayonetServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            Vector2 body = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            Vector2 center = body + dir * bayonetReach;
            var done = new HashSet<NetworkHealth>();
            foreach (var c in Physics2D.OverlapCircleAll(center, bayonetRadius))
            {
                var nh = c != null ? c.GetComponentInParent<NetworkHealth>() : null;
                if (nh == null || nh == ownHealth || nh.IsDead.Value || done.Contains(nh)) continue;
                done.Add(nh);
                var meMv = GetComponent<NetworkPlayerMovement>();
                nh.ApplyDamageServer(Mathf.RoundToInt(bayonetDamage * (meMv != null ? meMv.Body.melee : 1f)), nh.BodyCenter, DamageType.Melee, body);
                // La bayoneta SIEMPRE arranca algo (si las mutilaciones están activadas).
                if (!nh.IsDead.Value && MatchRules.EffectiveMutilations)
                {
                    int first = Random.value < 0.5f ? NetworkHealth.ZoneArms : NetworkHealth.ZoneLegs;
                    if (!nh.InjureServer(first) && !nh.InjureServer(first == NetworkHealth.ZoneArms ? NetworkHealth.ZoneLegs : NetworkHealth.ZoneArms))
                        nh.InjureServer(NetworkHealth.ZoneHead);
                }
                // Estocada hacia ARRIBA (el bate empuja de lado)
                nh.ApplyKnockbackClientRpc((dir * 0.5f + Vector2.up).normalized * bayonetPush, 0.45f);
            }
            FxClientRpc(1, center, dir);
            if (done.Count > 0 && movement != null) movement.ShortenEndTurnClientRpc(2f);
        }

        [ServerRpc]
        private void ShovelServerRpc(Vector2 dir, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            StartCoroutine(DigRoutine(dir));
        }

        private IEnumerator DigRoutine(Vector2 dir)
        {
            Vector2 start = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            for (int i = 1; i <= 9; i++)
            {
                Vector2 p = start + dir * (0.45f * i);
                NetworkDestructibleTerrain.Instance?.ServerDig(p, 0.62f);
                FxClientRpc(2, p, dir);
                yield return new WaitForSeconds(0.09f);
            }
        }

        // ==============================================================
        //  0.28 — ARSENAL COMPLETO (servidor)
        // ==============================================================
        [ServerRpc]
        private void ClickServerRpc(byte weapon, Vector2 p, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            var t = FindFirstObjectByType<DestructibleTerrain>();
            float top = t != null ? t.transform.position.y + t.WorldSize.y + 2f : p.y + 15f;
            Vector2 body = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            switch ((WeaponsMenuUI.WeaponId)weapon)
            {
                case WeaponsMenuUI.WeaponId.Homing:
                {
                    var g = SpawnGadget(NetworkGadget.Kind.Homing, body + Vector2.up * 0.7f, p);
                    if (g != null) g.ServerInit(Vector2.up * 4f);
                    FxClientRpc(0, body + Vector2.up * 0.7f, Vector2.up);
                    break;
                }
                case WeaponsMenuUI.WeaponId.Anvil:
                {
                    var g = SpawnGadget(NetworkGadget.Kind.Anvil, new Vector2(p.x, Mathf.Min(top, p.y + 10f)), p);   // desde encima del punto (no choca con islas de más arriba)
                    if (g != null) g.ServerInit(Vector2.down * 6f);
                    break;
                }
                case WeaponsMenuUI.WeaponId.MineStrike:
                    for (int i = 0; i < 5; i++)
                    {
                        var g = SpawnGadget(NetworkGadget.Kind.Mine, new Vector2(p.x + (i - 2) * 0.95f, top + i * 0.6f));
                        if (g != null) { g.quietSpawn = true; g.ServerInit(Vector2.down * 2f); }
                    }
                    FxClientRpc(4, p, Vector2.down);
                    break;
            }
        }

        [ServerRpc]
        private void InstantServerRpc(byte weapon, Vector2 dir, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc)) return;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            Vector2 body = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            float melee = movement != null ? movement.Body.melee : 1f;
            switch ((WeaponsMenuUI.WeaponId)weapon)
            {
                case WeaponsMenuUI.WeaponId.Pistol:
                    StartCoroutine(PistolRoutine(dir));
                    break;
                case WeaponsMenuUI.WeaponId.Fireball:
                {
                    var g = SpawnGadget(NetworkGadget.Kind.Fireball, Hand(dir));
                    if (g != null) g.ServerInit(dir * 11f);
                    FxClientRpc(5, Hand(dir), dir);
                    break;
                }
                case WeaponsMenuUI.WeaponId.FirePunch:
                case WeaponsMenuUI.WeaponId.Prod:
                {
                    bool punch = (WeaponsMenuUI.WeaponId)weapon == WeaponsMenuUI.WeaponId.FirePunch;
                    Vector2 center = body + new Vector2(dir.x >= 0f ? 0.55f : -0.55f, punch ? 0.2f : 0f);
                    var done = new HashSet<NetworkHealth>();
                    foreach (var c in Physics2D.OverlapCircleAll(center, 0.6f))
                    {
                        var nh = c != null ? c.GetComponentInParent<NetworkHealth>() : null;
                        if (nh == null || nh == ownHealth || nh.IsDead.Value || !done.Add(nh)) continue;
                        float side = Mathf.Sign(nh.BodyCenter.x - body.x);
                        if (punch)
                        {
                            nh.ApplyDamageServer(Mathf.RoundToInt(30 * melee), nh.BodyCenter, DamageType.Melee, body);
                            nh.ApplyKnockbackClientRpc(new Vector2(side * 2.5f, 12f), 0.6f);
                            FireSystem.ServerIgnite(nh.BodyCenter, Vector2.up, 0.6f, 3, 2f, 3f);
                        }
                        else
                        {
                            nh.ApplyDamageServer(Mathf.Max(1, Mathf.RoundToInt(3 * melee)), nh.BodyCenter, DamageType.Melee, body);
                            nh.ApplyKnockbackClientRpc(new Vector2(side * 8f, 2.5f), 0.5f);
                        }
                    }
                    if (punch) HopClientRpc(9f);
                    FxClientRpc(punch ? (byte)6 : (byte)1, center, dir);
                    if (done.Count > 0 && movement != null) movement.ShortenEndTurnClientRpc(2f);
                    break;
                }
                case WeaponsMenuUI.WeaponId.Kamikaze:
                    BlastServer(body, 3f, 60, 12f, ownHealth);
                    if (ownHealth != null && !ownHealth.IsDead.Value) ownHealth.ApplyDamageServer(9999, body, DamageType.Explosion, body);
                    break;
                case WeaponsMenuUI.WeaponId.Medkit:
                    if (ownHealth != null)
                    {
                        ownHealth.HealServer(30);
                        ownHealth.PoisonTurns.Value = 0;
                    }
                    FxClientRpc(7, body, Vector2.up);
                    break;
                case WeaponsMenuUI.WeaponId.Parachute:
                    ParachuteClientRpc(Time.time);
                    break;
                case WeaponsMenuUI.WeaponId.SuperJump:
                    FxClientRpc(8, body, Vector2.up);
                    break;
                case WeaponsMenuUI.WeaponId.Scales:
                {
                    var all = new List<NetworkHealth>();
                    foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
                        if (h != null && h.IsSpawned && !h.IsDead.Value) all.Add(h);
                    if (all.Count == 0) break;
                    int sum = 0; foreach (var h in all) sum += h.CurrentHealth.Value;
                    int avg = Mathf.Max(1, Mathf.RoundToInt(sum / (float)all.Count));
                    foreach (var h in all)
                    {
                        int diff = avg - h.CurrentHealth.Value;
                        if (diff > 0) h.HealServer(diff);
                        else if (diff < 0) h.ApplyDamageServer(-diff, h.BodyCenter, DamageType.Generic);
                    }
                    FxClientRpc(9, body, Vector2.up);
                    break;
                }
                case WeaponsMenuUI.WeaponId.Blowtorch:
                    StartCoroutine(TorchRoutine(dir, 14, 0.42f, 0.5f, true));
                    break;
                case WeaponsMenuUI.WeaponId.Drill:
                    StartCoroutine(TorchRoutine(Vector2.down, 10, 0.4f, 0.55f, false));
                    break;
                case WeaponsMenuUI.WeaponId.Earthquake:
                    foreach (var h in FindObjectsByType<NetworkHealth>(FindObjectsSortMode.None))
                    {
                        if (h == null || !h.IsSpawned || h.IsDead.Value) continue;
                        h.ApplyDamageServer(8, h.BodyCenter, DamageType.Generic);
                        h.ApplyKnockbackClientRpc(new Vector2(Random.Range(-5f, 5f), Random.Range(3f, 6f)), 0.6f);
                    }
                    FxClientRpc(10, body, Vector2.up);
                    break;
                case WeaponsMenuUI.WeaponId.Meteors:
                    StartCoroutine(MeteorRoutine());
                    break;
            }
        }

        private IEnumerator PistolRoutine(Vector2 dir)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector2 d = Quaternion.Euler(0f, 0f, Random.Range(-3f, 3f)) * dir;
                Vector2 o = Origin(d);
                Vector2 end = o + d * 30f;
                bool hitRat = false;
                if (FirstHit(o, d, 30f, out var h))
                {
                    end = h.point;
                    var nh = h.collider.GetComponentInParent<NetworkHealth>();
                    if (nh != null && !nh.IsDead.Value)
                    {
                        hitRat = true;
                        nh.ApplyDamageServer(12, nh.BodyCenter, DamageType.Bullet, o);
                        nh.ApplyKnockbackClientRpc((d + Vector2.up * 0.2f).normalized * 2f, 0.15f);
                    }
                    else NetworkDestructibleTerrain.Instance?.ServerCarveAt(h.point, 0.25f);
                }
                TracerClientRpc(o, end, hitRat);
                yield return new WaitForSeconds(0.22f);
            }
        }

        private IEnumerator TorchRoutine(Vector2 dir, int steps, float stepLen, float radius, bool burns)
        {
            Vector2 start = ownHealth != null ? ownHealth.BodyCenter : (Vector2)transform.position;
            var hurt = new HashSet<NetworkHealth>();
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = start + dir * (stepLen * i);
                NetworkDestructibleTerrain.Instance?.ServerDig(p, radius);
                if (burns)
                    foreach (var c in Physics2D.OverlapCircleAll(p, 0.55f))
                    {
                        var nh = c != null ? c.GetComponentInParent<NetworkHealth>() : null;
                        if (nh == null || nh == ownHealth || nh.IsDead.Value || !hurt.Add(nh)) continue;
                        nh.ApplyDamageServer(15, nh.BodyCenter, DamageType.Fire, p);
                    }
                FxClientRpc(burns ? (byte)11 : (byte)2, p, dir);
                yield return new WaitForSeconds(0.1f);
            }
        }

        private IEnumerator MeteorRoutine()
        {
            var t = FindFirstObjectByType<DestructibleTerrain>();
            if (t == null) yield break;
            float top = t.transform.position.y + t.WorldSize.y + 2f;
            for (int i = 0; i < 10; i++)
            {
                float x = t.transform.position.x + Random.Range(0.04f, 0.96f) * t.WorldSize.x;
                var g = SpawnGadget(NetworkGadget.Kind.Meteor, new Vector2(x, top + Random.Range(0f, 3f)));
                if (g != null) { g.quietSpawn = true; g.ServerInit(new Vector2(Random.Range(-2f, 2f), -5f)); }
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>(Servidor) Explosión "a pelo" (kamikaze).</summary>
        private void BlastServer(Vector2 at, float radius, int maxDamage, float knockback, NetworkHealth skip)
        {
            var done = new HashSet<NetworkHealth>();
            foreach (var col in Physics2D.OverlapCircleAll(at, radius))
            {
                var nh = col != null ? col.GetComponentInParent<NetworkHealth>() : null;
                if (nh == null || nh == skip || nh.IsDead.Value || !done.Add(nh)) continue;
                Vector2 b = nh.BodyCenter;
                float k = Mathf.Clamp01(1f - nh.BlastDistance(at) / radius);
                if (k <= 0f) continue;
                nh.ApplyDamageServer(Mathf.CeilToInt(maxDamage * k), b, DamageType.Explosion, at);
                Vector2 away = b - at; if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
                nh.ApplyKnockbackClientRpc((away.normalized + Vector2.up * 0.8f).normalized * knockback * Mathf.Max(0.35f, k), 0.5f);
            }
            NetworkDestructibleTerrain.Instance?.ServerCarveAt(at, radius);
            FxClientRpc(12, at, Vector2.up);
        }

        [ClientRpc]
        private void HopClientRpc(float vy)
        {
            if (!IsOwner) return;
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = new Vector2(rb.linearVelocity.x, vy);
            movement?.TestResetFallTracking();
        }

        // ---- Paracaídas (lo ven todos; la caída lenta la hace el dueño) ----
        private float parachuteUntil;
        private SpriteRenderer canopy;

        [ClientRpc]
        private void ParachuteClientRpc(float _)
        {
            parachuteUntil = Time.time + 9f;
            Sfx.Play(Sfx.Id.Swish, 0.8f);
        }

        private void UpdateParachute()
        {
            bool on = Time.time < parachuteUntil && (ownHealth == null || !ownHealth.IsDead.Value);
            if (on && canopy == null)
            {
                var go = new GameObject("Paracaidas");
                canopy = go.AddComponent<SpriteRenderer>();
                canopy.sprite = WeaponArt.ParachuteIcon();
                canopy.sortingOrder = 55;
            }
            if (canopy == null) return;
            canopy.gameObject.SetActive(on);
            if (!on) return;
            canopy.transform.position = transform.position + Vector3.up * 0.75f;
            canopy.transform.localScale = Vector3.one * 1.6f;
            if (IsOwner)
            {
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.y < -1.4f)
                {
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x + NetworkTurnManager.WindAcceleration * 0.02f, -1.4f);
                    movement?.TestResetFallTracking();
                }
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
                if (c.GetComponentInParent<NetworkGadget>() != null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; first = hits[i]; }
            }
            return best < float.MaxValue;
        }

        // ==============================================================
        //  EFECTOS (todos)
        // ==============================================================
        [ClientRpc]
        private void TracerClientRpc(Vector2 from, Vector2 to, bool hitRat)
        {
            var go = new GameObject("Bala");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.05f; lr.endWidth = 0.02f;
            lr.startColor = new Color(1f, 0.97f, 0.75f, 1f);
            lr.endColor = new Color(1f, 0.8f, 0.4f, 0.6f);
            lr.positionCount = 2;
            lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.sortingOrder = 300;
            Destroy(go, 0.09f);
            Sfx.Play(Sfx.Id.SniperShot, 1f, 0.05f);
            Vector2 d = (to - from).normalized;
            Fx.MuzzleFlash(from, d);
            Fx.Sparks(to, 6, new Color(1f, 0.85f, 0.4f, 1f), 2f, 5f);
            if (hitRat && Fx.GoreOn) Fx.Blood(to, d, 1.2f);
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.12f, 0.15f);
        }

        /// <summary>0 = boca del mortero, 1 = bayonetazo, 2 = palada, 3 = sacos puestos.</summary>
        [ClientRpc]
        private void FxClientRpc(byte kind, Vector2 at, Vector2 dir)
        {
            switch (kind)
            {
                case 0: Fx.MuzzleFlash(at, dir); Sfx.Play(Sfx.Id.Launch, 0.8f); if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.15f, 0.2f); break;
                case 1: Sfx.Play(Sfx.Id.Swish, 0.9f); Fx.Sparks(at, 3, new Color(0.9f, 0.9f, 1f, 1f), 1f, 3f); break;
                case 2: Sfx.Play(Sfx.Id.Dig, 0.6f, 0.15f); Fx.Dust(at, -dir, 5); break;
                // 0.28
                case 4: Sfx.Play(Sfx.Id.Plane, 0.6f); break;
                case 5: Fx.MuzzleFlash(at, dir); Sfx.Play(Sfx.Id.Flame, 0.9f); break;
                case 6: Sfx.Play(Sfx.Id.Whack, 1f); Fx.Sparks(at, 10, new Color(1f, 0.55f, 0.15f, 1f), 2f, 5f);
                        WorldDamagePopup.SpawnImpact(at + Vector2.up * 0.5f, "¡PUÑO DE FUEGO!", new Color(1f, 0.6f, 0.2f)); break;
                case 7: Sfx.Play(Sfx.Id.TurnStart, 0.7f); WorldDamagePopup.SpawnImpact(at + Vector2.up * 0.8f, "+30", new Color(0.5f, 1f, 0.5f)); break;
                case 8: Sfx.Play(Sfx.Id.Twang, 1f, 0.05f); Fx.Dust(at + Vector2.down * 0.4f, Vector2.up, 8); break;
                case 9: Sfx.Play(Sfx.Id.Choir, 0.6f); WorldDamagePopup.SpawnImpact(at + Vector2.up * 1f, "¡JUSTICIA!", new Color(1f, 0.9f, 0.5f)); break;
                case 10: Sfx.Play(Sfx.Id.Thunder, 1f, 0.05f); if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.6f, 1.6f);
                         WorldDamagePopup.SpawnImpact(at + Vector2.up * 1.2f, "¡TERREMOTO!", new Color(1f, 0.55f, 0.3f)); break;
                case 11: Sfx.Play(Sfx.Id.Flame, 0.5f, 0.15f); Fx.Sparks(at, 4, new Color(1f, 0.6f, 0.2f, 1f), 1f, 3f); break;
                case 12: Fx.Explosion(at, 3f); Sfx.Play(Sfx.Id.BigExplosion, 1f); WorldDamagePopup.SpawnImpact(at + Vector2.up, "¡BANZAI!", new Color(1f, 0.4f, 0.2f));
                         if (CameraFollow.Instance != null) { CameraFollow.Instance.Shake(0.7f, 0.8f); CameraFollow.Instance.FocusOnImpact(at, 1.5f); } break;
                default: Sfx.Play(Sfx.Id.Whack, 0.6f); Fx.Dust(at + Vector2.down * 0.3f, Vector2.up, 8); break;
            }
        }
    }
}
