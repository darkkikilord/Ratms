using System.Collections.Generic;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  0.30 — CONTROLES REASIGNABLES (accesibilidad).
    ///
    ///  Todas las teclas del juego pasan por aquí en vez de leer
    ///  <c>Input.GetKey(KeyCode.W)</c> a pelo. Cada acción tiene DOS teclas
    ///  (principal y alternativa) que el jugador cambia en Opciones →
    ///  Controles; se guardan en PlayerPrefs (<c>key_Accion_0/1</c>).
    ///
    ///  Además están las 10 RANURAS DE ARMAS FAVORITAS (teclas 1-9 y 0 por
    ///  defecto): cada ranura guarda qué arma saca (<c>fav_N</c>). Se cambian
    ///  en el arsenal (pasa el ratón por un arma y pulsa el número) o en
    ///  Opciones → Controles.
    ///
    ///  Mientras un menú de juego está abierto (el arsenal), <see cref="Blocked"/>
    ///  apaga las acciones de mover/disparar para que las flechas muevan el
    ///  cursor del menú y no la rata.
    /// </summary>
    public static class Controls
    {
        public enum Act
        {
            Left, Right, Jump, AimUp, AimDown, Fire,
            Arsenal, NextWeapon, PrevWeapon, Taunt, Overview, CameraBack,
            MenuUp, MenuDown, MenuLeft, MenuRight, MenuOk,
            Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, Slot9, Slot10,
            // 0.37 (al final para no mover las teclas guardadas de las demás)
            Chat, Emotes,
        }

        public const int Slots = 10;

        /// <summary>Nombre en español de cada acción (se traduce al pintarlo).</summary>
        public static string ActName(Act a) => a switch
        {
            Act.Left => "Andar a la izquierda",
            Act.Right => "Andar a la derecha",
            Act.Jump => "Saltar (dos veces: voltereta)",
            Act.AimUp => "Apuntar arriba",
            Act.AimDown => "Apuntar abajo",
            Act.Fire => "Disparar / usar",
            Act.Arsenal => "Abrir el arsenal",
            Act.NextWeapon => "Arma siguiente",
            Act.PrevWeapon => "Arma anterior",
            Act.Taunt => "Burla",
            Act.Overview => "Vista general",
            Act.CameraBack => "Cámara a tu rata",
            Act.MenuUp => "Menú: arriba",
            Act.MenuDown => "Menú: abajo",
            Act.MenuLeft => "Menú: izquierda",
            Act.MenuRight => "Menú: derecha",
            Act.MenuOk => "Menú: elegir",
            Act.Chat => "Chat (escribir)",
            Act.Emotes => "Emotes",
            _ => Loc.F("Arma favorita {0}", (int)a - (int)Act.Slot1 + 1),
        };

        /// <summary>Grupos para la pantalla de Opciones → Controles.</summary>
        public static readonly (string title, Act[] acts)[] Sections =
        {
            ("Movimiento", new[] { Act.Left, Act.Right, Act.Jump }),
            ("Armas", new[] { Act.AimUp, Act.AimDown, Act.Fire, Act.Arsenal, Act.NextWeapon, Act.PrevWeapon }),
            ("Gestos y cámara", new[] { Act.Taunt, Act.Chat, Act.Emotes, Act.Overview, Act.CameraBack }),
            ("Menús", new[] { Act.MenuUp, Act.MenuDown, Act.MenuLeft, Act.MenuRight, Act.MenuOk }),
        };

        private static (KeyCode a, KeyCode b) Default(Act a) => a switch
        {
            Act.Left => (KeyCode.LeftArrow, KeyCode.A),
            Act.Right => (KeyCode.RightArrow, KeyCode.D),
            Act.Jump => (KeyCode.UpArrow, KeyCode.None),
            Act.AimUp => (KeyCode.W, KeyCode.None),
            Act.AimDown => (KeyCode.S, KeyCode.None),
            Act.Fire => (KeyCode.Space, KeyCode.None),
            Act.Arsenal => (KeyCode.Tab, KeyCode.Mouse2),   // clic de la rueda también
            Act.NextWeapon => (KeyCode.None, KeyCode.None),
            Act.PrevWeapon => (KeyCode.None, KeyCode.None),
            Act.Taunt => (KeyCode.P, KeyCode.None),
            Act.Overview => (KeyCode.V, KeyCode.None),
            Act.CameraBack => (KeyCode.C, KeyCode.None),
            Act.Chat => (KeyCode.T, KeyCode.None),
            Act.Emotes => (KeyCode.E, KeyCode.None),
            Act.MenuUp => (KeyCode.UpArrow, KeyCode.W),
            Act.MenuDown => (KeyCode.DownArrow, KeyCode.S),
            Act.MenuLeft => (KeyCode.LeftArrow, KeyCode.A),
            Act.MenuRight => (KeyCode.RightArrow, KeyCode.D),
            Act.MenuOk => (KeyCode.Return, KeyCode.Space),
            Act.Slot10 => (KeyCode.Alpha0, KeyCode.Keypad0),
            _ when a >= Act.Slot1 && a <= Act.Slot9 => (KeyCode.Alpha1 + (a - Act.Slot1), KeyCode.Keypad1 + (a - Act.Slot1)),
            _ => (KeyCode.None, KeyCode.None),
        };

        /// <summary>Las del menú y las ranuras pueden repetir tecla con las de juego (no se usan a la vez).</summary>
        private static bool IsMenu(Act a) => a >= Act.MenuUp && a <= Act.MenuOk;

        private static readonly Dictionary<(Act, int), KeyCode> cache = new();

        public static KeyCode Key(Act a, int slot)
        {
            if (cache.TryGetValue((a, slot), out var k)) return k;
            var def = Default(a);
            KeyCode d = slot == 0 ? def.a : def.b;
            k = (KeyCode)PlayerPrefs.GetInt($"key_{a}_{slot}", (int)d);
            cache[(a, slot)] = k;
            return k;
        }

        /// <summary>
        ///  Cambia una tecla. Si otra acción del MISMO tipo (juego o menú) ya la
        ///  usaba, se la quita (para que una tecla no haga dos cosas a la vez).
        ///  Devuelve la acción a la que se le quitó, o null.
        /// </summary>
        public static Act? SetKey(Act a, int slot, KeyCode k)
        {
            Act? stolen = null;
            if (k != KeyCode.None)
                foreach (Act o in System.Enum.GetValues(typeof(Act)))
                    for (int s = 0; s < 2; s++)
                    {
                        if (o == a && s == slot) continue;
                        if (IsMenu(o) != IsMenu(a)) continue;
                        if (Key(o, s) != k) continue;
                        Store(o, s, KeyCode.None);
                        stolen = o;
                    }
            Store(a, slot, k);
            return stolen;
        }

        private static void Store(Act a, int slot, KeyCode k)
        {
            cache[(a, slot)] = k;
            PlayerPrefs.SetInt($"key_{a}_{slot}", (int)k);
        }

        public static void ResetAll()
        {
            foreach (Act a in System.Enum.GetValues(typeof(Act)))
                for (int s = 0; s < 2; s++) PlayerPrefs.DeleteKey($"key_{a}_{s}");
            for (int i = 0; i < Slots; i++) PlayerPrefs.DeleteKey($"fav_{i}");
            cache.Clear();
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------
        //  Lectura
        // ------------------------------------------------------------------
        /// <summary>
        ///  true = hay un menú del juego abierto (arsenal...): las acciones de
        ///  mover, apuntar y disparar no responden. Lo pone cada menú en su Update.
        /// </summary>
        public static bool Blocked => Time.frameCount <= blockedUntilFrame;
        private static int blockedUntilFrame = -1;
        /// <summary>Llamar cada frame mientras el menú esté abierto.</summary>
        public static void BlockGameplayThisFrame() => blockedUntilFrame = Time.frameCount + 1;

        private static bool Gated(Act a) => (Blocked && !IsMenu(a) && a != Act.Arsenal && !(a >= Act.Slot1 && a <= Act.Slot10))
                                            || (AllBlocked && !IsMenu(a));

        /// <summary>
        ///  0.37: true mientras se escribe en el chat o está abierto el menú de
        ///  emotes: NINGUNA tecla de juego responde (ni el arsenal ni las armas
        ///  favoritas 1-0, que con <see cref="Blocked"/> sí funcionan).
        /// </summary>
        public static bool AllBlocked => Time.frameCount <= allBlockedUntilFrame;
        private static int allBlockedUntilFrame = -1;
        public static void BlockAllThisFrame() { allBlockedUntilFrame = Time.frameCount + 1; blockedUntilFrame = Time.frameCount + 1; }

        public static bool Held(Act a)
        {
            if (Gated(a)) return false;
            var k0 = Key(a, 0); var k1 = Key(a, 1);
            return (k0 != KeyCode.None && Input.GetKey(k0)) || (k1 != KeyCode.None && Input.GetKey(k1));
        }

        public static bool Down(Act a)
        {
            if (Gated(a)) return false;
            var k0 = Key(a, 0); var k1 = Key(a, 1);
            return (k0 != KeyCode.None && Input.GetKeyDown(k0)) || (k1 != KeyCode.None && Input.GetKeyDown(k1));
        }

        public static bool Up(Act a)
        {
            var k0 = Key(a, 0); var k1 = Key(a, 1);
            return (k0 != KeyCode.None && Input.GetKeyUp(k0)) || (k1 != KeyCode.None && Input.GetKeyUp(k1));
        }

        /// <summary>-1, 0 o 1 (como Input.GetAxisRaw("Horizontal")).</summary>
        public static float Horizontal => (Held(Act.Right) ? 1f : 0f) - (Held(Act.Left) ? 1f : 0f);
        /// <summary>Apuntar: +1 arriba, -1 abajo.</summary>
        public static float Aim => (Held(Act.AimUp) ? 1f : 0f) - (Held(Act.AimDown) ? 1f : 0f);

        // ------------------------------------------------------------------
        //  Armas favoritas (ranuras 1-0)
        // ------------------------------------------------------------------
        /// <summary>Armas de las ranuras por defecto (WeaponId en número; -2 = vacía).</summary>
        private static readonly int[] DefaultFavs = { 0, 1, 3, 2, 4, 6, 5, 10, 11, -1 };
        // 1 bazuca · 2 granada · 3 escopeta · 4 dinamita · 5 bate · 6 cuerda · 7 teletransporte
        // 8 bomba de racimo · 9 ataque aéreo · 0 guardar el arma

        public static int Fav(int slot) => PlayerPrefs.GetInt($"fav_{slot}", slot < DefaultFavs.Length ? DefaultFavs[slot] : -2);
        public static void SetFav(int slot, int weapon)
        {
            // La misma arma no puede estar en dos ranuras: la vieja se vacía.
            for (int i = 0; i < Slots; i++) if (i != slot && Fav(i) == weapon) PlayerPrefs.SetInt($"fav_{i}", -2);
            PlayerPrefs.SetInt($"fav_{slot}", weapon);
            PlayerPrefs.Save();
        }
        /// <summary>Ranura de un arma (o -1 si no está en ninguna).</summary>
        public static int SlotOf(int weapon)
        {
            for (int i = 0; i < Slots; i++) if (Fav(i) == weapon) return i;
            return -1;
        }
        /// <summary>¿Se ha pulsado la tecla de alguna ranura este frame? (-1 = no)</summary>
        public static int SlotPressed()
        {
            for (int i = 0; i < Slots; i++) if (Down(Act.Slot1 + i)) return i;
            return -1;
        }

        // ------------------------------------------------------------------
        //  Nombres de las teclas
        // ------------------------------------------------------------------
        public static string KeyName(KeyCode k) => k switch
        {
            KeyCode.None => "—",
            KeyCode.Space => Loc.T("ESPACIO"),
            KeyCode.Return => "ENTER",
            KeyCode.KeypadEnter => "ENTER",
            KeyCode.Tab => "TAB",
            KeyCode.Escape => "ESC",
            KeyCode.Backspace => "⌫",
            KeyCode.LeftArrow => "←",
            KeyCode.RightArrow => "→",
            KeyCode.UpArrow => "↑",
            KeyCode.DownArrow => "↓",
            KeyCode.LeftShift => "SHIFT",
            KeyCode.RightShift => "SHIFT D",
            KeyCode.LeftControl => "CTRL",
            KeyCode.RightControl => "CTRL D",
            KeyCode.LeftAlt => "ALT",
            KeyCode.RightAlt => "ALT GR",
            KeyCode.Mouse2 => Loc.T("RUEDA"),
            KeyCode.Mouse3 => Loc.T("RATÓN 4"),
            KeyCode.Mouse4 => Loc.T("RATÓN 5"),
            >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
            >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => "NUM " + (int)(k - KeyCode.Keypad0),
            _ => k.ToString().ToUpperInvariant(),
        };

        /// <summary>Nombre corto de la tecla principal (o la alternativa si no hay principal).</summary>
        public static string Short(Act a)
        {
            var k = Key(a, 0);
            if (k == KeyCode.None) k = Key(a, 1);
            return KeyName(k);
        }

        /// <summary>
        ///  Cambia en un texto de ayuda las teclas de siempre ("W/S", "ESPACIO",
        ///  "TAB", "← →", "↑") por las que el jugador tiene puestas. Solo toca
        ///  lo que ha cambiado, así los textos por defecto quedan igual.
        /// </summary>
        public static string Explain(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string aim = Short(Act.AimUp) + "/" + Short(Act.AimDown);
            if (aim != "W/S") s = s.Replace("W/S", aim);
            string fire = Short(Act.Fire);
            if (Key(Act.Fire, 0) != KeyCode.Space) { s = s.Replace("ESPACIO", fire).Replace("SPACE", fire); }
            if (Key(Act.Arsenal, 0) != KeyCode.Tab) s = s.Replace("TAB", Short(Act.Arsenal));
            string lr = Short(Act.Left) + " " + Short(Act.Right);
            if (lr != "← →") s = s.Replace("← →", lr);
            if (Key(Act.Jump, 0) != KeyCode.UpArrow) s = s.Replace("↑", Short(Act.Jump));
            return s;
        }
    }
}
