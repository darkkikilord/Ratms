using UnityEngine;

public class TurnManager : MonoBehaviour
{
    public GameObject[] players;
    private int currentPlayerIndex = 0;

    void Start() => SetActivePlayer(0);

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Length;
            SetActivePlayer(currentPlayerIndex);
        }
    }

    void SetActivePlayer(int index)
    {
        for (int i = 0; i < players.Length; i++)
        {
            players[i].GetComponent<PlayerMovement>().enabled = (i == index);
        }
    }
}