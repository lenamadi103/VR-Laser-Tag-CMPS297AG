using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Starts and stops LAN sessions for the existing NetworkManager.
//
// - Host: listens on all network interfaces (0.0.0.0) on the game port
//   and shows its own LAN IP address so other players can type it in.
// - Client: connects to the IP address typed on the VR keypad.
// - At most maxPlayers players are accepted (connection approval).
//
// Desktop testing: start a build with "-lasertag-host" or
// "-lasertag-client 192.168.1.20" to connect without the VR menu.
[RequireComponent(typeof(NetworkManager))]
public class LanConnectionManager : MonoBehaviour
{
    public static LanConnectionManager Instance { get; private set; }

    // Raised when the connection status text or player list changes.
    public static event Action StatusChanged;

    [SerializeField] private int maxPlayers = 5;
    [SerializeField] private ushort port = 7777;
    [Tooltip("Address shown in the join field the first time the game runs.")]
    [SerializeField] private string defaultHostAddress = "192.168.1.";

    private const string LastAddressKey = "LaserTag.LastHostAddress";

    private NetworkManager networkManager;
    private UnityTransport transport;

    public int MaxPlayers => maxPlayers;
    public ushort Port => port;

    // The IP address typed by the player (persisted between sessions).
    public string HostAddressInput { get; set; }

    // This device's LAN address (shown to the host).
    public string LocalIPAddress { get; private set; }

    public string StatusMessage { get; private set; } = "Not connected";

    public bool IsConnected =>
        networkManager != null && (networkManager.IsServer || networkManager.IsConnectedClient);

    public bool IsBusy =>
        networkManager != null && (networkManager.IsServer || networkManager.IsClient);

    private void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        HostAddressInput = PlayerPrefs.GetString(LastAddressKey, defaultHostAddress);
        LocalIPAddress = FindLocalIPv4();
    }

    private void Start()
    {
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApprovalCheck;

        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        networkManager.OnTransportFailure += OnTransportFailure;

        HandleCommandLine();
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;

            if (networkManager.ConnectionApprovalCallback == ApprovalCheck)
                networkManager.ConnectionApprovalCallback = null;
        }

        if (Instance == this)
            Instance = null;
    }

    public bool StartHost()
    {
        if (IsBusy)
            return false;

        // Listen on every interface so headsets on the LAN can connect.
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        LocalIPAddress = FindLocalIPv4();
        bool started = networkManager.StartHost();

        SetStatus(started
            ? "Hosting. Players join with IP " + LocalIPAddress
            : "Could not start hosting (is the port " + port + " in use?)");

        return started;
    }

    public bool StartClient(string address)
    {
        if (IsBusy)
            return false;

        address = (address ?? "").Trim();

        if (!IPAddress.TryParse(address, out IPAddress ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork ||
            address.Split('.').Length != 4)
        {
            SetStatus("\"" + address + "\" is not a valid IP address");
            return false;
        }

        HostAddressInput = address;
        PlayerPrefs.SetString(LastAddressKey, address);
        PlayerPrefs.Save();

        transport.SetConnectionData(address, port);
        bool started = networkManager.StartClient();

        SetStatus(started ? "Connecting to " + address + "..." : "Could not start the client");
        return started;
    }

    public void Leave()
    {
        if (!IsBusy)
            return;

        bool wasHost = networkManager.IsServer;
        networkManager.Shutdown();
        SetStatus(wasHost ? "Session closed" : "Left the session");
    }

    // Runs on the server for every connection attempt (including the host itself).
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = true;

        bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;

        if (!isHostItself && networkManager.ConnectedClientsIds.Count >= maxPlayers)
        {
            response.Approved = false;
            response.Reason = "The game is full (" + maxPlayers + " players).";
            return;
        }

        response.Approved = true;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == networkManager.LocalClientId && !networkManager.IsServer)
            SetStatus("Connected to " + HostAddressInput);
        else
            StatusChanged?.Invoke();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // On a client this means we lost the host, were rejected,
        // or the connection attempt timed out.
        if (!networkManager.IsServer)
        {
            string reason = networkManager.DisconnectReason;

            SetStatus(string.IsNullOrEmpty(reason)
                ? "Disconnected. The host left or could not be reached."
                : "Disconnected: " + reason);
        }
        else
        {
            StatusChanged?.Invoke();
        }
    }

    private void OnTransportFailure()
    {
        SetStatus("Network error. Check that all headsets are on the same Wi-Fi.");
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
        Debug.Log("[LAN] " + message);
        StatusChanged?.Invoke();
    }

    private void HandleCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-lasertag-host")
                StartHost();
            else if (args[i] == "-lasertag-client" && i + 1 < args.Length)
                StartClient(args[i + 1]);
        }
    }

    // Finds this device's LAN IPv4 address.
    public static string FindLocalIPv4()
    {
        // 1) Ask the OS which interface it would use to reach the internet.
        //    Connecting a UDP socket sends no packets.
        try
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect("8.8.8.8", 65530);

                if (socket.LocalEndPoint is IPEndPoint endPoint &&
                    !IPAddress.IsLoopback(endPoint.Address))
                    return endPoint.Address.ToString();
            }
        }
        catch (Exception)
        {
            // No default route (e.g. router without internet). Try the interfaces.
        }

        // 2) Otherwise pick a private address from the active interfaces.
        try
        {
            List<string> candidates = new List<string>();

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation info in ni.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(info.Address))
                        candidates.Add(info.Address.ToString());
                }
            }

            foreach (string candidate in candidates)
            {
                if (candidate.StartsWith("192.168.") || candidate.StartsWith("10.") || candidate.StartsWith("172."))
                    return candidate;
            }

            if (candidates.Count > 0)
                return candidates[0];
        }
        catch (Exception)
        {
            // Ignore and fall through.
        }

        return "unknown";
    }
}
