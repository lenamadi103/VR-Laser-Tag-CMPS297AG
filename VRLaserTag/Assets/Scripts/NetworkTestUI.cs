
using UnityEngine;
using Unity.Netcode;

// Desktop testing panel (Editor and Windows builds only).
// The real interface in the headset is VRMenuUI; this panel is drawn on
// the monitor so Host/Client sessions can be tested with mouse and keyboard.
public class NetworkTestUI : MonoBehaviour
{
    private string address;

    private void Start()
    {
        // OnGUI is not visible inside a headset, so it is not used on Quest.
        if (Application.platform == RuntimePlatform.Android)
            enabled = false;
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager manager = NetworkManager.Singleton;
        LanConnectionManager lan = LanConnectionManager.Instance;

        if (address == null)
            address = lan != null ? lan.HostAddressInput : "127.0.0.1";

        if (!manager.IsClient && !manager.IsServer)
        {
            if (GUI.Button(new Rect(10, 10, 150, 40), "Start Host"))
            {
                if (lan != null) lan.StartHost();
                else manager.StartHost();
            }

            GUI.Label(new Rect(170, 10, 60, 20), "Host IP:");
            address = GUI.TextField(new Rect(170, 30, 140, 20), address);

            if (GUI.Button(new Rect(10, 60, 150, 40), "Start Client"))
            {
                if (lan != null) lan.StartClient(address);
                else manager.StartClient();
            }

            if (lan != null)
                GUI.Label(new Rect(10, 110, 500, 30), lan.StatusMessage);
        }
        else
        {
            string role = manager.IsHost ? "Host" :
                          manager.IsServer ? "Server" : "Client";

            GUI.Label(new Rect(10, 10, 400, 30),
                "Network role: " + role +
                (lan != null && manager.IsServer ? "  (IP " + lan.LocalIPAddress + ")" : ""));

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

            MatchManager match = MatchManager.Instance;

            if (match != null && match.IsSpawned)
            {
                GUI.Label(new Rect(10, 100, 400, 30),
                    "Match: " + match.State.Value + "  " +
                    MatchManager.FormatTime(match.RemainingSeconds));

                if (manager.IsServer && match.CanStart &&
                    GUI.Button(new Rect(10, 130, 150, 30), "Start Match"))
                {
                    match.ServerStartMatch();
                }
            }

            if (GUI.Button(new Rect(170, 130, 100, 30), "Leave"))
            {
                if (lan != null) lan.Leave();
                else manager.Shutdown();
            }
        }
    }
}
