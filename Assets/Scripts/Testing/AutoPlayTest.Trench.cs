using System.Collections;
using Ratms.Networking;
using Ratms.World;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Testing
{
    /// <summary>
    ///  Prueba del ARSENAL DE TRINCHERA (0.21): papel "trench"
    ///  (<c>tools\autotest.ps1 -Role trench</c>). Cada arma nueva contra una rata
    ///  diana, con capturas T00–T14 y un ✓/⚠ por arma en el informe.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunTrench()
        {
            Log("Papel: ARSENAL DE TRINCHERA 0.21");
            MatchRules.OverrideCrateChance = 0f;
            MatchRules.OverrideBarrels = 0;
            MatchRules.OverrideTurnSeconds = 90;
            MatchRules.OverrideSuddenDeathSeconds = 0f;
            MatchRules.OverrideMutilations = true;   // la bayoneta y las flechas amputan
            yield return new WaitForSeconds(2.5f);

            NetworkManager nm = null;
            yield return WaitFor(() => (nm = NetworkManager.Singleton) != null, 10f, "NetworkManager en el menú");
            if (finished) yield break;
            var menu = FindFirstObjectByType<MultiplayerMenuController>();
            if (menu != null) menu.HideAllPanels();
            var utp = nm.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData("127.0.0.1", 7777, "127.0.0.1");
            if (!nm.StartHost()) { Finish("No se pudo arrancar el host local"); yield break; }
            nm.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);

            NetworkPlayerMovement rat = null;
            yield return WaitFor(() => (rat = FindLocalRat()) != null, 20f, "que aparezca la rata local");
            if (finished) yield break;
            yield return WaitFor(() => NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.IsTurnOf(nm.LocalClientId), 15f, "el primer turno");
            if (finished) yield break;
            yield return new WaitForSeconds(1.5f);

            var tm = NetworkTurnManager.Instance;
            var weapons = rat.GetComponentInChildren<WeaponsMenuUI>();
            var trench = rat.GetComponent<NetworkTrenchArsenal>();
            if (trench == null) { Finish("La rata no tiene NetworkTrenchArsenal (falta RatmsPrefabSetup)"); yield break; }
            int team = rat.TeamIndex.Value;
            // Munición de sobra para todo (la bomba bíblica empieza a 0)
            for (int w = (int)WeaponsMenuUI.WeaponId.RatBomb; w <= (int)WeaponsMenuUI.WeaponId.Girder; w++)
                tm.TestGiveAmmo(team, (WeaponsMenuUI.WeaponId)w, 5);

            NetworkHealth dh = null;
            IEnumerator Prepare(string label, float dist, float dy = 0.6f)
            {
                yield return WaitFreshTurn(nm, rat);
                if (finished) yield break;
                // Rata como nueva (las explosiones de las pruebas anteriores
                // le arrancan los brazos y sin brazos no puede usar casi nada).
                var me = rat.GetComponent<NetworkHealth>();
                me.Injuries.Value = 0;
                me.PoisonTurns.Value = 0;
                me.CurrentHealth.Value = me.MaxHealth;
                yield return PlaceOnFlatSpot(rat, 0.3f, 0.7f, label);
                BuildRange(rat, Mathf.Max(dist, 4f) + 4f);
                yield return new WaitForSeconds(0.3f);
                if (dh != null && dh.IsSpawned) dh.GetComponent<NetworkObject>().Despawn(true);
                dh = null;
                if (dist > 0f)
                {
                    float side = rat.FacingRight ? 1f : -1f;
                    var dm = SpawnDummy((Vector2)rat.transform.position + new Vector2(side * dist, dy));
                    dh = dm != null ? dm.GetComponent<NetworkHealth>() : null;
                }
                yield return new WaitForSeconds(1f);
            }
            void Select(WeaponsMenuUI.WeaponId w) => weapons.SelectedIndexNet.Value = (int)w;
            int Hp() => dh != null ? dh.CurrentHealth.Value : 0;

            // 0.28: "-Role trench2" prueba SOLO las armas nuevas
            if (GetArg("-autoTestRole") == "trench2")
            {
                yield return RunTrench2(rat, tm, weapons, trench, (l, d) => Prepare(l, d), () => dh, Select, nm);
                Log("Arsenal completo (0.28): fin ✓");
                Finish(null);
                yield break;
            }

            // ---- T00: armas en la mano ----
            yield return PlaceOnFlatSpot(rat, 0.3f, 0.7f, "mano");
            foreach (var (w, shot) in new[] { (WeaponsMenuUI.WeaponId.Sniper, "T00a_mano_francotirador"), (WeaponsMenuUI.WeaponId.Bow, "T00b_mano_arco"),
                                              (WeaponsMenuUI.WeaponId.Bayonet, "T00c_mano_bayoneta"), (WeaponsMenuUI.WeaponId.Mortar, "T00d_mano_mortero"),
                                              (WeaponsMenuUI.WeaponId.HolyCheese, "T00e_mano_queso_biblico") })
            {
                Select(w);
                rat.TestSetAim(12f);
                yield return new WaitForSeconds(0.4f);
                yield return CloseupWithColliders(rat, shot, 1.2f, overlay: false);
            }
            Select(WeaponsMenuUI.WeaponId.None);
            yield return new WaitForSeconds(0.3f);
            weapons.SetVisible(true);
            yield return new WaitForSeconds(0.4f);
            yield return Capture("T00f_arsenal_completo");
            weapons.SetVisible(false);

            // ---- T01: francotirador ----
            Log("---- Francotirador ----");
            yield return Prepare("francotirador", 7f);
            if (dh == null) { Finish("No se pudo crear la diana"); yield break; }
            Select(WeaponsMenuUI.WeaponId.Sniper);
            AimAt(rat, dh.BodyCenter);
            yield return new WaitForSeconds(0.6f);
            yield return Capture("T01a_francotirador_laser");
            int h0 = Hp();
            rat.TestPressFire();
            yield return new WaitForSeconds(0.05f);
            yield return Capture("T01b_francotirador_disparo");
            yield return new WaitForSeconds(1f);
            Check($"Francotirador → la diana pierde {h0 - Hp()}", h0 - Hp() >= 40, "Francotirador: no hizo el daño esperado");

            // ---- T02: arco ----
            Log("---- Arco ----");
            yield return Prepare("arco", 5f);
            Select(WeaponsMenuUI.WeaponId.Bow);
            AimAt(rat, dh.BodyCenter + Vector2.up * 0.4f);
            yield return new WaitForSeconds(0.4f);
            h0 = Hp();
            byte inj0 = dh.Injuries.Value;
            rat.TestPressFire();
            yield return new WaitForSeconds(1.5f);   // arco tenso del todo: tiro casi recto
            rat.TestReleaseFire();
            yield return new WaitForSeconds(0.12f);
            yield return Capture("T02a_flecha_en_vuelo");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("T02b_flecha_efecto");
            bool effect = dh == null || dh.Poisoned || dh.Injuries.Value != inj0 || FireSystem.ActiveCount > 0 || h0 - Hp() > 16;
            Log($"Arco → daño {h0 - Hp()} · veneno {dh != null && dh.Poisoned} · heridas {inj0}→{(dh != null ? dh.Injuries.Value : 0)} · llamas {FireSystem.ActiveCount} {(h0 - Hp() > 0 && effect ? "✓" : "⚠")}");
            if (h0 - Hp() <= 0) Problem("Arco: la flecha no dio a la diana (puede ser el mapa)");

            // ---- T03: bayoneta ----
            Log("---- Bayoneta ----");
            yield return Prepare("bayoneta", 0.9f, 0.3f);
            Select(WeaponsMenuUI.WeaponId.Bayonet);
            AimAt(rat, dh.BodyCenter);
            yield return new WaitForSeconds(0.4f);
            h0 = Hp(); inj0 = dh.Injuries.Value;
            rat.TestPressFire();
            yield return new WaitForSeconds(0.25f);
            yield return Capture("T03_bayoneta");
            yield return new WaitForSeconds(1f);
            Check($"Bayoneta → daño {h0 - Hp()} · heridas {inj0}→{(dh != null ? dh.Injuries.Value : 0)}",
                  h0 - Hp() > 0 && dh != null && dh.Injuries.Value != inj0, "Bayoneta: no dañó o no amputó");

            // ---- T04: mortero ----
            Log("---- Mortero ----");
            yield return Prepare("mortero", 5f);
            Select(WeaponsMenuUI.WeaponId.Mortar);
            rat.TestSetAim(60f);
            yield return new WaitForSeconds(0.4f);
            h0 = Hp();
            rat.TestPressFire();
            yield return new WaitForSeconds(0.65f);
            rat.TestReleaseFire();
            yield return new WaitForSeconds(0.5f);
            yield return Capture("T04a_mortero_vuelo");
            yield return WaitFor(() => FindFirstObjectByType<NetworkGadget>() == null, 8f, "que caiga el mortero y su metralla", fatal: false);
            yield return Capture("T04b_mortero_impacto");
            Log($"Mortero → disparado y caído ✓ · la diana pierde {h0 - Hp()} (acertar depende del mapa)");

            // ---- T05: rata-bomba ----
            Log("---- Rata-bomba ----");
            yield return Prepare("rata-bomba", 4f, 0.3f);
            Select(WeaponsMenuUI.WeaponId.RatBomb);
            yield return new WaitForSeconds(0.3f);
            h0 = Hp();
            rat.TestPressFire();
            yield return new WaitForSeconds(1.2f);
            var rb0 = NetworkGadget.ActiveRatBombOf(rat.NetworkObjectId);
            bool hadBomb = rb0 != null;
            yield return Capture("T05a_rata_bomba_corre");
            Vector2 rbStart = rb0 != null ? (Vector2)rb0.transform.position : Vector2.zero;
            yield return new WaitForSeconds(1.2f);
            float ran = rb0 != null ? Vector2.Distance(rbStart, rb0.transform.position) : 0f;
            rat.TestPressFire();   // ¡bum!
            yield return new WaitForSeconds(0.6f);
            yield return Capture("T05b_rata_bomba_bum");
            bool gone = NetworkGadget.ActiveRatBombOf(rat.NetworkObjectId) == null;
            Check($"Rata-bomba → corre {ran:0.0} u en 1.2 s · detonada con ESPACIO: {gone} · la diana pierde {h0 - Hp()}",
                  hadBomb && ran > 0.8f && gone, "Rata-bomba: no corre o no se detona con ESPACIO");

            // ---- T06: mina ----
            Log("---- Mina ----");
            yield return Prepare("mina", 0f);
            Select(WeaponsMenuUI.WeaponId.Mine);
            yield return new WaitForSeconds(0.3f);
            rat.TestPressFire();
            yield return new WaitForSeconds(0.4f);
            // Como en Worms, la mina también salta con quien la puso: la rata se aparta.
            {
                var rrb = rat.GetComponent<Rigidbody2D>();
                float sd = rat.FacingRight ? 1f : -1f;
                rrb.position += new Vector2(sd * 3.5f, 0.2f);
                rrb.linearVelocity = Vector2.zero;
                rat.TestResetFallTracking();
            }
            yield return new WaitForSeconds(2.2f);
            NetworkGadget mineG = null;
            foreach (var g in FindObjectsByType<NetworkGadget>(FindObjectsSortMode.None)) if (g.Type == NetworkGadget.Kind.Mine) mineG = g;
            bool armed = mineG != null && mineG.Armed.Value;
            yield return Capture("T06a_mina_armada");
            if (mineG != null)
            {
                var dm = SpawnDummy((Vector2)mineG.transform.position + Vector2.up * 1.5f);
                dh = dm != null ? dm.GetComponent<NetworkHealth>() : null;
            }
            h0 = dh != null ? dh.MaxHealth : 0;
            yield return new WaitForSeconds(2f);
            yield return Capture("T06b_mina_bum");
            bool mineGone = mineG == null || !mineG.IsSpawned;
            Check($"Mina → armada: {armed} · explota al acercarse una rata: {mineGone} · la diana pierde {h0 - Hp()}",
                  armed && mineGone, "Mina: no se arma o no explota");

            // ---- T07: bidón de gas ----
            Log("---- Gas ----");
            yield return Prepare("gas", 3f);
            Select(WeaponsMenuUI.WeaponId.GasCan);
            AimAt(rat, dh.BodyCenter + Vector2.up * 1.2f);
            yield return new WaitForSeconds(0.3f);
            rat.TestPressFire();
            yield return new WaitForSeconds(0.42f);
            rat.TestReleaseFire();
            yield return new WaitForSeconds(4.5f);
            yield return Capture("T07_nube_de_gas");
            // Dónde cae el bidón depende del rebote; lo que se comprueba es que la NUBE envenene.
            bool anyPoisoned = (dh != null && dh.Poisoned) || rat.GetComponent<NetworkHealth>().Poisoned;
            Check($"Gas → envenenadas: diana {dh != null && dh.Poisoned} · lanzadora {rat.GetComponent<NetworkHealth>().Poisoned}",
                  anyPoisoned, "Gas: la nube no envenenó a nadie");

            // ---- T08: queso apestoso ----
            Log("---- Queso apestoso ----");
            yield return Prepare("queso", 5f);
            Select(WeaponsMenuUI.WeaponId.Cheese);
            AimAt(rat, (Vector2)rat.transform.position + new Vector2(rat.FacingRight ? 2f : -2f, 3f));
            yield return new WaitForSeconds(0.3f);
            float dx0 = dh != null ? dh.BodyCenter.x : 0f;
            rat.TestPressFire();
            yield return new WaitForSeconds(0.35f);
            rat.TestReleaseFire();
            yield return new WaitForSeconds(3.5f);
            yield return Capture("T08_queso_atrae");
            float moved = dh != null ? Mathf.Abs(dh.BodyCenter.x - dx0) : 0f;
            Check($"Queso → la diana se mueve {moved:0.0} u hacia él", moved > 0.6f, "Queso: no atrae a las ratas");
            yield return new WaitForSeconds(3f);

            // ---- T09: paloma ----
            Log("---- Paloma ----");
            yield return Prepare("paloma", 8f);
            Select(WeaponsMenuUI.WeaponId.Pigeon);
            yield return new WaitForSeconds(0.3f);
            h0 = Hp();
            trench.TestClickTarget = dh.BodyCenter;
            yield return new WaitForSeconds(1.2f);
            yield return Capture("T09a_paloma_vuela");
            yield return WaitFor(() => FindFirstObjectByType<NetworkGadget>() == null, 10f, "que llegue la paloma", fatal: false);
            yield return Capture("T09b_paloma_bum");
            Check($"Paloma → la diana pierde {h0 - Hp()}", h0 - Hp() > 0, "Paloma: no llegó a la diana");

            // ---- T10: pala ----
            Log("---- Pala ----");
            yield return Prepare("pala", 0f);
            Select(WeaponsMenuUI.WeaponId.Shovel);
            rat.TestSetAim(-35f);
            yield return new WaitForSeconds(0.3f);
            var body = rat.GetComponent<NetworkHealth>().BodyCenter;
            Vector2 digDir = new Vector2((rat.FacingRight ? 1f : -1f) * Mathf.Cos(35f * Mathf.Deg2Rad), -Mathf.Sin(35f * Mathf.Deg2Rad));
            Vector2 probe = body + digDir * 2.5f;
            bool solidBefore = DestructibleTerrain.IsSolidAtWorld(probe);
            rat.TestPressFire();
            yield return new WaitForSeconds(1.4f);
            yield return Capture("T10_pala_tunel");
            bool airAfter = !DestructibleTerrain.IsSolidAtWorld(probe);
            Check($"Pala → a 2.5 u: antes sólido {solidBefore}, después aire {airAfter}", airAfter, "Pala: no cavó el túnel");

            // ---- T11: sacos terreros ----
            Log("---- Sacos terreros ----");
            yield return Prepare("sacos", 0f);
            Select(WeaponsMenuUI.WeaponId.Sandbags);
            yield return new WaitForSeconds(0.3f);
            Vector2 spot = rat.GetComponent<NetworkHealth>().BodyCenter + new Vector2(rat.FacingRight ? 3f : -3f, 1.5f);
            bool airBefore = !DestructibleTerrain.IsSolidAtWorld(spot);
            trench.TestClickTarget = spot;
            yield return new WaitForSeconds(0.8f);
            yield return Capture("T11_sacos");
            Check($"Sacos → en el sitio elegido: antes aire {airBefore}, después sólido {DestructibleTerrain.IsSolidAtWorld(spot)}",
                  DestructibleTerrain.IsSolidAtWorld(spot), "Sacos: no aparecieron");

            // ---- T12: bomba bíblica de queso ----
            Log("---- Bomba bíblica de queso ----");
            yield return Prepare("queso bíblico", 5f);
            Select(WeaponsMenuUI.WeaponId.HolyCheese);
            AimAt(rat, dh.BodyCenter + Vector2.up * 2f);
            yield return new WaitForSeconds(0.3f);
            h0 = Hp();
            rat.TestPressFire();
            yield return new WaitForSeconds(0.55f);
            rat.TestReleaseFire();
            yield return new WaitForSeconds(3.2f);
            yield return Capture("T12a_aleluya");
            yield return new WaitForSeconds(1.2f);
            yield return Capture("T12b_queso_biblico_bum");
            Check($"Bomba bíblica → la diana pierde {h0 - Hp()}", dh == null || h0 - Hp() >= 30 || dh.IsDead.Value, "Bomba bíblica: poco daño");

            // ---- T13: veneno al empezar el turno (rata propia) ----
            Log("---- Veneno por turnos ----");
            var myH = rat.GetComponent<NetworkHealth>();
            myH.PoisonServer(2);
            int my0 = myH.CurrentHealth.Value;
            yield return WaitFor(() => tm.CurrentRatId.Value != rat.NetworkObjectId, 30f, "que acabe el turno", fatal: false);
            yield return WaitFor(() => tm.CurrentRatId.Value == rat.NetworkObjectId, 30f, "otro turno de mi rata", fatal: false);
            yield return new WaitForSeconds(0.6f);
            yield return Capture("T13_veneno");
            Check($"Veneno → al empezar su turno pierde {my0 - myH.CurrentHealth.Value} (quedan {myH.PoisonTurns.Value} turnos)",
                  my0 - myH.CurrentHealth.Value >= NetworkHealth.PoisonDamagePerTurn || myH.CurrentHealth.Value == 1, "Veneno: no hizo efecto al empezar el turno");

            Log("Arsenal de trinchera: fin ✓");
            Finish(null);
        }

        /// <summary>
        ///  CAMPO DE TIRO: suelo plano de tierra desde la rata hasta
        ///  <paramref name="length"/> u por delante, y aire despejado encima. Así
        ///  cada arma se prueba igual sea cual sea el mapa aleatorio (antes la
        ///  diana caía a otra isla y el francotirador daba en un montículo).
        /// </summary>
        private static void BuildRange(NetworkPlayerMovement rat, float length)
        {
            var h = rat.GetComponent<NetworkHealth>();
            var col = rat.GetComponent<Collider2D>();
            float feet = col != null ? col.bounds.min.y : h.BodyCenter.y - 0.3f;
            float side = rat.FacingRight ? 1f : -1f;
            float x0 = h.BodyCenter.x - side * 2f, x1 = h.BodyCenter.x + side * length;
            Vector2 a = new Vector2(Mathf.Min(x0, x1), feet - 1.6f), b = new Vector2(Mathf.Max(x0, x1), feet - 0.03f);
            DestructibleTerrain.TestFillRectWorld(a, b, new Color32(120, 84, 50, 255));
            // Aire encima (sin tocar a la rata: empezamos un poco por delante)
            float cx0 = h.BodyCenter.x + side * 0.6f;
            Vector2 c = new Vector2(Mathf.Min(cx0, x1), feet + 0.02f), d = new Vector2(Mathf.Max(cx0, x1), feet + 4f);
            DestructibleTerrain.TestFillRectWorld(c, d, new Color32(0, 0, 0, 0));
        }

        private void Check(string text, bool ok, string problem)
        {
            Log($"{text} {(ok ? "✓" : "⚠")}");
            if (!ok) Problem(problem);
        }
    }
}
