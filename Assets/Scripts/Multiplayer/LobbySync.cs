using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  SALA DE ESPERA ONLINE (antes de cargar la partida):
    ///   • Lista de jugadores con su NOMBRE y si están LISTOS.
    ///   • Las REGLAS que ha elegido el host (las ven todos).
    ///
    ///  Va por mensajes con nombre de NGO (CustomMessagingManager), así no
    ///  hace falta ningún objeto de red en la escena del menú:
    ///   cliente → servidor  "ratms_hello"  (nombre, listo)
    ///   servidor → todos    "ratms_roster" (jugadores + resumen de reglas)
    /// </summary>
    public class LobbySync : MonoBehaviour
    {
        public struct Entry { public ulong id; public string name; public bool ready; public sbyte side; }

        /// <summary>
        ///  0.26: BANDO elegido en la sala (-1 = automático: equipo propio;
        ///  0-3 = Rojos/Azules/Verdes/Amarillos). Los del mismo bando comparten equipo.
        /// </summary>
        public static int LocalSide
        {
            get => instance != null ? instance.localSide : -1;
            set { Ensure(); instance.localSide = Mathf.Clamp(value, -1, MatchRules.MaxTeams - 1); instance.SendHello(); }
        }
        private int localSide = -1;

        /// <summary>(Servidor) Bando elegido por un jugador (-1 = automático).</summary>
        public static int SideOf(ulong clientId)
        {
            if (instance == null) return -1;
            foreach (var e in instance.roster) if (e.id == clientId) return e.side;
            return instance.server.TryGetValue(clientId, out var s) ? s.side : -1;
        }

        private static LobbySync instance;
        public static IReadOnlyList<Entry> Roster => instance != null ? instance.roster : (IReadOnlyList<Entry>)System.Array.Empty<Entry>();
        public static string RulesSummary => instance != null ? instance.rules : "";

        /// <summary>¿Estoy listo? (lo cambia el botón del lobby)</summary>
        public static bool LocalReady
        {
            get => instance != null && instance.localReady;
            set { Ensure(); instance.localReady = value; instance.SendHello(); }
        }

        /// <summary>¿Están listos todos los jugadores (el host cuenta como listo)?</summary>
        public static bool AllReady
        {
            get
            {
                if (instance == null) return true;
                foreach (var e in instance.roster)
                    if (!e.ready && e.id != NetworkManager.ServerClientId) return false;
                return true;
            }
        }

        private readonly List<Entry> roster = new();
        private readonly Dictionary<ulong, TeamProfile.Data> profiles = new();

        /// <summary>(Servidor) "Mi equipo" que ha mandado un jugador (nombres y aspectos), o null.</summary>
        public static TeamProfile.Data ProfileOf(ulong clientId)
        {
            if (instance != null && instance.profiles.TryGetValue(clientId, out var p)) return p;
            var nm = NetworkManager.Singleton;
            return nm != null && clientId == nm.LocalClientId ? TeamProfile.Local() : null;
        }

        private static void WriteProfile(FastBufferWriter w, TeamProfile.Data d)
        {
            w.WriteValueSafe(d.team ?? "");
            for (int i = 0; i < TeamProfile.Slots; i++) { w.WriteValueSafe(d.names[i] ?? ""); w.WriteValueSafe(d.looks[i]); }
        }

        private static TeamProfile.Data ReadProfile(FastBufferReader r)
        {
            var d = new TeamProfile.Data();
            r.ReadValueSafe(out string team);
            d.team = team;
            for (int i = 0; i < TeamProfile.Slots; i++)
            {
                r.ReadValueSafe(out string n);
                r.ReadValueSafe(out ulong l);
                d.names[i] = n; d.looks[i] = l;
            }
            return d;
        }
        private readonly Dictionary<ulong, Entry> server = new();
        private string rules = "";
        private bool localReady;
        private bool registered, helloSent;
        private string lastRulesSent, lastNameSent;
        private float resendAt;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("[LobbySync]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LobbySync>();
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool listening = nm != null && nm.IsListening && nm.CustomMessagingManager != null;
            if (!listening)
            {
                if (registered) { registered = false; helloSent = false; roster.Clear(); server.Clear(); localReady = false; profiles.Clear(); }
                return;
            }
            if (!registered) Register(nm);

            // El host re-envía si cambia el nombre o las reglas; el cliente, su nombre.
            if (Time.unscaledTime >= resendAt)
            {
                resendAt = Time.unscaledTime + 0.5f;
                if (nm.IsServer)
                {
                    string r = Ratms.UI.RulesEditor.Summary();
                    if (r != lastRulesSent || PlayerNameRegistry.LocalName != lastNameSent || ProfileSig() != lastProfileSent) SendHello();
                }
                else if (nm.IsConnectedClient && (!helloSent || PlayerNameRegistry.LocalName != lastNameSent || ProfileSig() != lastProfileSent)) SendHello();
            }
        }

        private void Register(NetworkManager nm)
        {
            registered = true;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler("ratms_hello", OnHello);
            cmm.RegisterNamedMessageHandler("ratms_roster", OnRoster);
            cmm.RegisterNamedMessageHandler("ratms_bye", (sender, _) => NetworkTurnManager.Leavers.Add(sender));
            cmm.RegisterNamedMessageHandler("ratms_hostbye", (_, _) => ConnectionLostOverlay.HostClosed = true);
            if (nm.IsServer)
            {
                nm.OnClientDisconnectCallback -= OnClientLeft;
                nm.OnClientDisconnectCallback += OnClientLeft;
            }
            SendHello();
        }

        // 0.22.1: si cambias "Mi equipo" estando ya en la sala, se vuelve a mandar.
        private string lastProfileSent;
        private static string ProfileSig()
        {
            var d = TeamProfile.Local();
            var sb = new System.Text.StringBuilder(d.team ?? "");
            for (int i = 0; i < TeamProfile.Slots; i++) sb.Append('|').Append(d.names[i]).Append(':').Append(d.looks[i]);
            return sb.ToString();
        }

        /// <summary>
        ///  0.23: el cliente avisa de que se va A PROPÓSITO (antes de Shutdown),
        ///  para que el host no le guarde el sitio para reconectar.
        /// </summary>
        public static void SayGoodbye()
        {
            ConnectionLostOverlay.LeftOnPurpose = true;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)1);
            if (nm.IsServer)
            {
                // El host se va: a los clientes no les ofrecemos "volver".
                foreach (var id in nm.ConnectedClientsIds)
                    if (id != nm.LocalClientId)
                        nm.CustomMessagingManager.SendNamedMessage("ratms_hostbye", id, w, NetworkDelivery.Reliable);
                return;
            }
            if (!nm.IsConnectedClient) return;
            nm.CustomMessagingManager.SendNamedMessage("ratms_bye", NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        private void OnClientLeft(ulong id)
        {
            if (server.Remove(id)) Broadcast();
        }

        /// <summary>Manda mi nombre y si estoy listo (al host, o lo aplica directamente si SOY el host).</summary>
        private void SendHello()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !registered) return;
            lastProfileSent = ProfileSig();
            if (nm.IsServer)
            {
                lastNameSent = PlayerNameRegistry.LocalName;
                server[nm.LocalClientId] = new Entry { id = nm.LocalClientId, name = PlayerNameRegistry.LocalName, ready = true, side = (sbyte)localSide };
                profiles[nm.LocalClientId] = TeamProfile.Local();
                Broadcast();
                return;
            }
            if (!nm.IsConnectedClient) return;
            lastNameSent = PlayerNameRegistry.LocalName;
            helloSent = true;
            using var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe(PlayerNameRegistry.LocalName ?? "Rata");
            w.WriteValueSafe(localReady);
            WriteProfile(w, TeamProfile.Local());
            w.WriteValueSafe((sbyte)localSide);
            nm.CustomMessagingManager.SendNamedMessage("ratms_hello", NetworkManager.ServerClientId, w);
        }

        private void OnHello(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out bool ready);
            profiles[sender] = ReadProfile(r);
            sbyte side = -1;
            if (r.TryBeginRead(1)) r.ReadValueSafe(out side);
            server[sender] = new Entry { id = sender, name = string.IsNullOrWhiteSpace(name) ? "Rata" : name, ready = ready, side = side };
            PlayerNameRegistry.SetName(sender, server[sender].name);
            Broadcast();
        }

        private void Broadcast()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            lastRulesSent = Ratms.UI.RulesEditor.Summary();
            // El host también es jugador
            server[nm.LocalClientId] = new Entry { id = nm.LocalClientId, name = PlayerNameRegistry.LocalName, ready = true, side = (sbyte)localSide };
            // Copia local para el host
            roster.Clear();
            foreach (var e in server.Values) roster.Add(e);
            roster.Sort((a, b) => a.id.CompareTo(b.id));
            rules = lastRulesSent;

            using var w = new FastBufferWriter(4096, Allocator.Temp);
            w.WriteValueSafe(rules);
            w.WriteValueSafe(roster.Count);
            foreach (var e in roster) { w.WriteValueSafe(e.id); w.WriteValueSafe(e.name); w.WriteValueSafe(e.ready); w.WriteValueSafe(e.side); }
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId)
                    nm.CustomMessagingManager.SendNamedMessage("ratms_roster", id, w);
        }

        private void OnRoster(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out string rulesText);
            r.ReadValueSafe(out int n);
            roster.Clear();
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out ulong id);
                r.ReadValueSafe(out string name);
                r.ReadValueSafe(out bool ready);
                r.ReadValueSafe(out sbyte side);
                roster.Add(new Entry { id = id, name = name, ready = ready, side = side });
                PlayerNameRegistry.SetName(id, name);
            }
            rules = rulesText;
        }
    }
}
