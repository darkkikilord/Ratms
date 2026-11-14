using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  PARTIDA LOCAL en el mismo PC (varios equipos pasándose el teclado).
    ///
    ///  Arranca un host en 127.0.0.1 (sin internet ni Relay) y carga el
    ///  escenario. GameSceneSpawnManager ve <see cref="MatchMode.Local"/> y
    ///  crea <see cref="MatchRules.LocalTeams"/> equipos, todos del host; el
    ///  NetworkTurnManager va pasando el turno de equipo en equipo.
    /// </summary>
    public static class LocalMatchSession
    {
        public static bool Begin(string gameScene)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[Partida local] No hay NetworkManager en el menú."); return false; }
            if (nm.IsListening) { Debug.LogWarning("[Partida local] Ya hay una sesión de red en marcha."); return false; }

            MatchRules.Mode = MatchMode.Local;
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");
            NetcodeHygiene.BeforeSessionStart();
            if (!nm.StartHost())
            {
                Debug.LogError("[Partida local] No se pudo arrancar el host local.");
                MatchRules.Mode = MatchMode.Online;
                return false;
            }
            nm.SceneManager.LoadScene(gameScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
            Debug.Log($"[Partida local] {MatchRules.EffectiveLocalTeams} equipos × {MatchRules.EffectiveRatsPerTeam} ratas · " +
                      $"muerte súbita a los {MatchRules.EffectiveSuddenDeathSeconds:0} s (0 = nunca)");
            return true;
        }
    }
}
