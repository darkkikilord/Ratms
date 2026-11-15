using System.Collections;
using Ratms.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.36 — papel "quick": PARTIDA RÁPIDA de verdad (Relay + Lobby de Unity,
    ///  necesita internet). Lánzalo en DOS copias con perfiles distintos
    ///  (<c>-ratmsProfile a</c> / <c>-ratmsProfile b</c>) y unos segundos de
    ///  diferencia: la primera no encuentra sala, crea una y la anuncia; la
    ///  segunda la encuentra y entra sin código.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunQuick()
        {
            Log("Papel: PARTIDA RÁPIDA (Lobby)");
            MultiplayerMenuController menu = null;
            yield return WaitFor(() => (menu = MultiplayerMenuController.Instance) != null, 20f, "el menú multijugador");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);
            menu.ShowMultiplayer();
            yield return new WaitForSeconds(0.8f);
            yield return Capture("Q00_panel");
            menu.OnQuickMatchClicked();
            var nm = NetworkManager.Singleton;
            yield return WaitFor(() => (nm.IsListening && (nm.IsServer ? QuickMatch.Hosting : nm.IsConnectedClient)) || QuickMatch.LastError != null, 40f, "entrar o crear sala");
            if (finished) yield break;
            Check($"Partida rápida → {(QuickMatch.LastError ?? "sin errores")} · soy {(nm.IsServer ? "ANFITRIÓN (sala anunciada)" : "CLIENTE (entré sin código)")}",
                  QuickMatch.LastError == null, "Partida rápida: " + QuickMatch.LastError);
            yield return new WaitForSeconds(1.5f);
            yield return Capture(nm.IsServer ? "Q01_sala_anfitrion" : "Q01_sala_cliente");
            if (nm.IsServer)
            {
                float t0 = Time.time;
                yield return WaitFor(() => nm.ConnectedClientsIds.Count >= 2, 45f, "que entre alguien por partida rápida", fatal: false);
                Check($"Partida rápida → jugadores en la sala: {nm.ConnectedClientsIds.Count} (esperó {Time.time - t0:F0} s)",
                      nm.ConnectedClientsIds.Count >= 2, "Partida rápida: nadie encontró la sala");
                yield return new WaitForSeconds(2f);
                yield return Capture("Q02_sala_con_dos");
                yield return new WaitForSeconds(6f);   // que el cliente acabe antes
            }
            else yield return new WaitForSeconds(3f);
            Finish(null);
        }
    }
}
