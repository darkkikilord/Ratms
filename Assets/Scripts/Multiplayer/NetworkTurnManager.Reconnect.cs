using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  RECONEXIÓN (0.23).
    ///
    ///  Antes, si a un jugador se le caía internet, su equipo quedaba
    ///  eliminado al momento. Ahora (solo online):
    ///   • Al desconectarse, su equipo queda AUSENTE <see cref="ReconnectGrace"/> s:
    ///     sus ratas siguen en el mapa (el prefab no se destruye con el dueño,
    ///     <c>DontDestroyWithOwner</c>), se saltan sus turnos y se avisa a todos.
    ///   • Si alguien entra a la sala durante ese tiempo, recupera ese equipo
    ///     (<see cref="TryReclaimTeamServer"/>, lo llama GameSceneSpawnManager):
    ///     las ratas pasan a ser suyas y vuelve a tener turnos.
    ///   • Si se acaba el tiempo, el equipo queda eliminado como antes.
    /// </summary>
    public partial class NetworkTurnManager
    {
        /// <summary>Segundos para volver tras desconectarse (las pruebas lo acortan).</summary>
        public static float ReconnectGrace = 90f;
        private bool resumeCheck;

        /// <summary>(Servidor) Clientes que avisaron de que se iban a propósito ("ratms_bye").</summary>
        public static readonly System.Collections.Generic.HashSet<ulong> Leavers = new();

        /// <summary>(Servidor) ¿Hay algún equipo esperando a que vuelva su jugador?</summary>
        public bool HasAwayTeamServer => teams.Exists(t => t.awayUntil > 0f && !t.eliminated);

        /// <summary>(Servidor) Al desconectarse: ausente en vez de eliminado. Devuelve false si no aplica.</summary>
        private bool MarkAwayServer(ulong clientId)
        {
            if (Leavers.Remove(clientId)) return false;   // se fue a propósito: eliminado ya
            if (MatchRules.Mode != MatchMode.Online || GameEnded.Value || ReconnectGrace <= 0f) return false;
            bool any = false;
            foreach (var t in teams)
            {
                if (t.owner != clientId || t.eliminated || !TeamAlive(t)) continue;
                t.awayUntil = Time.time + ReconnectGrace;
                any = true;
                AnnounceClientRpc(Ratms.UI.Loc.Pack("¡{0} se ha desconectado!\nTiene {1} s para volver",
                    TeamDisplayName(t.index, translate: false), Mathf.RoundToInt(ReconnectGrace)), new Color(1f, 0.75f, 0.3f));
            }
            return any;
        }

        /// <summary>(Servidor) Un jugador que entra a mitad de partida recupera un equipo ausente.</summary>
        public bool TryReclaimTeamServer(ulong clientId)
        {
            if (!IsServer || GameEnded.Value) return false;
            var t = teams.Find(x => x.awayUntil > 0f && !x.eliminated);
            if (t == null) return false;
            // El nombre ANTES de cambiar de dueño (el del nuevo id aún no ha llegado).
            string teamName = TeamDisplayName(t.index, translate: false);
            string oldName = PlayerNameRegistry.GetName(t.owner);
            if (!oldName.StartsWith("Rata #")) PlayerNameRegistry.SetName(clientId, oldName);
            t.awayUntil = -1f;
            t.owner = clientId;
            t.members.Clear(); t.members.Add(clientId);
            foreach (var rat in t.rats) if (rat != null) t.ratOwner[rat.NetworkObjectId] = clientId;
            resumeCheck = true;
            foreach (var rat in t.rats)
            {
                if (rat == null || !rat.IsSpawned) continue;
                rat.NetworkObject.ChangeOwnership(clientId);
            }
            AnnounceClientRpc(Ratms.UI.Loc.Pack("¡{0} ha vuelto a la trinchera!", teamName),
                new Color(0.55f, 1f, 0.5f));
            return true;
        }

        /// <summary>
        ///  0.26 — (Servidor) Se va un jugador de un equipo COMPARTIDO: sus ratas
        ///  pasan a un compañero que siga en la sala (y el equipo no queda ausente).
        /// </summary>
        private void ShareOutRatsServer(ulong clientId)
        {
            foreach (var t in teams)
            {
                if (t.eliminated || !t.members.Contains(clientId)) continue;
                t.members.Remove(clientId);
                if (t.members.Count == 0) { t.owner = clientId; continue; }   // era el último: lo decide MarkAwayServer
                ulong heir = ulong.MaxValue;
                foreach (var m in t.members) { heir = m; break; }
                bool turnWasTheirs = false;
                foreach (var rat in t.rats)
                {
                    if (rat == null || !rat.IsSpawned) continue;
                    if (!t.ratOwner.TryGetValue(rat.NetworkObjectId, out var o) || o != clientId) continue;
                    t.ratOwner[rat.NetworkObjectId] = heir;
                    rat.NetworkObject.ChangeOwnership(heir);
                    if (CurrentRatId.Value == rat.NetworkObjectId) turnWasTheirs = true;
                }
                if (t.owner == clientId) t.owner = heir;
                AnnounceClientRpc(Ratms.UI.Loc.Pack("¡{0} se ha ido!\nSus ratas pasan a {1}", PlayerNameRegistry.GetName(clientId), PlayerNameRegistry.GetName(heir)),
                    new Color(1f, 0.75f, 0.3f));
                if (turnWasTheirs && !GameEnded.Value) AdvanceTurnServer();
            }
        }

        /// <summary>(Servidor, cada frame) Equipos ausentes a los que se les acabó el tiempo.</summary>
        private void AwayUpdateServer()
        {
            bool changed = false;
            foreach (var t in teams)
            {
                if (t.awayUntil <= 0f || t.eliminated || Time.time < t.awayUntil) continue;
                t.awayUntil = -1f;
                EliminateTeam(t);
                changed = true;
            }
            if (changed) CheckVictoryServer();
            // Si todos los que podían jugar estaban ausentes, nadie tenía turno:
            // en cuanto alguien vuelve (o se elimina al ausente), se reanuda.
            if ((changed || HasAwayTeamServer || resumeCheck) && !GameEnded.Value && !InTransition.Value
                && CurrentRatId.Value == ulong.MaxValue && TurnTimeRemaining.Value <= 0f)
                AdvanceTurnServer();
            resumeCheck = false;
        }
    }
}
