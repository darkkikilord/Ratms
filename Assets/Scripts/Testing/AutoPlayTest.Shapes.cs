using System.Collections;
using System.Linq;
using Ratms.Networking;
using Ratms.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  0.32 — papel "shapes": una partida local por cada FORMA de mapa (Isla,
    ///  Archipiélago, Montaña, Cañón, Cavernas, Cielo). Comprueba que salen
    ///  todas las ratas, que ninguna empieza dentro de la tierra ni en el agua,
    ///  y saca la vista general de cada mapa. En la primera partida prueba
    ///  además el GUARDIÁN DEL TERRENO: mete un objeto con física dentro de la
    ///  tierra (como la dinamita del bug) y mira que lo saque en vez de dejarlo
    ///  caer a través del mapa.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunShapes()
        {
            Log("Papel: FORMAS DE MAPA");
            MatchRules.OverrideLocalTeams = 3;
            MatchRules.OverrideRatsPerTeam = 3;
            MatchRules.OverrideTurnSeconds = 60;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            for (int i = 0; i < WormsTerrainShape.ShapeCount; i++)
            {
                if (finished) yield break;
                var shape = (MapShape)i;
                MatchRules.OverrideMapShape = shape;
                Log($"---- Mapa {shape} ----");
                Ratms.UI.MainMenuController mm = null;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
                if (finished) yield break;
                yield return new WaitForSeconds(1.2f);
                if (i == 0)
                {
                    mm.OnPlay();
                    yield return new WaitForSeconds(0.5f);
                    yield return Capture("F00_reglas_mapa");
                }
                mm.StartLocalMatch();
                NetworkTurnManager tm = null;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
                if (finished) yield break;
                yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 25f, "el primer turno");
                if (finished) yield break;
                yield return new WaitForSeconds(2f);

                bool shapeOk = TerrainGenerator.MatchShape == shape;
                var rats = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Where(m => m.IsSpawned && !m.IsTestDummy).ToList();
                var water = FindFirstObjectByType<WaterDeathZone>();
                int buried = rats.Count(r => DestructibleTerrain.IsSolidAtWorld(r.GetComponent<NetworkHealth>().BodyCenter));
                int dead = rats.Count(r => r.GetComponent<NetworkHealth>().IsDead.Value);
                Check($"Mapa {shape} → forma aplicada: {shapeOk} · {rats.Count} ratas · enterradas {buried} · muertas al empezar {dead}",
                      shapeOk && rats.Count == 9 && buried == 0 && dead == 0,
                      $"Mapa {shape}: forma {shapeOk}, {rats.Count} ratas, {buried} enterradas, {dead} muertas");

                CameraFollow.Instance?.ToggleOverview();
                yield return new WaitForSeconds(1.6f);
                yield return Capture($"F{i + 1}_{shape.ToString().ToLower()}");
                CameraFollow.Instance?.ToggleOverview();

                if (i == 0) yield return TestTerrainGuard(rats);

                // Volver al menú para la siguiente forma.
                TurnHUD.ExitToMenu();
                yield return new WaitForSeconds(1f);
            }
            MatchRules.OverrideMapShape = null;
            Finish(null);
        }

        /// <summary>
        ///  Mete una "dinamita" de prueba (cuerpo dinámico con colisión continua)
        ///  medio metro DENTRO de la tierra bajo una rata y otra dentro y cayendo
        ///  rápido. Sin guardián las dos atravesaban el mapa.
        /// </summary>
        private IEnumerator TestTerrainGuard(System.Collections.Generic.List<NetworkPlayerMovement> rats)
        {
            var rat = rats.FirstOrDefault();
            if (rat == null) yield break;
            // Un poco al lado de la rata (si no, la rata la empuja otra vez hacia abajo).
            Vector2 feet = rat.GetComponent<NetworkHealth>().BodyCenter + new Vector2(1.6f, 0.8f);
            Vector2 inside = feet;
            for (int k = 0; k < 60 && !DestructibleTerrain.IsSolidAtWorld(inside); k++) inside += Vector2.down * 0.1f;
            inside += Vector2.down * 0.5f;
            if (!DestructibleTerrain.IsSolidAtWorld(inside)) { Log("Guardián: no encontré tierra bajo la rata (salto la prueba)"); yield break; }

            GameObject Make(string n, Vector2 at, Vector2 vel)
            {
                var go = new GameObject(n);
                go.transform.position = at;
                var rb = go.AddComponent<Rigidbody2D>();
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.linearVelocity = vel;
                go.AddComponent<CircleCollider2D>().radius = 0.12f;
                TerrainGuard.Attach(go);
                return go;
            }
            int before = TerrainGuard.Rescues;
            var a = Make("PruebaDinamitaEnterrada", inside, Vector2.zero);
            var b = Make("PruebaDinamitaRapida", inside + Vector2.down * 0.3f, Vector2.down * 6f);
            yield return new WaitForSeconds(1.5f);
            // "Atravesar el mapa" = acabar en el agua del fondo. Rodar cuesta abajo es normal.
            var water = FindFirstObjectByType<WaterDeathZone>();
            float waterY = (water != null ? water.SurfaceY : -999f) + 0.3f;
            bool aOk = a.transform.position.y > waterY && !DestructibleTerrain.IsSolidAtWorld(a.GetComponent<Collider2D>().bounds.center);
            bool bOk = b.transform.position.y > waterY && !DestructibleTerrain.IsSolidAtWorld(b.GetComponent<Collider2D>().bounds.center);
            Check($"Guardián del terreno → enterrada sale: {aOk} · rápida no atraviesa: {bOk} · rescates {TerrainGuard.Rescues - before}",
                  aOk && bOk, "Guardián: un objeto metido en la tierra sigue atravesando el mapa");
            Object.Destroy(a);
            Object.Destroy(b);
        }
    }
}
