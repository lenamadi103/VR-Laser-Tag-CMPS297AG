using UnityEngine;
using UnityEngine.XR;

// Put on the XR Device Simulator. The simulator is kept for testing in
// the Editor and in Windows builds, but removed on Quest (Android) or
// whenever a real headset is running, where it would fight real input.
public class DisableWhenHeadsetActive : MonoBehaviour
{
    private void Awake()
    {
        if (Application.platform == RuntimePlatform.Android || XRSettings.isDeviceActive)
            Destroy(gameObject);
    }
}
