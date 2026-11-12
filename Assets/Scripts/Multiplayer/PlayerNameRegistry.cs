using System.Collections.Generic;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Registro simple de nombres de jugadores por clientId.
    ///
    ///  Cómo funciona:
    ///   · El nombre local se guarda en <see cref="LocalName"/> (static) y
    ///     también en PlayerPrefs, para recordarlo entre sesiones.
    ///   · Al arrancar el juego, cada rata publica su nombre por
    ///     <see cref="PlayerNameSync"/> (NetworkVariable) a todos los peers.
    ///   · El resto de scripts consulta por clientId en este registro
    ///     y obtiene el nombre público del jugador.
    /// </summary>
    public static class PlayerNameRegistry
    {
        private const string PLAYER_PREFS_KEY = "Ratms_PlayerName";
        private const string DEFAULT_NAME = "Rata";

        private static string _localName;
        /// <summary>Nombre que el jugador LOCAL eligió en el menú.</summary>
        public static string LocalName
        {
            get
            {
                if (_localName == null)
                    _localName = PlayerPrefs.GetString(PLAYER_PREFS_KEY, DEFAULT_NAME);
                return _localName;
            }
            set
            {
                _localName = string.IsNullOrWhiteSpace(value) ? DEFAULT_NAME : value.Trim();
                if (_localName.Length > 16) _localName = _localName.Substring(0, 16);
                PlayerPrefs.SetString(PLAYER_PREFS_KEY, _localName);
                PlayerPrefs.Save();
            }
        }

        // ==============================================================
        //  Registro de nombres por clientId — se rellena en runtime
        //  desde PlayerNameSync.OnNetworkSpawn.
        // ==============================================================
        private static readonly Dictionary<ulong, string> namesByClient = new();

        /// <summary>
        ///  Publica el nombre de un cliente. Llamado desde PlayerNameSync
        ///  cuando llega el NetworkVariable del nombre.
        /// </summary>
        public static void SetName(ulong clientId, string name)
        {
            namesByClient[clientId] = string.IsNullOrWhiteSpace(name) ? DEFAULT_NAME : name;
        }

        /// <summary>
        ///  Devuelve el nombre público del cliente, o un placeholder si aún
        ///  no ha llegado por red.
        /// </summary>
        public static string GetName(ulong clientId)
        {
            return namesByClient.TryGetValue(clientId, out var n) ? n : $"Rata #{clientId}";
        }

        /// <summary>Se limpia al desconectar / cambiar de partida.</summary>
        public static void Clear() => namesByClient.Clear();
    }
}
