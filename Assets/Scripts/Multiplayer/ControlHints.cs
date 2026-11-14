using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  AYUDAS DE CONTROLES (0.20): una sola línea, pequeña y semitransparente,
    ///  abajo a la izquierda y solo en TU turno. Dice lo justo para lo que
    ///  estás haciendo ahora mismo (sin arma, apuntando, arma de clic, colgado
    ///  de la cuerda, ya has disparado...). Se va atenuando con los turnos que
    ///  llevas jugados y se apaga del todo en Opciones ("Ayudas de controles",
    ///  PlayerPrefs <c>opt_hints</c>). La añade <see cref="CameraFollow"/>.
    /// </summary>
    public class ControlHints : MonoBehaviour
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("opt_hints", 1) == 1;
            set => PlayerPrefs.SetInt("opt_hints", value ? 1 : 0);
        }

        private GUIStyle style;
        private float builtScale = -1f;
        private bool wasMyTurn;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Enabled) return;
            var tm = NetworkTurnManager.Instance;
            if (tm == null || tm.GameEnded.Value || tm.InTransition.Value || Ratms.UI.PauseMenu.IsOpen) return;
            var rat = tm.CurrentRat;
            bool myTurn = rat != null && rat.IsOwner && rat.CanControlNow;

            // Cuántos turnos llevas (para ir atenuando la ayuda poco a poco).
            if (myTurn && !wasMyTurn) PlayerPrefs.SetInt("hint_turns", PlayerPrefs.GetInt("hint_turns", 0) + 1);
            wasMyTurn = myTurn;
            if (!myTurn) return;

            var menu = rat.GetComponentInChildren<WeaponsMenuUI>();
            if (menu != null && menu.IsOpen) return;   // el arsenal ya explica lo suyo

            string text = HintFor(rat, menu);
            if (string.IsNullOrEmpty(text)) return;

            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            Ensure(scale);
            float turns = PlayerPrefs.GetInt("hint_turns", 0);
            float alpha = Mathf.Lerp(0.62f, 0.32f, Mathf.Clamp01(turns / 40f));
            var r = new Rect(18f * scale, Screen.height - 40f * scale, Screen.width * 0.6f, 28f * scale);
            style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = new Color(1f, 0.96f, 0.86f, alpha);
            GUI.Label(r, text, style);
        }

        private static string HintFor(NetworkPlayerMovement rat, WeaponsMenuUI menu)
        {
            string T(string s) => Ratms.UI.Loc.T(s);
            var rope = rat.GetComponent<NetworkNinjaRope>();
            if (rope != null && rope.IsAttached)
                return T("← →: balancearte  ·  W/S: subir y bajar  ·  ESPACIO o ↑: soltarte");
            if (rat.FiredThisTurn)
                return T("Ya has usado tu arma: ¡corre a cubrirte!");

            var w = menu != null ? menu.SelectedWeapon : WeaponsMenuUI.WeaponId.None;
            if (w == WeaponsMenuUI.WeaponId.None)
                return T("TAB: armas  ·  ← →: andar  ·  ↑: saltar (dos veces: voltereta)  ·  clic derecho: mover la cámara");
            if (WeaponsMenuUI.ClickTargeted(w))
                return T("Clic: elegir el sitio  ·  clic derecho y arrastrar: mirar lejos  ·  V: vista general");
            bool charge = w == WeaponsMenuUI.WeaponId.Bazooka || w == WeaponsMenuUI.WeaponId.Grenade || w == WeaponsMenuUI.WeaponId.ClusterBomb;
            bool hold = w == WeaponsMenuUI.WeaponId.Flamethrower;
            string fire = charge ? T("ESPACIO: mantén para cargar, suelta para disparar")
                        : hold ? T("ESPACIO: mantén para echar fuego")
                        : w == WeaponsMenuUI.WeaponId.Dynamite ? T("ESPACIO: soltarla y ¡a correr!")
                        : w == WeaponsMenuUI.WeaponId.Rope ? T("ESPACIO: lanzar la cuerda")
                        : w == WeaponsMenuUI.WeaponId.Bat ? T("ESPACIO: batazo")
                        : T("ESPACIO: disparar");
            string aim = w == WeaponsMenuUI.WeaponId.Dynamite ? "" : T("W/S: apuntar") + "  ·  ";
            return aim + fire + "  ·  " + T("TAB: cambiar de arma");
        }

        private void Ensure(float scale)
        {
            if (style != null && Mathf.Approximately(builtScale, scale)) return;
            builtScale = scale;
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(17 * scale), wordWrap = false, clipping = TextClipping.Overflow };
            if (Ratms.UI.TrenchGUI.Typewriter != null) style.font = Ratms.UI.TrenchGUI.Typewriter;
        }
    }
}
