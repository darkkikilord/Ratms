using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  ESTADÍSTICAS DE LA PARTIDA (0.19) para la pantalla de victoria.
    ///
    ///  El SERVIDOR apunta, por equipo, el daño hecho y recibido, las bajas
    ///  enemigas, las bajas "propias" (suicidios y fuego amigo) y el mejor
    ///  golpe de la partida. El daño se atribuye a la rata que tiene el turno
    ///  (o la que acaba de tenerlo): es quien disparó. Al acabar, se manda todo
    ///  a los clientes con un ClientRpc y queda en <see cref="MatchStats.Last"/>.
    /// </summary>
    public partial class NetworkTurnManager
    {
        private class TeamStats { public int dealt, taken, kills, ownKills; }
        private readonly Dictionary<int, TeamStats> stats = new();
        private int bestHit;
        private string bestHitBy = "";

        private TeamStats StatsOf(int team)
        {
            if (!stats.TryGetValue(team, out var s)) stats[team] = s = new TeamStats();
            return s;
        }

        partial void StatsOnSpawn()
        {
            if (IsServer) NetworkHealth.OnDamagedServer += OnRatDamagedServer;
        }

        partial void StatsOnDespawn() => NetworkHealth.OnDamagedServer -= OnRatDamagedServer;

        partial void StatsOnGameStart()
        {
            stats.Clear();
            bestHit = 0;
            bestHitBy = "";
            MatchStats.Last = null;
        }

        /// <summary>(Servidor) Una rata ha perdido <paramref name="amount"/> de vida.</summary>
        private void OnRatDamagedServer(NetworkHealth victim, int amount)
        {
            if (!gameStarted || victim == null || amount <= 0) return;
            var vm = victim.GetComponent<NetworkPlayerMovement>();
            if (vm == null || vm.IsTestDummy) return;
            StatsOf(vm.TeamIndex.Value).taken += amount;

            var shooter = CurrentRat != null ? CurrentRat : LastTurnRat;
            if (shooter == null || shooter.TeamIndex.Value == vm.TeamIndex.Value) return;   // el daño propio no puntúa
            StatsOf(shooter.TeamIndex.Value).dealt += amount;
            if (amount > bestHit)
            {
                bestHit = amount;
                bestHitBy = shooter.RatName.Value.ToString();
            }
        }

        /// <summary>(Servidor) Apunta una baja. Lo llama <see cref="OnRatDiedServer"/>.</summary>
        private void StatsOnKill(NetworkPlayerMovement shooter, NetworkPlayerMovement victim)
        {
            if (victim == null || victim.IsTestDummy) return;
            if (shooter == null) return;
            if (shooter == victim || shooter.TeamIndex.Value == victim.TeamIndex.Value)
                StatsOf(shooter.TeamIndex.Value).ownKills++;
            else
                StatsOf(shooter.TeamIndex.Value).kills++;
        }

        /// <summary>(Servidor) Justo antes de anunciar el final: manda las cifras a todos.</summary>
        private void StatsSendServer()
        {
            var idx = new List<int>();
            foreach (var t in teams) idx.Add(t.index);
            int n = idx.Count;
            var dealt = new int[n]; var taken = new int[n]; var kills = new int[n]; var own = new int[n]; var alive = new int[n];
            for (int i = 0; i < n; i++)
            {
                var s = StatsOf(idx[i]);
                dealt[i] = s.dealt; taken[i] = s.taken; kills[i] = s.kills; own[i] = s.ownKills;
                foreach (var r in teams[i].rats)
                    if (r != null && r.TryGetComponent<NetworkHealth>(out var h) && !h.IsDead.Value) alive[i]++;
            }
            StatsClientRpc(idx.ToArray(), dealt, taken, kills, own, alive, bestHit, bestHitBy ?? "", MatchTime.Value);
        }

        [ClientRpc]
        private void StatsClientRpc(int[] team, int[] dealt, int[] taken, int[] kills, int[] ownKills, int[] alive,
                                    int best, string bestBy, float seconds)
        {
            var m = new MatchStats { bestHit = best, bestHitBy = bestBy, seconds = seconds };
            for (int i = 0; i < team.Length; i++)
                m.teams.Add(new MatchStats.Row
                {
                    team = team[i], dealt = dealt[i], taken = taken[i], kills = kills[i], ownKills = ownKills[i], alive = alive[i],
                });
            MatchStats.Last = m;
        }
    }

    /// <summary>Cifras de la última partida (las rellena el servidor al acabar).</summary>
    public class MatchStats
    {
        public class Row { public int team, dealt, taken, kills, ownKills, alive; }
        public readonly List<Row> teams = new();
        public int bestHit;
        public string bestHitBy;
        public float seconds;

        public static MatchStats Last;
    }
}
