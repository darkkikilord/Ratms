using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Gestor de Unity Relay.
    ///  Relay es un servicio GRATUITO de Unity que hace de puente entre dos PCs
    ///  sin necesidad de abrir puertos ni saber IPs públicas.
    ///
    ///  Flujo típico:
    ///   1. El host llama a <see cref="CreateHostAsync"/> → recibe un código corto
    ///      (por ejemplo "ABC123") y empieza a servir.
    ///   2. El host le pasa ese código al amigo (WhatsApp, Discord, lo que sea).
    ///   3. El amigo llama a <see cref="JoinAsync(string)"/> con ese código → conecta.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        [Tooltip("Máximo de jugadores adicionales que acepta la sala (sin contar al host).")]
        [SerializeField] private int maxAdditionalPlayers = 3;

        [Tooltip("Tipo de conexión de Relay. 'dtls' es seguro y recomendado.")]
        [SerializeField] private string connectionType = "dtls";

        /// <summary>Último código de sala generado por el host.</summary>
        public string LastJoinCode { get; private set; }
        /// <summary>0.23: último código con el que este PC entró como cliente (para reconectar).</summary>
        public static string LastJoinedCode;

        /// <summary>
        ///  Inicializa Unity Services y entra con autenticación anónima.
        ///  Se llama internamente antes de crear host o unirse.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // 0.36: "-ratmsProfile nombre" = otra cuenta anónima. Hace falta
                // para probar dos copias del juego en el MISMO PC (si no, las dos
                // son el mismo jugador y el Lobby no deja entrar en tu propia sala).
                string profile = null;
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ratmsProfile") profile = args[i + 1];
                if (!string.IsNullOrEmpty(profile))
                {
                    var options = new InitializationOptions();
                    options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                else await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Relay] Autenticado como {AuthenticationService.Instance.PlayerId}");
            }
        }

        /// <summary>
        ///  Crea una sala en Relay y arranca el NetworkManager como Host.
        /// </summary>
        /// <returns>El código de unión (6 caracteres aprox) para compartir con los amigos.</returns>
        public async Task<string> CreateHostAsync()
        {
            try
            {
                await InitializeAsync();

                // Reservamos un "cupo" en un relay server
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(maxAdditionalPlayers, MatchRules.MaxPlayers - 1));
                // 0.31: el valor de la escena era 3 (4 jugadores); ahora caben
                // MatchRules.MaxPlayers (10) aunque el Inspector diga menos.

                // Pedimos el código corto que usarán los clientes para unirse
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                LastJoinCode = joinCode;
                Debug.Log($"[Relay] Sala creada. Código: {joinCode}");

                // Le decimos a UnityTransport que use este relay server
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                var serverData = new RelayServerData(allocation, connectionType);
                transport.SetRelayServerData(serverData);

                // Arrancamos como host
                NetcodeHygiene.BeforeSessionStart();
                NetworkManager.Singleton.StartHost();

                return joinCode;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Relay] Error creando sala: {e.Message}");
                LastJoinCode = null;
                return null;
            }
        }

        /// <summary>
        ///  Se une a una sala de Relay existente usando un código.
        /// </summary>
        public async Task<bool> JoinAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Debug.LogWarning("[Relay] Código vacío, no se puede unir.");
                return false;
            }

            try
            {
                await InitializeAsync();

                // Pedimos entrar al relay server de ese código
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());

                // Le decimos a UnityTransport que use ese relay server
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                var serverData = new RelayServerData(allocation, connectionType);
                transport.SetRelayServerData(serverData);

                // Arrancamos como cliente
                NetcodeHygiene.BeforeSessionStart();
                NetworkManager.Singleton.StartClient();

                LastJoinedCode = joinCode.Trim().ToUpper();
                Debug.Log($"[Relay] Unido a sala {joinCode}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Relay] Error uniéndose con código '{joinCode}': {e.Message}");
                return false;
            }
        }
    }
}
