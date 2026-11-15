using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// World-space lobby / menu panel used with the controller ray.
//
// Not connected: type the host's IP on the keypad, then HOST or JOIN.
// Connected:     shows your player name, the host IP, the match state and
//                the player list. The host also gets START MATCH / PLAY AGAIN.
//
// The panel appears in front of the player. It hides when the countdown starts
// and reappears when the match ends. The left controller's menu button
// (or ` on the keyboard) shows/hides it at any time.
//
// The UI only *requests* actions; all game state stays on the server.
public class VRMenuUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject connectSection;
    [SerializeField] private GameObject sessionSection;
    [Tooltip("The VR camera. Found automatically when empty.")]
    [SerializeField] private Transform head;
    [SerializeField] private float distance = 1.3f;
    [SerializeField] private float heightOffset = -0.2f;

    [Header("Texts")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text addressText;
    [SerializeField] private Text sessionText;
    [SerializeField] private Text playersText;
    [SerializeField] private Text startButtonLabel;

    [Header("Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button closeButton;

    [Tooltip("Shows/hides the menu. Defaults: left controller menu button, or ` on the keyboard.")]
    public InputAction toggleAction = new InputAction("Toggle Menu", InputActionType.Button);

    private const int MaxAddressLength = 15;

    // True while the menu panel is shown (the HUD hides big messages then).
    public static bool IsVisible { get; private set; }

    private bool visible;
    private float nextRefreshTime;
    private float recenterUntil;
    private bool placedInSession; // first spawn of this session handled

    private void Awake()
    {
        if (toggleAction.bindings.Count == 0)
        {
            toggleAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            toggleAction.AddBinding("<Keyboard>/backquote");
        }

        if (hostButton != null) hostButton.onClick.AddListener(OnHostPressed);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinPressed);
        if (startButton != null) startButton.onClick.AddListener(OnStartPressed);
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeavePressed);
        if (closeButton != null) closeButton.onClick.AddListener(() => SetVisible(false));

        // Keypad buttons are named "Key_0" ... "Key_9", "Key_dot", "Key_back", "Key_clear".
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (!button.name.StartsWith("Key_"))
                continue;

            string key = button.name.Substring(4);
            button.onClick.AddListener(() => OnKeyPressed(key));
        }
    }

    private void OnEnable()
    {
        toggleAction.Enable();
        MatchManager.StateChanged += OnMatchStateChanged;
        LanConnectionManager.StatusChanged += OnConnectionStatusChanged;
        NetworkVRPlayer.LocalRigPlaced += OnLocalRigPlaced;
    }

    private void OnDisable()
    {
        toggleAction.Disable();
        MatchManager.StateChanged -= OnMatchStateChanged;
        LanConnectionManager.StatusChanged -= OnConnectionStatusChanged;
        NetworkVRPlayer.LocalRigPlaced -= OnLocalRigPlaced;
        IsVisible = false;
    }

    // The player was moved and turned to a spawn point: bring the panel
    // back in front of them.
    private void OnLocalRigPlaced()
    {
        // First spawn after joining a match that is already running: close
        // the lobby so the player sees the arena and the HUD right away
        // (the countdown event that normally closes it happened before
        // they joined).
        if (!placedInSession)
        {
            placedInSession = true;
            MatchManager match = MatchManager.Instance;
            if (visible && match != null && match.IsSpawned &&
                (match.State.Value == MatchState.Playing || match.State.Value == MatchState.Countdown))
            {
                SetVisible(false);
                return;
            }
        }

        if (!visible)
            return;

        recenterUntil = Time.unscaledTime + 0.5f;
        Recenter();
    }

    private void Start()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        SetVisible(true);
    }

    private void Update()
    {
        if (toggleAction.WasPressedThisFrame())
            SetVisible(!visible);

        // Keep the panel in front of the player while tracking starts up,
        // and bring it back if the player was moved (spawn or teleport).
        if (visible && head != null)
        {
            Vector3 offset = transform.position - head.position;
            offset.y = 0f;

            if (Time.unscaledTime < recenterUntil || offset.magnitude > 3f)
                Recenter();
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + 0.2f;
            Refresh();
        }
    }

    public void SetVisible(bool show)
    {
        bool wasVisible = visible;
        visible = show;
        IsVisible = show;

        if (panelRoot != null)
            panelRoot.SetActive(show);

        if (show && !wasVisible)
        {
            // Recenter for a short moment so the panel lands in front of
            // the headset even if tracking was not ready on the first frame.
            recenterUntil = Time.unscaledTime + 0.5f;
            Recenter();
            Refresh();
        }
    }

    private void Recenter()
    {
        if (head == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 position = head.position + forward * distance;
        position.y = head.position.y + heightOffset;

        transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
    }

    private void Refresh()
    {
        LanConnectionManager lan = LanConnectionManager.Instance;
        NetworkManager networkManager = NetworkManager.Singleton;

        if (lan == null || networkManager == null)
            return;

        bool inSession = lan.IsBusy;

        if (connectSection != null) connectSection.SetActive(!inSession);
        if (sessionSection != null) sessionSection.SetActive(inSession);

        if (statusText != null)
            statusText.text = lan.StatusMessage;

        if (addressText != null)
            addressText.text = (lan.HostAddressInput ?? "") + "_";

        if (!inSession)
            return;

        MatchManager match = MatchManager.Instance;

        if (!lan.IsConnected)
        {
            if (sessionText != null) sessionText.text = "Connecting to " + lan.HostAddressInput + "...";
            if (playersText != null) playersText.text = "";
            if (startButton != null) startButton.gameObject.SetActive(false);
            return;
        }

        PlayerInfo me = PlayerInfo.FindByClientId(networkManager.LocalClientId);
        string myName = me != null ? me.ColoredName : "...";
        string role = networkManager.IsHost ? "host" : "player";
        string hostIp = networkManager.IsServer ? lan.LocalIPAddress : lan.HostAddressInput;

        if (sessionText != null)
        {
            sessionText.text =
                "You are " + myName + " (" + role + ")\n" +
                "Host IP: <b>" + hostIp + "</b>\n\n" +
                DescribeMatch(match, networkManager.IsServer, networkManager.LocalClientId);
        }

        if (playersText != null)
            playersText.text = DescribePlayers(lan.MaxPlayers);

        if (startButton != null)
        {
            bool showStart = networkManager.IsServer && match != null &&
                             match.State.Value != MatchState.Playing &&
                             match.State.Value != MatchState.Countdown;

            startButton.gameObject.SetActive(showStart);
            startButton.interactable = match != null && match.CanStart;

            if (startButtonLabel != null && match != null)
                startButtonLabel.text = match.State.Value == MatchState.Finished ? "PLAY AGAIN" : "START MATCH";
        }
    }

    private static string DescribeMatch(MatchManager match, bool isHost, ulong localClientId)
    {
        if (match == null || !match.IsSpawned)
            return "Joining the arena...";

        switch (match.State.Value)
        {
            case MatchState.WaitingForPlayers:
                if (match.PlayerCount < match.MinPlayers)
                    return "Waiting for players (" + match.PlayerCount + "/" + match.MinPlayers + " needed)";
                return isHost ? "Ready! Press START MATCH." : "Ready. Waiting for the host to start.";

            case MatchState.Countdown:
                return "Match starting in " + Mathf.CeilToInt(match.RemainingSeconds) + "...";

            case MatchState.Playing:
                return "Match in progress: " + MatchManager.FormatTime(match.RemainingSeconds) + " left";

            case MatchState.Finished:
                string headline =
                    match.WinnerClientId.Value == localClientId ? "<color=#FFD54A><b>YOU WIN!</b></color>" :
                    match.WinnerClientId.Value == MatchManager.NoWinner ? "<b>MATCH OVER - IT'S A TIE</b>" :
                    "<b>MATCH OVER</b>";

                return headline + "\n" + match.ResultText.Value.ToString() +
                       (isHost ? "\nPress PLAY AGAIN for another round." : "\nWaiting for the host to restart.");
        }

        return "";
    }

    private static string DescribePlayers(int maxPlayers)
    {
        var standings = MatchManager.GetStandings();
        string text = "<b>Players (" + standings.Count + "/" + maxPlayers + ")</b>\n";

        foreach (PlayerInfo info in standings)
            text += info.ColoredName + "   " + info.Score.Value + " pts\n";

        return text;
    }

    private void OnKeyPressed(string key)
    {
        LanConnectionManager lan = LanConnectionManager.Instance;
        if (lan == null)
            return;

        string address = lan.HostAddressInput ?? "";

        switch (key)
        {
            case "back":
                if (address.Length > 0)
                    address = address.Substring(0, address.Length - 1);
                break;

            case "clear":
                address = "";
                break;

            case "dot":
                if (address.Length < MaxAddressLength)
                    address += ".";
                break;

            default:
                if (address.Length < MaxAddressLength)
                    address += key;
                break;
        }

        lan.HostAddressInput = address;
        Refresh();
    }

    private void OnHostPressed()
    {
        if (LanConnectionManager.Instance != null)
            LanConnectionManager.Instance.StartHost();
    }

    private void OnJoinPressed()
    {
        LanConnectionManager lan = LanConnectionManager.Instance;
        if (lan != null)
            lan.StartClient(lan.HostAddressInput);
    }

    private void OnStartPressed()
    {
        MatchManager match = MatchManager.Instance;

        if (match != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            match.ServerStartMatch();
    }

    private void OnLeavePressed()
    {
        if (LanConnectionManager.Instance != null)
            LanConnectionManager.Instance.Leave();
    }

    private void OnMatchStateChanged(MatchState previous, MatchState current)
    {
        // Close the menu when the countdown starts, so players can see the
        // big countdown on the HUD and get ready.
        if (current == MatchState.Countdown || current == MatchState.Playing)
            SetVisible(false);
        else if (current == MatchState.Finished)
            SetVisible(true);
    }

    private void OnConnectionStatusChanged()
    {
        // Show the menu again after leaving or losing the connection.
        LanConnectionManager lan = LanConnectionManager.Instance;
        if (lan != null && !lan.IsBusy)
        {
            placedInSession = false;
            SetVisible(true);
        }

        Refresh();
    }
}
