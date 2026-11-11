using UnityEngine;
using UnityEngine.XR;

// Put on the XR Device Simulator. The simulator is kept for testing in
// the Editor and in Windows builds, but removed on Quest (Android) or
// whenever a real headset is running.
//
// The simulator must never start on a headset: when it starts it removes
// the real headset from the Input System, so head and controller tracking
// break. This script therefore runs before every other script and turns the
// simulator object off before the simulator's own Awake/OnEnable can run.
// Quest builds also strip the simulator from the scene (StripSimulatorFromQuestBuild).
[DefaultExecutionOrder(-32000)]
public class DisableWhenHeadsetActive : MonoBehaviour
{
    private void Awake()
    {
        if (Application.platform == RuntimePlatform.Android || XRSettings.isDeviceActive)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
