using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Menú RÚSTICO de armas para el dueño de la rata.
    ///
    ///  Se abre/cierra con TAB. Al empezar la partida la rata viene SIN arma
    ///  en las manos. Para sacar el bazooka (o cualquier otra arma) el
    ///  jugador tiene que abrir TAB y seleccionarla.
    ///
    ///  Opciones:
    ///   • 0: Ninguna (guarda el arma)
    ///   • 1: Bazooka                (funciona hoy)
    ///   • 2: Granada  (próximamente)
    ///   • 3: Dinamita (próximamente)
    ///   • 4: Escopeta (próximamente)
    ///
    ///  La selección se sincroniza en red con una NetworkVariable escribible
    ///  por el Owner, así TODOS los clientes ven qué arma lleva cada rata.
    /// </summary>
    public class WeaponsMenuUI : NetworkBehaviour
    {
        public enum WeaponId
        {
            None      = -1,
            Bazooka   = 0,
            Grenade   = 1,
            Dynamite  = 2,
            Shotgun   = 3,
            Bat       = 4,
            Teleport  = 5,
            Rope      = 6,
            SelectRat = 7,
            // 0.18 — arsenal ampliado
            Flamethrower = 8,
            MachineGun   = 9,
            ClusterBomb  = 10,
            AirStrike    = 11,
            Napalm       = 12,
            // 0.21 — arsenal de trinchera
            RatBomb      = 13,   // rata kamikaze que corre sola
            Mine         = 14,
            Pigeon       = 15,   // paloma mensajera teledirigida
            Mortar       = 16,
            GasCan       = 17,
            Cheese       = 18,   // queso apestoso: atrae a las ratas
            Bayonet      = 19,
            Shovel       = 20,
            Sandbags     = 21,
            HolyCheese   = 22,   // la bomba bíblica de queso
            Sniper       = 23,
            Bow          = 24,
            // 0.28 — arsenal completo (sin tecla: se eligen en el TAB)
            Banana = 25, Molotov = 26, Fireball = 27, Homing = 28, Anvil = 29, MineStrike = 30, Meteors = 31,
            Earthquake = 32, RabidRat = 33, GrannyRat = 34, StinkRat = 35, Mole = 36, Stampede = 37, Pistol = 38,
            FirePunch = 39, Prod = 40, Kamikaze = 41, Medkit = 42, SuperJump = 43, Parachute = 44, Scales = 45,
            Blowtorch = 46, Drill = 47, Girder = 48,
        }

        /// <summary>Número de armas (tamaño del bloque de munición por equipo en la red).</summary>
        public const int Count = 49;

        private struct WeaponEntry
        {
            public WeaponId id;
            public string   displayName;
            public bool     implemented;
            public KeyCode  shortcut;
        }

        [Header("Teclas")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

        [Header("Panel")]
        [SerializeField] private Vector2 panelSize = new Vector2(300f, 320f);
        [Tooltip("Offset desde la esquina inferior izquierda.")]
        [SerializeField] private Vector2 panelOffset = new Vector2(20f, 20f);

        [Header("Arranque")]
        [Tooltip("Arma equipada al nacer. None = empezar sin arma (el jugador la saca con TAB).")]
        [SerializeField] private WeaponId defaultWeapon = WeaponId.None;

        /// <summary>
        ///  Arma seleccionada por el dueño. Se escribe desde el Owner y la leen
        ///  todos los clientes. -1 = ninguna (desenfundado).
        /// </summary>
        public readonly NetworkVariable<int> SelectedIndexNet = new(
            value: (int)WeaponId.None,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Owner);

        // Estado local del menú (solo del dueño)
        private bool visible;

        private readonly WeaponEntry[] weapons = new WeaponEntry[]
        {
            new WeaponEntry { id = WeaponId.None,     displayName = "Ninguna (guardar arma)",     implemented = true,  shortcut = KeyCode.Alpha0 },
            new WeaponEntry { id = WeaponId.Bazooka,  displayName = "Bazooka",                    implemented = true,  shortcut = KeyCode.Alpha1 },
            new WeaponEntry { id = WeaponId.Grenade,  displayName = "Granada",                    implemented = true,  shortcut = KeyCode.Alpha2 },
            new WeaponEntry { id = WeaponId.Dynamite, displayName = "Dinamita (ESPACIO = soltar)", implemented = true,  shortcut = KeyCode.Alpha3 },
            new WeaponEntry { id = WeaponId.Shotgun,  displayName = "Escopeta (2 disparos)",      implemented = true,  shortcut = KeyCode.Alpha4 },
            new WeaponEntry { id = WeaponId.Bat,      displayName = "Bate (ESPACIO = golpear)",   implemented = true,  shortcut = KeyCode.Alpha5 },
            new WeaponEntry { id = WeaponId.Teleport, displayName = "Teletransporte (clic)",      implemented = true,  shortcut = KeyCode.Alpha6 },
            new WeaponEntry { id = WeaponId.Rope,     displayName = "Cuerda ninja",               implemented = true,  shortcut = KeyCode.Alpha7 },
            new WeaponEntry { id = WeaponId.SelectRat, displayName = "Cambiar rata (clic, gasta turno)", implemented = true, shortcut = KeyCode.Alpha8 },
            new WeaponEntry { id = WeaponId.Flamethrower, displayName = "Lanzallamas (mantén ESPACIO)", implemented = true, shortcut = KeyCode.Alpha9 },
            new WeaponEntry { id = WeaponId.MachineGun,   displayName = "Metralleta (ráfaga)",        implemented = true, shortcut = KeyCode.Q },
            new WeaponEntry { id = WeaponId.ClusterBomb,  displayName = "Bomba de racimo",            implemented = true, shortcut = KeyCode.E },
            new WeaponEntry { id = WeaponId.AirStrike,    displayName = "Ataque aéreo (clic)",        implemented = true, shortcut = KeyCode.R },
            new WeaponEntry { id = WeaponId.Napalm,       displayName = "Napalm aéreo (clic)",        implemented = true, shortcut = KeyCode.F },
            new WeaponEntry { id = WeaponId.RatBomb,      displayName = "Rata-bomba",                 implemented = true, shortcut = KeyCode.T },
            new WeaponEntry { id = WeaponId.Mine,         displayName = "Mina",                       implemented = true, shortcut = KeyCode.G },
            new WeaponEntry { id = WeaponId.Pigeon,       displayName = "Paloma bomba",               implemented = true, shortcut = KeyCode.Y },
            new WeaponEntry { id = WeaponId.Mortar,       displayName = "Mortero",                    implemented = true, shortcut = KeyCode.H },
            new WeaponEntry { id = WeaponId.GasCan,       displayName = "Bidón de gas",               implemented = true, shortcut = KeyCode.Z },
            new WeaponEntry { id = WeaponId.Cheese,       displayName = "Queso apestoso",             implemented = true, shortcut = KeyCode.X },
            new WeaponEntry { id = WeaponId.Bayonet,      displayName = "Bayoneta",                   implemented = true, shortcut = KeyCode.B },
            new WeaponEntry { id = WeaponId.Shovel,       displayName = "Pala",                       implemented = true, shortcut = KeyCode.N },
            new WeaponEntry { id = WeaponId.Sandbags,     displayName = "Sacos terreros",             implemented = true, shortcut = KeyCode.M },
            new WeaponEntry { id = WeaponId.HolyCheese,   displayName = "Bomba bíblica de queso",     implemented = true, shortcut = KeyCode.J },
            new WeaponEntry { id = WeaponId.Sniper,       displayName = "Francotirador",              implemented = true, shortcut = KeyCode.K },
            new WeaponEntry { id = WeaponId.Bow,          displayName = "Arco",                       implemented = true, shortcut = KeyCode.L },
            new WeaponEntry { id = WeaponId.Banana,     displayName = "Plátano bomba",        implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Molotov,    displayName = "Cóctel molotov",       implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Fireball,   displayName = "Bola de fuego",        implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Homing,     displayName = "Misil teledirigido",   implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Anvil,      displayName = "Yunque",               implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.MineStrike, displayName = "Lluvia de minas",      implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Meteors,    displayName = "Lluvia de meteoritos", implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Earthquake, displayName = "Terremoto",            implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.RabidRat,   displayName = "Rata rabiosa",         implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.GrannyRat,  displayName = "Abuela rata",          implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.StinkRat,   displayName = "Rata apestosa",        implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Mole,       displayName = "Topo excavador",       implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Stampede,   displayName = "Estampida",            implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Pistol,     displayName = "Pistola",              implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.FirePunch,  displayName = "Puño de fuego",        implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Prod,       displayName = "Empujón",              implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Kamikaze,   displayName = "Kamikaze",             implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Medkit,     displayName = "Botiquín",             implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.SuperJump,  displayName = "Muelle",               implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Parachute,  displayName = "Paracaídas",           implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Scales,     displayName = "Balanza de la justicia", implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Blowtorch,  displayName = "Soplete",              implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Drill,      displayName = "Martillo neumático",   implemented = true, shortcut = KeyCode.None },
            new WeaponEntry { id = WeaponId.Girder,     displayName = "Pasarela",             implemented = true, shortcut = KeyCode.None },
        };

        /// <summary>Tecla que se pinta en el arsenal.</summary>
        /// <summary>
        ///  0.30: la tecla que se pinta es la de su RANURA FAVORITA (1-9, 0) si
        ///  la tiene; si no, nada. Adiós a las letras sueltas (Q, E, R, T...):
        ///  cada uno pone en las ranuras las armas que más usa.
        /// </summary>
        private static string KeyLabel(WeaponEntry w) => KeyLabel(w.id);
        public static string KeyLabel(WeaponId id)
        {
            int slot = Ratms.UI.Controls.SlotOf((int)id);
            return slot < 0 ? "" : Ratms.UI.Controls.Short(Ratms.UI.Controls.Act.Slot1 + slot);
        }

        /// <summary>Nombre corto del arma para el HUD.</summary>
        public static string ShortName(WeaponId id) => Ratms.UI.Loc.T(ShortNameEs(id));

        /// <summary>Nombre corto en español (la clave de traducción).</summary>
        public static string ShortNameEs(WeaponId id) => id switch
        {
            WeaponId.Bazooka  => "Bazooka",
            WeaponId.Grenade  => "Granada",
            WeaponId.Dynamite => "Dinamita",
            WeaponId.Shotgun  => "Escopeta",
            WeaponId.Bat      => "Bate",
            WeaponId.Teleport => "Teletransporte",
            WeaponId.Rope     => "Cuerda ninja",
            WeaponId.SelectRat => "Cambiar rata",
            WeaponId.Flamethrower => "Lanzallamas",
            WeaponId.MachineGun   => "Metralleta",
            WeaponId.ClusterBomb  => "Bomba de racimo",
            WeaponId.AirStrike    => "Ataque aéreo",
            WeaponId.Napalm       => "Napalm aéreo",
            WeaponId.RatBomb      => "Rata-bomba",
            WeaponId.Mine         => "Mina",
            WeaponId.Pigeon       => "Paloma bomba",
            WeaponId.Mortar       => "Mortero",
            WeaponId.GasCan       => "Bidón de gas",
            WeaponId.Cheese       => "Queso apestoso",
            WeaponId.Bayonet      => "Bayoneta",
            WeaponId.Shovel       => "Pala",
            WeaponId.Sandbags     => "Sacos terreros",
            WeaponId.HolyCheese   => "Bomba bíblica de queso",
            WeaponId.Sniper       => "Francotirador",
            WeaponId.Bow          => "Arco",
            WeaponId.Banana     => "Plátano bomba",
            WeaponId.Molotov    => "Cóctel molotov",
            WeaponId.Fireball   => "Bola de fuego",
            WeaponId.Homing     => "Misil teledirigido",
            WeaponId.Anvil      => "Yunque",
            WeaponId.MineStrike => "Lluvia de minas",
            WeaponId.Meteors    => "Lluvia de meteoritos",
            WeaponId.Earthquake => "Terremoto",
            WeaponId.RabidRat   => "Rata rabiosa",
            WeaponId.GrannyRat  => "Abuela rata",
            WeaponId.StinkRat   => "Rata apestosa",
            WeaponId.Mole       => "Topo excavador",
            WeaponId.Stampede   => "Estampida",
            WeaponId.Pistol     => "Pistola",
            WeaponId.FirePunch  => "Puño de fuego",
            WeaponId.Prod       => "Empujón",
            WeaponId.Kamikaze   => "Kamikaze",
            WeaponId.Medkit     => "Botiquín",
            WeaponId.SuperJump  => "Muelle",
            WeaponId.Parachute  => "Paracaídas",
            WeaponId.Scales     => "Balanza de la justicia",
            WeaponId.Blowtorch  => "Soplete",
            WeaponId.Drill      => "Martillo neumático",
            WeaponId.Girder     => "Pasarela",
            _                 => "Ninguna",
        };

        /// <summary>Arma actualmente seleccionada (leído por NetworkBazooka y demás).</summary>
        public WeaponId SelectedWeapon => (WeaponId)SelectedIndexNet.Value;
        public bool IsBazookaEquipped => SelectedWeapon == WeaponId.Bazooka;
        public bool IsVisible => visible;

        /// <summary>Abre/cierra el arsenal (prueba automática).</summary>
        public void SetVisible(bool v) => visible = v;
        /// <summary>¿Está abierto el panel de armas?</summary>
        public bool IsOpen => visible;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // Empezamos con el arma por defecto (None, por defecto → sin arma)
                SelectedIndexNet.Value = (int)defaultWeapon;
            }
        }

        private NetworkPlayerMovement ownerRat;

        /// <summary>
        ///  Solo atiende teclas/dibuja la rata DEL TURNO: con equipos el
        ///  jugador tiene varias ratas y si no todas abrirían el menú a la vez.
        /// </summary>
        private bool IsMyActiveRat()
        {
            if (!IsOwner) return false;
            if (ownerRat == null) ownerRat = GetComponentInParent<NetworkPlayerMovement>();
            if (ownerRat == null || NetworkTurnManager.Instance == null) return true;
            return ownerRat.IsActiveTurnRat;
        }

        // Aros sobre las compañeras cuando "Cambiar rata" está equipado.
        private readonly System.Collections.Generic.List<SpriteRenderer> mateMarkers = new();

        private void UpdateMateMarkers(bool show)
        {
            int n = 0;
            if (show && ownerRat != null)
            {
                foreach (var r in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                {
                    if (r == ownerRat || r.TeamIndex.Value != ownerRat.TeamIndex.Value || r.IsTestDummy) continue;
                    var h = r.GetComponent<NetworkHealth>();
                    if (h == null || h.IsDead.Value) continue;
                    if (n >= mateMarkers.Count)
                    {
                        var go = new GameObject("MarcaCompañera");
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = WeaponSprites.Ring();
                        sr.sortingOrder = 95;
                        mateMarkers.Add(sr);
                    }
                    var m = mateMarkers[n++];
                    m.gameObject.SetActive(true);
                    float pulse = 1.1f + 0.12f * Mathf.Sin(Time.time * 6f);
                    m.transform.position = (Vector3)h.BodyCenter;
                    m.transform.localScale = new Vector3(pulse, pulse, 1f);
                    m.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                }
            }
            for (int i = n; i < mateMarkers.Count; i++) mateMarkers[i].gameObject.SetActive(false);
        }

        public override void OnDestroy()
        {
            foreach (var m in mateMarkers) if (m != null) Destroy(m.gameObject);
            base.OnDestroy();
        }

        private void Update()
        {
            bool mine = IsMyActiveRat();
            UpdateMateMarkers(mine && SelectedWeapon == WeaponId.SelectRat);
            if (!mine) { visible = false; return; }

            // Sin brazos: se le cae el arma de las manos (que no tiene).
            if (!HandsOk && NeedsHands(SelectedWeapon)) SelectedIndexNet.Value = (int)WeaponId.None;

            if (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.Arsenal) && !Ratms.UI.PauseMenu.IsOpen)
            {
                visible = !visible;
                if (visible) { cursor = SelectedWeapon; cursorByKeys = false; }
            }
            if (visible && Input.GetKeyDown(KeyCode.Escape)) visible = false;
            if (visible) Ratms.UI.Controls.BlockGameplayThisFrame();   // las flechas mueven el cursor, no la rata

            // 0.30: RANURAS FAVORITAS (1-9 y 0, reasignables). Con el arsenal
            // abierto y un arma señalada (ratón encima o cursor movido con las
            // flechas), el número la GUARDA en esa ranura; si no, la saca.
            int slot = Ratms.UI.Controls.SlotPressed();
            if (slot >= 0)
            {
                if (visible && (hoverId.HasValue || cursorByKeys))
                {
                    var id = hoverId ?? cursor;
                    Ratms.UI.Controls.SetFav(slot, (int)id);
                    Sfx.Play(Sfx.Id.Whack, 0.3f, 0.1f);
                    favFlash = Time.unscaledTime;
                }
                else
                {
                    int fav = Ratms.UI.Controls.Fav(slot);
                    if (fav >= -1 && ((WeaponId)fav == WeaponId.None || Usable((WeaponId)fav))) Choose((WeaponId)fav);
                    else Sfx.Play(Sfx.Id.Whack, 0.15f, 0.4f);
                }
            }

            // Arma siguiente / anterior (sin tecla por defecto; se pone en Opciones → Controles)
            if (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.NextWeapon)) Cycle(+1);
            if (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.PrevWeapon)) Cycle(-1);

            if (visible) MenuKeys();
        }

        // ---------- 0.30: cursor del arsenal con teclado ----------
        private WeaponId cursor = WeaponId.None;
        private bool cursorByKeys;
        private WeaponId? hoverId;
        private float favFlash = -10f;

        /// <summary>Elige un arma y cierra el arsenal.</summary>
        public void Choose(WeaponId id)
        {
            SelectedIndexNet.Value = (int)id;
            visible = false;
            Sfx.Play(Sfx.Id.Whack, 0.25f, 0.15f);
        }

        /// <summary>Recorre las armas usables en el orden del arsenal.</summary>
        private void Cycle(int dir)
        {
            var all = new System.Collections.Generic.List<WeaponId>();
            foreach (var (_, ids) in Groups) foreach (var id in ids) if (id == WeaponId.None || Usable(id)) all.Add(id);
            if (all.Count == 0) return;
            int i = all.IndexOf(SelectedWeapon);
            i = ((i < 0 ? 0 : i + dir) % all.Count + all.Count) % all.Count;
            SelectedIndexNet.Value = (int)all[i];
            Sfx.Play(Sfx.Id.Whack, 0.2f, 0.2f);
        }

        /// <summary>Filas del arsenal tal y como se dibujan (8 por fila; cada grupo empieza fila).</summary>
        private static System.Collections.Generic.List<WeaponId[]> Rows()
        {
            var rows = new System.Collections.Generic.List<WeaponId[]>();
            foreach (var (_, ids) in Groups)
                for (int i = 0; i < ids.Length; i += 8)
                {
                    int n = Mathf.Min(8, ids.Length - i);
                    var r = new WeaponId[n];
                    System.Array.Copy(ids, i, r, 0, n);
                    rows.Add(r);
                }
            return rows;
        }

        private void MenuKeys()
        {
            int dx = (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.MenuRight) ? 1 : 0) - (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.MenuLeft) ? 1 : 0);
            int dy = (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.MenuDown) ? 1 : 0) - (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.MenuUp) ? 1 : 0);
            if (dx != 0 || dy != 0)
            {
                var rows = Rows();
                int ry = 0, rx = 0;
                for (int r = 0; r < rows.Count; r++) { int k = System.Array.IndexOf(rows[r], cursor); if (k >= 0) { ry = r; rx = k; break; } }
                if (dy != 0) { ry = Mathf.Clamp(ry + dy, 0, rows.Count - 1); rx = Mathf.Min(rx, rows[ry].Length - 1); }
                if (dx != 0)
                {
                    rx += dx;
                    if (rx < 0 && ry > 0) { ry--; rx = rows[ry].Length - 1; }
                    else if (rx >= rows[ry].Length && ry < rows.Count - 1) { ry++; rx = 0; }
                    rx = Mathf.Clamp(rx, 0, rows[ry].Length - 1);
                }
                cursor = rows[ry][rx];
                cursorByKeys = true;
                Sfx.Play(Sfx.Id.Whack, 0.08f, 0.5f);
            }
            if (Ratms.UI.Controls.Down(Ratms.UI.Controls.Act.MenuOk) && (cursor == WeaponId.None || Usable(cursor))) Choose(cursor);
        }

        /// <summary>Munición que le queda al equipo de esta rata (-1 = infinita).</summary>
        public int AmmoLeft(WeaponId id)
        {
            var tm = NetworkTurnManager.Instance;
            if (ownerRat == null) ownerRat = GetComponentInParent<NetworkPlayerMovement>();
            if (tm == null || ownerRat == null) return -1;
            return tm.AmmoOf(ownerRat.TeamIndex.Value, id);
        }

        public bool HasAmmo(WeaponId id) => AmmoLeft(id) != 0;

        /// <summary>¿Hace falta tener brazos para usar esta arma?</summary>
        public static bool NeedsHands(WeaponId id) => id != WeaponId.None && id != WeaponId.Teleport && id != WeaponId.SelectRat
                                                      && id != WeaponId.AirStrike && id != WeaponId.Napalm    // el avión se pide por radio
                                                      && id != WeaponId.Pigeon && id != WeaponId.RatBomb      // se van solas
                                                      // 0.28: animales que van solos, desastres y utilidades
                                                      && id != WeaponId.RabidRat && id != WeaponId.GrannyRat && id != WeaponId.StinkRat
                                                      && id != WeaponId.Mole && id != WeaponId.Stampede && id != WeaponId.MineStrike
                                                      && id != WeaponId.Meteors && id != WeaponId.Earthquake && id != WeaponId.Anvil
                                                      && id != WeaponId.Kamikaze && id != WeaponId.Medkit && id != WeaponId.SuperJump
                                                      && id != WeaponId.Parachute && id != WeaponId.Scales;

        /// <summary>¿Se apunta con el RATÓN (clic en el mapa) en vez de con W/S?</summary>
        public static bool ClickTargeted(WeaponId id) => id == WeaponId.Teleport || id == WeaponId.SelectRat
                                                         || id == WeaponId.AirStrike || id == WeaponId.Napalm
                                                         || id == WeaponId.Pigeon || id == WeaponId.Sandbags
                                                         || id == WeaponId.Homing || id == WeaponId.Anvil || id == WeaponId.MineStrike || id == WeaponId.Girder;

        private NetworkHealth ownerHealth;

        /// <summary>false = rata SIN BRAZOS (solo muerde, teletransporta o cambia de rata).</summary>
        public bool HandsOk
        {
            get
            {
                if (ownerHealth == null)
                {
                    if (ownerRat == null) ownerRat = GetComponentInParent<NetworkPlayerMovement>();
                    if (ownerRat != null) ownerHealth = ownerRat.GetComponent<NetworkHealth>();
                }
                return ownerHealth == null || ownerHealth.CanUseHands;
            }
        }

        private bool Usable(WeaponId id) => HasAmmo(id) && (HandsOk || !NeedsHands(id));

        // ==============================================================
        //  ARSENAL (panel de armas) — estilo caja de munición de trinchera
        // ==============================================================
        private GUIStyle rowStyle, rowSel, titleStyle, hintStyle, ammoStyle, keyStyle;
        private Texture2D panelTex, rowTex, rowSelTex, rowOffTex;
        private float stylesScale = -1f;

        private static Sprite IconOf(WeaponId id) => id switch
        {
            WeaponId.Bazooka => WeaponArt.Bazooka(),
            WeaponId.Grenade => WeaponArt.StickGrenade(),
            WeaponId.Dynamite => WeaponArt.Dynamite(),
            WeaponId.Shotgun => WeaponArt.Shotgun(),
            WeaponId.Bat => WeaponArt.Bat(),
            WeaponId.Teleport => WeaponArt.TeleportIcon(),
            WeaponId.Rope => WeaponArt.RopeIcon(),
            WeaponId.SelectRat => WeaponArt.WeaponCrate(),
            WeaponId.Flamethrower => WeaponArt.Flamethrower(),
            WeaponId.MachineGun => WeaponArt.MachineGun(),
            WeaponId.ClusterBomb => WeaponArt.ClusterBomb(),
            WeaponId.AirStrike => WeaponArt.Plane(),
            WeaponId.Napalm => WeaponArt.NapalmIcon(),
            WeaponId.RatBomb => WeaponArt.RatBombIcon(),
            WeaponId.Mine => WeaponArt.Mine(),
            WeaponId.Pigeon => WeaponArt.Pigeon(0),
            WeaponId.Mortar => WeaponArt.Mortar(),
            WeaponId.GasCan => WeaponArt.GasCan(),
            WeaponId.Cheese => WeaponArt.Cheese(),
            WeaponId.Bayonet => WeaponArt.Bayonet(),
            WeaponId.Shovel => WeaponArt.Shovel(),
            WeaponId.Sandbags => WeaponArt.Sandbags(),
            WeaponId.HolyCheese => WeaponArt.HolyCheese(),
            WeaponId.Sniper => WeaponArt.Sniper(),
            WeaponId.Bow => WeaponArt.Bow(),
            WeaponId.Banana => WeaponArt.Banana(),
            WeaponId.Molotov => WeaponArt.Molotov(),
            WeaponId.Fireball => WeaponArt.Fireball(),
            WeaponId.Homing => WeaponArt.Missile(),
            WeaponId.Anvil => WeaponArt.Anvil(),
            WeaponId.MineStrike => WeaponArt.Mine(),
            WeaponId.Meteors => WeaponArt.Meteor(),
            WeaponId.Earthquake => WeaponArt.Quake(),
            WeaponId.RabidRat => RatIcon(NetworkGadget.WalkerLook(NetworkGadget.Kind.RabidRat)),
            WeaponId.GrannyRat => RatIcon(NetworkGadget.WalkerLook(NetworkGadget.Kind.GrannyRat)),
            WeaponId.StinkRat => RatIcon(NetworkGadget.WalkerLook(NetworkGadget.Kind.StinkRat)),
            WeaponId.Mole => RatIcon(NetworkGadget.WalkerLook(NetworkGadget.Kind.Mole)),
            WeaponId.Stampede => WeaponArt.RatBombIcon(),
            WeaponId.Pistol => WeaponArt.Pistol(),
            WeaponId.FirePunch => WeaponArt.Fist(),
            WeaponId.Prod => WeaponArt.Prod(),
            WeaponId.Kamikaze => WeaponArt.Kamikaze(),
            WeaponId.Medkit => WeaponArt.HealthCrate(),
            WeaponId.SuperJump => WeaponArt.Spring(),
            WeaponId.Parachute => WeaponArt.ParachuteIcon(),
            WeaponId.Scales => WeaponArt.Scales(),
            WeaponId.Blowtorch => WeaponArt.Blowtorch(),
            WeaponId.Drill => WeaponArt.Drill(),
            WeaponId.Girder => WeaponArt.Sandbags(),
            _ => null,
        };

        private static Sprite RatIcon(RatLook look) => RatAppearance.Preview(look.Pack(), RatAppearance.ColorForTeam(0));

        // ---------- 0.20: cuadrícula por grupos + ficha de "cómo se usa" ----------
        // Orden y grupos del panel (como la caja de armas de Worms).
        private static readonly (string group, WeaponId[] ids)[] Groups =
        {
            ("Explosivos", new[] { WeaponId.Bazooka, WeaponId.Grenade, WeaponId.ClusterBomb, WeaponId.Dynamite, WeaponId.Mortar, WeaponId.Banana, WeaponId.Molotov, WeaponId.HolyCheese }),
            ("Armas de fuego", new[] { WeaponId.Shotgun, WeaponId.MachineGun, WeaponId.Pistol, WeaponId.Sniper, WeaponId.Bow, WeaponId.Flamethrower, WeaponId.Fireball, WeaponId.Homing }),
            ("Cuerpo a cuerpo", new[] { WeaponId.Bat, WeaponId.Bayonet, WeaponId.FirePunch, WeaponId.Prod, WeaponId.Kamikaze }),
            ("Animales y trampas", new[] { WeaponId.RatBomb, WeaponId.RabidRat, WeaponId.GrannyRat, WeaponId.StinkRat, WeaponId.Mole, WeaponId.Stampede, WeaponId.Mine, WeaponId.GasCan, WeaponId.Cheese }),
            ("Movimiento y obras", new[] { WeaponId.Rope, WeaponId.Teleport, WeaponId.SuperJump, WeaponId.Parachute, WeaponId.Shovel, WeaponId.Blowtorch, WeaponId.Drill, WeaponId.Sandbags, WeaponId.Girder }),
            ("Desde el cielo", new[] { WeaponId.AirStrike, WeaponId.Napalm, WeaponId.Pigeon, WeaponId.MineStrike, WeaponId.Anvil, WeaponId.Meteors, WeaponId.Earthquake }),
            ("Utilidades", new[] { WeaponId.Medkit, WeaponId.Scales, WeaponId.SelectRat, WeaponId.None }),
        };

        /// <summary>Cómo se usa cada arma (una o dos líneas, en español: se traduce).</summary>
        public static string HowTo(WeaponId id) => id switch
        {
            WeaponId.Bazooka => "W/S apunta. Mantén ESPACIO para cargar y suelta para disparar. El viento lo desvía.",
            WeaponId.Grenade => "W/S apunta. Mantén ESPACIO para la fuerza. Rebota y explota a los 3 segundos.",
            WeaponId.ClusterBomb => "Como la granada, pero al explotar suelta cinco bombetas.",
            WeaponId.Dynamite => "ESPACIO la deja a tus pies. Explota a los 5 segundos: ¡corre!",
            WeaponId.Shotgun => "W/S apunta y ESPACIO dispara. Tienes dos disparos este turno.",
            WeaponId.MachineGun => "W/S apunta y ESPACIO suelta una ráfaga de balas.",
            WeaponId.Flamethrower => "W/S apunta. Mantén ESPACIO para echar fuego.",
            WeaponId.Bat => "Ponte al lado y pulsa ESPACIO: manda a volar a quien tengas delante.",
            WeaponId.Rope => "W/S apunta y ESPACIO la lanza. ← → para balancearte, W/S para subir o bajar, ESPACIO o ↑ la suelta.",
            WeaponId.Teleport => "Haz clic donde quieras aparecer.",
            WeaponId.SelectRat => "Haz clic en otra rata tuya: jugará tu próximo turno (este lo pierdes).",
            WeaponId.AirStrike => "Haz clic en el mapa: caen cinco bombas en fila. Arrastra con el clic derecho para mirar lejos.",
            WeaponId.Napalm => "Haz clic en el mapa: lluvia de fuego. Arrastra con el clic derecho para mirar lejos.",
            WeaponId.RatBomb => "ESPACIO suelta una rata con dinamita que corre sola y salta. Vuelve a pulsar ESPACIO para hacerla estallar.",
            WeaponId.Mine => "ESPACIO la deja a tus pies. Se arma en 2 segundos y explota cuando alguien pasa cerca.",
            WeaponId.Pigeon => "Haz clic en el objetivo: la paloma vuela hacia allí esquivando el terreno por arriba.",
            WeaponId.Mortar => "W/S apunta. Mantén ESPACIO para cargar. El proyectil cae en picado y suelta metralla. No le afecta el viento.",
            WeaponId.GasCan => "W/S apunta. Mantén ESPACIO para lanzarlo. Suelta una nube venenosa que el viento arrastra.",
            WeaponId.Cheese => "W/S apunta. Mantén ESPACIO para lanzarlo. Las ratas de alrededor corren hacia él... y revienta.",
            WeaponId.Bayonet => "Ponte al lado y pulsa ESPACIO: estocada hacia arriba que siempre arranca algo.",
            WeaponId.Shovel => "W/S apunta y ESPACIO cava un túnel en esa dirección para esconderte.",
            WeaponId.Sandbags => "Haz clic donde quieras poner una barricada de sacos. Sirve de cobertura o de puente.",
            WeaponId.HolyCheese => "W/S apunta. Mantén ESPACIO para lanzarla. ¡Aleluya! Explosión enorme. Solo sale en cajas.",
            WeaponId.Sniper => "W/S apunta (la mira láser ayuda) y ESPACIO dispara. Mucho daño y llega muy lejos.",
            WeaponId.Bow => "W/S apunta. Mantén ESPACIO para tensar. Cada flecha hace un efecto al azar: quema, veneno, ceguera o amputación.",
            WeaponId.Banana => "W/S apunta. Mantén ESPACIO para lanzarlo. Rebota, explota y se parte en cinco plátanos más.",
            WeaponId.Molotov => "W/S apunta. Mantén ESPACIO para lanzarlo. Se rompe al tocar algo y lo llena todo de fuego.",
            WeaponId.Fireball => "W/S apunta y ESPACIO la lanza en línea recta. Revienta y quema lo que toca.",
            WeaponId.Homing => "Haz clic en el objetivo: el misil gira solo hasta él (no esquiva el terreno).",
            WeaponId.Anvil => "Haz clic en el mapa: cae un yunque desde el cielo, rebota y aplasta varias veces.",
            WeaponId.MineStrike => "Haz clic en el mapa: caen cinco minas desde el cielo.",
            WeaponId.Meteors => "ESPACIO: caen meteoritos por todo el mapa. ¡Nadie está a salvo!",
            WeaponId.Earthquake => "ESPACIO: tiembla la tierra, todas las ratas salen despedidas y se hacen daño.",
            WeaponId.RabidRat => "ESPACIO la suelta: corre muy rápido y salta mucho. ESPACIO otra vez para reventarla.",
            WeaponId.GrannyRat => "ESPACIO la suelta: anda despacito y explota a lo grande al cabo de un rato.",
            WeaponId.StinkRat => "ESPACIO la suelta: corre y al final suelta una nube enorme de gas venenoso.",
            WeaponId.Mole => "ESPACIO lo suelta: cava hacia abajo y hacia delante por dentro del terreno y explota.",
            WeaponId.Stampede => "ESPACIO suelta TRES ratas-bomba. ESPACIO las revienta una a una.",
            WeaponId.Pistol => "W/S apunta y ESPACIO dispara tres balas seguidas.",
            WeaponId.FirePunch => "Pégate al enemigo y pulsa ESPACIO: un gancho en llamas que lo lanza por los aires.",
            WeaponId.Prod => "Pégate al enemigo y pulsa ESPACIO: un empujoncito... ideal para tirarlo al agua.",
            WeaponId.Kamikaze => "ESPACIO: tu rata se inmola con una gran explosión. Adiós, valiente.",
            WeaponId.Medkit => "ESPACIO: tu rata se cura 30 de vida y se le quita el veneno.",
            WeaponId.SuperJump => "ESPACIO: un muelle te lanza muy alto. Luego puedes seguir moviéndote.",
            WeaponId.Parachute => "ESPACIO: abres el paracaídas y caes despacio el resto del turno.",
            WeaponId.Scales => "ESPACIO: todas las ratas se quedan con la misma vida (la media). ¡Justicia!",
            WeaponId.Blowtorch => "W/S apunta y ESPACIO: abre un túnel largo quemando todo lo que hay delante.",
            WeaponId.Drill => "ESPACIO: el martillo neumático cava un pozo hacia abajo.",
            WeaponId.Girder => "Haz clic para poner una pasarela larga (puente o muro).",
            _ => "Guarda el arma para andar y saltar con las manos libres.",
        };

        private WeaponEntry EntryOf(WeaponId id)
        {
            foreach (var w in weapons) if (w.id == id) return w;
            return weapons[0];
        }

        /// <summary>0.30: barra rápida de favoritas a la derecha (Opciones → "Barra de armas favoritas").</summary>
        private static Rect quickBarScreen;
        private static int quickBarFrame = -10;

        /// <summary>
        ///  ¿Está el ratón encima de la barra rápida? Las armas de clic
        ///  (teletransporte, avión...) no deben disparar con ese clic.
        /// </summary>
        public static bool MouseOverUI
        {
            get
            {
                if (Time.frameCount - quickBarFrame > 3) return false;
                var m = Input.mousePosition;
                return quickBarScreen.Contains(new Vector2(m.x, Screen.height - m.y));
            }
        }

        public static bool QuickBarEnabled
        {
            get => PlayerPrefs.GetInt("opt_quickbar", 1) == 1;
            set => PlayerPrefs.SetInt("opt_quickbar", value ? 1 : 0);
        }

        /// <summary>
        ///  0.30 — BARRA RÁPIDA: las 10 ranuras favoritas en una columna a la
        ///  derecha (tecla, dibujo y munición). Se ve en tu turno con el arsenal
        ///  cerrado; un clic elige el arma. Así no hace falta recordar nada.
        /// </summary>
        private void DrawQuickBar()
        {
            if (!QuickBarEnabled || Ratms.UI.PauseMenu.IsOpen) return;
            if (ownerRat != null && ownerRat.FiredThisTurn) return;   // ya disparó: que no estorbe
            Ratms.UI.TrenchGUI.Begin();
            EnsureStyles(1f);
            float vw = Ratms.UI.TrenchGUI.VW, vh = Ratms.UI.TrenchGUI.VH;
            const float t = 60f, gap = 5f;
            int n = Ratms.UI.Controls.Slots;
            float H = n * (t + gap) - gap;
            float x = vw - t - 14f, y0 = Mathf.Max(150f, (vh - H) * 0.5f);
            var e = Event.current;
            float S = Ratms.UI.TrenchGUI.Scale;
            quickBarScreen = new Rect((x - 4f) * S, (y0 - 4f) * S, (t + 8f) * S, (H + 8f) * S);
            quickBarFrame = Time.frameCount;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x, y0 + i * (t + gap), t, t);
                int fav = Ratms.UI.Controls.Fav(i);
                bool empty = fav < -1;
                var id = (WeaponId)fav;
                bool hover = r.Contains(e.mousePosition);
                if (e.type == EventType.Repaint)
                {
                    if (empty)
                    {
                        var c = GUI.color;
                        GUI.color = new Color(0f, 0f, 0f, 0.25f);
                        GUI.DrawTexture(r, Texture2D.whiteTexture);
                        GUI.color = c;
                        GUI.Label(new Rect(r.x + 4f, r.y + 2f, 30f, 20f), Ratms.UI.Controls.Short(Ratms.UI.Controls.Act.Slot1 + i), keyStyle);
                    }
                    else DrawTile(r, id, hover);
                }
                if (!empty && GUI.Button(r, GUIContent.none, GUIStyle.none) && (id == WeaponId.None || Usable(id)))
                    Choose(id);
                if (hover && !empty)   // nombre al pasar el ratón
                {
                    string name = id == WeaponId.None ? Ratms.UI.Loc.T("Guardar arma") : ShortName(id);
                    var lr = new Rect(x - 260f, r.y + 12f, 250f, 26f);
                    hintStyle.alignment = TextAnchor.MiddleRight;
                    hintStyle.normal.textColor = Color.white;
                    GUI.Label(lr, name, hintStyle);
                    hintStyle.alignment = TextAnchor.MiddleCenter;
                }
            }
            Ratms.UI.TrenchGUI.End();
        }

        private void OnGUI()
        {
            if (!IsMyActiveRat()) return;
            if (!visible) { DrawQuickBar(); return; }

            Ratms.UI.TrenchGUI.Begin();
            EnsureStyles(1f);
            float vh = Ratms.UI.TrenchGUI.VH;

            // 0.28: 8 columnas y los grupos largos en varias filas (hay 49 armas)
            const float tile = 54f, gap = 6f, pad = 18f, cols = 8f, groupH = 22f, infoH = 96f;
            float W = pad * 2f + cols * tile + (cols - 1f) * gap;
            float H = 54f;
            foreach (var g in Groups) H += groupH + Mathf.Ceil(g.ids.Length / cols) * (tile + gap);
            H += infoH + 52f;
            float x = panelOffset.x, y = Mathf.Max(10f, vh - H - panelOffset.y);
            var panel = new Rect(x, y, W, H);
            Ratms.UI.TrenchGUI.Panel(panel);
            Ratms.UI.TrenchGUI.Title(new Rect(x, y + 6f, W, 40f), "ARSENAL");

            WeaponId shown = cursor;   // la ficha: la que tenga el ratón encima, o la del cursor
            if (Event.current.type == EventType.Repaint) hoverId = null;
            float gy = y + 54f;
            var e = Event.current;
            foreach (var (group, ids) in Groups)
            {
                Ratms.UI.TrenchGUI.Small(new Rect(x + pad, gy, W - pad * 2f, groupH), group);
                gy += groupH;
                for (int i = 0; i < ids.Length; i++)
                {
                    var id = ids[i];
                    var r = new Rect(x + pad + (i % (int)cols) * (tile + gap), gy + (i / (int)cols) * (tile + gap), tile, tile);
                    bool hover = r.Contains(e.mousePosition);
                    if (hover) { shown = id; if (e.type == EventType.Repaint) { hoverId = id; cursor = id; cursorByKeys = false; } }
                    DrawTile(r, id, hover || (cursorByKeys && id == cursor));
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && (id == WeaponId.None || Usable(id)))
                        Choose(id);   // 0.30: elegir cierra el arsenal
                }
                gy += Mathf.Ceil(ids.Length / cols) * (tile + gap);
            }

            // Ficha: nombre + cómo se usa
            var info = new Rect(x + pad, gy + 2f, W - pad * 2f, infoH);
            if (e.type == EventType.Repaint)
            {
                var c = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(info, Texture2D.whiteTexture);
                GUI.color = c;
            }
            string title = shown == WeaponId.None ? Ratms.UI.Loc.T("Guardar arma") : ShortName(shown);
            int ammo = shown == WeaponId.None ? -1 : AmmoLeft(shown);
            Ratms.UI.TrenchGUI.Label(new Rect(info.x + 10f, info.y + 4f, info.width - 20f, 30f),
                title + (shown == WeaponId.None ? "" : ammo < 0 ? "   ∞" : $"   x{ammo}") + $"   [{KeyLabel(EntryOf(shown))}]");
            string how = Ratms.UI.Controls.Explain(Ratms.UI.Loc.T(HowTo(shown)));
            if (!HandsOk && NeedsHands(shown)) how = Ratms.UI.Loc.T("Sin brazos no puedes usarla: con ESPACIO muerdes.");
            else if (ammo == 0) how = Ratms.UI.Loc.T("Sin munición. Las cajas que caen del cielo traen más.");
            GUI.Label(new Rect(info.x + 10f, info.y + 36f, info.width - 20f, info.height - 40f), how, Ratms.UI.TrenchGUI.SmallStyle);

            bool flash = Time.unscaledTime - favFlash < 0.8f;
            string hint = flash ? Ratms.UI.Loc.T("¡Guardada en la ranura!")
                                : Ratms.UI.Controls.Explain(Ratms.UI.Loc.T("Clic, flechas + ENTER o 1-0 para elegir  ·  ratón encima + número = guardarla en esa ranura  ·  TAB cierra"));
            hintStyle.normal.textColor = flash ? new Color(1f, 0.9f, 0.4f) : new Color(0.8f, 0.76f, 0.66f);
            hintStyle.wordWrap = true;
            GUI.Label(new Rect(x + 10f, y + H - 50f, W - 20f, 44f), hint, hintStyle);
            Ratms.UI.TrenchGUI.End();
        }

        /// <summary>Casilla de un arma: icono, tecla en la esquina y munición.</summary>
        private void DrawTile(Rect r, WeaponId id, bool hover)
        {
            if (Event.current.type != EventType.Repaint) return;
            int ammo = id == WeaponId.None ? -1 : AmmoLeft(id);
            bool usable = Usable(id) || id == WeaponId.None;
            bool sel = id == SelectedWeapon;
            var c = GUI.color;
            // Marco: latón si está elegida, claro al pasar el ratón
            GUI.color = sel ? new Color(1f, 0.8f, 0.3f, 1f) : hover ? new Color(1f, 1f, 1f, 0.55f) : new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
            GUI.color = sel ? new Color(0.42f, 0.30f, 0.12f, 1f) : usable ? new Color(0.22f, 0.18f, 0.12f, 1f) : new Color(0.13f, 0.11f, 0.09f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;

            var icon = IconOf(id);
            if (icon != null)
            {
                var tr = icon.textureRect;
                var tex = icon.texture;
                float maxW = r.width - 16f, maxH = r.height - 30f;
                float k = Mathf.Min(maxW / tr.width, maxH / tr.height);
                float iw = tr.width * k, ih = tr.height * k;
                if (!usable) GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTextureWithTexCoords(new Rect(r.center.x - iw * 0.5f, r.y + 8f + (maxH - ih) * 0.5f, iw, ih), tex,
                    new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
                GUI.color = c;
            }
            else
            {
                // "Guardar arma": sin dibujo, solo el texto
                int fs = rowStyle.fontSize;
                rowStyle.alignment = TextAnchor.MiddleCenter;
                rowStyle.fontSize = 13;
                GUI.Label(new Rect(r.x, r.y + 4f, r.width, r.height - 20f), Ratms.UI.Loc.T("Manos libres"), rowStyle);
                rowStyle.alignment = TextAnchor.MiddleLeft;
                rowStyle.fontSize = fs;
            }
            // Tecla (arriba a la izquierda) y munición (abajo)
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, 30f, 20f), KeyLabel(EntryOf(id)), keyStyle);
            if (id != WeaponId.None)
            {
                ammoStyle.normal.textColor = ammo == 0 ? new Color(0.95f, 0.35f, 0.3f) : new Color(1f, 0.88f, 0.55f);
                GUI.Label(new Rect(r.x, r.yMax - 24f, r.width - 6f, 22f), ammo < 0 ? "∞" : $"x{ammo}", ammoStyle);
            }
        }

        private void EnsureStyles(float scale)
        {
            if (rowStyle != null && Mathf.Approximately(stylesScale, scale)) return;
            stylesScale = scale;
            panelTex = Tex(new Color(0.14f, 0.11f, 0.08f, 0.94f));
            rowTex = Tex(new Color(0.26f, 0.21f, 0.14f, 1f));
            rowSelTex = Tex(new Color(0.52f, 0.38f, 0.14f, 1f));
            rowOffTex = Tex(new Color(0.18f, 0.15f, 0.12f, 1f));
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            rowStyle.normal.textColor = new Color(0.9f, 0.86f, 0.76f);
            rowStyle.hover.textColor = Color.white;
            rowSel = new GUIStyle(rowStyle);
            rowSel.normal.textColor = rowSel.hover.textColor = new Color(1f, 0.92f, 0.5f);
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.95f, 0.78f, 0.4f);
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(0.8f, 0.76f, 0.66f);
            ammoStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            if (Ratms.UI.TrenchGUI.Stencil != null) ammoStyle.font = keyStyle.font = Ratms.UI.TrenchGUI.Stencil;
            if (Ratms.UI.TrenchGUI.Typewriter != null) hintStyle.font = Ratms.UI.TrenchGUI.Typewriter;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
