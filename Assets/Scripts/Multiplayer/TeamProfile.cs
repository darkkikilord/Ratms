using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  MI EQUIPO: nombre del equipo, nombres de las 4 ratas y su aspecto
    ///  (<see cref="RatLook"/> empaquetado; 0 = al azar en cada partida).
    ///  Se guarda en PlayerPrefs. Es el primer paso del editor de cosméticos:
    ///  más adelante se podrá elegir pieza a pieza (casco, cola...).
    ///
    ///  En partida LOCAL lo usa el primer equipo; ONLINE cada jugador manda el
    ///  suyo al host en la sala de espera (<see cref="LobbySync"/>).
    /// </summary>
    public static class TeamProfile
    {
        public const int Slots = 4;

        public static string TeamName
        {
            get => PlayerPrefs.GetString("team_name", "");
            set => PlayerPrefs.SetString("team_name", Clean(value, 20));
        }

        public static string RatName(int i) => PlayerPrefs.GetString($"team_rat{i}", "");
        public static void SetRatName(int i, string v) => PlayerPrefs.SetString($"team_rat{i}", Clean(v, 16));

        /// <summary>Aspecto fijo de la rata i (0 = al azar).</summary>
        public static uint Look(int i) => (uint)PlayerPrefs.GetInt($"team_look{i}", 0);
        public static void SetLook(int i, uint code) => PlayerPrefs.SetInt($"team_look{i}", (int)code);

        public static bool HasAnything()
        {
            if (!string.IsNullOrEmpty(TeamName)) return true;
            for (int i = 0; i < Slots; i++) if (!string.IsNullOrEmpty(RatName(i)) || Look(i) != 0) return true;
            return false;
        }

        private static string Clean(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        /// <summary>Perfil en memoria (el que llega por red de otro jugador, o el propio).</summary>
        public class Data
        {
            public string team = "";
            public readonly string[] names = new string[Slots];
            public readonly uint[] looks = new uint[Slots];
        }

        public static Data Local()
        {
            var d = new Data { team = TeamName };
            for (int i = 0; i < Slots; i++) { d.names[i] = RatName(i); d.looks[i] = Look(i); }
            return d;
        }
    }
}
