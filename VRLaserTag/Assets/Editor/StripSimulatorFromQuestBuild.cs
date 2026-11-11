using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Removes the XR Device Simulator from scenes when building for Android
// (Meta Quest). On a real headset the simulator would remove the headset
// from the Input System and break head and controller tracking.
// The scene file itself is not changed; Editor Play mode keeps the simulator.
public class StripSimulatorFromQuestBuild : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // report is null when entering Play mode in the Editor.
        if (report == null || report.summary.platform != BuildTarget.Android)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<DisableWhenHeadsetActive>() != null)
            {
                Debug.Log("[StripSimulatorFromQuestBuild] Removed '" + root.name + "' from the Quest build.");
                Object.DestroyImmediate(root);
            }
        }
    }
}
