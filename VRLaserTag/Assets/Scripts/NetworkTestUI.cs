
using UnityEngine;
using Unity.Netcode;

public class NetworkTestUI : MonoBehaviour
{
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager manager = NetworkManager.Singleton;

        if (!manager.IsClient && !manager.IsServer)
        {
            if (GUI.Button(new Rect(10, 10, 150, 40), "Start Host"))
                manager.StartHost();

            if (GUI.Button(new Rect(10, 60, 150, 40), "Start Client"))
                manager.StartClient();
        }
        else
        {
            string role = manager.IsHost ? "Host" :
                          manager.IsServer ? "Server" : "Client";

            GUI.Label(new Rect(10, 10, 250, 30),
                "Network role: " + role);

            if (manager.IsServer)
            {
                GUI.Label(new Rect(10, 40, 250, 30),
                    "Connected players: " +
                    manager.ConnectedClientsIds.Count);
            }

            if (manager.IsClient)
            {
                GUI.Label(new Rect(10, 70, 300, 30),
                    "Connected: " + manager.IsConnectedClient);
            }
        }
    }
}
