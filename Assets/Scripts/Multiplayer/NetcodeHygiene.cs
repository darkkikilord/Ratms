using System.Collections;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  LIMPIEZA de Netcode entre sesiones (0.19).
    ///
    ///  Netcode for GameObjects 2.1 guarda en un diccionario ESTÁTICO
    ///  (<c>NetworkObject.NetworkObjectsToSynchronizeSceneChanges</c>) los
    ///  objetos de red, por su NetworkObjectId, y no lo vacía al apagar la red.
    ///  En la siguiente sesión los ids vuelven a empezar: el objeto nuevo no se
    ///  apunta (el id "ya está") y el viejo, ya destruido, da un
    ///  NullReferenceException en CADA fotograma. Pasaba al jugar una segunda
    ///  partida sin cerrar el juego (revancha, o salir al menú y volver a jugar).
    ///
    ///  Llama a <see cref="BeforeSessionStart"/> justo antes de StartHost/StartClient.
    ///  Es un campo interno del paquete, así que se toca por reflexión; si una
    ///  versión futura lo cambia, esto simplemente no hace nada.
    /// </summary>
    public static class NetcodeHygiene
    {
        private static FieldInfo field;
        private static bool searched;

        /// <summary>
        ///  0.36.1 — RED DE SEGURIDAD: al cargar cada escena, quita de ese mismo
        ///  diccionario los objetos YA DESTRUIDOS (si alguno se destruyó sin pasar
        ///  por Netcode). Sin esto, uno solo bastaba para un error por fotograma y
        ///  el bucle de red del cliente dejaba de enviar nada.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => PurgeDestroyed();
        }

        public static void PurgeDestroyed()
        {
            Find();
            if (!(field?.GetValue(null) is IDictionary dict) || dict.Count == 0) return;
            var dead = new System.Collections.Generic.List<object>();
            foreach (DictionaryEntry e in dict)
                if (e.Value is Object o ? o == null : e.Value == null) dead.Add(e.Key);
            foreach (var k in dead) dict.Remove(k);
            if (dead.Count > 0) Debug.Log($"[NetcodeHygiene] Quito {dead.Count} objetos de red ya destruidos.");
        }

        private static void Find()
        {
            if (searched) return;
            searched = true;
            field = typeof(NetworkObject).GetField("NetworkObjectsToSynchronizeSceneChanges",
                                                   BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        public static void BeforeSessionStart()
        {
            if (!searched)
            {
                searched = true;
                field = typeof(NetworkObject).GetField("NetworkObjectsToSynchronizeSceneChanges",
                                                       BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            }
            if (field?.GetValue(null) is IDictionary dict && dict.Count > 0)
            {
                Debug.Log($"[NetcodeHygiene] Limpio {dict.Count} objetos de red de la sesión anterior.");
                dict.Clear();
            }
        }
    }
}
