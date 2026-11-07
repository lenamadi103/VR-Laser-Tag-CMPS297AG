
using UnityEngine;
using Unity.Netcode;

public class SpawnManager : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;

    private int nextSpawnIndex = 0;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // Only the server decides where players spawn.
        if (!NetworkManager.Singleton.IsServer)
            return;

        if (spawnPoints.Length == 0)
            return;

        // Find the player object belonging to this client.
        NetworkObject player =
            NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;

        if (player == null)
            return;

        // Choose the next spawn point.
        Transform spawnPoint = spawnPoints[nextSpawnIndex];

        // Move the player to that position.
        player.transform.SetPositionAndRotation(
            spawnPoint.position,
            spawnPoint.rotation
        );


        NetworkVRPlayer networkVRPlayer =
            player.GetComponent<NetworkVRPlayer>();

        if (networkVRPlayer != null)
        {
            networkVRPlayer.InitializeSpawnRpc(
            spawnPoint.position,
            networkVRPlayer.RpcTarget.Single(clientId, RpcTargetUse.Temp)
            );
        }


        // Advance to the next spawn point.
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;

        Debug.Log("Spawned client " + clientId +
                  " at " + spawnPoint.position);
    }
}
