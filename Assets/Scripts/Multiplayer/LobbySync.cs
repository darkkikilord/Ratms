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
        public struct Entry { public ulong id; public string name; public bool ready; }

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
                r.ReadValueSafe(out uint l);
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
                    if (r != lastRulesSent || PlayerNameRegistry.LocalName != lastNameSent) SendHello();
                }
                else if (nm.IsConnectedClient && (!helloSent || PlayerNameRegistry.LocalName != lastNameSent)) SendHello();
            }
        }

        private void Register(NetworkManager nm)
        {
            registered = true;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler("ratms_hello", OnHello);
            cmm.RegisterNamedMessageHandler("ratms_roster", OnRoster);
            if (nm.IsServer)
            {
                nm.OnClientDisconnectCallback -= OnClientLeft;
                nm.OnClientDisconnectCallback += OnClientLeft;
            }
            SendHello();
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
            if (nm.IsServer)
            {
                lastNameSent = PlayerNameRegistry.LocalName;
                server[nm.LocalClientId] = new Entry { id = nm.LocalClientId, name = PlayerNameRegistry.LocalName, ready = true };
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
            nm.CustomMessagingManager.SendNamedMessage("ratms_hello", NetworkManager.ServerClientId, w);
        }

        private void OnHello(ulong sender, FastBufferReader r)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out bool ready);
            profiles[sender] = ReadProfile(r);
            server[sender] = new Entry { id = sender, name = string.IsNullOrWhiteSpace(name) ? "Rata" : name, ready = ready };
            PlayerNameRegistry.SetName(sender, server[sender].name);
            Broadcast();
        }

        private void Broadcast()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            lastRulesSent = Ratms.UI.RulesEditor.Summary();
            // El host también es jugador
            server[nm.LocalClientId] = new Entry { id = nm.LocalClientId, name = PlayerNameRegistry.LocalName, ready = true };
            // Copia local para el host
            roster.Clear();
            foreach (var e in server.Values) roster.Add(e);
            roster.Sort((a, b) => a.id.CompareTo(b.id));
            rules = lastRulesSent;

            using var w = new FastBufferWriter(4096, Allocator.Temp);
            w.WriteValueSafe(rules);
            w.WriteValueSafe(roster.Count);
            foreach (var e in roster) { w.WriteValueSafe(e.id); w.WriteValueSafe(e.name); w.WriteValueSafe(e.ready); }
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
                roster.Add(new Entry { id = id, name = name, ready = ready });
                PlayerNameRegistry.SetName(id, name);
            }
            rules = rulesText;
        }
    }
}
