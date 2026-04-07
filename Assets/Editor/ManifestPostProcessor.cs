using UnityEditor.Android;
using UnityEngine;
using System.IO;

public class ManifestPostProcessor : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 99;

    private const string ACTIVITY_BLOCK = @"
    <activity
        android:name=""com.unity3d.player.UnityPlayerGameActivity""
        android:theme=""@style/BaseUnityGameActivityTheme""
        android:enabled=""true""
        android:exported=""true""
        android:configChanges=""mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|fontScale|layoutDirection|density""
        android:hardwareAccelerated=""false""
        android:launchMode=""singleTask""
        android:screenOrientation=""fullUser""
        android:resizeableActivity=""true"">
        <intent-filter>
            <action android:name=""android.intent.action.MAIN"" />
            <category android:name=""android.intent.category.LAUNCHER"" />
        </intent-filter>
    </activity>";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        FixUnityLibraryManifest(path);
        FixLauncherManifest(path);
    }

    private void FixUnityLibraryManifest(string path)
    {
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning("[ManifestPostProcessor] unityLibrary manifest not found");
            return;
        }

        string content = File.ReadAllText(manifestPath);
        bool modified = false;

        // Fix any existing enabled="false" → enabled="true"
        if (content.Contains("android:enabled=\"false\""))
        {
            content = content.Replace("android:enabled=\"false\"", "android:enabled=\"true\"");
            modified = true;
            Debug.Log("[ManifestPostProcessor] Fixed enabled=false → enabled=true");
        }

        // If no activity exists, inject one
        if (!content.Contains("UnityPlayerGameActivity") && !content.Contains("UnityPlayerActivity"))
        {
            // Insert activity before </application>
            if (content.Contains("</application>"))
            {
                content = content.Replace("</application>", ACTIVITY_BLOCK + "\n  </application>");
                modified = true;
                Debug.Log("[ManifestPostProcessor] Injected UnityPlayerGameActivity into unityLibrary manifest");
            }
        }

        if (modified)
        {
            File.WriteAllText(manifestPath, content);
            Debug.Log("[ManifestPostProcessor] unityLibrary manifest patched successfully");
        }
    }

    private void FixLauncherManifest(string path)
    {
        string launcherManifest = Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"));
        if (!File.Exists(launcherManifest))
        {
            Debug.LogWarning("[ManifestPostProcessor] Launcher manifest not found");
            return;
        }

        string content = File.ReadAllText(launcherManifest);
        if (!content.Contains("tools:replace"))
        {
            if (!content.Contains("xmlns:tools"))
            {
                content = content.Replace(
                    "xmlns:android=\"http://schemas.android.com/apk/res/android\"",
                    "xmlns:android=\"http://schemas.android.com/apk/res/android\" xmlns:tools=\"http://schemas.android.com/tools\"");
            }
            content = content.Replace("<application ", "<application tools:replace=\"android:label,android:icon\" ");
            File.WriteAllText(launcherManifest, content);
            Debug.Log("[ManifestPostProcessor] Launcher manifest patched with tools:replace");
        }
    }
}
