using System;
using System.Collections;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEngine;

// Visual check of the UI (does nothing in normal play).
//
// Activated only by "-lasertag-screenshots <folder>" on a windowed desktop build:
//   VRLaserTag.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -lasertag-screenshots C:\shots
//
// Hosts a solo match and saves a PNG of each important screen:
// lobby, session, countdown, HUD, damage, scoreboard, eliminated, results.
public class ScreenshotTour : MonoBehaviour
{
    private string folder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-lasertag-screenshots");
        if (index < 0 || index + 1 >= args.Length)
            return;

        GameObject go = new GameObject("ScreenshotTour");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenshotTour>().folder = args[index + 1];
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        Directory.CreateDirectory(folder);

        yield return new WaitForSeconds(2.5f);
        PrepareHeadsetLikeView();
        yield return new WaitForSeconds(0.3f);
        yield return Capture("1_lobby_connect");

        LanConnectionManager.Instance.StartHost();
        yield return new WaitUntil(() =>
            MatchManager.Instance != null && MatchManager.Instance.IsSpawned && PlayerCombat.Local != null);

        MatchManager match = MatchManager.Instance;
        PlayerCombat me = PlayerCombat.Local;
        match.ConfigureForTesting(1, 12f, 3f);

        yield return new WaitForSeconds(2f);
        yield return Capture("2_lobby_session");

        match.ServerStartMatch();
        yield return new WaitForSeconds(1.2f);
        yield return Capture("3_countdown");

        yield return new WaitUntil(() => match.State.Value == MatchState.Playing);
        yield return new WaitForSeconds(0.3f);
        yield return Capture("4_hud_go");

        // Fire a few shots so the ammo counter changes.
        for (int i = 0; i < 3; i++)
        {
            Transform head = Camera.main.transform;
            me.RequestFire(head.position, head.forward);
            yield return new WaitForSeconds(0.35f);
        }

        me.ServerApplyDamage(25, null);
        yield return new WaitForSeconds(0.1f);
        yield return Capture("5_hud_damage");

        // Walk to the middle and look at the north scoreboard.
        FaceDirection(new Vector3(0f, 0f, 2.5f), Vector3.forward);
        yield return new WaitForSeconds(0.5f);
        yield return Capture("6_scoreboard");

        me.ServerApplyDamage(me.MaxHealth, null);
        yield return new WaitForSeconds(0.6f);
        yield return Capture("7_eliminated");

        yield return new WaitUntil(() => match.State.Value == MatchState.Finished);
        yield return new WaitForSeconds(1.5f);
        yield return Capture("8_results");

        Debug.Log("SCREENSHOT TOUR DONE: " + folder);
        LanConnectionManager.Instance.Leave();
        Application.Quit();
    }

    // Desktop-only clutter is hidden and the hands are placed where a
    // player would hold them, so the screenshots resemble the headset view.
    private static void PrepareHeadsetLikeView()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                canvas.enabled = false;
        }

        NetworkTestUI desktopPanel = FindAnyObjectByType<NetworkTestUI>();
        if (desktopPanel != null)
            desktopPanel.enabled = false;

        XROrigin origin = FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return;

        Vector3 eye = origin.Camera.transform.localPosition;
        PlaceHand(origin.transform.Find("Camera Offset/Left Hand"), eye + new Vector3(-0.2f, -0.4f, 0.3f));
        PlaceHand(origin.transform.Find("Camera Offset/Right Hand"), eye + new Vector3(0.18f, -0.33f, 0.38f));
    }

    private static void PlaceHand(Transform hand, Vector3 localPosition)
    {
        if (hand == null)
            return;

        foreach (Behaviour driver in hand.GetComponents<Behaviour>())
        {
            if (driver.GetType().Name == "TrackedPoseDriver")
                driver.enabled = false;
        }

        hand.localPosition = localPosition;
        hand.localRotation = Quaternion.Euler(10f, 0f, 0f);
    }

    private IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
        yield return null;
        yield return new WaitForSeconds(0.2f);
        Debug.Log("Screenshot: " + name);
    }

    private static void FaceDirection(Vector3 position, Vector3 forward)
    {
        XROrigin origin = NetworkVRPlayer.Local != null ? NetworkVRPlayer.Local.LocalXROrigin : null;
        if (origin == null)
            return;

        CharacterController cc = origin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        origin.MatchOriginUpCameraForward(Vector3.up, forward);
        Vector3 headOffset = origin.Camera.transform.position - origin.transform.position;
        origin.transform.position = new Vector3(position.x - headOffset.x, origin.transform.position.y, position.z - headOffset.z);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = true;
    }
}
