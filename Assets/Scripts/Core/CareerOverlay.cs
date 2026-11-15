using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  0.35 — Pantalla "HOJA DE SERVICIOS": estadísticas de por vida a la
    ///  izquierda y los LOGROS a la derecha (conseguidos en dorado, el resto con
    ///  su barra de progreso y, si lo tienen, el sombrero que desbloquean).
    ///  Se abre desde Jugar → "Logros". ESC o "Volver" la cierran.
    /// </summary>
    public class CareerOverlay : MonoBehaviour
    {
        private static CareerOverlay instance;
        public static bool IsOpen => instance != null;
        private Vector2 scroll;

        public static void Open()
        {
            if (instance != null) return;
            instance = new GameObject("HojaDeServicios").AddComponent<CareerOverlay>();
        }

        public static void Close()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnGUI()
        {
            GUI.depth = -40;
            TrenchGUI.Begin();
            TrenchGUI.Dim(0.55f);
            var r = new Rect((TrenchGUI.VW - 1500f) * 0.5f, (TrenchGUI.VH - 960f) * 0.5f, 1500f, 960f);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 16f, r.width, 50f), "HOJA DE SERVICIOS");
            var d = Career.Stats;

            // --- Estadísticas ---
            float x = r.x + 40f, y = r.y + 96f, w = 440f;
            TrenchGUI.Label(new Rect(x, y, w, 34f), "Estadísticas");
            y += 44f;
            void Row(string label, string value)
            {
                TrenchGUI.Small(new Rect(x, y, w - 120f, 30f), label);
                var al = TrenchGUI.LabelStyle.alignment;
                TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleRight;
                TrenchGUI.Label(new Rect(x + w - 160f, y - 3f, 160f, 34f), value);
                TrenchGUI.LabelStyle.alignment = al;
                y += 36f;
            }
            Row("Partidas jugadas", d.matches.ToString());
            Row("Victorias", d.wins.ToString());
            Row("Ratas enemigas reventadas", d.kills.ToString());
            Row("Fuego amigo", d.ownKills.ToString());
            Row("Ratas tuyas caídas", d.deaths.ToString());
            Row("Daño hecho", d.damage.ToString());
            Row("Mejor golpe", d.bestHit.ToString());
            Row("Más bajas en un turno", d.bestTurnKills.ToString());
            Row("Ahogadas", d.killsByType[(int)DamageType.Water].ToString());
            Row("Con fuego", d.killsByType[(int)DamageType.Fire].ToString());
            Row("Con explosiones", d.killsByType[(int)DamageType.Explosion].ToString());
            Row("A golpes", d.killsByType[(int)DamageType.Melee].ToString());
            Row("A balazos", d.killsByType[(int)DamageType.Bullet].ToString());
            Row("Burlas", d.taunts.ToString());
            int got = 0;
            foreach (var a in Career.All) if (Career.IsUnlocked(a.id)) got++;
            y += 10f;
            var c0 = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.3f);
            TrenchGUI.Label(new Rect(x, y, w, 34f), Loc.F("Logros: {0} de {1}", got, Career.All.Length));
            GUI.color = c0;

            // --- Logros (2 columnas) ---
            float gx = r.x + 520f, gy = r.y + 96f, gw = r.width - 560f;
            var view = new Rect(gx, gy, gw, r.height - 200f);
            int rows = (Career.All.Length + 1) / 2;
            float cellH = 84f, colW = (gw - 30f) * 0.5f;
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, gw - 20f, rows * cellH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            for (int i = 0; i < Career.All.Length; i++)
            {
                var a = Career.All[i];
                bool done = Career.IsUnlocked(a.id);
                var cell = new Rect((i % 2) * (colW + 10f), (i / 2) * cellH, colW, cellH - 8f);
                var c = GUI.color;
                GUI.color = done ? new Color(0.55f, 0.42f, 0.12f, 0.55f) : new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                GUI.color = done ? new Color(1f, 0.85f, 0.35f) : new Color(0.75f, 0.75f, 0.7f);
                TrenchGUI.Label(new Rect(cell.x + 12f, cell.y + 4f, cell.width - 24f, 32f), (done ? "★ " : "") + Loc.T(a.name));
                if (a.hat.HasValue)
                {
                    // Premio a la derecha del título (abajo no cabía junto a la descripción).
                    var al = TrenchGUI.SmallStyle.alignment;
                    TrenchGUI.SmallStyle.alignment = TextAnchor.MiddleRight;
                    GUI.color = done ? new Color(1f, 0.85f, 0.35f) : new Color(0.85f, 0.7f, 0.4f);
                    TrenchGUI.Small(new Rect(cell.x + 12f, cell.y + 6f, cell.width - 24f, 28f), Loc.F("Premio: {0}", Loc.T(RatLook.HatName(a.hat.Value))));
                    TrenchGUI.SmallStyle.alignment = al;
                }
                GUI.color = done ? new Color(1f, 0.95f, 0.8f) : new Color(0.7f, 0.7f, 0.66f);
                TrenchGUI.Small(new Rect(cell.x + 12f, cell.y + 36f, cell.width - 24f, 26f), Loc.T(a.desc));
                if (!done)
                {
                    int p = Mathf.Min(a.progress(d), a.goal);
                    var bar = new Rect(cell.x + 12f, cell.y + cellH - 20f, cell.width - 24f, 6f);
                    GUI.color = new Color(0f, 0f, 0f, 0.5f);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture);
                    GUI.color = new Color(0.9f, 0.7f, 0.25f);
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * p / Mathf.Max(1f, a.goal), bar.height), Texture2D.whiteTexture);
                }
                GUI.color = c;
            }
            GUI.EndScrollView();

            if (TrenchGUI.Button(new Rect(r.x + r.width * 0.5f - 160f, r.yMax - 84f, 320f, 56f), "Volver")) Close();
            TrenchGUI.End();
        }
    }
}
