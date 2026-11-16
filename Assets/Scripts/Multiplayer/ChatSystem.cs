using System.Collections.Generic;
using System.Linq;
using Ratms.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ratms.Networking
{
    /// <summary>
    ///  0.37 — CHAT y EMOTES.
    ///
    ///  CHAT (solo online; en la sala de espera y en partida): T abre la caja,
    ///  ENTER envía, ESC cancela. Al llegar el mensaje, la rata de quien lo
    ///  escribió lo PRONUNCIA en ratuno con su voz (<see cref="RatVoice.SayText"/>)
    ///  y sale en un bocadillo encima de ella. Mientras alguien escribe, los
    ///  demás ven "…" sobre su rata y "X está escribiendo…" en el chat.
    ///
    ///  EMOTES (online y en local): E abre el menú, 1-8 o clic eligen. La rata
    ///  hace el gesto con animación, partículas (<see cref="EmoteFx"/>), bocadillo
    ///  y voz. Cualquiera puede hacerlos aunque no sea su turno.
    ///
    ///  Red: mensajes con nombre de Netcode (como <see cref="LobbySync"/>), así
    ///  funcionan igual en la sala que en la partida. El cliente manda al
    ///  anfitrión; el anfitrión comprueba (la rata es suya, longitud, no más de
    ///  un mensaje cada 0,6 s) y lo reenvía a todos.
    /// </summary>
    public class ChatSystem : MonoBehaviour
    {
        public const int MaxLength = 100;

        private static ChatSystem instance;
        /// <summary>¿Se está escribiendo o está abierto el menú de emotes? (la pausa no se abre con ESC)</summary>
        public static bool Busy => instance != null && (instance.typing || instance.emoteMenu);
        public static bool Typing => instance != null && instance.typing;

        private struct Line { public string name, text; public Color color; public float time; }
        private readonly List<Line> lines = new();
        private readonly Dictionary<ulong, (float until, ulong rat, string name)> typers = new();
        private readonly Dictionary<ulong, float> lastFrom = new();   // servidor: anti-spam

        private bool registered, typing, emoteMenu, focusField;
        private string draft = "";
        private float typingSentAt = -10f;
        private float nextDots;

        // ------------------------------------------------------------
        //  EMOTES
        // ------------------------------------------------------------
        public struct Emote
        {
            public string name, bubble, voice;   // voice: lo que "dice" en ratuno (null = risa)
            public EmoteFx.Kind fx;
            public RatAppearance.EmoteAnim anim;
        }

        public static readonly Emote[] Emotes =
        {
            new() { name = "¡Toma ya!", bubble = "¡TOMA YA!", voice = "¡Toma ya!", fx = EmoteFx.Kind.Stars, anim = RatAppearance.EmoteAnim.Cheer },
            new() { name = "Risa", bubble = "¡JA, JA, JA!", voice = null, fx = EmoteFx.Kind.Sparks, anim = RatAppearance.EmoteAnim.Laugh },
            new() { name = "Baile", bubble = "¡A bailar!", voice = "¡La la la, la la!", fx = EmoteFx.Kind.Notes, anim = RatAppearance.EmoteAnim.Dance },
            new() { name = "Saludo", bubble = "¡Eh, hola!", voice = "¡Eh, hola!", fx = EmoteFx.Kind.Sparks, anim = RatAppearance.EmoteAnim.Wave },
            new() { name = "Llorar", bubble = "Snif… snif…", voice = "snif… snif… ay…", fx = EmoteFx.Kind.Tears, anim = RatAppearance.EmoteAnim.Cry },
            new() { name = "Enfado", bubble = "¡GRRRR!", voice = "¡GRAAAAH!", fx = EmoteFx.Kind.Steam, anim = RatAppearance.EmoteAnim.Angry },
            new() { name = "Amor", bubble = "¡Te quiero, rata!", voice = "¡Te quiero, rata!", fx = EmoteFx.Kind.Hearts, anim = RatAppearance.EmoteAnim.Cheer },
            new() { name = "Pedorreta", bubble = "¡PRRRRT!", voice = null, fx = EmoteFx.Kind.Gas, anim = RatAppearance.EmoteAnim.Fart },
            // 0.39: memes
            new() { name = "Dab", bubble = "¡DAB!", voice = "¡Dab!", fx = EmoteFx.Kind.Dab, anim = RatAppearance.EmoteAnim.Dab },
            new() { name = "Manita chula", bubble = "Chao, chao~", voice = "chao, chao…", fx = EmoteFx.Kind.Wave, anim = RatAppearance.EmoteAnim.Smug },
            new() { name = "Madre mía", bubble = "Madre mía…", voice = "madre mía…", fx = EmoteFx.Kind.Palm, anim = RatAppearance.EmoteAnim.Facepalm },
            new() { name = "Gorra MRGA", bubble = "¡Make Ratms Great Again!", voice = "¡Make Ratms great again!", fx = EmoteFx.Kind.Cap, anim = RatAppearance.EmoteAnim.Cap },
        };

        /// <summary>Teclas del menú de emotes: 1-9, 0, - y =.</summary>
        private static KeyCode EmoteKey(int i) => i < 9 ? KeyCode.Alpha1 + i : i == 9 ? KeyCode.Alpha0 : i == 10 ? KeyCode.Minus : KeyCode.Equals;
        private static string EmoteKeyLabel(int i) => i < 9 ? (i + 1).ToString() : i == 9 ? "0" : i == 10 ? "-" : "=";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("[Chat]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ChatSystem>();
        }

        // ------------------------------------------------------------
        //  ¿DÓNDE ESTAMOS?
        // ------------------------------------------------------------
        private static NetworkManager NM => NetworkManager.Singleton;
        private static bool Listening => NM != null && NM.IsListening && NM.CustomMessagingManager != null;
        private static bool InGame => NetworkTurnManager.Instance != null && NetworkTurnManager.Instance.IsSpawned;
        private static bool InLobby => MultiplayerMenuController.Instance != null && MultiplayerMenuController.Instance.LobbyVisible;
        /// <summary>El chat solo tiene sentido online (en local estáis en el mismo PC).</summary>
        private static bool ChatAllowed => Listening && MatchRules.Mode == MatchMode.Online && (InGame || InLobby);

        /// <summary>La rata que "habla" por mí: la del turno si es mía; si no, la primera viva mía.</summary>
        public static NetworkPlayerMovement MyRat()
        {
            var tm = NetworkTurnManager.Instance;
            if (tm == null || !tm.IsSpawned) return null;
            var cur = tm.CurrentRat;
            if (cur != null && cur.IsOwner && !cur.IsTestDummy) return cur;
            if (MatchRules.Mode != MatchMode.Online) return null;   // en local solo la del turno
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsSortMode.InstanceID))
            {
                if (m == null || !m.IsSpawned || !m.IsOwner || m.IsTestDummy) continue;
                var h = m.GetComponent<NetworkHealth>();
                if (h != null && !h.IsDead.Value) return m;
            }
            return null;
        }

        private static NetworkPlayerMovement RatById(ulong id)
        {
            var nm = NM;
            if (nm == null || nm.SpawnManager == null || id == ulong.MaxValue) return null;
            return nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) ? no.GetComponent<NetworkPlayerMovement>() : null;
        }

        private static ulong MyLobbyLook()
        {
            var p = TeamProfile.Local();
            return p != null && p.looks != null && p.looks.Length > 0 && p.looks[0] != 0 ? p.looks[0]
                 : (ulong)(PlayerNameRegistry.LocalName ?? "rata").GetHashCode() | 1UL;
        }

        // ------------------------------------------------------------
        //  RED
        // ------------------------------------------------------------
        private void Register(NetworkManager nm)
        {
            registered = true;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler("ratms_chat", OnChatToServer);
            cmm.RegisterNamedMessageHandler("ratms_chat_all", (_, r) => ReadAndShowChat(r));
            cmm.RegisterNamedMessageHandler("ratms_typing", OnTypingToServer);
            cmm.RegisterNamedMessageHandler("ratms_typing_all", (_, r) => ReadTyping(r));
            cmm.RegisterNamedMessageHandler("ratms_emote", OnEmoteToServer);
            cmm.RegisterNamedMessageHandler("ratms_emote_all", (_, r) => { r.ReadValueSafe(out ulong rat); r.ReadValueSafe(out byte e); r.ReadValueSafe(out int seed); PlayEmote(rat, e, seed); });
        }

        private static void SendToServer(string msg, FastBufferWriter w)
        {
            var nm = NM;
            if (nm.IsServer) return;
            nm.CustomMessagingManager.SendNamedMessage(msg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }

        private static void SendToOthers(string msg, FastBufferWriter w)
        {
            var nm = NM;
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId)
                    nm.CustomMessagingManager.SendNamedMessage(msg, id, w, NetworkDelivery.ReliableSequenced);
        }

        private bool RateOk(ulong sender, float gap)
        {
            if (lastFrom.TryGetValue(sender, out float t) && Time.unscaledTime - t < gap) return false;
            lastFrom[sender] = Time.unscaledTime;
            return true;
        }

        /// <summary>¿Esta rata es de quien dice que la usa? (en la sala no hay ratas: vale)</summary>
        private static bool OwnsRat(ulong sender, ulong ratId)
        {
            if (ratId == ulong.MaxValue) return true;
            var rat = RatById(ratId);
            return rat != null && rat.OwnerClientId == sender;
        }

        // --- Chat ---
        public static void SendChat(string text)
        {
            if (instance == null || !Listening) return;
            text = Clean(text);
            if (text.Length == 0) return;
            var rat = MyRat();
            ulong ratId = rat != null ? rat.NetworkObjectId : ulong.MaxValue;
            ulong look = rat != null ? rat.LookCode.Value : MyLobbyLook();
            string name = PlayerNameRegistry.LocalName ?? "Rata";
            using var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe(text); w.WriteValueSafe(ratId); w.WriteValueSafe(look); w.WriteValueSafe(name);
            if (NM.IsServer) instance.ServerChat(NM.LocalClientId, text, ratId, look, name);
            else SendToServer("ratms_chat", w);
        }

        private void OnChatToServer(ulong sender, FastBufferReader r)
        {
            if (!NM.IsServer) return;
            r.ReadValueSafe(out string text); r.ReadValueSafe(out ulong ratId); r.ReadValueSafe(out ulong look); r.ReadValueSafe(out string name);
            ServerChat(sender, text, ratId, look, name);
        }

        private void ServerChat(ulong sender, string text, ulong ratId, ulong look, string name)
        {
            text = Clean(text);
            if (text.Length == 0 || !RateOk(sender, 0.6f)) return;
            if (!OwnsRat(sender, ratId)) ratId = ulong.MaxValue;
            if (string.IsNullOrWhiteSpace(name)) name = "Rata";
            if (name.Length > 24) name = name.Substring(0, 24);
            using var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe(sender); w.WriteValueSafe(name); w.WriteValueSafe(text); w.WriteValueSafe(ratId); w.WriteValueSafe(look);
            SendToOthers("ratms_chat_all", w);
            ShowChat(sender, name, text, ratId, look);
        }

        private void ReadAndShowChat(FastBufferReader r)
        {
            r.ReadValueSafe(out ulong sender); r.ReadValueSafe(out string name); r.ReadValueSafe(out string text);
            r.ReadValueSafe(out ulong ratId); r.ReadValueSafe(out ulong look);
            ShowChat(sender, name, text, ratId, look);
        }

        /// <summary>(Todos los PCs) Apunta el mensaje, bocadillo y la rata lo dice en ratuno.</summary>
        private void ShowChat(ulong sender, string name, string text, ulong ratId, ulong look)
        {
            typers.Remove(sender);
            var rat = RatById(ratId);
            Color c = rat != null ? TurnHUD.TeamTextColor(rat.TeamIndex.Value) : new Color(0.95f, 0.9f, 0.75f);
            lines.Add(new Line { name = name, text = text, color = c, time = Time.unscaledTime });
            if (lines.Count > 30) lines.RemoveAt(0);
            if (rat != null) SpeechBubbles.Say(rat.transform, text, Mathf.Clamp(1.8f + text.Length * 0.06f, 2.5f, 6f));
            RatVoice.SayText(rat, look, text);
            Sfx.Play(Sfx.Id.Tick, 0.25f, 0f);
            Received++;
        }

        /// <summary>Para las pruebas: mensajes recibidos.</summary>
        public static int Received;

        private static string Clean(string t)
        {
            if (t == null) return "";
            t = t.Replace("\n", " ").Replace("\r", " ").Trim();
            return t.Length > MaxLength ? t.Substring(0, MaxLength) : t;
        }

        // --- "Está escribiendo…" ---
        private void SendTyping(bool on)
        {
            if (!Listening) return;
            var rat = MyRat();
            ulong ratId = rat != null ? rat.NetworkObjectId : ulong.MaxValue;
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(on); w.WriteValueSafe(ratId); w.WriteValueSafe(PlayerNameRegistry.LocalName ?? "Rata");
            if (NM.IsServer) ServerTyping(NM.LocalClientId, on, ratId, PlayerNameRegistry.LocalName ?? "Rata");
            else SendToServer("ratms_typing", w);
        }

        private void OnTypingToServer(ulong sender, FastBufferReader r)
        {
            if (!NM.IsServer) return;
            r.ReadValueSafe(out bool on); r.ReadValueSafe(out ulong ratId); r.ReadValueSafe(out string name);
            ServerTyping(sender, on, ratId, name);
        }

        private void ServerTyping(ulong sender, bool on, ulong ratId, string name)
        {
            if (!OwnsRat(sender, ratId)) ratId = ulong.MaxValue;
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(sender); w.WriteValueSafe(on); w.WriteValueSafe(ratId); w.WriteValueSafe(name ?? "Rata");
            SendToOthers("ratms_typing_all", w);
            if (sender != NM.LocalClientId) SetTyper(sender, on, ratId, name);
        }

        private void ReadTyping(FastBufferReader r)
        {
            r.ReadValueSafe(out ulong sender); r.ReadValueSafe(out bool on); r.ReadValueSafe(out ulong ratId); r.ReadValueSafe(out string name);
            if (NM != null && sender == NM.LocalClientId) return;
            SetTyper(sender, on, ratId, name);
        }

        private void SetTyper(ulong sender, bool on, ulong ratId, string name)
        {
            if (on) typers[sender] = (Time.unscaledTime + 4f, ratId, name);
            else typers.Remove(sender);
        }

        // --- Emotes ---
        public static void SendEmote(int e)
        {
            if (instance == null || e < 0 || e >= Emotes.Length) return;
            var rat = MyRat();
            var nm = NM;
            if (rat == null || nm == null || !nm.IsListening) return;
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(rat.NetworkObjectId); w.WriteValueSafe((byte)e);
            if (nm.IsServer) instance.ServerEmote(nm.LocalClientId, rat.NetworkObjectId, (byte)e);
            else SendToServer("ratms_emote", w);
        }

        private void OnEmoteToServer(ulong sender, FastBufferReader r)
        {
            if (!NM.IsServer) return;
            r.ReadValueSafe(out ulong ratId); r.ReadValueSafe(out byte e);
            ServerEmote(sender, ratId, e);
        }

        private void ServerEmote(ulong sender, ulong ratId, byte e)
        {
            if (e >= Emotes.Length || !OwnsRat(sender, ratId) || RatById(ratId) == null) return;
            if (!RateOk(sender + 1000UL, 1.2f)) return;   // clave distinta del chat
            int seed = Random.Range(0, 100000);   // la misma canción/variante en todos los PCs
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(ratId); w.WriteValueSafe(e); w.WriteValueSafe(seed);
            SendToOthers("ratms_emote_all", w);
            PlayEmote(ratId, e, seed);
        }

        /// <summary>Para las pruebas: emotes vistos.</summary>
        public static int EmotesPlayed;

        /// <summary>(Todos los PCs) La rata hace el gesto.</summary>
        private static void PlayEmote(ulong ratId, byte e, int seed)
        {
            var rat = RatById(ratId);
            if (rat == null || e >= Emotes.Length) return;
            var em = Emotes[e];
            var look = rat.GetComponent<RatAppearance>();
            if (look != null) look.PlayEmote(em.anim);
            EmoteFx.Play(rat, em.fx);
            SpeechBubbles.Say(rat.transform, Loc.T(em.bubble), 2.4f);
            if (em.anim == RatAppearance.EmoteAnim.Dance)
            {
                // 0.39: ¡canción! (electrónica, pop, k-pop, rap o reggaetón, al azar)
                DanceTunes.Play(seed);
                string[] names = { "(electrónica)", "(pop)", "(k-pop)", "(rap)", "(reggaetón)" };
                SpeechBubbles.Say(rat.transform, Loc.T(em.bubble) + "  " + names[(int)DanceTunes.StyleOf(seed)], DanceTunes.Seconds);
            }
            else if (em.voice != null) RatVoice.SayText(rat, 0, Loc.T(em.voice));
            else if (em.fx == EmoteFx.Kind.Gas)
            {
                Sfx.PlayPitched(Sfx.Id.Splat, 0.7f, 0.45f);   // ¡prrrt!
                instance.StartCoroutine(instance.LaughCo(rat));   // y luego se ríe
            }
            else RatVoice.Say(rat, RatVoice.Kind.Laugh);
            EmotesPlayed++;
        }

        private System.Collections.IEnumerator LaughCo(NetworkPlayerMovement rat)
        {
            yield return new WaitForSeconds(0.45f);
            if (rat != null) RatVoice.Say(rat, RatVoice.Kind.Laugh, 0.9f);
        }

        // ------------------------------------------------------------
        //  ENTRADA
        // ------------------------------------------------------------
        private void Update()
        {
            var nm = NM;
            bool listening = Listening;
            if (!listening)
            {
                if (registered) { registered = false; lines.Clear(); typers.Clear(); }
                typing = false; emoteMenu = false;
                return;
            }
            if (!registered) Register(nm);
            if (SceneManager.GetActiveScene().name == "MainMenu" && !InLobby) { typing = false; emoteMenu = false; }

            if (typing || emoteMenu) Controls.BlockAllThisFrame();
            if (typing)
            {
                if (!ChatAllowed) { CloseChat(false); return; }
                if (draft.Length > 0 && Time.unscaledTime - typingSentAt > 2f) { typingSentAt = Time.unscaledTime; SendTyping(true); }
                return;
            }
            bool other = PauseMenu.IsOpen || ControlsOverlay.IsOpen || GUIUtility.keyboardControl != 0;
            if (emoteMenu)
            {
                for (int i = 0; i < Emotes.Length; i++)
                    if (Input.GetKeyDown(EmoteKey(i)) || (i < 9 && Input.GetKeyDown(KeyCode.Keypad1 + i))) { emoteMenu = false; SendEmote(i); }
                return;
            }
            if (other) return;
            // Abrir el chat: T (o ENTER en la sala de espera)
            KeyCode c0 = Controls.Key(Controls.Act.Chat, 0), c1 = Controls.Key(Controls.Act.Chat, 1);
            bool chatKey = (c0 != KeyCode.None && Input.GetKeyDown(c0)) || (c1 != KeyCode.None && Input.GetKeyDown(c1))
                           || (InLobby && Input.GetKeyDown(KeyCode.Return));
            if (ChatAllowed && chatKey)
            {
                typing = true; focusField = true; draft = ""; typingSentAt = -10f;
                return;
            }
            // Menú de emotes: E (en partida, si tengo una rata que lo haga)
            if (InGame && !DeathReplay.Playing && (Input.GetKeyDown(Controls.Key(Controls.Act.Emotes, 0)) ||
                (Controls.Key(Controls.Act.Emotes, 1) != KeyCode.None && Input.GetKeyDown(Controls.Key(Controls.Act.Emotes, 1)))) && MyRat() != null)
                emoteMenu = true;

            // "…" sobre las ratas de quien está escribiendo
            if (Time.unscaledTime >= nextDots)
            {
                nextDots = Time.unscaledTime + 0.9f;
                foreach (var kv in typers.ToList())
                {
                    if (Time.unscaledTime > kv.Value.until) { typers.Remove(kv.Key); continue; }
                    var r = RatById(kv.Value.rat);
                    if (r != null) SpeechBubbles.Say(r.transform, "…", 1.1f);
                }
            }
        }

        private void CloseChat(bool send)
        {
            if (send && draft.Trim().Length > 0) SendChat(draft);
            typing = false;
            draft = "";
            GUIUtility.keyboardControl = 0;
            SendTyping(false);
        }

        // ------------------------------------------------------------
        //  DIBUJO
        // ------------------------------------------------------------
        private GUIStyle lineStyle, fieldStyle;

        private void OnGUI()
        {
            if (!Listening) return;
            bool show = ChatAllowed || (emoteMenu && InGame);
            if (!show) return;
            GUI.depth = -5;
            TrenchGUI.Begin();
            if (lineStyle == null)
            {
                lineStyle = new GUIStyle(TrenchGUI.SmallStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false, richText = false };
                fieldStyle = new GUIStyle(TrenchGUI.FieldStyle ?? GUI.skin.textField);
            }
            float vw = TrenchGUI.VW, vh = TrenchGUI.VH;
            if (ChatAllowed) DrawChat(vw, vh);
            if (emoteMenu) DrawEmoteMenu(vw, vh);
            TrenchGUI.End();
        }

        private void DrawChat(float vw, float vh)
        {
            // En partida abajo a la izquierda (encima de las ayudas); en la sala, a la izquierda.
            float w = 640f, lineH = 30f;
            float x = 24f, bottom = InGame ? vh - 120f : vh - 60f;
            const int maxLines = 8;
            float now = Time.unscaledTime;
            var e = Event.current;

            // Caja de texto
            if (typing)
            {
                if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { e.Use(); CloseChat(true); return; }
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); CloseChat(false); return; }
                var box = new Rect(x, bottom - 44f, w, 40f);
                var c0 = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = c0;
                GUI.SetNextControlName("RatmsChat");
                string before = draft;
                draft = GUI.TextField(new Rect(box.x + 8f, box.y + 5f, box.width - 16f, 30f), draft, MaxLength, fieldStyle);
                if (draft.Length > 0 && before.Length == 0) { typingSentAt = Time.unscaledTime; SendTyping(true); }
                if (focusField) { GUI.FocusControl("RatmsChat"); focusField = false; }
                bottom -= 50f;
            }

            // Quién está escribiendo
            var who = typers.Where(t => t.Value.until > now).Select(t => t.Value.name).ToList();
            if (who.Count > 0)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(x + 6f, bottom - lineH, w, lineH), who.Count == 1 ? Loc.F("{0} está escribiendo…", who[0]) : Loc.T("Varios están escribiendo…"), lineStyle);
                GUI.color = c;
                bottom -= lineH;
            }

            // Mensajes (se apagan a los 12 s salvo que estés escribiendo)
            int shown = 0;
            for (int i = lines.Count - 1; i >= 0 && shown < maxLines; i--)
            {
                var l = lines[i];
                float age = now - l.time;
                float a = typing ? 1f : Mathf.Clamp01((12f - age) / 2f);
                if (a <= 0f) break;
                var r = new Rect(x, bottom - lineH * (shown + 1), w, lineH);
                var c = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.45f * a);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(l.color.r, l.color.g, l.color.b, a);
                float nameW = Mathf.Min(220f, lineStyle.CalcSize(new GUIContent(l.name + ":")).x + 6f);
                GUI.Label(new Rect(r.x + 8f, r.y, nameW, r.height), l.name + ":", lineStyle);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Label(new Rect(r.x + 8f + nameW, r.y, r.width - nameW - 12f, r.height), l.text, lineStyle);
                GUI.color = c;
                shown++;
            }

            if (!typing && shown == 0 && InLobby)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                GUI.Label(new Rect(x + 6f, bottom - lineH, w, lineH), Loc.T("ENTER o T: escribir en el chat"), lineStyle);
                GUI.color = c;
            }
        }

        private void DrawEmoteMenu(float vw, float vh)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == Controls.Key(Controls.Act.Emotes, 0))) { e.Use(); emoteMenu = false; return; }
            const int cols = 4;
            float cw = 210f, ch = 120f, gap = 12f;
            int rowsN = (Emotes.Length + cols - 1) / cols;
            float w = cols * cw + (cols - 1) * gap + 40f, h = rowsN * ch + (rowsN - 1) * gap + 116f;
            var r = new Rect((vw - w) * 0.5f, vh - h - 150f, w, h);
            TrenchGUI.Panel(r);
            TrenchGUI.Title(new Rect(r.x, r.y + 10f, r.width, 44f), "EMOTES");
            for (int i = 0; i < Emotes.Length; i++)
            {
                var cell = new Rect(r.x + 20f + (i % cols) * (cw + gap), r.y + 64f + (i / cols) * (ch + gap), cw, ch);
                bool hover = cell.Contains(e.mousePosition);
                var c = GUI.color;
                GUI.color = hover ? new Color(0.9f, 0.7f, 0.25f, 0.45f) : new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                GUI.color = c;
                var icon = EmoteFx.Icon(Emotes[i].fx);
                if (icon != null) GUI.DrawTexture(new Rect(cell.center.x - 28f, cell.y + 10f, 56f, 56f), icon, ScaleMode.ScaleToFit);
                var al = TrenchGUI.LabelStyle.alignment;
                TrenchGUI.LabelStyle.alignment = TextAnchor.MiddleCenter;
                TrenchGUI.Label(new Rect(cell.x, cell.y + 70f, cell.width, 32f), Emotes[i].name);
                TrenchGUI.LabelStyle.alignment = al;
                GUI.color = new Color(1f, 0.85f, 0.4f);
                TrenchGUI.Small(new Rect(cell.x + 8f, cell.y + 4f, 30f, 26f), EmoteKeyLabel(i));
                GUI.color = c;
                if (hover && e.type == EventType.MouseDown && e.button == 0) { e.Use(); emoteMenu = false; SendEmote(i); return; }
            }
            var c2 = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            TrenchGUI.Small(new Rect(r.x + 20f, r.yMax - 34f, r.width - 40f, 26f), "1-0, - o = (o clic): elegir  ·  ESC: cerrar");
            GUI.color = c2;
        }

        /// <summary>Para las pruebas: abre/cierra el menú de emotes.</summary>
        public static void TestOpenEmoteMenu(bool open) { if (instance != null) instance.emoteMenu = open; }
    }
}
