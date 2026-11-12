using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Sincroniza el NOMBRE del jugador dueño de esta rata a todos los peers.
    ///
    ///  Flujo:
    ///   1) En OnNetworkSpawn, si soy el Owner, publico mi PlayerNameRegistry.LocalName
    ///      con un ServerRpc → el servidor escribe el NetworkVariable.
    ///   2) Todos los clientes reciben el cambio y lo registran en
    ///      PlayerNameRegistry.SetName(clientId, name).
    ///
    ///  Los scripts que necesiten mostrar el nombre (etiquetas, victoria, HUD)
    ///  llaman a PlayerNameRegistry.GetName(clientId).
    /// </summary>
    public class PlayerNameSync : NetworkBehaviour
    {
        // NetworkVariable de string fija (32 bytes) — bajísimo tráfico.
        private readonly NetworkVariable<FixedString32Bytes> playerName = new(
            value: default,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            playerName.OnValueChanged += OnNameChanged;

            // Si ya tenemos un valor cuando spawneamos (late-join), lo registramos.
            if (!playerName.Value.IsEmpty)
                PlayerNameRegistry.SetName(OwnerClientId, playerName.Value.ToString());

            // Si soy el Owner, mando mi nombre al servidor para que lo publique
            if (IsOwner)
            {
                string myName = PlayerNameRegistry.LocalName;
                SubmitNameServerRpc(new FixedString32Bytes(myName));
            }
        }

        public override void OnNetworkDespawn()
        {
            playerName.OnValueChanged -= OnNameChanged;
        }

        private void OnNameChanged(FixedString32Bytes _, FixedString32Bytes newValue)
        {
            PlayerNameRegistry.SetName(OwnerClientId, newValue.ToString());
        }

        [ServerRpc]
        private void SubmitNameServerRpc(FixedString32Bytes name)
        {
            playerName.Value = name;
            // También lo registramos en el servidor por si el OnValueChanged
            // no se dispara para el propio escritor.
            PlayerNameRegistry.SetName(OwnerClientId, name.ToString());
        }
    }
}
