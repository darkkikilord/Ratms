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

        public static bool Handles(WeaponsMenuUI.WeaponId id) => id >= WeaponsMenuUI.WeaponId.RatBomb && id <= WeaponsMenuUI.WeaponId.Bow;

        /// <summary>¿Esta arma usa la parábola de puntos al apuntar?</summary>
        public static bool UsesTrajectory(WeaponsMenuUI.WeaponId id) =>
            id == WeaponsMenuUI.WeaponId.Mortar || id == WeaponsMenuUI.WeaponId.GasCan || id == WeaponsMenuUI.WeaponId.Cheese ||
            id == WeaponsMenuUI.WeaponId.HolyCheese || id == WeaponsMenuUI.WeaponId.Bow;

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
                        endTurnIn = id == WeaponsMenuUI.WeaponId.HolyCheese ? 7f : id == WeaponsMenuUI.WeaponId.Cheese ? 9f
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
                case WeaponsMenuUI.WeaponId.Pigeon:
                case WeaponsMenuUI.WeaponId.Sandbags:
                {
                    Vector2? p = TestClickTarget;
                    TestClickTarget = null;
                    if (p == null && (menu == null || !menu.IsVisible) && Input.GetMouseButtonDown(0) && Camera.main != null)
                        p = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    if (p == null) return false;
                    if (id == WeaponsMenuUI.WeaponId.Sandbags)
                    {
                        if (!SandbagSpotOk(p.Value)) { WorldDamagePopup.SpawnImpact(p.Value, "¡Ahí no cabe!", new Color(1f, 0.6f, 0.4f)); return false; }
                        SandbagsServerRpc(p.Value);
                        endTurnIn = 1.5f;
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
                        || id == WeaponsMenuUI.WeaponId.Mortar || id == WeaponsMenuUI.WeaponId.Shovel;
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
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            bool mine = IsOwner && movement != null && movement.CanControlNow && !movement.FiredThisTurn;
            var id = Equipped;
            UpdateHeld(id);
            UpdateReticle(mine && id == WeaponsMenuUI.WeaponId.Pigeon && Camera.main != null);

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
            bool ghost = mine && id == WeaponsMenuUI.WeaponId.Sandbags && Camera.main != null;
            if (ghost)
            {
                if (sandbagGhost == null) sandbagGhost = MakeGhost();
                Vector2 p = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                sandbagGhost.enabled = true;
                sandbagGhost.transform.position = new Vector3(p.x, p.y, -1f);
                sandbagGhost.transform.localScale = new Vector3(sandbagW, sandbagH, 1f);
                sandbagGhost.color = SandbagSpotOk(p) ? new Color(0.85f, 0.75f, 0.5f, 0.5f) : new Color(1f, 0.3f, 0.25f, 0.45f);
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

        private bool SandbagSpotOk(Vector2 p)
        {
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
                if (Mathf.Abs(b.x - p.x) < sandbagW * 0.5f + 0.25f && Mathf.Abs(b.y - p.y) < sandbagH * 0.5f + 0.55f) return false;
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
            var kind = (WeaponsMenuUI.WeaponId)weapon == WeaponsMenuUI.WeaponId.RatBomb ? NetworkGadget.Kind.RatBomb : NetworkGadget.Kind.Mine;
            var g = SpawnGadget(kind, at, default, d);
            if (g != null) g.ServerInit(new Vector2(d * 1.2f, 1f));
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
        private void SandbagsServerRpc(Vector2 at, ServerRpcParams rpc = default)
        {
            if (!CanAct(rpc) || !SandbagSpotOk(at)) return;
            NetworkDestructibleTerrain.Instance?.ServerSandbags(at, sandbagW, sandbagH);
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
                default: Sfx.Play(Sfx.Id.Whack, 0.6f); Fx.Dust(at + Vector2.down * 0.3f, Vector2.up, 8); break;
            }
        }
    }
}
