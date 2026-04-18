using Unity.Netcode;
using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  HUD de turnos. Un panel IMGUI arriba-centro que muestra:
    ///   • De quién es el turno
    ///   • Cuánto tiempo le queda
    ///   • Una marquita si ES TU turno para que lo veas claro
    ///
    ///  Como el NetworkBootstrap, esto es "de andamio". En Fase 5 lo haremos
    ///  con UI Toolkit/uGUI bonita. Por ahora sirve para jugar con amigos.
    /// </summary>
    public class TurnHUD : MonoBehaviour
    {
        [Header("Apariencia")]
        [SerializeField] private Vector2 panelSize = new Vector2(420f, 90f);
        [SerializeField] private float topMargin = 16f;
        [SerializeField] private float referenceHeight = 1080f;

        private void OnGUI()
        {
            // No dibujamos nada si no hay conexión o no hay TurnManager
            if (NetworkManager.Singleton == null) return;
            if (!(NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)) return;

            var tm = NetworkTurnManager.Instance;
            if (tm == null) return;

            // Escalado por resolución, igual que hace NetworkBootstrap
            float scale = Mathf.Max(0.8f, Screen.height / referenceHeight);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Centramos el panel arriba
            float virtualWidth = Screen.width / scale;
            float x = (virtualWidth - panelSize.x) * 0.5f;
            GUILayout.BeginArea(new Rect(x, topMargin, panelSize.x, panelSize.y), GUI.skin.box);

            GUI.skin.label.fontSize = 20;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;

            if (tm.InTransition.Value)
            {
                GUILayout.Label("— Cambiando de turno —");
            }
            else if (tm.CurrentTurnClientId.Value == ulong.MaxValue)
            {
                GUILayout.Label("Esperando para empezar...");
            }
            else
            {
                ulong turnOf = tm.CurrentTurnClientId.Value;
                ulong me = NetworkManager.Singleton.LocalClientId;
                bool myTurn = turnOf == me;

                string who = myTurn ? "¡TU TURNO!" : $"Turno del Jugador {turnOf}";
                string time = $"{Mathf.CeilToInt(tm.TurnTimeRemaining.Value)}s";

                GUILayout.Label($"{who}   —   {time}");
            }

            GUILayout.EndArea();
            GUI.matrix = previous;
        }
    }
}
