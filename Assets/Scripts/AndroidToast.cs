using UnityEngine;

/// <summary>
/// Shows native Android toast messages. Falls back to Debug.Log in Editor.
/// </summary>
public static class AndroidToast
{
    public static void Show(string message, bool longDuration = false)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            var toastClass = new AndroidJavaClass("android.widget.Toast");
            int duration = longDuration ? 1 : 0; // LENGTH_LONG=1, LENGTH_SHORT=0
            var toast = toastClass.CallStatic<AndroidJavaObject>("makeText", activity, message, duration);
            toast.Call("show");
        }));
#else
        Debug.Log($"[Toast] {message}");
#endif
    }
}
