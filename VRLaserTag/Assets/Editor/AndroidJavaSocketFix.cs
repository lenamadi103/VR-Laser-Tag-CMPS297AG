using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Fix for "Gradle build failed: java.io.IOException: Unable to establish
// loopback connection" on some Windows PCs.
//
// Java 17 (used by Unity's Android/Gradle build) creates a small Unix-domain
// socket file in the Windows TEMP folder. On some machines that folder does
// not support these sockets, so Gradle cannot start and the APK build fails.
//
// During Android builds only, TEMP/TMP point to Library/JavaTemp (inherited
// by the Java/Gradle processes Unity starts). They are restored afterwards.
// Does nothing on macOS/Linux or for other build targets.
public class AndroidJavaSocketFix : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private static string savedTemp;
    private static string savedTmp;

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor ||
            report.summary.platform != BuildTarget.Android)
            return;

        string folder = Path.GetFullPath(Path.Combine("Library", "JavaTemp"));
        Directory.CreateDirectory(folder);

        savedTemp = Environment.GetEnvironmentVariable("TEMP");
        savedTmp = Environment.GetEnvironmentVariable("TMP");

        Environment.SetEnvironmentVariable("TEMP", folder);
        Environment.SetEnvironmentVariable("TMP", folder);
    }

    // Not called when a build fails; TEMP then stays on Library/JavaTemp
    // until the Editor restarts, which is harmless.
    public void OnPostprocessBuild(BuildReport report)
    {
        if (savedTemp != null)
            Environment.SetEnvironmentVariable("TEMP", savedTemp);

        if (savedTmp != null)
            Environment.SetEnvironmentVariable("TMP", savedTmp);

        savedTemp = null;
        savedTmp = null;
    }
}
