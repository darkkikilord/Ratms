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

        private RelayManager relay;

        // Estado de la UI
        private string joinCodeInput = "";
        private string statusMessage = "";
        private bool busy = false;

        private void Awake()
        {
            relay = GetComponent<RelayManager>();
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

            GUILayout.BeginArea(new Rect(panelMargin.x, panelMargin.y, panelSize.x, panelSize.y),
                                GUI.skin.box);

            if (NetworkManager.Singleton == null)
            {
                GUILayout.Label("NetworkManager no encontrado en la escena.");
                GUILayout.EndArea();
                GUI.matrix = previousMatrix;
                return;
            }

            bool isConnected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;

            if (!isConnected)
            {
                DrawDisconnectedPanel();
            }
            else
            {
                DrawConnectedPanel();
            }

            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
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
            string mode = NetworkManager.Singleton.IsHost ? "HOST"
                        : NetworkManager.Singleton.IsServer ? "SERVER"
                        : "CLIENT";

            GUILayout.Label($"Conectado como: {mode}");
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
