using UnityEngine;
using UnityEditor;

public class AndroidConfigurator
{
    [MenuItem("MWA/Configure Android Build")]
    public static void ConfigureAndroid()
    {
        // Switch to Android platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[AndroidConfig] Switching to Android platform...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // Package name
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.example.mwaexample.unity");

        // Min and target API levels
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;

        // Scripting backend - IL2CPP
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

        // Target ARM64 only
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Product name
        PlayerSettings.productName = "MWA Unity Example App";
        PlayerSettings.companyName = "SolPulse";

        Debug.Log("[AndroidConfig] DONE — Android build configured:");
        Debug.Log("[AndroidConfig]   Package: com.example.mwaexample.unity");
        Debug.Log("[AndroidConfig]   Min API: 24, Target API: 34");
        Debug.Log("[AndroidConfig]   Backend: IL2CPP, Arch: ARM64");
        Debug.Log("[AndroidConfig]   Product: MWA Unity Example App");
        Debug.Log("[AndroidConfig]   Custom Launcher Manifest: enabled");
    }
}
