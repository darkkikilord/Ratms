using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>Tipo de partida que se va a jugar.</summary>
    public enum MatchMode
    {
        /// <summary>Por internet / red: un equipo por jugador conectado.</summary>
        Online,
        /// <summary>Varios equipos en el MISMO PC, pasándose el teclado.</summary>
        Local,
        /// <summary>Tú solo contra ratas diana (botón "Práctica").</summary>
        Practice,
    }

    /// <summary>
    ///  REGLAS DE PARTIDA. Las lee el HOST al empezar (los clientes reciben el
    ///  resultado por red: vida, turnos, equipos...).
    ///
    ///  Se guardan en PlayerPrefs ("rule_*") para que, cuando exista el menú
    ///  de configuración de partida, basta con escribir aquí y se recuerdan
    ///  entre sesiones. Valores por defecto: 4 ratas por equipo, 100 de vida,
    ///  turnos de 30 s y MUERTE SÚBITA a los 10 minutos.
    /// </summary>
    public static class MatchRules
    {
        public const int MaxTeams = 4;
        public const int MaxRatsPerTeam = 4;

        /// <summary>Tipo de partida que se arranca ahora (lo pone quien la lanza).</summary>
        public static MatchMode Mode = MatchMode.Online;

        /// <summary>Número de equipos en partida LOCAL (2-4).</summary>
        public static int LocalTeams
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rule_local_teams", 2), 2, MaxTeams);
            set => PlayerPrefs.SetInt("rule_local_teams", Mathf.Clamp(value, 2, MaxTeams));
        }

        /// <summary>Ratas por equipo (1-4).</summary>
        public static int RatsPerTeam
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rule_rats", 4), 1, MaxRatsPerTeam);
            set => PlayerPrefs.SetInt("rule_rats", Mathf.Clamp(value, 1, MaxRatsPerTeam));
        }

        /// <summary>Vida inicial de cada rata.</summary>
        public static int StartHealth
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rule_hp", 100), 10, 500);
            set => PlayerPrefs.SetInt("rule_hp", Mathf.Clamp(value, 10, 500));
        }

        /// <summary>Segundos de cada turno.</summary>
        public static int TurnSeconds
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rule_turn", 30), 10, 120);
            set => PlayerPrefs.SetInt("rule_turn", Mathf.Clamp(value, 10, 120));
        }

        /// <summary>Minutos hasta la MUERTE SÚBITA (el agua empieza a subir). 0 = nunca.</summary>
        public static float SuddenDeathMinutes
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat("rule_sd_min", 10f), 0f, 60f);
            set => PlayerPrefs.SetFloat("rule_sd_min", Mathf.Clamp(value, 0f, 60f));
        }

        /// <summary>Cuánto sube el agua (u) al final de cada turno en muerte súbita.</summary>
        public static float SuddenDeathRisePerTurn
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat("rule_sd_rise", 1.2f), 0.2f, 5f);
            set => PlayerPrefs.SetFloat("rule_sd_rise", Mathf.Clamp(value, 0.2f, 5f));
        }

        /// <summary>
        ///  Si tiene valor, manda sobre <see cref="RatsPerTeam"/> (lo usa la
        ///  prueba automática para seguir con 1 rata y medir sin líos).
        /// </summary>
        public static int? OverrideRatsPerTeam;

        /// <summary>Overrides de la prueba automática (no tocan los PlayerPrefs del jugador).</summary>
        public static int? OverrideLocalTeams, OverrideTurnSeconds;
        public static float? OverrideSuddenDeathSeconds;

        public static int EffectiveLocalTeams => OverrideLocalTeams ?? LocalTeams;
        public static int EffectiveTurnSeconds => OverrideTurnSeconds ?? TurnSeconds;

        /// <summary>Segundos hasta la muerte súbita en la partida que empieza (0 = nunca; nunca en práctica).</summary>
        public static float EffectiveSuddenDeathSeconds =>
            Mode == MatchMode.Practice ? 0f : (OverrideSuddenDeathSeconds ?? SuddenDeathMinutes * 60f);

        /// <summary>Ratas por equipo que se usan DE VERDAD en la partida que empieza.</summary>
        public static int EffectiveRatsPerTeam =>
            Mode == MatchMode.Practice ? 1 : (OverrideRatsPerTeam ?? RatsPerTeam);

        // ------------------------------------------------------------
        //  ECONOMÍA DE ARMAS
        // ------------------------------------------------------------
        /// <summary>Munición inicial de cada arma por equipo (-1 = infinita). En práctica, todo infinito.</summary>
        public static int DefaultAmmo(WeaponsMenuUI.WeaponId w)
        {
            if (Mode == MatchMode.Practice) return w == WeaponsMenuUI.WeaponId.SelectRat ? 0 : -1;
            return w switch
            {
                WeaponsMenuUI.WeaponId.Grenade => 3,
                WeaponsMenuUI.WeaponId.Dynamite => 1,
                WeaponsMenuUI.WeaponId.Teleport => 1,
                WeaponsMenuUI.WeaponId.SelectRat => 1,
                _ => -1,   // bazuca, escopeta, bate y cuerda: infinitas
            };
        }

        /// <summary>Barriles de gasolina en el mapa al empezar.</summary>
        public static int Barrels
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("rule_barrels", 5), 0, 12);
            set => PlayerPrefs.SetInt("rule_barrels", Mathf.Clamp(value, 0, 12));
        }
        public static int? OverrideBarrels;
        public static int EffectiveBarrels => OverrideBarrels ?? Barrels;

        /// <summary>
        ///  MUTILACIONES: los golpes fuertes arrancan patas y brazos o dejan
        ///  tuerta a la rata, y eso cambia cómo se juega (ver NetworkHealth).
        /// </summary>
        public static bool Mutilations
        {
            get => PlayerPrefs.GetInt("rule_mutil", 1) == 1;
            set => PlayerPrefs.SetInt("rule_mutil", value ? 1 : 0);
        }
        public static bool? OverrideMutilations;
        public static bool EffectiveMutilations => OverrideMutilations ?? Mutilations;

        /// <summary>Probabilidad (0-1) de que caiga una caja al empezar un turno.</summary>
        public static float CrateChance
        {
            get => Mathf.Clamp01(OverrideCrateChance ?? PlayerPrefs.GetFloat("rule_crates", 0.4f));
            set => PlayerPrefs.SetFloat("rule_crates", Mathf.Clamp01(value));
        }
        public static float? OverrideCrateChance;

        /// <summary>Vida que da un botiquín.</summary>
        public static int HealthCrateAmount = 30;

        /// <summary>Nombres de los equipos en partida local (uno por color de casco).</summary>
        public static readonly string[] LocalTeamNames = { "Rojos", "Azules", "Verdes", "Amarillos" };

        /// <summary>Nombres de rata de trinchera (se reparten al azar sin repetir dentro del equipo).</summary>
        public static readonly string[] RatNames =
        {
            "Sarnoso", "Cabo Queso", "Tuerto", "Rabocorto", "Pulgas", "Sargento Moho", "Bigotes", "Cloaca",
            "Mordisco", "Roñoso", "Chepa", "Colmillo", "Garrapata", "Peste", "Mugre", "Rata Gorda",
            "Soldado Raso", "Desertor", "Teniente Tufo", "Gangrena", "Pellejo", "Cicatriz", "Zarpas", "Cascarria",
        };
    }
}
