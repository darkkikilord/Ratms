using System.Collections.Generic;
using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [Header("Prefabs y Spawn")]
    [SerializeField] private GameObject ratPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private readonly List<PlayerMovement> players = new();
    private int current;
    private CameraFollow cam;

    private void Awake()
    {
        cam = Camera.main.GetComponent<CameraFollow>();
        if (cam == null) Debug.LogError("❌  CameraFollow no encontrado.");
    }

    private void Start()
    {
        SpawnPlayers();
        ActivatePlayer(0);
    }

    private void SpawnPlayers()
    {
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            GameObject go = Instantiate(ratPrefab, spawnPoints[i].position, Quaternion.identity);
            go.name = $"rat_{i}";
            players.Add(go.GetComponent<PlayerMovement>());
        }
        Debug.Log($"TurnManager ▶️   {players.Count} ratas creadas.");
    }

    public void EndTurn()
    {
        current = (current + 1) % players.Count;
        ActivatePlayer(current);
    }

    private void ActivatePlayer(int idx)
    {
        for (int i = 0; i < players.Count; i++)
        {
            bool myTurn = (i == idx);
            players[i].SetTurnActive(myTurn);
        }

        cam.target = players[idx].transform;
        cam.SetZoom(4.5f);

        Debug.Log($"TurnManager ▶️  Turno de {players[idx].name}");
    }
}
