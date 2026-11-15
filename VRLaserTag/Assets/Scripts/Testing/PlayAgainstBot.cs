#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

// Development-only: the bot is a separate, ordinary network client.
public class PlayAgainstBot : MonoBehaviour
{
    private const string BotArgument = "-lasertag-bot";
    private const float ConnectionTimeout = 30f;
    private const float ShotInterval = 1f;
    private const float MoveSpeed = 1f;
    private float nextShotTime;
    private bool connected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        int botIndex = Array.IndexOf(args, BotArgument);

        GameObject controller = new GameObject("PlayAgainstBotTest");
        PlayAgainstBot test = controller.AddComponent<PlayAgainstBot>();
        if (botIndex >= 0)
        {
            if (botIndex + 1 >= args.Length || args[botIndex + 1].StartsWith("-"))
            {
                Debug.LogError("[Bot test] Supply the host IP after -lasertag-bot.");
                Application.Quit(1);
                return;
            }

            test.StartCoroutine(test.RunBot(args[botIndex + 1]));
        }
        else
        {
            test.AddTestButton();
        }
    }

    private void AddTestButton()
    {
        VRMenuUI menu = FindAnyObjectByType<VRMenuUI>();
        Transform connect = menu != null ? menu.transform.Find("MenuCanvas/ConnectSection") : null;
        Transform host = connect != null ? connect.Find("HostButton") : null;
        Transform help = connect != null ? connect.Find("Help") : null;
        if (host == null || help == null)
        {
            Debug.LogError("[Bot test] Could not find the VR lobby's host button or help text.");
            return;
        }

        Button button = Instantiate(host.gameObject, connect).GetComponent<Button>();
        button.name = "PlayAgainstBotButton";
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(StartTestHost);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.04f, 0.49f);
        rect.anchorMax = new Vector2(0.46f, 0.63f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text label = button.GetComponentInChildren<Text>();
        label.text = "PLAY AGAINST A BOT\n(TEST ONLY)";
        label.fontSize = 25;

        Text helpText = help.GetComponent<Text>();
        helpText.text = "BOT TEST: run the PC bot using the host IP.\n" +
                        "When it joins, press START MATCH.\n\n" +
                        "For multiplayer, host or join on the same Wi-Fi.\n\n" +
                        "Trigger: fire / click    A: reload\n" +
                        "Thumbstick: teleport    Left menu: show/hide";
        RectTransform helpRect = help.GetComponent<RectTransform>();
        helpRect.anchorMax = new Vector2(helpRect.anchorMax.x, 0.46f);
        helpRect.offsetMax = Vector2.zero;
        helpText.fontSize = 19;
    }

    private static void StartTestHost()
    {
        LanConnectionManager lan = LanConnectionManager.Instance;
        if (lan == null)
        {
            Debug.LogError("[Bot test] LAN connection manager is missing.");
            return;
        }

        if (!lan.StartHost())
            Debug.LogError("[Bot test] Could not host the bot test: " + lan.StatusMessage);
    }

    private IEnumerator RunBot(string address)
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        yield return null;

        LanConnectionManager lan = LanConnectionManager.Instance;
        if (lan == null || !lan.StartClient(address))
        {
            Debug.LogError("[Bot test] Could not start client: " +
                           (lan != null ? lan.StatusMessage : "LAN connection manager is missing."));
            Application.Quit(1);
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + ConnectionTimeout;
        while (true)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsConnectedClient)
                connected = true;
            else if (connected)
            {
                Debug.Log("[Bot test] Disconnected from host; stopping bot.");
                Application.Quit();
                yield break;
            }
            else if (Time.realtimeSinceStartup >= deadline || !lan.IsBusy)
            {
                Debug.LogError("[Bot test] Connection failed: " + lan.StatusMessage);
                Application.Quit(1);
                yield break;
            }

            Act();
            yield return null;
        }
    }

    private void Act()
    {
        PlayerCombat self = PlayerCombat.Local;
        NetworkVRPlayer rig = NetworkVRPlayer.Local;
        MatchManager match = MatchManager.Instance;
        if (self == null || rig == null || !rig.IsTrackingReady || self.NetworkHead == null ||
            self.IsEliminated.Value || match == null || !match.IsCombatActive)
            return;

        PlayerCombat target = null;
        float nearest = float.PositiveInfinity;
        foreach (PlayerCombat other in PlayerCombat.All)
        {
            if (other == null || other == self || other.IsEliminated.Value || other.NetworkHead == null)
                continue;

            float distance = (other.NetworkHead.position - self.NetworkHead.position).sqrMagnitude;
            if (distance < nearest)
            {
                nearest = distance;
                target = other;
            }
        }

        if (target == null)
            return;

        Vector3 eye = self.NetworkHead.position;
        Vector3 origin = eye + Vector3.down * 0.2f;
        Vector3 aim = target.NetworkHead.position;
        bool blocked = Physics.Linecast(origin, aim, PlayerCombat.ObstacleMask, QueryTriggerInteraction.Ignore);
        MoveAndFace(rig.LocalXROrigin, aim, blocked);

        if (Time.time < nextShotTime)
            return;

        if (self.Ammo.Value == 0)
        {
            self.RequestReload();
            return;
        }

        if (blocked || !self.CanFireLocally(out _))
            return;

        nextShotTime = Time.time + Mathf.Max(ShotInterval, self.FireInterval);
        self.RequestFire(origin, (aim - origin).normalized, eye);
    }

    private static void MoveAndFace(XROrigin origin, Vector3 target, bool blocked)
    {
        if (origin == null || origin.Camera == null)
            return;

        Vector3 direction = Vector3.ProjectOnPlane(target - origin.Camera.transform.position, Vector3.up);
        float distance = direction.magnitude;
        if (distance < 0.01f)
            return;

        direction /= distance;
        Vector3 forward = Vector3.ProjectOnPlane(origin.Camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f)
            origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(forward, direction, Vector3.up));

        CharacterController controller = origin.GetComponent<CharacterController>();
        if (controller == null || !controller.enabled || (!blocked && distance <= 6f))
            return;

        if (blocked)
        {
            float side = Mathf.FloorToInt(Time.time / 3f) % 2 == 0 ? 1f : -1f;
            direction = (direction + Vector3.Cross(Vector3.up, direction) * side).normalized;
        }

        controller.Move((direction * MoveSpeed + Vector3.down * 0.1f) * Time.deltaTime);
    }
}
#endif
