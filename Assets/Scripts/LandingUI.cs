using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class LandingUI : MonoBehaviour
{
    private const string TAG = "[LandingUI]";

    [Header("UI References")]
    public Button connectButton;
    public Button reconnectButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI titleText;

    private void Start()
    {
        Debug.Log($"{TAG} Start | BEGIN");

        connectButton.onClick.AddListener(OnConnectPressed);
        reconnectButton.onClick.AddListener(OnReconnectPressed);

        MWAManager.Instance.OnAuthorized += OnAuthorized;
        MWAManager.Instance.OnAuthorizationFailed += OnAuthFailed;
        MWAManager.Instance.OnStatusUpdated += OnStatusUpdated;
        Debug.Log($"{TAG} Start | signals connected");

        statusText.text = "Tap Connect to link your wallet";

        // Show reconnect if cached auth exists
        var cached = MWAManager.Instance.Cache.GetLatest();
        bool hasCached = cached != null;
        reconnectButton.gameObject.SetActive(hasCached);
        if (hasCached)
            AndroidToast.Show($"Cached session found: {cached.pubkey[..Math.Min(8, cached.pubkey.Length)]}...");
        Debug.Log($"{TAG} Start | DONE cached_auth={hasCached} reconnect_visible={hasCached}");
    }

    private void OnDestroy()
    {
        Debug.Log($"{TAG} OnDestroy | cleaning up event subscriptions");
        if (MWAManager.Instance != null)
        {
            MWAManager.Instance.OnAuthorized -= OnAuthorized;
            MWAManager.Instance.OnAuthorizationFailed -= OnAuthFailed;
            MWAManager.Instance.OnStatusUpdated -= OnStatusUpdated;
        }
    }

    private async void OnConnectPressed()
    {
        Debug.Log($"{TAG} OnConnectPressed | START");
        connectButton.interactable = false;
        reconnectButton.interactable = false;
        statusText.text = "Opening wallet...";

        Debug.Log($"{TAG} OnConnectPressed | calling MWAManager.Authorize()");
        bool success = await MWAManager.Instance.Authorize();
        Debug.Log($"{TAG} OnConnectPressed | DONE success={success}");

        connectButton.interactable = true;
        reconnectButton.interactable = true;
    }

    private async void OnReconnectPressed()
    {
        Debug.Log($"{TAG} OnReconnectPressed | START");
        connectButton.interactable = false;
        reconnectButton.interactable = false;
        statusText.text = "Reconnecting...";

        Debug.Log($"{TAG} OnReconnectPressed | calling MWAManager.Reauthorize()");
        bool success = await MWAManager.Instance.Reauthorize();
        Debug.Log($"{TAG} OnReconnectPressed | DONE success={success}");

        connectButton.interactable = true;
        reconnectButton.interactable = true;
    }

    private void OnAuthorized(string pubkey)
    {
        Debug.Log($"{TAG} OnAuthorized | pubkey={pubkey} loading Home scene");
        statusText.text = "Connected! Loading...";
        SceneManager.LoadScene("Home");
    }

    private void OnAuthFailed(string error)
    {
        Debug.Log($"{TAG} OnAuthFailed | error={error}");
        statusText.text = $"Failed: {error}";
    }

    private void OnStatusUpdated(string message)
    {
        Debug.Log($"{TAG} OnStatusUpdated | {message}");
        statusText.text = message;
    }
}
