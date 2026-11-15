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

            // --- 0.30: el clima cambia el juego ---
            WeatherFx.Current = Weather.Lluvia;
            bool rainRules = WeatherRules.FireLife < 0.6f && WeatherRules.GasLife < 0.7f;
            WeatherFx.Current = Weather.Nieve;
            bool snowRules = WeatherRules.Slippery && WeatherRules.FallDamage < 1f;
            WeatherFx.Current = Weather.Niebla;
            int fogHidden = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).Count(m => WeatherRules.FogHides(m));
            Check($"Clima en el juego → lluvia: fuego ×{0.5f} y gas ×0.6: {rainRules} · nieve resbala y amortigua: {snowRules} · niebla esconde {fogHidden} rata(s) lejana(s)",
                  rainRules && snowRules, "Clima: las reglas de juego no cambian con el clima");
            WeatherFx.Current = Weather.Despejado;

            // --- 0.30: tamaño del cuerpo (gigante = blanco grande, enana = pequeño) ---
            {
                var any = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None).FirstOrDefault(m => m.IsSpawned && !m.IsTestDummy);
                if (any != null)
                {
                    var box = any.GetComponent<BoxCollider2D>();
                    var hh = any.GetComponent<NetworkHealth>();
                    ulong old = any.LookCode.Value;
                    var giant = RatLook.Unpack(old); giant.body = RatLook.BodyType.Gigante;
                    var dwarf = RatLook.Unpack(old); dwarf.body = RatLook.BodyType.Enana;
                    any.LookCode.Value = giant.Pack();
                    yield return null;
                    Vector2 gs = box.size + Vector2.one * box.edgeRadius * 2f;
                    float gd = hh.BlastDistance(hh.BodyCenter + Vector2.right * 2f);
                    yield return CloseupWithColliders(any, "S01b_gigante", size: 3.2f, overlay: true);
                    any.LookCode.Value = dwarf.Pack();
                    yield return null;
                    Vector2 ds = box.size + Vector2.one * box.edgeRadius * 2f;
                    float dd = hh.BlastDistance(hh.BodyCenter + Vector2.right * 2f);
                    yield return CloseupWithColliders(any, "S01c_enana", size: 3.2f, overlay: true);
                    any.LookCode.Value = old;
                    yield return null;
                    bool sizeOk = gs.x > ds.x * 1.4f && gs.y > ds.y * 1.4f && gd < dd;
                    Check($"Tamaño del cuerpo → gigante {gs.x:0.00}×{gs.y:0.00} (explosión a 2 u cuenta como {gd:0.00}) · enana {ds.x:0.00}×{ds.y:0.00} (cuenta como {dd:0.00})",
                          sizeOk, "Tamaño: la gigante no es un blanco más grande que la enana");
                }
            }

            // --- 0.30: barra rápida de favoritas, arsenal con cursor y ventana de controles ---
            {
                yield return Capture("S01d_barra_rapida");
                var r0 = FindLocalRatOfTurn(tm);
                var menu = r0 != null ? r0.GetComponentInChildren<WeaponsMenuUI>() : null;
                bool keysOk = Ratms.UI.Controls.Key(Ratms.UI.Controls.Act.Fire, 0) == KeyCode.Space
                              && Ratms.UI.Controls.Key(Ratms.UI.Controls.Act.Jump, 0) == KeyCode.UpArrow;
                Check($"Controles → teclas por defecto (ESPACIO dispara, ↑ salta) · ranura 1 = {(WeaponsMenuUI.WeaponId)Ratms.UI.Controls.Fav(0)}",
                      keysOk, "Controles: las teclas por defecto no son las de siempre");
                if (menu != null)
                {
                    menu.SetVisible(true);
                    yield return new WaitForSeconds(0.4f);
                    bool blocked = Ratms.UI.Controls.Blocked;
                    yield return Capture("S01e_arsenal");
                    menu.SetVisible(false);
                    Check($"Arsenal abierto → la rata no se mueve con las flechas: {blocked}", blocked, "Arsenal: no bloquea el movimiento");
                }
                Ratms.UI.ControlsOverlay.Open();
                yield return new WaitForSeconds(0.4f);
                yield return Capture("S01f_controles");
                Ratms.UI.ControlsOverlay.Close();
                yield return new WaitForSeconds(0.2f);
            }

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
            Check($"Rata reventada '{deadName}' → lápida con su nombre: {named}",
                  named, "Lápida: no aparece o no lleva el nombre de la rata");

            // --- 0.30: REPETICIÓN a cámara lenta al acabar el turno ---
            if (SlowMotion.Enabled)
            {
                bool pending = DeathReplay.HasPending;
                tm.EndTurnServerRpc();
                float t0 = Time.unscaledTime;
                while (!DeathReplay.Playing && Time.unscaledTime - t0 < 6f) yield return null;
                bool playing = DeathReplay.Playing;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Capture("S04b_repeticion");
                yield return new WaitForSecondsRealtime(1.8f);
                yield return Capture("S04c_repeticion_lenta");
                Check($"Repetición → grabada: {pending} · se pone al acabar el turno: {playing}", pending && playing, "Repetición: no se reproduce al reventar una rata");
                t0 = Time.unscaledTime;
                while (DeathReplay.Playing && Time.unscaledTime - t0 < 10f) yield return null;
            }
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
