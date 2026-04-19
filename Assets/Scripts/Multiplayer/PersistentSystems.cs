using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Marca este GameObject como persistente entre escenas.
    ///
    ///  Úsalo en el GameObject raíz del MainMenu que contiene:
    ///    • NetworkManager
    ///    • UnityTransport
    ///    • RelayManager
    ///    • MultiplayerMenuController
    ///    • (opcional) un Canvas con los paneles del menú.
    ///
    ///  Así, cuando se cargue el "Escenario 1" con NetworkManager.SceneManager.LoadScene(...)
    ///  estos componentes sobreviven y podemos:
    ///    • Seguir escuchando desconexiones
    ///    • Volver al MainMenu pulsando "Desconectar" sin reiniciar el juego
    ///
    ///  Si el MainMenu se carga dos veces (porque volvemos desde el Escenario 1),
    ///  evitamos tener DUPLICADO destruyendo el nuevo y quedándonos con el original.
    /// </summary>
    public class PersistentSystems : MonoBehaviour
    {
        private static PersistentSystems instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                // Ya había uno persistente vivo → este es un duplicado (se creó
                // al volver a cargar la escena MainMenu). Lo destruimos.
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
