using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [Header("Ratas jugables")]
    public GameObject[] players;

    private int currentPlayerIndex = 0;
    private CameraFollow cameraFollow;

    void Start()
    {
        cameraFollow = Camera.main.GetComponent<CameraFollow>();

        if (cameraFollow == null)
        {
            Debug.LogError("No se encontró el script CameraFollow en la cámara principal.");
        }

        SetActivePlayer(0);
    }

    // Eliminamos el cambio de turno manual aquí 👇

    public void EndTurn()
    {
        currentPlayerIndex = (currentPlayerIndex + 1) % players.Length;
        SetActivePlayer(currentPlayerIndex);
    }

    void SetActivePlayer(int index)
    {
        for (int i = 0; i < players.Length; i++)
        {
            var movement = players[i].GetComponent<PlayerMovement>();

            if (movement != null)
            {
                bool isActive = (i == index);
                movement.enabled = isActive;

                if (isActive)
                {
                    movement.ResetTurn(); // reinicia variables de disparo
                }
            }
        }

        if (cameraFollow != null)
        {
            cameraFollow.target = players[index].transform;
            cameraFollow.SetZoom(4.5f);
        }
    }
}
