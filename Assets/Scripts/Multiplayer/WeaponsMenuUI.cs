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
        }

        /// <summary>Número de armas (tamaño del bloque de munición por equipo).</summary>
        public const int Count = 13;

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
        };

        /// <summary>Tecla que se pinta en el arsenal.</summary>
        private static string KeyLabel(WeaponEntry w) => w.shortcut switch
        {
            KeyCode.Q => "Q", KeyCode.E => "E", KeyCode.R => "R", KeyCode.F => "F",
            _ => w.id == WeaponId.None ? "0" : $"{(int)w.id + 1}",
        };

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

            if (Input.GetKeyDown(toggleKey))
                visible = !visible;

            // Atajos numéricos rápidos (0 = desenfundar, 1 = bazooka, etc.)
            foreach (var w in weapons)
            {
                if (Input.GetKeyDown(w.shortcut) && w.implemented && Usable(w.id))
                    SelectedIndexNet.Value = (int)w.id;
            }
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
                                                      && id != WeaponId.AirStrike && id != WeaponId.Napalm;   // el avión se pide por radio

        /// <summary>¿Se apunta con el RATÓN (clic en el mapa) en vez de con W/S?</summary>
        public static bool ClickTargeted(WeaponId id) => id == WeaponId.Teleport || id == WeaponId.SelectRat
                                                         || id == WeaponId.AirStrike || id == WeaponId.Napalm;

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
            _ => null,
        };

        // ---------- 0.20: cuadrícula por grupos + ficha de "cómo se usa" ----------
        // Orden y grupos del panel (como la caja de armas de Worms).
        private static readonly (string group, WeaponId[] ids)[] Groups =
        {
            ("Explosivos", new[] { WeaponId.Bazooka, WeaponId.Grenade, WeaponId.ClusterBomb, WeaponId.Dynamite }),
            ("Armas de fuego", new[] { WeaponId.Shotgun, WeaponId.MachineGun, WeaponId.Flamethrower, WeaponId.Bat }),
            ("Movimiento y trucos", new[] { WeaponId.Rope, WeaponId.Teleport, WeaponId.SelectRat, WeaponId.None }),
            ("Apoyo aéreo", new[] { WeaponId.AirStrike, WeaponId.Napalm }),
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
            _ => "Guarda el arma para andar y saltar con las manos libres.",
        };

        private WeaponEntry EntryOf(WeaponId id)
        {
            foreach (var w in weapons) if (w.id == id) return w;
            return weapons[0];
        }

        private void OnGUI()
        {
            if (!visible || !IsMyActiveRat()) return;

            Ratms.UI.TrenchGUI.Begin();
            EnsureStyles(1f);
            float vh = Ratms.UI.TrenchGUI.VH;

            const float tile = 78f, gap = 8f, pad = 18f, cols = 4f, groupH = 24f, infoH = 104f;
            float W = pad * 2f + cols * tile + (cols - 1f) * gap;
            float H = 54f;
            foreach (var g in Groups) H += groupH + tile + gap;
            H += infoH + 34f;
            float x = panelOffset.x, y = Mathf.Max(10f, vh - H - panelOffset.y);
            var panel = new Rect(x, y, W, H);
            Ratms.UI.TrenchGUI.Panel(panel);
            Ratms.UI.TrenchGUI.Title(new Rect(x, y + 6f, W, 40f), "ARSENAL");

            WeaponId shown = SelectedWeapon;   // la ficha: la que tenga el ratón encima, o la elegida
            float gy = y + 54f;
            var e = Event.current;
            foreach (var (group, ids) in Groups)
            {
                Ratms.UI.TrenchGUI.Small(new Rect(x + pad, gy, W - pad * 2f, groupH), group);
                gy += groupH;
                for (int i = 0; i < ids.Length; i++)
                {
                    var id = ids[i];
                    var r = new Rect(x + pad + i * (tile + gap), gy, tile, tile);
                    bool hover = r.Contains(e.mousePosition);
                    if (hover) shown = id;
                    DrawTile(r, id, hover);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && Usable(id))
                    {
                        SelectedIndexNet.Value = (int)id;
                        Sfx.Play(Sfx.Id.Whack, 0.25f, 0.15f);
                    }
                }
                gy += tile + gap;
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
            string how = Ratms.UI.Loc.T(HowTo(shown));
            if (!HandsOk && NeedsHands(shown)) how = Ratms.UI.Loc.T("Sin brazos no puedes usarla: con ESPACIO muerdes.");
            else if (ammo == 0) how = Ratms.UI.Loc.T("Sin munición. Las cajas que caen del cielo traen más.");
            GUI.Label(new Rect(info.x + 10f, info.y + 36f, info.width - 20f, info.height - 40f), how, Ratms.UI.TrenchGUI.SmallStyle);

            GUI.Label(new Rect(x, y + H - 32f, W, 24f), Ratms.UI.Loc.T("Clic o tecla para elegir  ·  TAB cierra"), hintStyle);
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
