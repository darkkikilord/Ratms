using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Gestor de turnos POR EQUIPOS (multijugador y partida local).
    ///
    ///  Cómo funciona:
    ///   • Es un NetworkBehaviour único en la escena de juego. SOLO el
    ///     servidor (el host) decide de quién es el turno y cuánto queda; los
    ///     clientes leen el estado vía NetworkVariables.
    ///   • Cada rata tiene un EQUIPO (<see cref="NetworkPlayerMovement.TeamIndex"/>).
    ///     Online hay un equipo por jugador; en partida local todos los
    ///     equipos son del host y se pasan el teclado.
    ///   • Los turnos van alternando EQUIPOS, y dentro de cada equipo la rata
    ///     ROTA SOLA (como en Worms): cada vez le toca a la siguiente viva.
    ///   • Un equipo queda eliminado cuando no le quedan ratas vivas (o su
    ///     jugador se desconecta). Si queda uno → gana. Si no queda ninguno → empate.
    ///   • MUERTE SÚBITA: pasados <see cref="MatchRules.SuddenDeathMinutes"/>
    ///     el agua sube un poco al acabar cada turno.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public partial class NetworkTurnManager : NetworkBehaviour
    {
        public static NetworkTurnManager Instance { get; private set; }

        // =========================================================
        // Config (visible en Inspector)
        // =========================================================
        [Header("Turnos")]
        [Tooltip("Segundos de cada turno si no hay regla de partida (normalmente manda MatchRules.TurnSeconds).")]
        [SerializeField] private float turnDuration = 30f;

        [Tooltip("Pequeño retardo entre un turno que acaba y el siguiente que empieza.")]
        [SerializeField] private float transitionDelay = 1.5f;

        [Header("Viento")]
        [SerializeField] private bool windEnabled = true;
        [Range(0f, 1f)] [SerializeField] private float calmChance = 0.15f;
        [Tooltip("Aceleración horizontal (u/s²) con viento al MÁXIMO (Wind = ±1) para un proyectil con windFactor=1.")]
        [SerializeField] private float maxWindAcceleration = 3.5f;

        public float MaxWindAcceleration => maxWindAcceleration;

        /// <summary>Segundos totales de cada turno (para la barra del HUD).</summary>
        public float TurnDuration => TurnSecondsNet.Value > 0 ? TurnSecondsNet.Value : turnDuration;

        // =========================================================
        // Estado sincronizado
        // =========================================================
        /// <summary>Jugador (cliente) dueño de la rata que tiene el turno. MaxValue = nadie.</summary>
        public readonly NetworkVariable<ulong> CurrentTurnClientId = new(ulong.MaxValue);

        /// <summary>NetworkObjectId de la RATA que tiene el turno. MaxValue = ninguna.</summary>
        public readonly NetworkVariable<ulong> CurrentRatId = new(ulong.MaxValue);

        /// <summary>Equipo que tiene el turno (-1 = ninguno).</summary>
        public readonly NetworkVariable<int> CurrentTeam = new(-1);

        public readonly NetworkVariable<float> TurnTimeRemaining = new(0f);
        public readonly NetworkVariable<bool> InTransition = new(false);

        /// <summary>Dueño del equipo ganador (compatibilidad). MaxValue = empate o sin ganador.</summary>
        public readonly NetworkVariable<ulong> WinnerClientId = new(ulong.MaxValue);

        /// <summary>Equipo ganador (-1 = empate o sin ganador aún).</summary>
        public readonly NetworkVariable<int> WinnerTeam = new(-1);

        public readonly NetworkVariable<bool> GameEnded = new(false);

        /// <summary>Viento del turno: -1 (izquierda) … +1 (derecha). 0 = calma.</summary>
        public readonly NetworkVariable<float> Wind = new(0f);

        /// <summary>Segundos jugados de partida (para el reloj y la muerte súbita).</summary>
        public readonly NetworkVariable<float> MatchTime = new(0f);

        /// <summary>Segundos de partida a los que empieza la muerte súbita (0 = nunca).</summary>
        public readonly NetworkVariable<float> SuddenDeathAt = new(0f);

        public readonly NetworkVariable<bool> SuddenDeath = new(false);

        /// <summary>Cuánto ha subido el agua (u) por la muerte súbita. Lo lee WaterDeathZone.</summary>
        public readonly NetworkVariable<float> WaterRise = new(0f);

        public readonly NetworkVariable<int> TurnSecondsNet = new(0);

        /// <summary>
        ///  Nombre de cada equipo (índice = TeamIndex). Vacío = usar el nombre
        ///  del jugador dueño (partidas online).
        /// </summary>
        public NetworkList<FixedString32Bytes> TeamNames;

        /// <summary>Aceleración del viento AHORA (u/s², + = derecha) para un proyectil con factor 1.</summary>
        public static float WindAcceleration =>
            Instance != null ? Instance.Wind.Value * Instance.maxWindAcceleration : 0f;

        /// <summary>Aviso grande en pantalla (lo pinta TurnHUD). Lo recibe cada cliente.</summary>
        public static event System.Action<string, Color> OnAnnouncement;

        // =========================================================
        // Estado interno del servidor
        // =========================================================
        private class Team
        {
            public int index;
            public ulong owner;
            public readonly List<NetworkPlayerMovement> rats = new();
            public int nextRat;
            public bool eliminated;
            public NetworkPlayerMovement forcedRat;   // elegida con "Cambiar rata" para su próximo turno
        }

        private readonly List<Team> teams = new();
        private int currentTeamPos = -1;     // posición en "teams" (no el TeamIndex)
        private float transitionTimer;
        private bool gameStarted;

        private void Awake()
        {
            TeamNames = new NetworkList<FixedString32Bytes>();
            ItemsAwake();
        }

        // Ganchos para la parte de munición, cajas y barriles (NetworkTurnManager.Items.cs).
        partial void ItemsAwake();
        partial void ItemsOnSpawn();
        partial void ItemsOnDespawn();
        partial void ItemsOnGameStart();
        partial void ItemsOnTurnBegin();
        partial void ItemsUpdate();
        partial void StatsOnSpawn();
        partial void StatsOnDespawn();
        partial void StatsOnGameStart();

        // =========================================================
        // CICLO DE VIDA
        // =========================================================
        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (FindFirstObjectByType<TurnHUD>() == null)
                gameObject.AddComponent<TurnHUD>();
            Sfx.Warmup();

            CurrentRatId.OnValueChanged += OnCurrentRatChanged;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                TurnSecondsNet.Value = MatchRules.EffectiveTurnSeconds;
                SuddenDeathAt.Value = MatchRules.EffectiveSuddenDeathSeconds;

                // Las ratas las crea GameSceneSpawnManager poco después de
                // cargar la escena. Esperamos un poco y arrancamos.
                Invoke(nameof(TryStartGameServer), 1.5f);
            }
            ItemsOnSpawn();
            StatsOnSpawn();
        }

        public override void OnNetworkDespawn()
        {
            CurrentRatId.OnValueChanged -= OnCurrentRatChanged;
            ItemsOnDespawn();
            StatsOnDespawn();
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            if (Instance == this) Instance = null;
        }

        private void TryStartGameServer()
        {
            if (!IsServer || gameStarted) return;
            SyncTeamsFromScene();
            if (teams.Count == 0)
            {
                // Aún no hay ratas (carga lenta): reintentamos.
                Invoke(nameof(TryStartGameServer), 0.5f);
                return;
            }
            gameStarted = true;
            currentTeamPos = -1;
            Debug.Log($"[NetworkTurnManager] Empieza la partida: {teams.Count} equipos.");
            ItemsOnGameStart();
            StatsOnGameStart();
            AdvanceTurnServer();
        }

        /// <summary>Fuerza el inicio del juego (menú contextual del Inspector).</summary>
        [ContextMenu("Start Game (Server)")]
        public void StartGameServer() => TryStartGameServer();

        private void OnClientDisconnected(ulong clientId)
        {
            // Desconectarse = sus equipos quedan eliminados.
            foreach (var t in teams)
                if (t.owner == clientId && !t.eliminated) EliminateTeam(t);
            CheckVictoryServer();
            if (!GameEnded.Value && CurrentTurnClientId.Value == clientId) AdvanceTurnServer();
        }

        // =========================================================
        // EQUIPOS (servidor)
        // =========================================================
        /// <summary>Añade a la lista los equipos/ratas que haya en escena y aún no conozcamos.</summary>
        private void SyncTeamsFromScene()
        {
            var all = FindObjectsByType<NetworkPlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var m in all)
            {
                if (m == null || !m.IsSpawned || m.IsTestDummy) continue;
                int ti = m.TeamIndex.Value;
                if (ti < 0) continue;
                var team = teams.Find(t => t.index == ti);
                if (team == null)
                {
                    team = new Team { index = ti, owner = m.OwnerClientId };
                    teams.Add(team);
                    teams.Sort((a, b) => a.index.CompareTo(b.index));
                    if (currentTeamPos >= 0) currentTeamPos = Mathf.Min(currentTeamPos, teams.Count - 1);
                }
                if (!team.rats.Contains(m))
                {
                    team.rats.Add(m);
                    team.rats.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
                }
            }
            // Nombres de equipo en red (por si alguno nuevo)
            while (TeamNames.Count < (teams.Count > 0 ? teams[teams.Count - 1].index + 1 : 0))
            {
                int i = TeamNames.Count;
                string n = GameSceneSpawnManager.CustomTeamNames.TryGetValue(i, out var custom) ? custom
                         : MatchRules.Mode == MatchMode.Local ? MatchRules.LocalTeamNames[i % MatchRules.LocalTeamNames.Length] : "";
                TeamNames.Add(new FixedString32Bytes(n));
            }
        }

        private static bool IsAlive(NetworkPlayerMovement m)
        {
            if (m == null || !m.IsSpawned) return false;
            var h = m.GetComponent<NetworkHealth>();
            return h != null && !h.IsDead.Value;
        }

        private static bool TeamAlive(Team t) => !t.eliminated && t.rats.Exists(IsAlive);

        private void EliminateTeam(Team t)
        {
            if (t.eliminated) return;
            t.eliminated = true;
            AnnounceClientRpc(Ratms.UI.Loc.Pack("¡{0} ELIMINADOS!", TeamDisplayName(t.index, translate: false)), new Color(1f, 0.35f, 0.25f));
        }

        // =========================================================
        // SERVIDOR: bucle
        // =========================================================
        private void Update()
        {
            ItemsUpdate();
            if (!IsServer || !gameStarted || GameEnded.Value) return;

            MatchTime.Value += Time.deltaTime;
            if (!SuddenDeath.Value && SuddenDeathAt.Value > 0f && MatchTime.Value >= SuddenDeathAt.Value)
            {
                SuddenDeath.Value = true;
                AnnounceClientRpc("¡MUERTE SÚBITA!\nEl agua sube cada turno", new Color(1f, 0.25f, 0.2f));
            }

            if (InTransition.Value)
            {
                transitionTimer -= Time.deltaTime;
                if (transitionTimer <= 0f)
                {
                    InTransition.Value = false;
                    BeginNextTurnServer();
                }
                return;
            }

            if (TurnTimeRemaining.Value > 0f)
            {
                TurnTimeRemaining.Value -= Time.deltaTime;
                if (TurnTimeRemaining.Value <= 0f)
                {
                    TurnTimeRemaining.Value = 0f;
                    AdvanceTurnServer();
                }
            }
        }

        /// <summary>Acaba el turno actual: transición y, en muerte súbita, el agua sube.</summary>
        private void AdvanceTurnServer()
        {
            if (!IsServer || GameEnded.Value) return;

            bool wasRealTurn = CurrentRatId.Value != ulong.MaxValue;
            if (SuddenDeath.Value && wasRealTurn)
                WaterRise.Value += MatchRules.SuddenDeathRisePerTurn;

            InTransition.Value = true;
            transitionTimer = transitionDelay;
            CurrentTurnClientId.Value = ulong.MaxValue;
            CurrentRatId.Value = ulong.MaxValue;
            CurrentTeam.Value = -1;
            TurnTimeRemaining.Value = 0f;
        }

        private void BeginNextTurnServer()
        {
            if (!IsServer || GameEnded.Value) return;
            SyncTeamsFromScene();
            foreach (var t in teams) if (!t.eliminated && !TeamAlive(t)) EliminateTeam(t);
            if (CheckVictoryServer()) return;

            // Siguiente equipo con ratas vivas
            for (int k = 1; k <= teams.Count; k++)
            {
                int pos = (currentTeamPos + k + teams.Count) % teams.Count;
                var team = teams[pos];
                if (!TeamAlive(team)) continue;

                // ¿Alguien usó "Cambiar rata"? Esa rata va primero.
                if (team.forcedRat != null && IsAlive(team.forcedRat))
                {
                    int fi = team.rats.IndexOf(team.forcedRat);
                    if (fi >= 0) team.nextRat = fi;
                }
                team.forcedRat = null;

                // Siguiente rata viva de ese equipo (rotación automática)
                for (int j = 0; j < team.rats.Count; j++)
                {
                    int ri = (team.nextRat + j) % team.rats.Count;
                    var rat = team.rats[ri];
                    if (!IsAlive(rat)) continue;

                    currentTeamPos = pos;
                    team.nextRat = (ri + 1) % team.rats.Count;
                    CurrentTeam.Value = team.index;
                    CurrentTurnClientId.Value = team.owner;
                    CurrentRatId.Value = rat.NetworkObjectId;
                    TurnTimeRemaining.Value = TurnDuration;
                    Wind.Value = RollWind();
                    ItemsOnTurnBegin();
                    return;
                }
            }
        }

        /// <summary>
        ///  ¿Se acabó? Solo cuenta si la partida tiene (o tuvo) varios equipos;
        ///  en práctica (1 equipo) solo acaba si muere tu rata (empate).
        /// </summary>
        private bool CheckVictoryServer()
        {
            if (GameEnded.Value) return true;
            int alive = 0;
            Team last = null;
            foreach (var t in teams) if (TeamAlive(t)) { alive++; last = t; }

            bool multiTeam = teams.Count >= 2;
            if (alive >= 2 || (!multiTeam && alive == 1)) return false;

            CurrentTurnClientId.Value = ulong.MaxValue;
            CurrentRatId.Value = ulong.MaxValue;
            CurrentTeam.Value = -1;
            TurnTimeRemaining.Value = 0f;
            InTransition.Value = false;
            gameStarted = false;

            if (alive == 1)
            {
                WinnerTeam.Value = last.index;
                WinnerClientId.Value = last.owner;
                Debug.Log($"[NetworkTurnManager] ¡GANADOR! equipo {last.index} (cliente {last.owner})");
            }
            else
            {
                WinnerTeam.Value = -1;
                WinnerClientId.Value = ulong.MaxValue;
                Debug.Log("[NetworkTurnManager] Partida terminada en empate.");
            }
            StatsSendServer();   // cifras para la pantalla de victoria (antes de anunciar el final)
            GameEnded.Value = true;
            return true;
        }

        private float RollWind()
        {
            if (!windEnabled || Random.value < calmChance) return 0f;
            float magnitude = Mathf.Max(0.1f, Random.value * Random.value * 1.4f);
            magnitude = Mathf.Min(1f, magnitude);
            float sign = Random.value < 0.5f ? -1f : 1f;
            return Mathf.Round(sign * magnitude * 10f) / 10f;
        }

        // =========================================================
        // RPCs y avisos
        // =========================================================
        /// <summary>
        ///  Lo llama el jugador que tiene el turno cuando termina su acción
        ///  (por ejemplo, al disparar). Validamos que sea realmente su turno.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void EndTurnServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (CurrentTurnClientId.Value != rpcParams.Receive.SenderClientId) return;
            AdvanceTurnServer();
        }

        [ClientRpc]
        private void AnnounceClientRpc(string text, Color color) => OnAnnouncement?.Invoke(text, color);

        /// <summary>
        ///  Lo llama la rata (servidor) al morir: si era la del turno, el turno
        ///  pasa; si su equipo se ha quedado sin ratas, queda eliminado y se
        ///  comprueba la victoria.
        /// </summary>
        // Bajas seguidas (misma explosión) para los avisos cómicos.
        private int killStreak;
        private float killStreakUntil;
        private static readonly string[] SuicideLines = { "¡SE HA SUICIDADO!", "¡AUTOGOL!", "¡ESO NO SE APUNTA ASÍ!", "¡PREMIO DARWIN!" };
        private static readonly string[] FriendlyLines = { "¡FUEGO AMIGO!", "¡TRAICIÓN EN LA TRINCHERA!", "¡ERA DE LOS TUYOS!" };

        public void OnRatDiedServer(NetworkPlayerMovement rat)
        {
            if (!IsServer || !gameStarted) return;

            // --- Avisos cómicos ---
            var shooter = CurrentRat != null ? CurrentRat : LastTurnRat;
            StatsOnKill(shooter, rat);
            if (shooter != null && PlayerPrefs.GetInt("opt_laughs", 1) == 1)
            {
                if (shooter == rat)
                    AnnounceClientRpc(SuicideLines[Random.Range(0, SuicideLines.Length)], new Color(1f, 0.6f, 0.2f));
                else if (shooter.TeamIndex.Value == rat.TeamIndex.Value)
                    AnnounceClientRpc(FriendlyLines[Random.Range(0, FriendlyLines.Length)], new Color(1f, 0.6f, 0.2f));
            }
            killStreak = Time.time < killStreakUntil ? killStreak + 1 : 1;
            killStreakUntil = Time.time + 1.2f;
            if (killStreak >= 2)
                AnnounceClientRpc(killStreak == 2 ? "¡DOBLE BAJA!" : killStreak == 3 ? "¡TRIPLE BAJA!" : "¡MASACRE EN LA TRINCHERA!", new Color(1f, 0.25f, 0.2f));
            var team = teams.Find(t => t.rats.Contains(rat));
            if (team != null && !team.eliminated && !TeamAlive(team)) EliminateTeam(team);
            if (CheckVictoryServer()) return;
            if (CurrentRatId.Value == rat.NetworkObjectId) AdvanceTurnServer();
        }

        // =========================================================
        // Cámara: sigue a la rata del turno (en todos los clientes)
        // =========================================================
        /// <summary>Última rata que tuvo el turno (sigue valiendo durante la transición: "quién disparó").</summary>
        public NetworkPlayerMovement LastTurnRat { get; private set; }

        private void OnCurrentRatChanged(ulong _, ulong now)
        {
            var rat = FindRat(now);
            if (rat == null) return;
            LastTurnRat = rat;
            var cam = CameraFollow.Instance;
            if (cam != null) cam.SetTarget(rat.transform);
        }

        // =========================================================
        // Helpers para todos
        // =========================================================
        /// <summary>¿El cliente tiene el turno ahora? (alguna de sus ratas)</summary>
        public bool IsTurnOf(ulong clientId) =>
            !InTransition.Value && CurrentTurnClientId.Value == clientId;

        /// <summary>¿Es el turno de ESTA rata en concreto?</summary>
        public bool IsActiveRat(ulong networkObjectId) =>
            !InTransition.Value && CurrentRatId.Value != ulong.MaxValue && CurrentRatId.Value == networkObjectId;

        /// <summary>Validación de servidor: el cliente que pide es el dueño Y la rata es la del turno.</summary>
        public bool CanAct(ulong senderClientId, ulong ratNetworkObjectId) =>
            IsTurnOf(senderClientId) && IsActiveRat(ratNetworkObjectId);

        /// <summary>Rata que tiene el turno (o null).</summary>
        public NetworkPlayerMovement CurrentRat => FindRat(CurrentRatId.Value);

        private NetworkPlayerMovement FindRat(ulong id)
        {
            if (id == ulong.MaxValue || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) ? no.GetComponent<NetworkPlayerMovement>() : null;
        }

        /// <summary>Nombre visible de un equipo: el de la regla local o el del jugador dueño.</summary>
        /// <param name="translate">false = en español tal cual (para mandarlo por red
        /// y que cada PC lo traduzca); true = en el idioma de este PC ("Rojos" → "Reds").</param>
        public string TeamDisplayName(int teamIndex, bool translate = true)
        {
            if (teamIndex < 0) return "?";
            if (TeamNames != null && teamIndex < TeamNames.Count && !TeamNames[teamIndex].IsEmpty)
            {
                string n = TeamNames[teamIndex].ToString();
                return translate ? Ratms.UI.Loc.T(n) : n;
            }
            foreach (var m in FindObjectsByType<NetworkPlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m.IsSpawned && m.TeamIndex.Value == teamIndex) return PlayerNameRegistry.GetName(m.OwnerClientId);
            return translate ? Ratms.UI.Loc.F("Equipo {0}", teamIndex + 1) : $"Equipo {teamIndex + 1}";
        }
    }
}
