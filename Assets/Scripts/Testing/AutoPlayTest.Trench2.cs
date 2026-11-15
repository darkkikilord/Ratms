using System;
using System.Collections;
using System.Linq;
using Ratms.Networking;
using Ratms.World;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Testing
{
    /// <summary>
    ///  Prueba del ARSENAL COMPLETO (0.28): papel "trench2". Cada arma nueva
    ///  contra una diana en el "campo de tiro" plano de la prueba de trinchera.
    /// </summary>
    public partial class AutoPlayTest
    {
        private IEnumerator RunTrench2(NetworkPlayerMovement rat, NetworkTurnManager tm, WeaponsMenuUI weapons, NetworkTrenchArsenal trench,
                                       Func<string, float, IEnumerator> prepare, Func<NetworkHealth> diana,
                                       Action<WeaponsMenuUI.WeaponId> select, NetworkManager nm)
        {
            Log("Papel: ARSENAL COMPLETO 0.28 (24 armas nuevas)");
            int Hp() { var d = diana(); return d != null ? d.CurrentHealth.Value : 0; }
            int Gadgets(NetworkGadget.Kind k) => FindObjectsByType<NetworkGadget>(FindObjectsSortMode.None).Count(g => g.IsSpawned && g.Type == k);
            var me = rat.GetComponent<NetworkHealth>();
            var rb = rat.GetComponent<Rigidbody2D>();

            // El TAB con las 49 armas
            weapons.SetVisible(true);
            yield return new WaitForSeconds(0.5f);
            yield return Capture("A00_arsenal_49_armas");
            weapons.SetVisible(false);

            IEnumerator Charge(WeaponsMenuUI.WeaponId w, Vector2 at, float secs)
            {
                select(w);
                AimAt(rat, at);
                yield return new WaitForSeconds(0.3f);
                rat.TestPressFire();
                yield return new WaitForSeconds(secs);
                rat.TestReleaseFire();
            }
            IEnumerator Press(WeaponsMenuUI.WeaponId w, Vector2? aim = null)
            {
                select(w);
                if (aim.HasValue) AimAt(rat, aim.Value);
                yield return new WaitForSeconds(0.35f);
                rat.TestPressFire();
            }
            IEnumerator Click(WeaponsMenuUI.WeaponId w, Vector2 at)
            {
                select(w);
                yield return new WaitForSeconds(0.35f);
                trench.TestClickTarget = at;
            }

            // ---- Lanzables ----
            yield return prepare("plátano", 6f); int h0 = Hp();
            yield return Charge(WeaponsMenuUI.WeaponId.Banana, diana().BodyCenter + Vector2.up * 2.5f, 0.5f);
            int shards = 0;
            for (float t = 0; t < 7f; t += 0.2f) { shards = Mathf.Max(shards, Gadgets(NetworkGadget.Kind.BananaShard)); yield return new WaitForSeconds(0.2f); }
            yield return Capture("A01_platano");
            Check($"Plátano bomba → se parte en {shards} trozos · la diana pierde {h0 - Hp()}", shards >= 3, "Plátano: no se parte en trozos");

            yield return prepare("molotov", 4f); h0 = Hp();
            yield return Charge(WeaponsMenuUI.WeaponId.Molotov, diana().BodyCenter + Vector2.up * 1.5f, 0.55f);
            yield return new WaitForSeconds(1.2f);
            yield return Capture("A02_molotov");
            yield return new WaitForSeconds(4f);
            Check($"Cóctel molotov → la diana pierde {h0 - Hp()} (fuego)", h0 - Hp() > 0, "Molotov: no quemó a la diana");

            yield return prepare("bola de fuego", 5f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.Fireball, diana().BodyCenter);
            yield return new WaitForSeconds(2.5f);
            Check($"Bola de fuego → la diana pierde {h0 - Hp()}", h0 - Hp() > 0, "Bola de fuego: no dio a la diana");

            // ---- Con clic ----
            yield return prepare("misil teledirigido", 8f); h0 = Hp();
            yield return Click(WeaponsMenuUI.WeaponId.Homing, diana().BodyCenter);
            yield return new WaitForSeconds(1.2f);
            yield return Capture("A03_misil_teledirigido");
            yield return WaitFor(() => Gadgets(NetworkGadget.Kind.Homing) == 0, 10f, "que llegue el misil", fatal: false);
            Check($"Misil teledirigido → la diana pierde {h0 - Hp()}", h0 - Hp() > 0, "Misil teledirigido: no llegó a la diana");

            yield return prepare("yunque", 5f); h0 = Hp();
            yield return Click(WeaponsMenuUI.WeaponId.Anvil, diana().BodyCenter);
            yield return new WaitForSeconds(2.2f);
            yield return Capture("A04_yunque");
            yield return new WaitForSeconds(2f);
            Check($"Yunque → la diana pierde {h0 - Hp()}", h0 - Hp() > 0, "Yunque: no aplastó a la diana");

            yield return prepare("lluvia de minas", 0f);
            int m0 = Gadgets(NetworkGadget.Kind.Mine);
            yield return Click(WeaponsMenuUI.WeaponId.MineStrike, (Vector2)rat.transform.position + new Vector2(rat.FacingRight ? 4f : -4f, 0f));
            yield return new WaitForSeconds(3f);
            int mines = Gadgets(NetworkGadget.Kind.Mine) - m0;
            Check($"Lluvia de minas → minas nuevas: {mines}", mines >= 3, "Lluvia de minas: no cayeron minas");

            // ---- Desastres ----
            yield return prepare("meteoritos", 0f);
            me.CurrentHealth.Value = 900;   // los meteoritos también pueden caerte encima: que aguante la prueba
            yield return Press(WeaponsMenuUI.WeaponId.Meteors);
            int met = 0;
            for (float t = 0; t < 4f; t += 0.2f) { met = Mathf.Max(met, Gadgets(NetworkGadget.Kind.Meteor)); yield return new WaitForSeconds(0.2f); }
            CameraFollow.Instance?.ToggleOverview();
            yield return new WaitForSeconds(0.8f);
            yield return Capture("A05_meteoritos");
            CameraFollow.Instance?.ToggleOverview();
            Check($"Lluvia de meteoritos → meteoritos a la vez: {met}", met >= 2, "Meteoritos: no caen");
            yield return new WaitForSeconds(3f);

            yield return prepare("terremoto", 3f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.Earthquake);
            yield return new WaitForSeconds(1.2f);
            Check($"Terremoto → la diana pierde {h0 - Hp()}", h0 - Hp() >= 8, "Terremoto: no hace daño");

            // ---- Ratas que andan solas ----
            yield return prepare("rata rabiosa", 6f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.RabidRat);
            yield return new WaitForSeconds(1.0f);
            yield return Capture("A06_rata_rabiosa");
            yield return new WaitForSeconds(0.3f);
            bool det = trench.TryDetonateRatBomb();
            yield return new WaitForSeconds(1f);
            Check($"Rata rabiosa → reventada con ESPACIO: {det} · la diana pierde {h0 - Hp()}", det, "Rata rabiosa: no se puede reventar");

            yield return prepare("abuela rata", 2.5f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.GrannyRat);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("A07_abuela_rata");
            yield return WaitFor(() => Gadgets(NetworkGadget.Kind.GrannyRat) == 0, 9f, "que explote la abuela", fatal: false);
            Check($"Abuela rata → la diana pierde {h0 - Hp()}", h0 - Hp() > 0, "Abuela rata: no hizo daño");

            yield return prepare("rata apestosa", 2.5f);
            yield return Press(WeaponsMenuUI.WeaponId.StinkRat);
            yield return WaitFor(() => Gadgets(NetworkGadget.Kind.StinkRat) == 0, 9f, "que reviente la rata apestosa", fatal: false);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("A08_rata_apestosa_gas");
            Check($"Rata apestosa → diana envenenada: {diana() != null && diana().Poisoned}", diana() != null && (diana().Poisoned || diana().CurrentHealth.Value < diana().MaxHealth), "Rata apestosa: el gas no llegó");

            yield return prepare("topo", 0f);
            Vector2 start = me.BodyCenter;
            yield return Press(WeaponsMenuUI.WeaponId.Mole);
            yield return new WaitForSeconds(1.5f);
            yield return Capture("A09_topo_cavando");
            yield return WaitFor(() => Gadgets(NetworkGadget.Kind.Mole) == 0, 6f, "que explote el topo", fatal: false);
            Check($"Topo excavador → ha cavado y explotado: {Gadgets(NetworkGadget.Kind.Mole) == 0}", Gadgets(NetworkGadget.Kind.Mole) == 0, "Topo: no explotó");

            yield return prepare("estampida", 0f);
            yield return Press(WeaponsMenuUI.WeaponId.Stampede);
            yield return new WaitForSeconds(1.8f);
            int herd = Gadgets(NetworkGadget.Kind.RatBomb);
            yield return Capture("A10_estampida");
            for (int i = 0; i < 3; i++) { trench.TryDetonateRatBomb(); yield return new WaitForSeconds(0.3f); }
            Check($"Estampida → ratas-bomba a la vez: {herd}", herd >= 3, "Estampida: no salen tres ratas-bomba");

            // ---- Disparo y cuerpo a cuerpo ----
            yield return prepare("pistola", 5f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.Pistol, diana().BodyCenter);
            yield return new WaitForSeconds(1.2f);
            Check($"Pistola → la diana pierde {h0 - Hp()}", h0 - Hp() >= 12, "Pistola: no dio");

            yield return prepare("puño de fuego", 0.85f); h0 = Hp();
            float y0 = diana().BodyCenter.y;
            yield return Press(WeaponsMenuUI.WeaponId.FirePunch, diana().BodyCenter);
            yield return new WaitForSeconds(0.35f);
            yield return Capture("A11_puno_de_fuego");
            float lift = diana() != null ? diana().BodyCenter.y - y0 : 0f;
            yield return new WaitForSeconds(1f);
            Check($"Puño de fuego → la diana pierde {h0 - Hp()} · sale volando {lift:0.0} u", h0 - Hp() >= 20 && lift > 0.5f, "Puño de fuego: no pega o no lanza");

            yield return prepare("empujón", 0.85f);
            float x0 = diana().BodyCenter.x;
            yield return Press(WeaponsMenuUI.WeaponId.Prod, diana().BodyCenter);
            yield return new WaitForSeconds(0.8f);
            float pushed = diana() != null ? Mathf.Abs(diana().BodyCenter.x - x0) : 0f;
            Check($"Empujón → la diana se mueve {pushed:0.0} u", pushed > 0.5f, "Empujón: no mueve a la diana");

            // ---- Utilidades ----
            yield return prepare("botiquín", 0f);
            me.CurrentHealth.Value = me.MaxHealth - 40; me.PoisonTurns.Value = 2;
            int hp0 = me.CurrentHealth.Value;
            yield return Press(WeaponsMenuUI.WeaponId.Medkit);
            yield return new WaitForSeconds(0.6f);
            Check($"Botiquín → vida {hp0} → {me.CurrentHealth.Value} · veneno quitado: {!me.Poisoned}", me.CurrentHealth.Value >= hp0 + 25 && !me.Poisoned, "Botiquín: no cura");

            yield return prepare("muelle", 0f);
            float yy = rb.position.y;
            yield return Press(WeaponsMenuUI.WeaponId.SuperJump);
            float peak = yy;
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { peak = Mathf.Max(peak, rb.position.y); yield return null; }
            Check($"Muelle → sube {peak - yy:0.0} u", peak - yy > 2.5f, "Muelle: no salta");

            yield return prepare("paracaídas", 0f);
            yield return Press(WeaponsMenuUI.WeaponId.Parachute);
            yield return new WaitForSeconds(0.3f);
            rb.position += Vector2.up * 7f; rb.linearVelocity = Vector2.zero; rat.TestResetFallTracking();
            yield return new WaitForSeconds(1.4f);
            float vy = rb.linearVelocity.y;
            yield return Capture("A12_paracaidas");
            Check($"Paracaídas → velocidad de caída {vy:0.0} u/s", vy > -2f, "Paracaídas: cae a plomo");
            yield return new WaitForSeconds(3f);

            yield return prepare("balanza", 3f);
            me.CurrentHealth.Value = me.MaxHealth; diana().CurrentHealth.Value = 20;
            yield return Press(WeaponsMenuUI.WeaponId.Scales);
            yield return new WaitForSeconds(0.6f);
            int a = me.CurrentHealth.Value, b = Hp();
            Check($"Balanza de la justicia → mía {a} · diana {b}", Mathf.Abs(a - b) <= 1, "Balanza: no iguala la vida");

            yield return prepare("soplete", 0f);
            Vector2 tp = me.BodyCenter + new Vector2(rat.FacingRight ? 2.5f : -2.5f, -1.2f);
            bool solidBefore = DestructibleTerrain.IsSolidAtWorld(tp);
            yield return Press(WeaponsMenuUI.WeaponId.Blowtorch, tp);
            yield return new WaitForSeconds(2f);
            Check($"Soplete → antes sólido {solidBefore}, después aire {!DestructibleTerrain.IsSolidAtWorld(tp)}", !DestructibleTerrain.IsSolidAtWorld(tp), "Soplete: no abre túnel");

            yield return prepare("martillo", 0f);
            Vector2 dp = me.BodyCenter + Vector2.down * 2.2f;
            bool solid2 = DestructibleTerrain.IsSolidAtWorld(dp);
            yield return Press(WeaponsMenuUI.WeaponId.Drill);
            yield return new WaitForSeconds(1.6f);
            yield return Capture("A13_martillo_neumatico");
            Check($"Martillo neumático → antes sólido {solid2}, después aire {!DestructibleTerrain.IsSolidAtWorld(dp)}", !DestructibleTerrain.IsSolidAtWorld(dp), "Martillo: no cava hacia abajo");

            yield return prepare("pasarela", 0f);
            Vector2 gp = me.BodyCenter + new Vector2(rat.FacingRight ? 3.5f : -3.5f, 2.2f);
            yield return Click(WeaponsMenuUI.WeaponId.Girder, gp);
            yield return new WaitForSeconds(0.8f);
            yield return Capture("A14_pasarela");
            Check($"Pasarela → sólido en el sitio: {DestructibleTerrain.IsSolidAtWorld(gp)}", DestructibleTerrain.IsSolidAtWorld(gp), "Pasarela: no aparece");

            // ---- Kamikaze (la última: la rata muere) ----
            yield return prepare("kamikaze", 1.8f); h0 = Hp();
            yield return Press(WeaponsMenuUI.WeaponId.Kamikaze);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("A15_kamikaze");
            yield return new WaitForSeconds(0.8f);
            Check($"Kamikaze → la diana pierde {h0 - Hp()} · mi rata muere: {me.IsDead.Value}", me.IsDead.Value && h0 - Hp() > 0, "Kamikaze: no explota o no muere");
        }
    }
}
