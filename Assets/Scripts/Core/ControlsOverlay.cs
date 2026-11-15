using System;
using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  0.30 — OPCIONES → CONTROLES Y ACCESIBILIDAD.
    ///
    ///  · Cambiar las teclas de cada acción (dos por acción): clic en la
    ///    casilla y pulsa la tecla nueva (ESC cancela, RETROCESO la borra).
    ///    Si esa tecla ya la usaba otra acción, se le quita a la otra.
    ///  · Las 10 armas favoritas (ranuras 1-0) con ◀ ▶.
    ///  · Barra rápida de favoritas, colores para daltónicos y tamaño de los
    ///    textos.
    ///
    ///  Se abre encima de cualquier ventana de opciones (menú principal o
    ///  pausa) y se come los clics y teclas mientras está abierta.
    /// </summary>
    public class ControlsOverlay : MonoBehaviour
    {
        public static bool IsOpen => instance != null && instance.enabled;
        private static ControlsOverlay instance;

        private Controls.Act? waitAct;
        private int waitSlot;
        private string notice;
        private float noticeUntil;
        private Vector2 scroll;
        private static KeyCode[] allKeys;
        private int cancelFrame = -1;

        public static void Open()
        {
            if (instance == null)
            {
                instance = new GameObject("ControlesYAccesibilidad").AddComponent<ControlsOverlay>();
                DontDestroyOnLoad(instance.gameObject);
            }
            instance.enabled = true;
            instance.waitAct = null;
        }

        public static void Close()
        {
            if (instance != null) instance.enabled = false;
            PlayerPrefs.Save();
        }

        /// <summary>
        ///  Captura de tecla en Update (más fiable que los eventos de IMGUI
        ///  para MAYÚS, CTRL y los botones laterales del ratón).
        /// </summary>
        private void Update()
        {
            Controls.BlockGameplayThisFrame();   // que al probar teclas no se mueva la rata
            if (waitAct == null) return;
            if (allKeys == null)
            {
                var list = new System.Collections.Generic.List<KeyCode>();
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                    if (k != KeyCode.None && k < KeyCode.JoystickButton0 && k != KeyCode.Mouse0 && k != KeyCode.Mouse1) list.Add(k);
                allKeys = list.ToArray();
            }
            foreach (var k in allKeys)
            {
                if (!Input.GetKeyDown(k)) continue;
                var a = waitAct.Value;
                waitAct = null;
                if (k == KeyCode.Escape) { cancelFrame = Time.frameCount; return; }   // cancelar (sin cerrar la ventana)
                if (k == KeyCode.Backspace || k == KeyCode.Delete) { Controls.SetKey(a, waitSlot, KeyCode.None); return; }
                var stolen = Controls.SetKey(a, waitSlot, k);
                if (stolen.HasValue)
                {
                    notice = Loc.F("{0} ya no tiene esa tecla", Loc.T(Controls.ActName(stolen.Value)));
                    noticeUntil = Time.unscaledTime + 3f;
                }
                Sfx.Play(Sfx.Id.Whack, 0.25f, 0.1f);
                return;
            }
        }

        private void OnGUI()
        {
            GUI.depth = -30;   // por encima de Opciones y de la pausa
            TrenchGUI.Begin();
            TrenchGUI.Dim(0.6f);
            var e = Event.current;
            bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;

            float W = Mathf.Min(1180f, TrenchGUI.VW - 20f), H = Mathf.Min(900f, TrenchGUI.VH - 20f);
            var r = new Rect((TrenchGUI.VW - W) * 0.5f, (TrenchGUI.VH - H) * 0.5f, W, H);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 14f, r.width, 50f), "CONTROLES Y ACCESIBILIDAD");

            float colW = (W - 120f) * 0.5f;
            float lx = r.x + 40f, rx = lx + colW + 40f, top = r.y + 76f;

            // ---------------- Teclas ----------------
            float y = top;
            const float rowH = 32f, keyW = 120f;
            TrenchGUI.Small(new Rect(lx + colW - keyW * 2f - 8f, y, keyW, 24f), "Tecla");
            TrenchGUI.Small(new Rect(lx + colW - keyW, y, keyW, 24f), "Otra tecla");
            y += 22f;
            foreach (var (title, acts) in Controls.Sections)
            {
                var c0 = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f);
                TrenchGUI.Small(new Rect(lx, y, colW, 26f), title);
                GUI.color = c0;
                y += 26f;
                foreach (var a in acts)
                {
                    TrenchGUI.Small(new Rect(lx + 12f, y + 2f, colW - keyW * 2f - 20f, 28f), Controls.ActName(a));
                    for (int s = 0; s < 2; s++)
                    {
                        var kr = new Rect(lx + colW - keyW * (2 - s) - (s == 0 ? 8f : 0f), y, keyW - 6f, rowH - 4f);
                        bool waiting = waitAct == a && waitSlot == s;
                        string label = waiting ? "..." : Controls.KeyName(Controls.Key(a, s));
                        if (KeyButton(kr, label, waiting)) { waitAct = a; waitSlot = s; }
                    }
                    y += rowH;
                }
                y += 4f;
            }
            if (waitAct != null)
                TrenchGUI.Small(new Rect(lx, y + 4f, colW, 28f), "Pulsa la tecla nueva (ESC cancela, RETROCESO la quita)");
            else if (Time.unscaledTime < noticeUntil && notice != null)
            {
                var c0 = GUI.color; GUI.color = new Color(1f, 0.7f, 0.4f);
                TrenchGUI.Small(new Rect(lx, y + 4f, colW, 28f), notice);
                GUI.color = c0;
            }

            // ---------------- Favoritas ----------------
            y = top;
            {
                var c0 = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f);
                TrenchGUI.Small(new Rect(rx, y, colW, 26f), "Armas favoritas (teclas 1-0)");
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                TrenchGUI.Small(new Rect(rx, y + 24f, colW, 26f), "También en el arsenal: ratón encima + número");
                GUI.color = c0;
            }
            y += 54f;
            for (int i = 0; i < Controls.Slots; i++)
            {
                int fav = Controls.Fav(i);
                string name = fav < -1 ? "—" : fav == -1 ? Loc.T("Guardar arma") : Ratms.Networking.WeaponsMenuUI.ShortName((Ratms.Networking.WeaponsMenuUI.WeaponId)fav);
                int step = TrenchGUI.Stepper(new Rect(rx, y, colW, 34f), Controls.KeyName(Controls.Key(Controls.Act.Slot1 + i, 0)), name, 110f);
                if (step != 0) Controls.SetFav(i, NextWeapon(fav, step));
                y += 36f;
            }
            y += 6f;

            // ---------------- Accesibilidad ----------------
            {
                var c0 = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f);
                TrenchGUI.Small(new Rect(rx, y, colW, 26f), "Accesibilidad");
                GUI.color = c0;
            }
            y += 30f;
            const float tw = 330f;
            Ratms.Networking.WeaponsMenuUI.QuickBarEnabled = TrenchGUI.Toggle(new Rect(rx, y, colW, 38f), "Barra de armas favoritas", Ratms.Networking.WeaponsMenuUI.QuickBarEnabled, tw); y += 44f;
            bool cb = TrenchGUI.Toggle(new Rect(rx, y, colW, 38f), "Colores para daltónicos", Accessibility.ColorBlind, tw);
            if (cb != Accessibility.ColorBlind) Accessibility.ColorBlind = cb;
            y += 44f;
            int st = TrenchGUI.Stepper(new Rect(rx, y, colW, 38f), "Tamaño de los textos", Mathf.RoundToInt(Accessibility.UiScale * 100f) + " %", tw);
            if (st != 0) Accessibility.UiScale = Mathf.Clamp(Accessibility.UiScale + st * 0.1f, 0.8f, 1.3f);
            y += 44f;
            bool hold = TrenchGUI.Toggle(new Rect(rx, y, colW, 38f), "Cargar sin mantener", Accessibility.TapToCharge, tw);
            if (hold != Accessibility.TapToCharge) Accessibility.TapToCharge = hold;

            // ---------------- Botones ----------------
            if (TrenchGUI.Button(new Rect(r.x + 40f, r.yMax - 70f, 300f, 52f), "Restablecer todo"))
            {
                Controls.ResetAll();
                notice = Loc.T("Teclas y favoritas como al principio");
                noticeUntil = Time.unscaledTime + 3f;
            }
            if (TrenchGUI.Button(new Rect(r.xMax - 340f, r.yMax - 70f, 300f, 52f), "Volver", big: true) || (esc && waitAct == null && Time.frameCount != cancelFrame))
                Close();

            // Que la ventana de debajo no reciba nada mientras esta está abierta
            if (e.type == EventType.KeyDown || e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel)
                e.Use();
            TrenchGUI.End();
        }

        private static bool KeyButton(Rect r, string label, bool waiting)
        {
            if (Event.current.type == EventType.Repaint)
            {
                var c = GUI.color;
                bool hover = r.Contains(Event.current.mousePosition);
                GUI.color = waiting ? new Color(0.9f, 0.7f, 0.25f) : hover ? new Color(0.45f, 0.38f, 0.25f) : new Color(0.2f, 0.17f, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 3f, r.width, 3f), Texture2D.whiteTexture);
                GUI.color = c;
            }
            var st = TrenchGUI.SmallStyle;
            var al = st.alignment;
            st.alignment = TextAnchor.MiddleCenter;
            GUI.Label(r, label, st);
            st.alignment = al;
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Arma siguiente/anterior para una ranura (vacía → guardar arma → 0..48 → vacía).</summary>
        private static int NextWeapon(int cur, int step)
        {
            int n = Ratms.Networking.WeaponsMenuUI.Count;
            int i = cur + 2;                    // -2 → 0, -1 → 1, 0 → 2 ...
            i = ((i + step) % (n + 2) + n + 2) % (n + 2);
            return i - 2;
        }
    }

    /// <summary>
    ///  0.30 — opciones de ACCESIBILIDAD (PlayerPrefs), leídas por el resto del juego.
    /// </summary>
    public static class Accessibility
    {
        private static int cb = -1;
        /// <summary>Paleta de equipos que se distingue con daltonismo (naranja, azul, amarillo, morado).</summary>
        public static bool ColorBlind
        {
            get { if (cb < 0) cb = PlayerPrefs.GetInt("opt_colorblind", 0); return cb == 1; }
            set { cb = value ? 1 : 0; PlayerPrefs.SetInt("opt_colorblind", cb); }
        }

        private static float ui = -1f;
        /// <summary>Multiplica el tamaño de todos los menús y paneles (TrenchGUI).</summary>
        public static float UiScale
        {
            get { if (ui < 0f) ui = PlayerPrefs.GetFloat("opt_uiscale", 1f); return ui; }
            set { ui = value; PlayerPrefs.SetFloat("opt_uiscale", value); }
        }

        private static int tap = -1;
        /// <summary>Armas de carga: pulsar una vez empieza a cargar y otra vez dispara (sin mantener).</summary>
        public static bool TapToCharge
        {
            get { if (tap < 0) tap = PlayerPrefs.GetInt("opt_tapcharge", 0); return tap == 1; }
            set { tap = value ? 1 : 0; PlayerPrefs.SetInt("opt_tapcharge", tap); }
        }
    }
}
