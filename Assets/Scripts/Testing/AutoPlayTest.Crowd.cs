using System.Collections;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.31 — papel "crowd": partida local con 10 EQUIPOS × 4 ratas (el máximo).
    ///  Comprueba que el mapa crece (40 ratas no caben en el normal), que hay
    ///  10 equipos con colores distintos, que las ratas no aparecen pegadas y
    ///  que el HUD (barras en dos columnas) y la vista general se ven bien.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunCrowd()
        {
            Log("Papel: MULTITUD (10 equipos × 4 ratas)");
            MatchRules.OverrideLocalTeams = MatchRules.MaxTeams;
            MatchRules.OverrideRatsPerTeam = 4;
            MatchRules.OverrideTurnSeconds = 60;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            Ratms.UI.MainMenuController mm = null;
            yield return WaitFor(() => (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);
            mm.OnPlay();
            yield return new WaitForSeconds(0.6f);
            yield return Capture("C00_jugar_equipos");
            mm.StartLocalMatch();
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
            if (finished) yield break;
            yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 25f, "el primer turno");
            if (finished) yield break;
            yield return new WaitForSeconds(2.5f);

            // --- Mapa más grande ---
            var gen = FindFirstObjectByType<Ratms.World.TerrainGenerator>();
            int width = gen != null ? gen.TextureWidth : 0;
            int expected = MatchRules.MapWidthFor(MatchRules.MaxTeams, 4);
            Check($"Mapa → {width} px de ancho (esperado {expected})", width == expected, $"El mapa no creció: {width} px en vez de {expected}");

            // --- 10 equipos, 40 ratas, colores distintos ---
            var rats = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None)
                .Where(m => m.IsSpawned && !m.IsTestDummy).ToList();
            int teams = rats.Select(m => m.TeamIndex.Value).Distinct().Count();
            int colors = Enumerable.Range(0, MatchRules.MaxTeams).Select(i => RatAppearance.ColorForTeam(i)).Distinct().Count();
            Check($"Equipos → {teams} equipos, {rats.Count} ratas, {colors} colores distintos",
                  teams == MatchRules.MaxTeams && rats.Count == MatchRules.MaxTeams * 4 && colors == MatchRules.MaxTeams,
                  $"Multitud: {teams} equipos / {rats.Count} ratas / {colors} colores");

            // --- Separación al aparecer ---
            float minD = float.MaxValue;
            int close = 0;
            for (int i = 0; i < rats.Count; i++)
                for (int j = i + 1; j < rats.Count; j++)
                {
                    float d = Vector2.Distance(rats[i].transform.position, rats[j].transform.position);
                    minD = Mathf.Min(minD, d);
                    if (d < 1.5f) close++;
                }
            Check($"Separación → la pareja más cercana a {minD:F1} u; {close} pareja(s) a menos de 1,5 u", close <= 2,
                  $"Multitud: {close} parejas de ratas casi encima");

            yield return Capture("C01_partida_10_equipos");
            var cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null)
            {
                cam.ToggleOverview();
                yield return new WaitForSeconds(1.8f);
                yield return Capture("C02_vista_general");
                cam.ToggleOverview();
            }

            // --- Pasan unos turnos con tantos equipos ---
            int startTeam = tm.CurrentTeam.Value;
            for (int k = 0; k < 3; k++)
            {
                ulong cur = tm.CurrentRatId.Value;
                tm.EndTurnServerRpc();
                yield return WaitFor(() => tm.CurrentRatId.Value != cur && !tm.InTransition.Value, 12f, "el siguiente turno");
                if (finished) yield break;
            }
            Check($"Turnos → del equipo {startTeam} al {tm.CurrentTeam.Value} sin atascos", tm.CurrentTeam.Value != startTeam, "Multitud: los turnos no avanzan");
            yield return new WaitForSeconds(1f);
            yield return Capture("C03_turno_4");
            Finish(null);
        }
    }
}
