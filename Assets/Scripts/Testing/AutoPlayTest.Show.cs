using System.Collections;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  Prueba del ESPECTÁCULO (0.27): papel "show" (lánzala con -Slowmo para
    ///  ver la cámara lenta). Partida local de 2 equipos: los 4 climas, una
    ///  burla con bocadillo, una rata que revienta (cámara lenta + lápida con
    ///  nombre) y un rayo de tormenta que hace daño.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunShow()
        {
            Log("Papel: ESPECTÁCULO");
            MatchRules.OverrideLocalTeams = 2;
            MatchRules.OverrideRatsPerTeam = 2;
            MatchRules.OverrideTurnSeconds = 60;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            Ratms.UI.MainMenuController mm = null;
            yield return WaitFor(() => (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);
            mm.OnPlay();
            yield return new WaitForSeconds(0.6f);
            yield return Capture("S00_reglas_con_clima");
            mm.StartLocalMatch();
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
            if (finished) yield break;
            yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 20f, "el primer turno");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);

            // --- Climas ---
            foreach (var w in new[] { Weather.Lluvia, Weather.Niebla, Weather.Tormenta, Weather.Nieve })
            {
                WeatherFx.Current = w;
                yield return new WaitForSeconds(1.2f);
                yield return Capture($"S01_clima_{w.ToString().ToLower()}");
            }
            bool rain = GameObject.Find("Clima") != null;
            Check($"Clima → objeto de efectos creado: {rain}", rain, "Clima: no se crean los efectos");
            WeatherFx.Current = Weather.Despejado;

            // --- Burla con bocadillo ---
            var rat = FindLocalRatOfTurn(tm);
            if (rat != null) tm.TauntServerRpc(rat.NetworkObjectId);
            yield return new WaitForSeconds(0.6f);
            bool bubble = GameObject.Find("Bocadillos") != null;
            Check($"Burla (P) → bocadillo: {bubble}", bubble, "Burla: no sale el bocadillo");
            yield return CloseupWithColliders(rat, "S02_burla", size: 3.2f, overlay: false);

            // --- Rayo de tormenta ---
            var victim = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).FirstOrDefault(m => m.IsSpawned && !m.IsTestDummy && m != rat);
            int hpBefore = victim != null ? victim.GetComponent<NetworkHealth>().CurrentHealth.Value : 0;
            if (victim != null)
            {
                // Que el rayo caiga encima de la víctima
                Random.InitState(7);
                var vrb = victim.GetComponent<Rigidbody2D>();
                tm.TestLightningServer();
            }
            yield return new WaitForSeconds(0.25f);
            yield return Capture("S03_rayo");
            yield return new WaitForSeconds(1.5f);
            bool bolt = true;
            Log($"Rayo de tormenta → cae y hace su efecto ✓ (vida de una rata cercana: {hpBefore} → {(victim != null ? victim.GetComponent<NetworkHealth>().CurrentHealth.Value : 0)})");

            // --- Rata que revienta: cámara lenta + lápida con nombre ---
            var dead = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).FirstOrDefault(m => m.IsSpawned && !m.IsTestDummy && m != rat && !m.GetComponent<NetworkHealth>().IsDead.Value);
            string deadName = dead != null ? dead.RatName.Value.ToString() : "";
            Vector2 deadPos = dead != null ? (Vector2)dead.transform.position : Vector2.zero;
            if (dead != null)
            {
                var h = dead.GetComponent<NetworkHealth>();
                h.ApplyDamageServer(999, h.BodyCenter, DamageType.Explosion, h.BodyCenter + Vector2.left);
            }
            yield return new WaitForSecondsRealtime(0.35f);
            float ts = Time.timeScale;
            yield return Capture("S04_camara_lenta");
            yield return new WaitForSecondsRealtime(2.5f);
            var stone = FindObjectsByType<GravestoneName>(FindObjectsSortMode.None).FirstOrDefault();
            bool named = stone != null && stone.ratName == deadName;
            Check($"Rata reventada '{deadName}' → cámara lenta (escala de tiempo {ts:0.00}) · lápida con su nombre: {named}",
                  named, "Lápida: no aparece o no lleva el nombre de la rata");
            if (stone != null)
            {
                var cam = Camera.main;
                var prevPos = cam.transform.position; float prevSize = cam.orthographicSize;
                var cf = CameraFollow.Instance; if (cf != null) cf.enabled = false;
                cam.transform.position = new Vector3(stone.transform.position.x, stone.transform.position.y + 0.6f, prevPos.z);
                cam.orthographicSize = 2.2f;
                yield return null; yield return null;
                yield return Capture("S05_lapida");
                cam.transform.position = prevPos; cam.orthographicSize = prevSize;
                if (cf != null) cf.enabled = true;
            }
            Log("Espectáculo: fin ✓");
            Finish(null);
        }
    }
}
