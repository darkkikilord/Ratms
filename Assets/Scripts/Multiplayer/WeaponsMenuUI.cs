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
            SelectRat = 7
        }

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
        };

        /// <summary>Nombre corto del arma para el HUD.</summary>
        public static string ShortName(WeaponId id) => id switch
        {
            WeaponId.Bazooka  => "Bazooka",
            WeaponId.Grenade  => "Granada",
            WeaponId.Dynamite => "Dinamita",
            WeaponId.Shotgun  => "Escopeta",
            WeaponId.Bat      => "Bate",
            WeaponId.Teleport => "Teletransporte",
            WeaponId.Rope     => "Cuerda ninja",
            WeaponId.SelectRat => "Cambiar rata",
            _                 => "Ninguna",
        };

        /// <summary>Arma actualmente seleccionada (leído por NetworkBazooka y demás).</summary>
        public WeaponId SelectedWeapon => (WeaponId)SelectedIndexNet.Value;
        public bool IsBazookaEquipped => SelectedWeapon == WeaponId.Bazooka;
        public bool IsVisible => visible;

        /// <summary>Abre/cierra el arsenal (prueba automática).</summary>
        public void SetVisible(bool v) => visible = v;

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

            if (Input.GetKeyDown(toggleKey))
                visible = !visible;

            // Atajos numéricos rápidos (0 = desenfundar, 1 = bazooka, etc.)
            foreach (var w in weapons)
            {
                if (Input.GetKeyDown(w.shortcut) && w.implemented && HasAmmo(w.id))
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

        // ==============================================================
        //  ARSENAL (panel de armas) — estilo caja de munición de trinchera
        // ==============================================================
        private GUIStyle rowStyle, rowSel, titleStyle, hintStyle, ammoStyle;
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
            _ => null,
        };

        private void OnGUI()
        {
            if (!visible || !IsMyActiveRat()) return;

            float scale = Mathf.Max(0.7f, Screen.height / 1080f);
            EnsureStyles(scale);
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float vh = Screen.height / scale;

            const float W = 360f, rowH = 40f, pad = 12f;
            float H = 52f + weapons.Length * (rowH + 4f) + 34f;
            float x = panelOffset.x, y = vh - H - panelOffset.y;
            GUI.DrawTexture(new Rect(x, y, W, H), panelTex);
            GUI.Label(new Rect(x, y + 8f, W, 34f), "ARSENAL", titleStyle);

            float ry = y + 50f;
            foreach (var w in weapons)
            {
                int ammo = w.id == WeaponId.None ? -1 : AmmoLeft(w.id);
                bool empty = ammo == 0;
                bool sel = w.id == SelectedWeapon;
                var r = new Rect(x + pad, ry, W - pad * 2, rowH);
                GUI.DrawTexture(r, empty ? rowOffTex : sel ? rowSelTex : rowTex);

                // Icono del arma (su dibujo pixel-art)
                var icon = IconOf(w.id);
                if (icon != null)
                {
                    var tr = icon.textureRect;
                    var tex = icon.texture;
                    float iw = 52f, ih = Mathf.Min(rowH - 8f, iw * tr.height / tr.width);
                    float iwFit = ih * tr.width / tr.height;
                    var pc = GUI.color;
                    if (empty) GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + 6f + (iw - iwFit) * 0.5f, r.y + (rowH - ih) * 0.5f, iwFit, ih), tex,
                        new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
                    GUI.color = pc;
                }

                string number = w.id == WeaponId.None ? "0" : $"{(int)w.id + 1}";
                string name = w.id == WeaponId.None ? "Guardar arma" : ShortName(w.id);
                if (GUI.Button(new Rect(r.x + 64f, r.y, r.width - 64f - 60f, rowH), $"{number}  {name}", sel ? rowSel : rowStyle) && !empty)
                    SelectedIndexNet.Value = (int)w.id;

                ammoStyle.normal.textColor = empty ? new Color(0.9f, 0.35f, 0.3f) : new Color(1f, 0.88f, 0.55f);
                GUI.Label(new Rect(r.xMax - 60f, r.y, 54f, rowH), w.id == WeaponId.None ? "" : ammo < 0 ? "∞" : $"x{ammo}", ammoStyle);
                ry += rowH + 4f;
            }
            GUI.Label(new Rect(x, y + H - 30f, W, 24f), "Apuntar W/S  ·  Usar ESPACIO  ·  TAB cierra", hintStyle);
            GUI.matrix = prevMatrix;
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
            ammoStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
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
