using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Small helper for controller vibration.
// Uses the XR InputDevices API, which works with OpenXR on Quest.
// Calls are ignored safely when no headset is connected (Editor + simulator).
public static class XRHaptics
{
    private static readonly List<InputDevice> devices = new List<InputDevice>();

    public static void Right(float amplitude, float duration)
    {
        Send(XRNode.RightHand, amplitude, duration);
    }

    public static void Left(float amplitude, float duration)
    {
        Send(XRNode.LeftHand, amplitude, duration);
    }

    public static void Both(float amplitude, float duration)
    {
        Send(XRNode.LeftHand, amplitude, duration);
        Send(XRNode.RightHand, amplitude, duration);
    }

    private static void Send(XRNode node, float amplitude, float duration)
    {
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(node, devices);

        foreach (InputDevice device in devices)
        {
            if (device.isValid &&
                device.TryGetHapticCapabilities(out HapticCapabilities caps) &&
                caps.supportsImpulse)
            {
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
            }
        }
    }
}
