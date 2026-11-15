using System.Collections;
using System.IO;
using System.Linq;
using Ratms.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  FOTOS PARA LA PÁGINA (itch.io): papel "promo" (lánzalo con -Slowmo y
    ///  1920×1080). Partida local de 3 equipos: menú, partida, arsenal, grupo
    ///  de ratas para la PORTADA (solo la cámara, sin interfaz), explosión,
    ///  nieve, tormenta con rayo y repetición. Nada de líneas de colisiones.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunPromo()
        {
            Log("Papel: PROMO (fotos para itch.io)");
            MatchRules.OverrideLocalTeams = 3;
            MatchRules.OverrideRatsPerTeam = 3;
            MatchRules.OverrideTurnSeconds = 90;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.Game = GameMode.Normal;
            Ratms.UI.MainMenuController mm = null;
            yield return WaitFor(() => (mm = FindFirstObjectByType<Ratms.UI.MainMenuController>()) != null, 20f, "el menú principal");
            if (finished) yield break;
            yield return new WaitForSeconds(4f);   // que desfilen ratas por el menú
            yield return Capture("P00_menu");
            mm.OnPlay();
            yield return new WaitForSeconds(0.5f);
            var pm = FindFirstObjectByType<Ratms.UI.PlayModeMenu>();
            pm?.OnTeam?.Invoke();
            yield return new WaitForSeconds(0.6f);
            yield return Capture("P01_mi_equipo");
            FindFirstObjectByType<Ratms.UI.TeamEditor>()?.OnClose?.Invoke();
            yield return new WaitForSeconds(0.3f);
            mm.StartLocalMatch();
            NetworkTurnManager tm = null;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == gameScene && (tm = NetworkTurnManager.Instance) != null, 30f, "entrar en la partida");
            if (finished) yield break;
            yield return WaitFor(() => tm.CurrentRatId.Value != ulong.MaxValue, 20f, "el primer turno");
            if (finished) yield break;
            yield return new WaitForSeconds(2.5f);
            yield return Capture("P02_partida");

            var rat = FindLocalRatOfTurn(tm);
            var menu = rat != null ? rat.GetComponentInChildren<WeaponsMenuUI>() : null;
            if (menu != null)
            {
                menu.SetVisible(true);
                yield return new WaitForSeconds(0.5f);
                yield return Capture("P03_arsenal");
                menu.SetVisible(false);
            }

            // --- Grupo de ratas de varios equipos para la portada ---
            var others = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None)
                .Where(m => m.IsSpawned && !m.IsTestDummy && m != rat).OrderBy(m => m.TeamIndex.Value).ToList();
            if (rat != null)
            {
                var gen = FindFirstObjectByType<Ratms.World.TerrainGenerator>();
                Vector2 basePos = rat.transform.position;
                // El lado con el suelo más llano (que no se caigan por un barranco)
                float Rough(float s)
                {
                    float worst = 0f;
                    for (int i = 0; i < 4; i++)
                    {
                        float? y = gen != null ? CurrentSurfaceY(gen, basePos.x + s * (1.3f + i * 1.25f)) : null;
                        worst = Mathf.Max(worst, y.HasValue ? Mathf.Abs(y.Value - basePos.y) : 99f);
                    }
                    return worst;
                }
                float side = Rough(1f) <= Rough(-1f) ? 1f : -1f;
                float feetOff = rat.transform.position.y - rat.GetComponent<Collider2D>().bounds.min.y;
                int n = 0;
                foreach (var t in new[] { 1, 2, 0, 1 })
                {
                    var o = others.FirstOrDefault(m => m.TeamIndex.Value == t);
                    if (o == null) continue;
                    others.Remove(o);
                    float px = basePos.x + side * (1.3f + n * 1.25f);
                    float? sy = gen != null ? CurrentSurfaceY(gen, px) : null;
                    var p = new Vector2(px, (sy ?? basePos.y) + feetOff + 0.15f);
                    var orb = o.GetComponent<Rigidbody2D>();
                    o.transform.position = p;
                    if (orb != null) { orb.position = p; orb.linearVelocity = Vector2.zero; }
                    n++;
                }
                yield return new WaitForSeconds(2f);
                var h = rat.GetComponent<NetworkHealth>();
                Vector2 mid = h.BodyCenter + new Vector2(side * 2.6f, 0.55f);
                yield return CameraOnly("P_portada_sin_texto", mid, 2.5f, 1260, 1000);
                yield return CameraOnly("P_ratas_cerca", mid + Vector2.down * 0.5f, 2.4f, 1920, 1080);

                // --- ¡BUM! explosión con fuego entre ellas ---
                var cf = CameraFollow.Instance;
                Vector2 boom = h.BodyCenter + new Vector2(side * 6.2f, -0.3f);
                tm.ServerBlast(boom, 1.5f, 18, 7f, true);
                FireSystem.ServerIgnite(boom + Vector2.up * 0.3f, Vector2.up * 3f, 2.5f, 10);
                yield return new WaitForSeconds(0.16f);
                yield return CameraOnly("P04_explosion", boom + new Vector2(-side * 1.2f, 0.9f), 3.2f, 1920, 1080);
                yield return new WaitForSeconds(2.5f);
            }

            // --- Climas ---
            WeatherFx.Current = Weather.Nieve;
            yield return new WaitForSeconds(2f);
            yield return Capture("P05_nieve");
            WeatherFx.Current = Weather.Tormenta;
            yield return new WaitForSeconds(1.5f);
            tm.TestLightningServer();
            yield return new WaitForSeconds(0.12f);
            yield return Capture("P06_tormenta_rayo");
            yield return new WaitForSeconds(1.5f);
            WeatherFx.Current = Weather.Despejado;

            // --- Una rata revienta → repetición ---
            var dead = FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m.IsSpawned && !m.IsTestDummy && m != rat && !m.GetComponent<NetworkHealth>().IsDead.Value);
            if (dead != null)
            {
                var dh = dead.GetComponent<NetworkHealth>();
                CameraFollow.Instance?.FocusOnImpact(dh.BodyCenter, 2f);
                yield return new WaitForSeconds(0.8f);
                tm.ServerBlast(dh.BodyCenter + Vector2.left * 0.4f, 2f, 999, 8f, true);
                yield return new WaitForSeconds(0.15f);
                yield return Capture("P07_revienta");
                if (SlowMotion.Enabled)
                {
                    yield return new WaitForSecondsRealtime(1.6f);
                    tm.EndTurnServerRpc();
                    float t0 = Time.unscaledTime;
                    while (!DeathReplay.Playing && Time.unscaledTime - t0 < 6f) yield return null;
                    yield return new WaitForSecondsRealtime(2.2f);
                    yield return Capture("P08_repeticion");
                }
            }
            Log("Promo: fin ✓");
            Finish(null);
        }

        /// <summary>Foto SOLO de la cámara (sin HUD ni menús), centrada y con el zoom pedido.</summary>
        private IEnumerator CameraOnly(string name, Vector2 center, float orthoSize, int w, int h)
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            var cf = CameraFollow.Instance;
            bool cfOn = cf != null && cf.enabled;
            // Sin rótulos (nombres, vida, números de daño) ni flecha del turno: foto limpia
            var texts = FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Where(t => t.enabled).ToList();
            foreach (var t in texts) t.enabled = false;
            var marker = FindFirstObjectByType<TurnMarker>();
            if (marker != null) marker.enabled = false;
            var arrow = GameObject.Find("FlechaTurno");
            var arrowR = arrow != null ? arrow.GetComponent<SpriteRenderer>() : null;
            bool arrowOn = arrowR != null && arrowR.enabled;
            if (arrowR != null) arrowR.enabled = false;
            if (cf != null) cf.enabled = false;
            var prevPos = cam.transform.position; float prevSize = cam.orthographicSize;
            cam.transform.position = new Vector3(center.x, center.y, prevPos.z);
            cam.orthographicSize = orthoSize;
            yield return null;
            yield return new WaitForEndOfFrame();
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            rt.Release();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Log($"📷 {name}.png (solo cámara, {w}×{h})");
            cam.transform.position = prevPos; cam.orthographicSize = prevSize;
            if (cf != null) cf.enabled = cfOn;
            foreach (var t in texts) if (t != null) t.enabled = true;
            if (arrowR != null) arrowR.enabled = arrowOn;
            if (marker != null) marker.enabled = true;
        }
    }
}
