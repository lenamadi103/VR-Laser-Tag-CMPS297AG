
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Server-side spawn system.
//
// - First spawn: when a client connects.
// - Respawn: when PlayerCombat's respawn delay has passed.
// - Match start: MatchManager moves everyone to separate spawn points.
//
// The chosen spawn point is the one furthest from other living
// players, so nobody respawns on top of (or right next to) an enemy.
// Moving a player always goes through NetworkVRPlayer.ServerMoveToSpawn,
// which moves both the networked avatar and the owner's local XR Origin.
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private Transform[] spawnPoints;

    public int SpawnPointCount => spawnPoints != null ? spawnPoints.Length : 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnClientConnected(ulong clientId)
    {
        // Only the server decides where players spawn.
        if (!NetworkManager.Singleton.IsServer)
            return;

        if (SpawnPointCount == 0)
            return;

        // Find the player object belonging to this client.
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return;

        NetworkObject player = client.PlayerObject;

        if (player == null)
            return;

        Transform spawnPoint = ChooseSpawnPoint(OtherLivingPlayerPositions(clientId));
        PlacePlayer(player, spawnPoint);
    }

    // Server only. Called by PlayerCombat when the respawn delay ends.
    public void ServerRespawnPlayer(NetworkObject player)
    {
        if (player == null || SpawnPointCount == 0)
            return;

        Transform spawnPoint = ChooseSpawnPoint(OtherLivingPlayerPositions(player.OwnerClientId));
        PlacePlayer(player, spawnPoint);
    }

    // Server only. Called at match start: every player gets a
    // different spawn point (up to the number of spawn points).
    public void ServerRespawnAll()
    {
        if (SpawnPointCount == 0)
            return;

        List<Vector3> used = new List<Vector3>();

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            Transform spawnPoint = ChooseSpawnPoint(used);
            used.Add(spawnPoint.position);
            PlacePlayer(client.PlayerObject, spawnPoint);
        }
    }

    private void PlacePlayer(NetworkObject player, Transform spawnPoint)
    {
        NetworkVRPlayer networkVRPlayer =
            player.GetComponent<NetworkVRPlayer>();

        if (networkVRPlayer != null)
        {
            // Moves the networked root and tells the owner
            // to move their local XR Origin.
            networkVRPlayer.ServerMoveToSpawn(
                spawnPoint.position,
                spawnPoint.rotation
            );
        }
        else
        {
            player.transform.position = spawnPoint.position;
        }

        Debug.Log("Spawned client " + player.OwnerClientId +
                  " at " + spawnPoint.name);
    }

    // Picks the spawn point whose nearest "avoid" position is furthest away.
    // With nothing to avoid, a random spawn point is used.
    private Transform ChooseSpawnPoint(List<Vector3> avoid)
    {
        Transform best = spawnPoints[0];
        float bestScore = float.MinValue;

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null)
                continue;

            float nearest = 1000f;

            foreach (Vector3 position in avoid)
            {
                Vector3 offset = spawnPoint.position - position;
                offset.y = 0f;
                nearest = Mathf.Min(nearest, offset.magnitude);
            }

            // Small random value breaks ties between equal choices.
            float score = nearest + Random.value * 0.01f;

            if (score > bestScore)
            {
                bestScore = score;
                best = spawnPoint;
            }
        }

        return best;
    }

    private static List<Vector3> OtherLivingPlayerPositions(ulong excludeClientId)
    {
        List<Vector3> positions = new List<Vector3>();

        foreach (PlayerCombat player in PlayerCombat.All)
        {
            if (player == null ||
                player.OwnerClientId == excludeClientId ||
                player.IsEliminated.Value)
                continue;

            Transform head = player.NetworkHead != null ? player.NetworkHead : player.transform;
            positions.Add(head.position);
        }

        return positions;
    }
}
