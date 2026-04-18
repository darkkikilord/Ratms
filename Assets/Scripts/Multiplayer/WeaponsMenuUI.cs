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
            Shotgun   = 3
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
        [SerializeField] private Vector2 panelSize = new Vector2(280f, 280f);
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
            new WeaponEntry { id = WeaponId.Grenade,  displayName = "Granada (próximamente)",     implemented = false, shortcut = KeyCode.Alpha2 },
            new WeaponEntry { id = WeaponId.Dynamite, displayName = "Dinamita (próximamente)",    implemented = false, shortcut = KeyCode.Alpha3 },
            new WeaponEntry { id = WeaponId.Shotgun,  displayName = "Escopeta (próximamente)",    implemented = false, shortcut = KeyCode.Alpha4 },
        };

        /// <summary>Arma actualmente seleccionada (leído por NetworkBazooka y demás).</summary>
        public WeaponId SelectedWeapon => (WeaponId)SelectedIndexNet.Value;
        public bool IsBazookaEquipped => SelectedWeapon == WeaponId.Bazooka;
        public bool IsVisible => visible;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // Empezamos con el arma por defecto (None, por defecto → sin arma)
                SelectedIndexNet.Value = (int)defaultWeapon;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (Input.GetKeyDown(toggleKey))
                visible = !visible;

            // Atajos numéricos rápidos (0 = desenfundar, 1 = bazooka, etc.)
            foreach (var w in weapons)
            {
                if (Input.GetKeyDown(w.shortcut) && w.implemented)
                    SelectedIndexNet.Value = (int)w.id;
            }
        }

        private void OnGUI()
        {
            if (!IsOwner || !visible) return;

            float x = panelOffset.x;
            float y = Screen.height - panelSize.y - panelOffset.y;

            GUI.Box(new Rect(x, y, panelSize.x, panelSize.y), "Armas  [TAB para cerrar]");

            float buttonY = y + 30f;
            float buttonH = 32f;
            float pad = 10f;

            for (int i = 0; i < weapons.Length; i++)
            {
                var w = weapons[i];
                GUI.enabled = w.implemented;

                bool isSelected = (w.id == SelectedWeapon);
                string prefix = isSelected ? "▶ " : "   ";
                string number = (w.id == WeaponId.None) ? "0" : $"{(int)w.id + 1}";
                string label = $"{prefix}{number}. {w.displayName}";

                if (GUI.Button(new Rect(x + pad, buttonY, panelSize.x - pad * 2, buttonH), label))
                {
                    if (w.implemented)
                        SelectedIndexNet.Value = (int)w.id;
                }

                buttonY += buttonH + 4f;
            }

            GUI.enabled = true;

            GUI.Label(new Rect(x + pad, y + panelSize.y - 26f, panelSize.x - pad * 2, 20f),
                "Apuntar ↑↓  ·  Cargar/disparar ESPACIO");
        }
    }
}
