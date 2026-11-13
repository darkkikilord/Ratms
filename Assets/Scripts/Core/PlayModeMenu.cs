using System;
using Ratms.Networking;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  Panel del botón "Jugar": PRÁCTICA (tú solo contra dianas) o PARTIDA
    ///  LOCAL en el mismo PC, con todas sus reglas. Estilo <see cref="TrenchGUI"/>.
    ///  Las reglas se guardan en <see cref="MatchRules"/> (se recuerdan).
    /// </summary>
    public class PlayModeMenu : MonoBehaviour
    {
        public Action OnPractice;
        public Action OnLocal;
        public Action OnBack;
        public Action OnTeam;

        private void OnGUI()
        {
            TrenchGUI.Begin();
            var r = new Rect((TrenchGUI.VW - 860f) * 0.5f, (TrenchGUI.VH - 844f) * 0.5f, 860f, 844f);
            TrenchGUI.Panel(r);
            float x = r.x + 40f, w = r.width - 80f, y = r.y + 18f;
            TrenchGUI.Title(new Rect(r.x, y, r.width, 50f), "¿A QUÉ JUGAMOS?");
            y += 70f;

            if (TrenchGUI.Button(new Rect(x, y, w, 58f), "Práctica  ·  tú solo contra ratas diana", big: true)) OnPractice?.Invoke();
            y += 80f;

            TrenchGUI.Label(new Rect(x, y, w, 34f), "PARTIDA LOCAL  (mismo PC, os pasáis el teclado)");
            y += 44f;
            MatchRules.LocalTeams = TrenchGUI.Choice(new Rect(x, y, w, 44f), "Equipos", new[] { "2", "3", "4" }, MatchRules.LocalTeams - 2) + 2;
            y += 54f;
            y += RulesEditor.Draw(new Rect(x, y, w, 0f));
            y += 10f;

            if (TrenchGUI.Button(new Rect(x, y, w, 64f), "¡A LA TRINCHERA!", big: true)) { PlayerPrefs.Save(); OnLocal?.Invoke(); }
            y += 80f;
            if (TrenchGUI.Button(new Rect(x, y, w * 0.48f, 46f), "Mi equipo (nombres y ratas)")) OnTeam?.Invoke();
            if (TrenchGUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 46f), "Volver")) OnBack?.Invoke();
            TrenchGUI.End();
        }
    }

    /// <summary>
    ///  Editor de REGLAS de partida (ratas, vida, turno, muerte súbita,
    ///  barriles, cajas). Lo usan la partida local y el lobby online (host).
    /// </summary>
    public static class RulesEditor
    {
        private static readonly int[] Hp = { 50, 100, 150, 200 };
        private static readonly int[] Turn = { 20, 30, 45, 60 };
        private static readonly float[] Sd = { 5f, 10f, 15f, 0f };
        private static readonly int[] Barrels = { 0, 3, 5, 8 };
        private static readonly float[] Crates = { 0f, 0.2f, 0.4f, 0.7f };

        /// <summary>Dibuja las reglas en filas; devuelve la altura usada.</summary>
        public static float Draw(Rect r, float rowH = 44f, float gap = 54f)
        {
            float y = r.y;
            MatchRules.RatsPerTeam = TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Ratas por equipo", new[] { "1", "2", "3", "4" }, MatchRules.RatsPerTeam - 1) + 1; y += gap;
            MatchRules.StartHealth = Hp[TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Vida inicial", new[] { "50", "100", "150", "200" }, Idx(Hp, MatchRules.StartHealth, 1))]; y += gap;
            MatchRules.TurnSeconds = Turn[TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Tiempo de turno", new[] { "20 s", "30 s", "45 s", "60 s" }, Idx(Turn, MatchRules.TurnSeconds, 1))]; y += gap;
            MatchRules.SuddenDeathMinutes = Sd[TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Muerte súbita", new[] { "5 min", "10 min", "15 min", "Nunca" }, Idx(Sd, MatchRules.SuddenDeathMinutes, 1))]; y += gap;
            MatchRules.Barrels = Barrels[TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Barriles", new[] { "Ninguno", "Pocos", "Normal", "Muchos" }, Idx(Barrels, MatchRules.Barrels, 2))]; y += gap;
            MatchRules.CrateChance = Crates[TrenchGUI.Choice(new Rect(r.x, y, r.width, rowH), "Cajas", new[] { "Nunca", "Pocas", "Normal", "Muchas" }, Idx(Crates, MatchRules.CrateChance, 2))]; y += gap;
            MatchRules.Mutilations = TrenchGUI.Toggle(new Rect(r.x, y, r.width, rowH), "Mutilaciones", MatchRules.Mutilations, 230f); y += gap;
            return y - r.y;
        }

        /// <summary>Resumen de una línea (lo ven los clientes en el lobby).</summary>
        public static string Summary() =>
            $"{MatchRules.RatsPerTeam} ratas · {MatchRules.StartHealth} vida · turnos de {MatchRules.TurnSeconds} s · " +
            $"muerte súbita {(MatchRules.SuddenDeathMinutes > 0 ? MatchRules.SuddenDeathMinutes + " min" : "nunca")} · " +
            $"{MatchRules.Barrels} barriles · cajas {(MatchRules.CrateChance <= 0f ? "nunca" : MatchRules.CrateChance < 0.3f ? "pocas" : MatchRules.CrateChance < 0.5f ? "normal" : "muchas")} · " +
            $"mutilaciones {(MatchRules.Mutilations ? "sí" : "no")}";

        private static int Idx(int[] arr, int v, int def) { int i = Array.IndexOf(arr, v); return i < 0 ? def : i; }
        private static int Idx(float[] arr, float v, int def)
        {
            for (int i = 0; i < arr.Length; i++) if (Mathf.Abs(arr[i] - v) < 0.01f) return i;
            return def;
        }
    }
}
