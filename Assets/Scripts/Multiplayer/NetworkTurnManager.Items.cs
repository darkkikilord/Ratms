using System.Collections.Generic;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ECONOMÍA DE ARMAS (parte del NetworkTurnManager):
    ///   • MUNICIÓN por equipo y arma (<see cref="Ammo"/>, -1 = infinita).
    ///   • CAJAS que caen en paracaídas al empezar algunos turnos: armas,
    ///     botiquín o… trampa. Se cogen pasando por encima.
    ///   • BARRILES de gasolina repartidos por el mapa que revientan (en
    ///     cadena) cuando les alcanza una explosión.
    ///   • "CAMBIAR RATA": eliges qué rata de tu equipo juega el próximo
    ///     turno, pero gastas el turno actual.
    ///
    ///  Las cajas y barriles NO son objetos de red: el servidor decide y avisa
    ///  por ClientRpc (aparece / desaparece) y cada cliente los dibuja y los
    ///  deja caer igual (el terreno es idéntico en todos). Las decisiones
    ///  (coger, explotar) solo las toma el servidor.
    /// </summary>
    public partial class NetworkTurnManager
    {
        public const int WeaponCount = 8;   // WeaponsMenuUI.WeaponId 0..7

        /// <summary>Munición: índice = equipo × WeaponCount + arma. -1 = infinita.</summary>
        public NetworkList<int> Ammo;

        partial void ItemsAwake() => Ammo = new NetworkList<int>();

        /// <summary>Balas que le quedan a un equipo de un arma (-1 = infinitas).</summary>
        public int AmmoOf(int team, WeaponsMenuUI.WeaponId w)
        {
            if (w == WeaponsMenuUI.WeaponId.None || team < 0) return -1;
            int i = team * WeaponCount + (int)w;
            return Ammo != null && i < Ammo.Count ? Ammo[i] : MatchRules.DefaultAmmo(w);
        }

        public bool HasAmmo(int team, WeaponsMenuUI.WeaponId w) => AmmoOf(team, w) != 0;

        /// <summary>El dueño de la rata del turno avisa de que ha gastado un arma.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ConsumeAmmoServerRpc(int weapon, ServerRpcParams rpc = default)
        {
            if (!IsTurnOf(rpc.Receive.SenderClientId) && !(InTransition.Value && LastTurnRat != null && LastTurnRat.OwnerClientId == rpc.Receive.SenderClientId)) return;
            var rat = CurrentRat != null ? CurrentRat : LastTurnRat;
            if (rat == null) return;
            AddAmmoServer(rat.TeamIndex.Value, (WeaponsMenuUI.WeaponId)weapon, -1);
        }

        private void AddAmmoServer(int team, WeaponsMenuUI.WeaponId w, int delta)
        {
            if (team < 0 || w == WeaponsMenuUI.WeaponId.None) return;
            int i = team * WeaponCount + (int)w;
            while (Ammo.Count <= i) Ammo.Add(MatchRules.DefaultAmmo((WeaponsMenuUI.WeaponId)(Ammo.Count % WeaponCount)));
            if (Ammo[i] < 0) return;   // infinita
            Ammo[i] = Mathf.Max(0, Ammo[i] + delta);
        }

        private void InitAmmoServer()
        {
            Ammo.Clear();
            int maxTeam = 0;
            foreach (var t in teams) maxTeam = Mathf.Max(maxTeam, t.index + 1);
            for (int t = 0; t < maxTeam; t++)
                for (int w = 0; w < WeaponCount; w++)
                    Ammo.Add(MatchRules.DefaultAmmo((WeaponsMenuUI.WeaponId)w));
        }

        // =========================================================
        //  CAMBIAR RATA
        // =========================================================
        /// <summary>
        ///  Usar "Cambiar rata": la rata indicada (o la siguiente del equipo si
        ///  es 0) jugará el PRÓXIMO turno del equipo. Cuesta el turno actual.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void SelectRatServerRpc(ulong targetRatId, ServerRpcParams rpc = default)
        {
            var cur = CurrentRat;
            if (cur == null || !IsTurnOf(rpc.Receive.SenderClientId)) return;
            var team = teams.Find(t => t.rats.Contains(cur));
            if (team == null || AmmoOf(team.index, WeaponsMenuUI.WeaponId.SelectRat) == 0) return;

            NetworkPlayerMovement target = targetRatId != 0 ? FindRat(targetRatId) : null;
            if (target == null || !team.rats.Contains(target) || target == cur || !IsAlive(target))
            {
                // Sin objetivo válido: la siguiente viva que no sea la actual.
                int start = team.rats.IndexOf(cur);
                for (int k = 1; k < team.rats.Count && target == null; k++)
                {
                    var r = team.rats[(start + k) % team.rats.Count];
                    if (r != cur && IsAlive(r)) target = r;
                }
            }
            if (target == null) return;
            team.forcedRat = target;
            AddAmmoServer(team.index, WeaponsMenuUI.WeaponId.SelectRat, -1);
            AnnounceClientRpc($"¡{target.RatName.Value} toma el relevo!", TurnHUD.TeamTextColor(team.index));
            AdvanceTurnServer();
        }

        // =========================================================
        //  CAJAS Y BARRILES
        // =========================================================
        /// <summary>(Servidor) Aviso grande para todos (lo usan otros scripts: mutilaciones...).</summary>
        public void AnnounceServer(string text, Color color)
        {
            if (IsServer) AnnounceClientRpc(text, color);
        }

        public enum ItemKind : byte { WeaponCrate, HealthCrate, Barrel }

        private class Item
        {
            public int id;
            public ItemKind kind;
            public byte content;
            public Vector2 pos;
            public float fall;
            public bool landed, gone;
            public GameObject view;          // dibujo local
            public GameObject chute;
        }

        private readonly Dictionary<int, Item> items = new();
        private int nextItemId = 1;
        private readonly List<(float at, Vector2 pos)> pendingBlasts = new();
        private WaterDeathZone waterZone;

        // Contenido de las cajas de armas
        private static readonly (WeaponsMenuUI.WeaponId w, int n, string text)[] CrateLoot =
        {
            (WeaponsMenuUI.WeaponId.Grenade, 2, "+2 Granadas"),
            (WeaponsMenuUI.WeaponId.Dynamite, 1, "+1 Dinamita"),
            (WeaponsMenuUI.WeaponId.Teleport, 1, "+1 Teletransporte"),
            (WeaponsMenuUI.WeaponId.SelectRat, 1, "+1 Cambiar rata"),
            (WeaponsMenuUI.WeaponId.Grenade, 1, "+1 Granada"),
        };
        private const byte TrapContent = 250;

        /// <summary>(Prueba) cuántas cajas/barriles quedan.</summary>
        public int ItemCount(ItemKind kind)
        {
            int n = 0;
            foreach (var it in items.Values) if (it.kind == kind && !it.gone) n++;
            return n;
        }

        partial void ItemsOnSpawn()
        {
            if (IsServer) NetworkDestructibleTerrain.OnServerExplosion += OnServerExplosion;
        }

        partial void ItemsOnDespawn()
        {
            NetworkDestructibleTerrain.OnServerExplosion -= OnServerExplosion;
            foreach (var it in items.Values) DestroyView(it);
            items.Clear();
        }

        partial void ItemsOnGameStart()
        {
            InitAmmoServer();
            if (MatchRules.Mode == MatchMode.Practice) return;
            // Barriles repartidos por el mapa, lejos de las ratas.
            var gen = FindFirstObjectByType<TerrainGenerator>();
            if (gen == null) return;
            var avoid = new List<Vector3>();
            foreach (var t in teams) foreach (var r in t.rats) if (r != null) avoid.Add(r.transform.position);
            for (int i = 0; i < MatchRules.EffectiveBarrels; i++)
            {
                var p = gen.GetRandomValidSpawnWorld(avoid, 3f);
                if (!p.HasValue) break;
                avoid.Add(p.Value);
                SpawnItemServer(ItemKind.Barrel, 0, (Vector2)p.Value + Vector2.up * 0.05f);
            }
        }

        partial void ItemsOnTurnBegin()
        {
            if (MatchRules.Mode == MatchMode.Practice) return;
            if (ItemCount(ItemKind.WeaponCrate) + ItemCount(ItemKind.HealthCrate) >= 3) return;
            if (Random.value > MatchRules.CrateChance) return;
            DropRandomCrateServer();
        }

        /// <summary>Deja caer una caja al azar desde el cielo sobre un sitio pisable.</summary>
        public void DropRandomCrateServer()
        {
            var gen = FindFirstObjectByType<TerrainGenerator>();
            var terrain = FindFirstObjectByType<DestructibleTerrain>();
            if (gen == null || terrain == null) return;
            var p = gen.GetRandomValidSpawnWorld(null, 0f);
            if (!p.HasValue) return;
            float top = Mathf.Min(terrain.transform.position.y + terrain.WorldSize.y - 0.5f, p.Value.y + 7f);
            bool health = Random.value < 0.3f;
            byte content = health ? (byte)0 : Random.value < 0.12f ? TrapContent : (byte)Random.Range(0, CrateLoot.Length);
            SpawnItemServer(health ? ItemKind.HealthCrate : ItemKind.WeaponCrate, content, new Vector2(p.Value.x, top));
            AnnounceClientRpc(health ? "¡Botiquín en camino!" : "¡Caja de suministros!", new Color(0.95f, 0.85f, 0.5f));
        }

        /// <summary>(Prueba / servidor) Pone una caja o barril en un punto concreto.</summary>
        public int SpawnItemServer(ItemKind kind, byte content, Vector2 at)
        {
            if (!IsServer) return -1;
            int id = nextItemId++;
            SpawnItemClientRpc(id, (byte)kind, content, at);
            return id;
        }

        [ClientRpc]
        private void SpawnItemClientRpc(int id, byte kind, byte content, Vector2 at)
        {
            var it = new Item { id = id, kind = (ItemKind)kind, content = content, pos = at };
            it.view = new GameObject(it.kind == ItemKind.Barrel ? "Barril" : "Caja");
            it.view.transform.position = at;
            var sr = it.view.AddComponent<SpriteRenderer>();
            sr.sprite = it.kind switch
            {
                ItemKind.Barrel => WeaponArt.Barrel(),
                ItemKind.HealthCrate => WeaponArt.HealthCrate(),
                _ => WeaponArt.WeaponCrate(),
            };
            sr.sortingOrder = 45;
            if (it.kind == ItemKind.Barrel)
            {
                // Sólido: las ratas pueden subirse y los misiles chocan contra él.
                var box = it.view.AddComponent<BoxCollider2D>();
                box.size = new Vector2(10f / 32f, 13f / 32f);
                box.offset = new Vector2(0f, 7f / 32f);
                it.landed = DestructibleTerrain.IsSolidAtWorld(at + Vector2.down * 0.06f);
            }
            else
            {
                it.chute = new GameObject("Paracaídas");
                it.chute.transform.SetParent(it.view.transform, false);
                it.chute.transform.localPosition = new Vector3(0f, 12f / 32f, 0f);
                var csr = it.chute.AddComponent<SpriteRenderer>();
                csr.sprite = WeaponArt.Parachute();
                csr.sortingOrder = 44;
            }
            items[id] = it;
        }

        [ClientRpc]
        private void RemoveItemClientRpc(int id, string popup, Color color)
        {
            if (!items.TryGetValue(id, out var it)) return;
            it.gone = true;
            if (!string.IsNullOrEmpty(popup))
                WorldDamagePopup.SpawnImpact(it.pos + Vector2.up * 0.7f, popup, color);
            if (it.kind != ItemKind.Barrel) Sfx.Play(Sfx.Id.Squeak, 0.4f, 0.3f);
            DestroyView(it);
            items.Remove(id);
        }

        private static void DestroyView(Item it)
        {
            if (it.view != null) Destroy(it.view);
            it.view = null;
        }

        partial void ItemsUpdate()
        {
            float dt = Time.deltaTime;
            // --- Caída (igual en todos los clientes) ---
            foreach (var it in items.Values)
            {
                if (it.gone) continue;
                bool ground = DestructibleTerrain.IsSolidAtWorld(it.pos + Vector2.down * 0.04f);
                if (ground)
                {
                    if (!it.landed) { it.landed = true; it.fall = 0f; if (it.chute != null) Destroy(it.chute); }
                }
                else
                {
                    it.landed = false;
                    bool chute = it.chute != null;
                    it.fall = chute ? 1.6f : Mathf.Min(it.fall + 15f * dt, 12f);
                    it.pos += Vector2.down * it.fall * dt;
                    if (chute) it.pos.x += WindAcceleration * 0.12f * dt;   // el viento mece el paracaídas
                }
                if (it.view != null)
                {
                    it.view.transform.position = it.pos;
                    if (it.chute != null)
                        it.chute.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.2f + it.id) * 8f);
                }
            }

            if (!IsServer) return;

            // --- Explosiones pendientes (barriles en cadena) ---
            for (int i = pendingBlasts.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingBlasts[i].at) continue;
                var p = pendingBlasts[i].pos;
                pendingBlasts.RemoveAt(i);
                ServerBlast(p, 2.6f, 35, 9f, big: true);
            }

            // --- Recoger cajas / caer al agua ---
            var toRemove = new List<(int id, string text, Color col)>();
            if (waterZone == null) waterZone = FindFirstObjectByType<WaterDeathZone>();
            var water = waterZone;
            foreach (var it in items.Values)
            {
                if (it.gone) continue;
                if (water != null && it.pos.y < water.SurfaceY - 0.3f) { toRemove.Add((it.id, it.kind == ItemKind.Barrel ? "" : "glub", Color.cyan)); continue; }
                if (it.kind == ItemKind.Barrel) continue;
                foreach (var t in teams)
                    foreach (var r in t.rats)
                    {
                        if (!IsAlive(r)) continue;
                        var h = r.GetComponent<NetworkHealth>();
                        if (Vector2.Distance(h.BodyCenter, it.pos + Vector2.up * 0.2f) > 0.6f) continue;
                        toRemove.Add(PickUpServer(it, t, r, h));
                        goto next;
                    }
                next:;
            }
            foreach (var (id, text, col) in toRemove)
                if (items.TryGetValue(id, out var it) && !it.gone)
                {
                    it.gone = true;
                    RemoveItemClientRpc(id, text, col);
                }
        }

        private (int, string, Color) PickUpServer(Item it, Team team, NetworkPlayerMovement rat, NetworkHealth h)
        {
            if (it.kind == ItemKind.HealthCrate)
            {
                h.HealServer(MatchRules.HealthCrateAmount);
                return (it.id, $"+{MatchRules.HealthCrateAmount} vida", new Color(0.5f, 1f, 0.5f));
            }
            if (it.content == TrapContent)
            {
                pendingBlasts.Add((Time.time + 0.05f, it.pos + Vector2.up * 0.2f));
                return (it.id, "¡TRAMPA!", new Color(1f, 0.3f, 0.2f));
            }
            // PRÓTESIS: si la rata está mutilada, a veces la caja trae una pata de palo o un garfio.
            if (h != null && h.NeedsProsthesis && (Random.value < 0.5f || NetworkHealth.ForceInjuries))
            {
                string pt = h.GiveProsthesisServer();
                if (pt != null) { AnnounceServer($"¡{rat.RatName.Value} se pone {(pt.Contains("PATA") ? "una pata de palo" : "un garfio")}!", new Color(0.95f, 0.85f, 0.5f)); return (it.id, pt, new Color(1f, 0.9f, 0.4f)); }
            }
            var loot = CrateLoot[it.content % CrateLoot.Length];
            AddAmmoServer(team.index, loot.w, loot.n);
            return (it.id, loot.text, new Color(1f, 0.9f, 0.4f));
        }

        /// <summary>Una explosión en el mundo: los barriles cercanos revientan; las cajas, también.</summary>
        private void OnServerExplosion(Vector2 at, float radius)
        {
            var hit = new List<Item>();
            foreach (var it in items.Values)
            {
                if (it.gone) continue;
                Vector2 c = it.pos + Vector2.up * 0.2f;
                if (Vector2.Distance(c, at) > radius + 0.35f) continue;
                it.gone = true;
                hit.Add(it);
            }
            // (Los RPC van fuera del bucle: en el host se ejecutan al momento y tocan la lista.)
            foreach (var it in hit)
            {
                if (it.kind == ItemKind.Barrel || it.content == TrapContent)
                    pendingBlasts.Add((Time.time + 0.18f, it.pos + Vector2.up * 0.2f));   // pausa: reacción en cadena visible
                RemoveItemClientRpc(it.id, it.kind == ItemKind.Barrel ? "" : "¡crash!", Color.white);
            }
        }

        /// <summary>
        ///  (Servidor) Explosión genérica (barriles, cajas trampa): daño con
        ///  caída lineal, empujón, agujero en el terreno y efectos.
        /// </summary>
        public void ServerBlast(Vector2 at, float radius, int maxDamage, float knockback, bool big = false)
        {
            if (!IsServer) return;
            var done = new HashSet<NetworkHealth>();
            foreach (var col in Physics2D.OverlapCircleAll(at, radius + 0.5f))
            {
                var nh = col != null ? col.GetComponentInParent<NetworkHealth>() : null;
                if (nh == null || nh.IsDead.Value || !done.Add(nh)) continue;
                Vector2 body = nh.BodyCenter;
                float t = Mathf.Clamp01(1f - Vector2.Distance(at, body) / radius);
                int dmg = Mathf.CeilToInt(maxDamage * t);
                if (dmg <= 0) continue;
                nh.ApplyDamageServer(dmg, body, DamageType.Explosion, at);
                Vector2 away = body - at;
                if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
                nh.ApplyKnockbackClientRpc((away.normalized + Vector2.up * 0.8f).normalized * knockback * Mathf.Sqrt(t), 0.6f);
            }
            BlastFxClientRpc(at, radius, big);
            if (NetworkDestructibleTerrain.Instance != null)
                NetworkDestructibleTerrain.Instance.ServerCarveAt(at, radius * 0.8f);
        }

        [ClientRpc]
        private void BlastFxClientRpc(Vector2 at, float radius, bool big)
        {
            Fx.Explosion(at, radius);
            if (big) Fx.FireBurst(at, radius);
            Sfx.Play(big ? Sfx.Id.BigExplosion : Sfx.Id.Explosion);
            WorldDamagePopup.SpawnImpact(at, big ? "¡BRUUUM!" : "¡BUM!", new Color(1f, 0.45f, 0.15f));
            if (CameraFollow.Instance != null) CameraFollow.Instance.Shake(0.3f, 0.4f);
        }
    }
}
