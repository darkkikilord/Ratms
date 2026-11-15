using System.Collections;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  Prueba de los MODOS DE JUEGO (0.25): papel "modes". Arranca una
    ///  partida local de 2 equipos en cada modo y comprueba lo suyo:
    ///  Fuertes (cada equipo en su mitad), Colina (punto al acabar el turno
    ///  en la bandera), Duelo (1 rata y una sola arma con munición), Minas
    ///  (minas en el mapa) y Barra libre (munición infinita).
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunModes()
        {
            Log("Papel: MODOS DE JUEGO");
            MatchRules.OverrideLocalTeams = 2;
            MatchRules.OverrideRatsPerTeam = 2;
            MatchRules.OverrideTurnSeconds = 30;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            var modes = new[] { GameMode.Fuertes, GameMode.Colina, GameMode.Duelo, GameMode.Minas, GameMode.BarraLibre };
            foreach (var mode in modes)
            {
                if (finished) yield break;
                MatchRules.Game = mode;
                Log($"---- Modo {mode} ----");
                Ratms.UI.MainMenuController mm = null;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
                if (finished) yield break;
                yield return new WaitForSeconds(1.5f);
                mm.OnPlay();
                yield return new WaitForSeconds(0.4f);
                mm.StartLocalMatch();
                NetworkTurnManager tm = null;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
                if (finished) yield break;
                yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 20f, "el primer turno");
                if (finished) yield break;
                yield return new WaitForSeconds(1.5f);
                bool modeOk = tm.CurrentGame == mode;
                var rats = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Where(m => !m.IsTestDummy && m.IsSpawned).ToList();
                string tag = mode.ToString().ToLower();

                switch (mode)
                {
                    case GameMode.Fuertes:
                    {
                        var terrain = FindFirstObjectByType<Ratms.World.DestructibleTerrain>();
                        float mid = terrain.transform.position.x + terrain.WorldSize.x * 0.5f;
                        var t0 = rats.Where(r => r.TeamIndex.Value == 0).Select(r => r.transform.position.x).ToList();
                        var t1 = rats.Where(r => r.TeamIndex.Value == 1).Select(r => r.transform.position.x).ToList();
                        bool sides = t0.Count > 0 && t1.Count > 0 && t0.All(x => x < mid) && t1.All(x => x > mid);
                        Check($"Fuertes → modo {modeOk} · equipo 0 a la izquierda y 1 a la derecha: {sides}", modeOk && sides, "Fuertes: los equipos no aparecen cada uno en su mitad");
                        CameraFollow.Instance?.ToggleOverview();
                        yield return new WaitForSeconds(1.5f);
                        yield return Capture("Z1_fuertes_vista");
                        CameraFollow.Instance?.ToggleOverview();
                        break;
                    }
                    case GameMode.Colina:
                    {
                        bool flag = tm.HillPos.Value != Vector2.zero;
                        var rat = FindLocalRatOfTurn(tm);
                        int team = tm.CurrentTeam.Value;
                        if (rat != null)
                        {
                            var rb = rat.GetComponent<Rigidbody2D>();
                            rb.position = tm.HillPos.Value + Vector2.up * 0.6f;
                            rb.linearVelocity = Vector2.zero;
                            rat.TestResetFallTracking();
                        }
                        yield return new WaitForSeconds(1.2f);
                        yield return Capture("Z2_colina_bandera");
                        tm.TurnTimeRemaining.Value = 0.05f;   // se acaba el turno
                        yield return WaitFor(() => tm.InTransition.Value || tm.GameEnded.Value, 5f, "fin del turno", fatal: false);
                        yield return new WaitForSeconds(0.5f);
                        int pts = team >= 0 && team < tm.HillScore.Count ? tm.HillScore[team] : -1;
                        Check($"Colina → bandera {flag} en {tm.HillPos.Value} · puntos del equipo {team} tras acabar en ella: {pts}", modeOk && flag && pts == 1, "Colina: no suma punto al acabar el turno en la bandera");
                        break;
                    }
                    case GameMode.Duelo:
                    {
                        int perTeam = rats.Count(r => r.TeamIndex.Value == 0);
                        int team = tm.CurrentTeam.Value;
                        var withAmmo = Enumerable.Range(0, WeaponsMenuUI.Count).Select(w => (WeaponsMenuUI.WeaponId)w)
                            .Where(w => w != WeaponsMenuUI.WeaponId.Rope && w != WeaponsMenuUI.WeaponId.Shovel && tm.AmmoOf(team, w) != 0).ToList();
                        var rat = FindLocalRatOfTurn(tm);
                        var sel = rat != null ? rat.GetComponentInChildren<WeaponsMenuUI>().SelectedWeapon : WeaponsMenuUI.WeaponId.None;
                        bool ok = perTeam == 1 && withAmmo.Count == 1 && sel == withAmmo[0];
                        Check($"Duelo → ratas por equipo {perTeam} · armas con munición: {string.Join(",", withAmmo)} · en la mano: {sel}", modeOk && ok, "Duelo: no hay una sola rata o una sola arma (en la mano)");
                        yield return Capture("Z3_duelo");
                        break;
                    }
                    case GameMode.Minas:
                    {
                        int mines = FindObjectsByType<NetworkGadget>(FindObjectsSortMode.None).Count(g => g.Type == NetworkGadget.Kind.Mine);
                        Check($"Minas → minas en el mapa: {mines}", modeOk && mines >= 6, "Minas: no se sembraron minas");
                        CameraFollow.Instance?.ToggleOverview();
                        yield return new WaitForSeconds(1.5f);
                        yield return Capture("Z4_minas_vista");
                        CameraFollow.Instance?.ToggleOverview();
                        break;
                    }
                    case GameMode.BarraLibre:
                    {
                        bool inf = tm.AmmoOf(0, WeaponsMenuUI.WeaponId.Grenade) < 0 && tm.AmmoOf(1, WeaponsMenuUI.WeaponId.HolyCheese) < 0;
                        Check($"Barra libre → granadas y bomba bíblica infinitas: {inf}", modeOk && inf, "Barra libre: la munición no es infinita");
                        break;
                    }
                }
                Ratms.Networking.TurnHUD.ExitToMenu();
                yield return new WaitForSeconds(2f);
            }
            MatchRules.Game = GameMode.Normal;
            Log("Modos de juego: fin ✓");
            Finish(null);
        }

        private static NetworkPlayerMovement FindLocalRatOfTurn(NetworkTurnManager tm)
        {
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                if (m.IsSpawned && m.NetworkObjectId == tm.CurrentRatId.Value) return m;
            return null;
        }
    }
}
