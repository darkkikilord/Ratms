using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Panel flotante de prueba para multijugador.
    ///  Permite dos modos de conexión:
    ///    • LOCAL  (127.0.0.1) → solo dos ventanas en el mismo PC, para test.
    ///    • RELAY  → jugar entre dos PCs distintos por internet.
    ///
    ///  Es UI "de andamio" con IMGUI. En Fase 5 lo reemplazaremos por un menú
    ///  bonito integrado en el MainMenu.
    ///
    ///  Cuando ya estás CONECTADO, el panel se auto-minimiza a los pocos
    ///  segundos para no tapar la partida. En la esquina queda un botoncito
    ///  "Red ▾" que lo vuelve a desplegar. También puedes alternarlo con H.
    /// </summary>
    [RequireComponent(typeof(RelayManager))]
    public class NetworkBootstrap : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private Vector2 panelSize = new Vector2(400f, 270f);
        [SerializeField] private Vector2 panelMargin = new Vector2(12f, 12f);

        [Header("Escalado automático")]
        [Tooltip("Altura de referencia: el panel se escala para que se vea bien en cualquier resolución.")]
        [SerializeField] private float referenceHeight = 1080f;

        [Header("Auto-minimizar")]
        [Tooltip("Segundos tras conectar antes de plegar el panel automáticamente.")]
        [SerializeField] private float autoMinimizeAfter = 6f;
        [SerializeField] private KeyCode toggleKey = KeyCode.H;

        private RelayManager relay;

        // Estado de la UI
        private string joinCodeInput = "";
        private string statusMessage = "";
        private bool busy = false;

        // Auto-ocultar
        private bool minimized = false;
        private bool autoMinimizeArmed = false;
        private float connectedAt = -1f;

        private void Awake()
        {
            relay = GetComponent<RelayManager>();
        }

        private void Update()
        {
            // Tecla H → alternar manualmente
            if (Input.GetKeyDown(toggleKey))
                minimized = !minimized;

            // Auto-minimizar cuando acabamos de conectar
            bool connected = NetworkManager.Singleton != null &&
                             (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

            if (connected && !autoMinimizeArmed)
            {
                connectedAt = Time.time;
                autoMinimizeArmed = true;
            }
            else if (!connected && autoMinimizeArmed)
            {
                // Nos desconectaron: volver a mostrar el panel entero
                autoMinimizeArmed = false;
                minimized = false;
                connectedAt = -1f;
            }

            if (connected && autoMinimizeArmed && !minimized &&
                Time.time - connectedAt >= autoMinimizeAfter)
            {
                minimized = true;
            }
        }

        private void OnGUI()
        {
            // Escalamos toda la UI según la resolución actual de la pantalla,
            // así se ve igual de bien en el Editor y en builds Full HD / 4K.
            float scale = Mathf.Max(0.8f, Screen.height / referenceHeight);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            GUI.skin.button.fontSize = 20;
            GUI.skin.label.fontSize = 18;
            GUI.skin.textField.fontSize = 22;

            if (NetworkManager.Singleton == null)
            {
                GUILayout.BeginArea(new Rect(panelMargin.x, panelMargin.y, panelSize.x, 60), GUI.skin.box);
                GUILayout.Label("NetworkManager no encontrado en la escena.");
                GUILayout.EndArea();
                GUI.matrix = previousMatrix;
                return;
            }

            bool isConnected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;

            // Si está minimizado y conectado → solo una pastillita con código
            if (minimized && isConnected)
            {
                DrawMinimizedPill();
            }
            else
            {
                GUILayout.BeginArea(new Rect(panelMargin.x, panelMargin.y, panelSize.x, panelSize.y),
                                    GUI.skin.box);

                if (!isConnected)
                {
                    DrawDisconnectedPanel();
                }
                else
                {
                    DrawConnectedPanel();
                }

                GUILayout.EndArea();
            }

            GUI.matrix = previousMatrix;
        }

        // ==========================================================
        // MINIMIZADO (pastilla en la esquina)
        // ==========================================================
        private void DrawMinimizedPill()
        {
            float w = 230f;
            float h = 44f;
            GUILayout.BeginArea(new Rect(panelMargin.x, panelMargin.y, w, h), GUI.skin.box);
            GUILayout.BeginHorizontal();

            string code = relay.LastJoinCode;
            if (NetworkManager.Singleton.IsHost && !string.IsNullOrEmpty(code))
                GUILayout.Label($"Host · {code}", GUILayout.ExpandWidth(true));
            else if (NetworkManager.Singleton.IsHost)
                GUILayout.Label("Host", GUILayout.ExpandWidth(true));
            else
                GUILayout.Label("Cliente", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Red ▾", GUILayout.Width(80)))
                minimized = false;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ==========================================================
        // DESCONECTADO (pantalla inicial)
        // ==========================================================
        private void DrawDisconnectedPanel()
        {
            GUILayout.Label("RATMS — Panel multijugador");
            GUILayout.Space(4);

            GUI.enabled = !busy;

            // ---------------- RELAY (jugar entre PCs) ----------------
            GUILayout.Label("— Jugar por internet (Relay) —");

            if (GUILayout.Button("🌐  Crear sala (ser host)"))
                _ = CreateRelayHost();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Código:", GUILayout.Width(60));
            joinCodeInput = GUILayout.TextField(joinCodeInput, 10).ToUpper();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("🚪  Unirme con código"))
                _ = JoinRelay(joinCodeInput);

            GUILayout.Space(8);

            // ---------------- LOCAL (mismo PC, para test) ------------
            GUILayout.Label("— Local (mismo PC, para test) —");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host"))  { NetworkManager.Singleton.StartHost();   ClearStatus(); }
            if (GUILayout.Button("Client")){ NetworkManager.Singleton.StartClient(); ClearStatus(); }
            GUILayout.EndHorizontal();

            GUI.enabled = true;

            // ---------------- Mensaje de estado ----------------------
            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Space(6);
                GUILayout.Label(statusMessage);
            }
        }

        // ==========================================================
        // CONECTADO (ya en partida)
        // ==========================================================
        private void DrawConnectedPanel()
        {
            GUILayout.BeginHorizontal();
            string mode = NetworkManager.Singleton.IsHost ? "HOST"
                        : NetworkManager.Singleton.IsServer ? "SERVER"
                        : "CLIENT";
            GUILayout.Label($"Conectado como: {mode}", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Ocultar ▴", GUILayout.Width(100)))
                minimized = true;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Jugadores conectados: {NetworkManager.Singleton.ConnectedClientsIds.Count}");

            // Si el host tiene un código de Relay, lo mostramos bien grande
            if (!string.IsNullOrEmpty(relay.LastJoinCode) && NetworkManager.Singleton.IsHost)
            {
                GUILayout.Space(6);
                GUILayout.Label("Pasa este CÓDIGO a tus amigos:");
                var style = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                GUILayout.TextField(relay.LastJoinCode, style, GUILayout.Height(40));
            }

            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
                GUILayout.Label($"Mi ClientId: {NetworkManager.Singleton.LocalClientId}");

            GUILayout.Space(6);

            if (GUILayout.Button("❌  Desconectar"))
            {
                NetworkManager.Singleton.Shutdown();
                ClearStatus();
            }

            GUILayout.Label("(pulsa H para mostrar/ocultar)", GUI.skin.label);
        }

        // ==========================================================
        // RELAY — acciones asíncronas
        // ==========================================================
        private async System.Threading.Tasks.Task CreateRelayHost()
        {
            busy = true;
            statusMessage = "Creando sala en Relay...";
            string code = await relay.CreateHostAsync();
            busy = false;

            statusMessage = string.IsNullOrEmpty(code)
                ? "❌ Error creando sala (revisa consola)"
                : $"✅ Sala creada. Código: {code}";
        }

        private async System.Threading.Tasks.Task JoinRelay(string code)
        {
            busy = true;
            statusMessage = $"Uniéndose a sala {code}...";
            bool ok = await relay.JoinAsync(code);
            busy = false;

            statusMessage = ok ? "✅ Conectado" : "❌ Error. Revisa el código.";
        }

        private void ClearStatus()
        {
            statusMessage = "";
        }
    }
}
