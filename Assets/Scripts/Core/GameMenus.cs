using System;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  Ventana de OPCIONES (menú principal y pausa): volúmenes, sangre,
    ///  risas, temblor de cámara y pantalla completa. Estilo <see cref="TrenchGUI"/>.
    /// </summary>
    public class OptionsOverlay : MonoBehaviour
    {
        public Action OnClose;

        private void OnGUI()
        {
            GUI.depth = -10;
            TrenchGUI.Begin();
            var r = new Rect((TrenchGUI.VW - 760f) * 0.5f, (TrenchGUI.VH - 640f) * 0.5f, 760f, 640f);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 18f, r.width, 50f), "OPCIONES");
            TrenchGUI.OptionsBlock(new Rect(r.x + 40f, r.y + 90f, r.width - 80f, 420f));
            if (TrenchGUI.Button(new Rect(r.x + r.width * 0.3f, r.yMax - 76f, r.width * 0.4f, 52f), "Volver", big: true) ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                PlayerPrefs.Save();
                OnClose?.Invoke();
            }
            TrenchGUI.End();
        }
    }

    /// <summary>
    ///  MENÚ DE PAUSA de la partida (ESC): seguir, opciones, cómo se juega y
    ///  salir al menú (con confirmación). La partida NO se para (es online y
    ///  por turnos); solo tapa la pantalla.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        public Action OnExitToMenu;
        private enum Page { Main, Options, Help, ConfirmExit }
        private Page page;

        public void Open() { IsOpen = true; page = Page.Main; enabled = true; }
        public void Close() { IsOpen = false; enabled = false; }

        private void OnDisable() => IsOpen = false;

        private void OnGUI()
        {
            GUI.depth = -10;
            TrenchGUI.Begin();
            TrenchGUI.Dim(0.5f);
            bool esc = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
            if (esc) Event.current.Use();

            switch (page)
            {
                case Page.Main:
                {
                    var r = Centered(560f, 440f);
                    TrenchGUI.Panel(r);
                    TrenchGUI.Title(new Rect(r.x, r.y + 18f, r.width, 50f), "PAUSA");
                    float y = r.y + 100f, x = r.x + 70f, w = r.width - 140f;
                    if (TrenchGUI.Button(new Rect(x, y, w, 64f), "Seguir luchando", big: true) || esc) Close(); y += 84f;
                    if (TrenchGUI.Button(new Rect(x, y, w, 56f), "Opciones")) page = Page.Options; y += 72f;
                    if (TrenchGUI.Button(new Rect(x, y, w, 56f), "Cómo se juega")) page = Page.Help; y += 72f;
                    if (TrenchGUI.Button(new Rect(x, y, w, 56f), "Salir al menú")) page = Page.ConfirmExit;
                    break;
                }
                case Page.Options:
                {
                    var r = Centered(760f, 640f);
                    TrenchGUI.Panel(r);
                    TrenchGUI.Title(new Rect(r.x, r.y + 18f, r.width, 50f), "OPCIONES");
                    TrenchGUI.OptionsBlock(new Rect(r.x + 40f, r.y + 90f, r.width - 80f, 420f));
                    if (TrenchGUI.Button(new Rect(r.x + r.width * 0.3f, r.yMax - 76f, r.width * 0.4f, 52f), "Volver", big: true) || esc)
                    { PlayerPrefs.Save(); page = Page.Main; }
                    break;
                }
                case Page.Help:
                {
                    var r = Centered(860f, 780f);
                    TrenchGUI.Panel(r);
                    TrenchGUI.Title(new Rect(r.x, r.y + 18f, r.width, 50f), "CÓMO SE JUEGA");
                    HelpText(new Rect(r.x + 50f, r.y + 86f, r.width - 100f, 600f));
                    if (TrenchGUI.Button(new Rect(r.x + r.width * 0.3f, r.yMax - 76f, r.width * 0.4f, 52f), "Volver", big: true) || esc)
                        page = Page.Main;
                    break;
                }
                case Page.ConfirmExit:
                {
                    var r = Centered(620f, 300f);
                    TrenchGUI.Panel(r);
                    TrenchGUI.Title(new Rect(r.x, r.y + 20f, r.width, 50f), "¿DESERTAR?");
                    TrenchGUI.Small(new Rect(r.x + 50f, r.y + 84f, r.width - 100f, 60f),
                                    "Si eres el anfitrión, la partida se acaba para todos.");
                    if (TrenchGUI.Button(new Rect(r.x + 50f, r.yMax - 90f, 240f, 56f), "Sí, me largo")) { Close(); OnExitToMenu?.Invoke(); }
                    if (TrenchGUI.Button(new Rect(r.xMax - 290f, r.yMax - 90f, 240f, 56f), "No, a luchar") || esc) page = Page.Main;
                    break;
                }
            }
            TrenchGUI.End();
        }

        private static Rect Centered(float w, float h) => new Rect((TrenchGUI.VW - w) * 0.5f, (TrenchGUI.VH - h) * 0.5f, w, h);

        /// <summary>Controles y reglas básicas.</summary>
        public static void HelpText(Rect r)
        {
            string[] lines =
            {
                "Flechas IZQ / DER   andar          Flecha ARRIBA   saltar",
                "W / S   apuntar          ESPACIO   disparar (mantén para más fuerza)",
                "1-8 o TAB   elegir arma        ESC   pausa",
                "",
                "• Cada turno usas UN arma; después aún puedes moverte para huir.",
                "• Las ratas de tu equipo juegan por turnos, una cada vez.",
                "• Munición limitada: granadas, dinamita, teletransporte… Las CAJAS",
                "  que caen en paracaídas dan más (o explotan: ¡trampa!). El BOTIQUÍN cura.",
                "• Los BARRILES revientan en cadena si les alcanza una explosión.",
                "• CAMBIAR RATA: haz clic en otra rata tuya; jugará tu próximo turno.",
                "• MUTILACIONES: los golpes fuertes arrancan patas (por debajo), brazos",
                "  (de lado) u ojos (por arriba). Coja = lenta; SIN PATAS = a rastras y sin",
                "  saltar; SIN BRAZOS = solo MORDISCO; tuerta = ves menos y apuntas peor.",
                "  Las cajas pueden traer PATA DE PALO o GARFIO.",
                "• El agua mata. En MUERTE SÚBITA sube cada turno.",
                "• Gana el último equipo con alguna rata viva.",
            };
            float y = r.y;
            foreach (var l in lines)
            {
                TrenchGUI.Small(new Rect(r.x, y, r.width, 34f), l);
                y += 36f;
            }
        }
    }
}
