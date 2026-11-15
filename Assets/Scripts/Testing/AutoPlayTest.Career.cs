using System.Collections;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.35 — papel "career": HOJA DE SERVICIOS y LOGROS (en memoria: no toca
    ///  la hoja del jugador). Partida local de 2 equipos × 2 ratas; en el turno
    ///  del equipo 0 (el "tuyo") revientan las dos ratas enemigas. Deben salir
    ///  las bajas, la masacre parcial (2 en un turno), la victoria sin rasguños,
    ///  los carteles de logro y la pantalla de Logros.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunCareer()
        {
            Log("Papel: LOGROS Y ESTADÍSTICAS");
            Career.ResetForTest();
            MatchRules.OverrideLocalTeams = 2;
            MatchRules.OverrideRatsPerTeam = 2;
            MatchRules.OverrideTurnSeconds = 60;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            bool hatLockedBefore = Career.IsHatLocked(RatLook.HatType.Corona);

            Ratms.UI.MainMenuController mm = null;
            yield return WaitFor(() => (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
            if (finished) yield break;
            yield return new WaitForSeconds(1f);
            mm.OnPlay();
            yield return new WaitForSeconds(0.3f);
            mm.StartLocalMatch();
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
            if (finished) yield break;
            yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 20f, "el primer turno");
            if (finished) yield break;
            // Que juegue el equipo 0
            for (int k = 0; k < 4 && tm.CurrentTeam.Value != 0; k++)
            {
                ulong cur = tm.CurrentRatId.Value;
                tm.EndTurnServerRpc();
                yield return WaitFor(() => tm.CurrentRatId.Value != cur && !tm.InTransition.Value, 12f, "el siguiente turno");
            }
            yield return new WaitForSeconds(1f);

            var enemies = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None)
                .Where(m => m.IsSpawned && !m.IsTestDummy && m.TeamIndex.Value != 0).ToList();
            foreach (var e in enemies)
            {
                var h = e.GetComponent<NetworkHealth>();
                h.ApplyDamageServer(999, h.BodyCenter, DamageType.Explosion);
                yield return new WaitForSeconds(0.4f);
            }
            yield return new WaitForSeconds(0.6f);
            yield return Capture("L01_cartel_logro");
            yield return WaitFor(() => tm.GameEnded.Value, 10f, "el final de la partida");
            yield return new WaitForSeconds(2.5f);
            yield return Capture("L02_victoria");

            var d = Career.Stats;
            Check($"Hoja de servicios → partidas {d.matches} · victorias {d.wins} · bajas {d.kills} · mejor turno {d.bestTurnKills} · sin rasguño {d.flawless} · daño {d.damage}",
                  d.matches == 1 && d.wins == 1 && d.kills == 2 && d.bestTurnKills == 2,
                  "Logros: la hoja de servicios no apunta bien la partida");
            bool achievements = Career.IsUnlocked("first_blood") && Career.IsUnlocked("winner");
            Check($"Logros → primera sangre: {Career.IsUnlocked("first_blood")} · primera victoria: {Career.IsUnlocked("winner")} · sin un rasguño: {Career.IsUnlocked("flawless")} · corona bloqueada: {hatLockedBefore}",
                  achievements && hatLockedBefore, "Logros: no se desbloquean");

            // Pantalla de logros desde el menú
            Ratms.Networking.TurnHUD.ExitToMenu();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && FindFirstObjectByType<Ratms.UI.MainMenuController>() != null, 20f, "volver al menú");
            yield return new WaitForSeconds(1f);
            Ratms.UI.CareerOverlay.Open();
            yield return new WaitForSeconds(0.5f);
            yield return Capture("L03_hoja_de_servicios");
            Ratms.UI.CareerOverlay.Close();
            Finish(null);
        }
    }
}
