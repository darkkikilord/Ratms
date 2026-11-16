using System;
using System.Collections.Generic;
using Ratms.UI;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.35 — CARRERA: estadísticas de por vida y LOGROS de este PC.
    ///
    ///  Todo es LOCAL (PlayerPrefs "career_v1" en JSON): cada jugador lleva su
    ///  hoja de servicios. ¿Qué cuenta como "tuyo"?
    ///   • Online: las ratas que son tuyas (IsOwner).
    ///   • Partida local (mismo PC): el primer equipo, el de "Mi equipo".
    ///   • Práctica: no cuenta (son dianas).
    ///
    ///  De dónde salen los datos (siempre de código que ya corre en todos los PCs):
    ///   • Bajas, muertes, golpes: <see cref="NetworkHealth"/> (popups y muerte).
    ///   • Fin de partida: <see cref="MatchStats"/> + ganador (<see cref="VictoryScreen"/>).
    ///   • Burlas (P) y GIF guardados.
    ///
    ///  Algunos logros DESBLOQUEAN sombreros en "Mi equipo" (<see cref="IsHatLocked"/>).
    ///  Al conseguir uno sale un cartel arriba (lo dibuja <see cref="CareerToasts"/>).
    /// </summary>
    public static class Career
    {
        [Serializable]
        public class Data
        {
            public int matches, wins, kills, ownKills, deaths, damage, bestHit, taunts, gifs, flawless;
            public int[] killsByType = new int[16];   // índice = DamageType
            public int shapesPlayed;                   // bits: formas de mapa jugadas
            public int modesWon;                       // bits: modos ganados
            public int bestTurnKills;
            public List<string> unlocked = new();
        }

        private const string Key = "career_v1";
        private static Data data;
        public static Data Stats { get { Load(); return data; } }

        private static void Load()
        {
            if (data != null) return;
            try { data = JsonUtility.FromJson<Data>(PlayerPrefs.GetString(Key, "")); } catch { data = null; }
            data ??= new Data();
            if (data.killsByType == null || data.killsByType.Length < 16) data.killsByType = new int[16];
            data.unlocked ??= new List<string>();
        }

        private static void Save()
        {
            if (TestMode) return;   // las pruebas no tocan la hoja del jugador
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        /// <summary>Pruebas automáticas: hoja en memoria, sin guardar.</summary>
        public static bool TestMode;
        public static void ResetForTest() { TestMode = true; data = new Data(); }

        // ------------------------------------------------------------
        //  LOGROS
        // ------------------------------------------------------------
        public class Achievement
        {
            public string id, name, desc;
            public Func<Data, int> progress;   // valor actual
            public int goal;
            public RatLook.HatType? hat;       // sombrero que desbloquea
        }

        public static readonly Achievement[] All =
        {
            new() { id = "first_blood", name = "Primera sangre", desc = "Revienta a tu primera rata enemiga.", progress = d => d.kills, goal = 1 },
            new() { id = "butcher", name = "Carnicero", desc = "100 bajas enemigas.", progress = d => d.kills, goal = 100, hat = RatLook.HatType.Vikingo },
            new() { id = "veteran", name = "Veterano", desc = "Juega 25 partidas.", progress = d => d.matches, goal = 25, hat = RatLook.HatType.Chistera },
            new() { id = "winner", name = "Primera victoria", desc = "Gana una partida.", progress = d => d.wins, goal = 1 },
            new() { id = "champion", name = "Campeón", desc = "Gana 10 partidas.", progress = d => d.wins, goal = 10, hat = RatLook.HatType.Corona },
            new() { id = "flawless", name = "Sin un rasguño", desc = "Gana sin perder ninguna rata.", progress = d => d.flawless, goal = 1 },
            new() { id = "drown", name = "Al agua, patos", desc = "Ahoga a 10 ratas enemigas.", progress = d => d.killsByType[(int)DamageType.Water], goal = 10 },
            new() { id = "fire", name = "Pirómano", desc = "15 bajas con fuego.", progress = d => d.killsByType[(int)DamageType.Fire], goal = 15 },
            new() { id = "melee", name = "Cuerpo a cuerpo", desc = "10 bajas a golpes.", progress = d => d.killsByType[(int)DamageType.Melee], goal = 10, hat = RatLook.HatType.Bicornio },
            new() { id = "sniper", name = "Ojo de halcón", desc = "10 bajas a balazos.", progress = d => d.killsByType[(int)DamageType.Bullet], goal = 10, hat = RatLook.HatType.Vaquero },
            new() { id = "boom", name = "Dinamitero", desc = "50 bajas con explosiones.", progress = d => d.killsByType[(int)DamageType.Explosion], goal = 50 },
            new() { id = "friendly", name = "Fuego amigo", desc = "Revienta a una rata de tu propio equipo.", progress = d => d.ownKills, goal = 1 },
            new() { id = "bighit", name = "¡Menudo golpazo!", desc = "Haz 80 de daño de un solo golpe.", progress = d => d.bestHit, goal = 80 },
            new() { id = "multikill", name = "Masacre", desc = "3 bajas enemigas en un mismo turno.", progress = d => d.bestTurnKills, goal = 3, hat = RatLook.HatType.Fez },
            new() { id = "damage", name = "Demoledor", desc = "5000 de daño en total.", progress = d => d.damage, goal = 5000 },
            new() { id = "taunt", name = "Bocazas", desc = "Provoca 50 veces (tecla P).", progress = d => d.taunts, goal = 50 },
            new() { id = "explorer", name = "Explorador", desc = "Juega en 6 formas de mapa distintas.", progress = d => Bits(d.shapesPlayed), goal = 6, hat = RatLook.HatType.Minero },
            new() { id = "modes", name = "Todoterreno", desc = "Gana en 4 modos de juego distintos.", progress = d => Bits(d.modesWon), goal = 4 },
            new() { id = "gif", name = "Director de cine", desc = "Guarda una repetición en GIF (G).", progress = d => d.gifs, goal = 1 },
            new() { id = "survivor", name = "Carne de cañón", desc = "Pierde 100 ratas en combate.", progress = d => d.deaths, goal = 100 },
        };

        private static int Bits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

        public static bool IsUnlocked(string id) => Stats.unlocked.Contains(id);

        /// <summary>0.38: logros conseguidos en esta sesión con su hora (para el cartel de victoria).</summary>
        private static readonly List<(Achievement a, float time)> recentUnlocks = new();
        public static List<Achievement> UnlockedSince(float time)
        {
            var list = new List<Achievement>();
            foreach (var r in recentUnlocks) if (r.time >= time) list.Add(r.a);
            return list;
        }

        /// <summary>¿Este sombrero aún no se puede elegir en "Mi equipo"?</summary>
        public static bool IsHatLocked(RatLook.HatType hat)
        {
            foreach (var a in All) if (a.hat == hat) return !IsUnlocked(a.id);
            return false;
        }

        /// <summary>El logro que desbloquea un sombrero (o null).</summary>
        public static Achievement HatAchievement(RatLook.HatType hat)
        {
            foreach (var a in All) if (a.hat == hat) return a;
            return null;
        }

        /// <summary>Comprueba logros nuevos y avisa.</summary>
        private static void CheckAchievements()
        {
            Load();
            bool changed = false;
            foreach (var a in All)
            {
                if (data.unlocked.Contains(a.id) || a.progress(data) < a.goal) continue;
                data.unlocked.Add(a.id);
                recentUnlocks.Add((a, Time.unscaledTime));
                changed = true;
                CareerToasts.Show(a);
                Debug.Log($"[Carrera] ¡Logro! {a.name}");
            }
            if (changed) Save();
        }

        // ------------------------------------------------------------
        //  ¿ES MÍA ESTA RATA?
        // ------------------------------------------------------------
        public static bool IsMine(NetworkPlayerMovement rat)
        {
            if (rat == null || rat.IsTestDummy) return false;
            switch (MatchRules.Mode)
            {
                case MatchMode.Practice: return false;
                case MatchMode.Local: return rat.TeamIndex.Value == 0;
                default: return rat.IsOwner;
            }
        }

        // ------------------------------------------------------------
        //  EVENTOS (todos los PCs; cada uno apunta lo suyo)
        // ------------------------------------------------------------
        private static ulong turnKey = ulong.MaxValue;
        private static int turnKills;

        /// <summary>Una rata ha muerto. <paramref name="killer"/> = la rata del turno (puede ser null).</summary>
        public static void OnRatDied(NetworkPlayerMovement victim, NetworkPlayerMovement killer, DamageType type)
        {
            if (victim == null || victim.IsTestDummy) return;
            Load();
            bool changed = false;
            if (IsMine(victim)) { data.deaths++; changed = true; }
            if (IsMine(killer) && killer != victim)
            {
                if (killer.TeamIndex.Value == victim.TeamIndex.Value) data.ownKills++;
                else
                {
                    data.kills++;
                    int t = Mathf.Clamp((int)type, 0, data.killsByType.Length - 1);
                    data.killsByType[t]++;
                    var tm = NetworkTurnManager.Instance;
                    ulong key = tm != null ? tm.CurrentRatId.Value : 0;
                    if (key != turnKey) { turnKey = key; turnKills = 0; }
                    turnKills++;
                    data.bestTurnKills = Mathf.Max(data.bestTurnKills, turnKills);
                }
                changed = true;
            }
            else if (IsMine(killer) && killer == victim) { data.ownKills++; changed = true; }
            if (changed) { Save(); CheckAchievements(); }
        }

        /// <summary>Un golpe (popup de daño) de la rata del turno.</summary>
        public static void OnHit(NetworkPlayerMovement attacker, NetworkPlayerMovement victim, int amount)
        {
            if (!IsMine(attacker) || victim == null || victim.IsTestDummy || attacker == victim) return;
            if (attacker.TeamIndex.Value == victim.TeamIndex.Value) return;
            // El golpe pedido puede ser mayor que la vida (999 del agua, kamikaze...):
            // cuenta como mucho la vida máxima de la víctima.
            var vh = victim.GetComponent<NetworkHealth>();
            if (vh != null) amount = Mathf.Min(amount, vh.MaxHealth);
            Load();
            data.damage += amount;
            data.bestHit = Mathf.Max(data.bestHit, amount);
            Save();
            CheckAchievements();
        }

        public static void OnTaunt() { if (MatchRules.Mode == MatchMode.Practice) return; Load(); data.taunts++; Save(); CheckAchievements(); }
        public static void OnGifSaved() { Load(); data.gifs++; Save(); CheckAchievements(); }

        // Fin de partida: hace falta el ganador (GameEnded) y las cifras (StatsClientRpc).
        private static MatchStats processedStats;

        /// <summary>(Todos los PCs) La partida ha acabado. Se apunta una vez por partida.</summary>
        public static void OnMatchEnded(int winnerTeam)
        {
            if (MatchRules.Mode == MatchMode.Practice) return;
            var stats = MatchStats.Last;
            if (stats == null || stats == processedStats) return;
            processedStats = stats;
            Load();
            // Mis equipos
            var mine = new HashSet<int>();
            if (MatchRules.Mode == MatchMode.Local) mine.Add(0);
            else
                foreach (var m in UnityEngine.Object.FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.None))
                    if (m != null && m.IsSpawned && m.IsOwner && !m.IsTestDummy && m.TeamIndex.Value >= 0) mine.Add(m.TeamIndex.Value);
            data.matches++;
            data.shapesPlayed |= 1 << (int)Ratms.World.TerrainGenerator.MatchShape;
            if (winnerTeam >= 0 && mine.Contains(winnerTeam))
            {
                data.wins++;
                var tm = NetworkTurnManager.Instance;
                if (tm != null) data.modesWon |= 1 << (int)tm.CurrentGame;
                foreach (var row in stats.teams)
                    if (row.team == winnerTeam && row.taken == 0) data.flawless++;
            }
            Save();
            CheckAchievements();
            Debug.Log($"[Carrera] Partida apuntada: {data.matches} partidas, {data.wins} victorias, {data.kills} bajas.");
        }
    }

    /// <summary>
    ///  Carteles de "¡LOGRO DESBLOQUEADO!" (arriba, 4 s cada uno, en cola).
    ///  Objeto persistente que se crea solo.
    /// </summary>
    public class CareerToasts : MonoBehaviour
    {
        private static CareerToasts instance;
        private readonly Queue<Career.Achievement> queue = new();
        private Career.Achievement current;
        private float shownAt;

        public static void Show(Career.Achievement a)
        {
            if (instance == null)
            {
                var go = new GameObject("CartelesDeLogros");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<CareerToasts>();
            }
            instance.queue.Enqueue(a);
        }

        private void Update()
        {
            // 4 s cada cartel; si hay más esperando, 2,5 s.
            if (current != null && Time.unscaledTime - shownAt > (queue.Count > 0 ? 2.5f : 4f)) current = null;
            if (current == null && queue.Count > 0)
            {
                current = queue.Dequeue();
                shownAt = Time.unscaledTime;
                Sfx.Play(Sfx.Id.Victory, 0.5f, 0f);
            }
        }

        private void OnGUI()
        {
            if (current == null) return;
            GUI.depth = -60;
            TrenchGUI.Begin();
            float t = Time.unscaledTime - shownAt;
            float life = queue.Count > 0 ? 2.5f : 4f;
            float slide = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((life - t) / 0.3f);
            var r = new Rect((TrenchGUI.VW - 620f) * 0.5f, -110f + 140f * slide, 620f, 100f);
            TrenchGUI.Panel(r);
            var c = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.3f);
            TrenchGUI.Label(new Rect(r.x + 24f, r.y + 12f, r.width - 48f, 36f), Loc.T("¡LOGRO!") + "  " + Loc.T(current.name));
            GUI.color = c;
            string extra = current.hat.HasValue ? "  ·  " + Loc.F("Desbloqueas: {0}", Loc.T(RatLook.HatName(current.hat.Value))) : "";
            TrenchGUI.Small(new Rect(r.x + 24f, r.y + 54f, r.width - 48f, 30f), Loc.T(current.desc) + extra);
            TrenchGUI.End();
        }
    }
}
