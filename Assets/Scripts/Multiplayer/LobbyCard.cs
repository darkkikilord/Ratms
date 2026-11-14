using Ratms.UI;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Tarjeta de la SALA DE ESPERA (estilo trinchera), al lado del panel
    ///  del lobby: jugadores y si están listos, botón "¡Listo!" para los
    ///  clientes y las REGLAS de la partida (el host las cambia; el resto
    ///  las ve).
    /// </summary>
    public class LobbyCard : MonoBehaviour
    {
        private MultiplayerMenuController menu;

        public void Init(MultiplayerMenuController m) => menu = m;

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (menu == null || !menu.LobbyVisible || nm == null || !nm.IsListening) return;
            TrenchGUI.Begin();
            bool host = nm.IsServer;
            float w = 640f, h = host ? 810f : 540f;
            var r = new Rect(TrenchGUI.VW - w - 30f, (TrenchGUI.VH - h) * 0.5f, w, h);
            TrenchGUI.Panel(r);
            float x = r.x + 30f, cw = w - 60f, y = r.y + 14f;
            TrenchGUI.Title(new Rect(r.x, y, w, 46f), "SALA DE ESPERA");
            y += 58f;

            // --- Jugadores ---
            var roster = LobbySync.Roster;
            if (roster.Count == 0) { TrenchGUI.Small(new Rect(x, y, cw, 30f), "Conectando…"); y += 34f; }
            int i = 0;
            foreach (var e in roster)
            {
                var c = GUI.color;
                GUI.color = TurnHUD.TeamTextColor(i);
                TrenchGUI.Label(new Rect(x, y, cw - 160f, 34f), $"{i + 1}. {e.name}{(e.id == nm.LocalClientId ? "  " + Loc.T("(tú)") : "")}");
                GUI.color = e.ready || e.id == NetworkManager.ServerClientId ? new Color(0.6f, 1f, 0.55f) : new Color(1f, 0.7f, 0.5f);
                TrenchGUI.Label(new Rect(x + cw - 150f, y, 150f, 34f), e.id == NetworkManager.ServerClientId ? "ANFITRIÓN" : e.ready ? "¡LISTO!" : "esperando…");
                GUI.color = c;
                y += 38f;
                i++;
            }
            y += 10f;

            if (!host)
            {
                bool ready = LobbySync.LocalReady;
                if (TrenchGUI.Button(new Rect(x, y, cw, 60f), ready ? "LISTO  (pulsa para esperar)" : "¡ESTOY LISTO!", selected: ready, big: !ready))
                    LobbySync.LocalReady = !ready;
                y += 76f;
                TrenchGUI.Label(new Rect(x, y, cw, 30f), "Reglas del anfitrión:");
                y += 34f;
                TrenchGUI.Small(new Rect(x, y, cw, 90f), string.IsNullOrEmpty(LobbySync.RulesSummary) ? "…" : Loc.Show(LobbySync.RulesSummary));
            }
            else
            {
                TrenchGUI.Label(new Rect(x, y, cw, 30f), "Reglas de la partida");
                y += 40f;
                y += RulesEditor.Draw(new Rect(x, y, cw, 0f), 40f, 48f);
                TrenchGUI.Small(new Rect(x, y + 4f, cw, 50f), LobbySync.AllReady
                    ? "Todos listos: pulsa EMPEZAR cuando quieras."
                    : "Esperando a que todos marquen ¡Listo!…");
            }
            TrenchGUI.End();
        }
    }
}
