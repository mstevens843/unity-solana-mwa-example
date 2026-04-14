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
    public TextMeshProUGUI subtitleText;

    [Header("Wallet Picker Buttons (USE_OS_PICKER = false)")]
    public Button seedVaultButton;
    public Button phantomButton;
    public Button solflareButton;
    public Button jupiterButton;
    public Button backpackButton;

    private void Start()
    {
        Debug.Log($"{TAG} Start | BEGIN use_os_picker={AppConfig.UseOsPicker}");

        // Wire OS picker button
        connectButton.onClick.AddListener(OnConnectPressed);
        reconnectButton.onClick.AddListener(OnReconnectPressed);

        // Wire wallet picker buttons
        if (seedVaultButton != null) seedVaultButton.onClick.AddListener(() => OnWalletPressed(-1));
        if (phantomButton != null) phantomButton.onClick.AddListener(() => OnWalletPressed(MWAManager.WALLET_PHANTOM));
        if (solflareButton != null) solflareButton.onClick.AddListener(() => OnWalletPressed(MWAManager.WALLET_SOLFLARE));
        if (jupiterButton != null) jupiterButton.onClick.AddListener(() => OnWalletPressed(MWAManager.WALLET_JUPITER));
        if (backpackButton != null) backpackButton.onClick.AddListener(() => OnWalletPressed(MWAManager.WALLET_BACKPACK));

        MWAManager.Instance.OnAuthorized += OnAuthorized;
        MWAManager.Instance.OnAuthorizationFailed += OnAuthFailed;
        MWAManager.Instance.OnStatusUpdated += OnStatusUpdated;
        Debug.Log($"{TAG} Start | signals connected");

        // Toggle UI based on flag
        if (AppConfig.UseOsPicker)
        {
            connectButton.gameObject.SetActive(true);
            SetWalletButtonsActive(false);
            if (subtitleText != null) subtitleText.text = "Solana Mobile Wallet Adapter Demo";
            statusText.text = "Tap Connect to link your wallet";
        }
        else
        {
            connectButton.gameObject.SetActive(false);
            SetWalletButtonsActive(true);
            if (subtitleText != null) subtitleText.text = "Choose your wallet";
            statusText.text = "Select a wallet to connect";
        }

        // Show reconnect if cached auth exists
        var cached = MWAManager.Instance.Cache.GetLatest();
        bool hasCached = cached != null;
        reconnectButton.gameObject.SetActive(hasCached);
        if (hasCached)
        {
            reconnectButton.GetComponentInChildren<TextMeshProUGUI>().text = "Reconnect (Cached)";
            AndroidToast.Show($"Cached session found: {cached.pubkey[..Math.Min(8, cached.pubkey.Length)]}...");
        }
        Debug.Log($"{TAG} Start | DONE use_os_picker={AppConfig.UseOsPicker} cached_auth={hasCached} reconnect_visible={hasCached}");
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

    // OS picker mode — single button
    private async void OnConnectPressed()
    {
        Debug.Log($"{TAG} OnConnectPressed | START");
        SetAllInteractable(false);
        statusText.text = "Opening wallet...";

        Debug.Log($"{TAG} OnConnectPressed | calling MWAManager.Authorize()");
        bool success = await MWAManager.Instance.Authorize();
        Debug.Log($"{TAG} OnConnectPressed | DONE success={success}");

        SetAllInteractable(true);
    }

    // In-app picker mode — wallet-specific button
    private async void OnWalletPressed(int walletTypeId)
    {
        Debug.Log($"{TAG} OnWalletPressed | START wallet_type={walletTypeId} ({MWAManager.WalletTypeName(walletTypeId)})");
        SetAllInteractable(false);
        statusText.text = $"Connecting to {MWAManager.WalletTypeName(walletTypeId)}...";

        bool success = await MWAManager.Instance.Authorize(walletTypeId);
        Debug.Log($"{TAG} OnWalletPressed | DONE success={success}");

        SetAllInteractable(true);
    }

    private async void OnReconnectPressed()
    {
        Debug.Log($"{TAG} OnReconnectPressed | START");
        SetAllInteractable(false);
        statusText.text = "Reconnecting...";

        Debug.Log($"{TAG} OnReconnectPressed | calling MWAManager.Reauthorize()");
        bool success = await MWAManager.Instance.Reauthorize();
        Debug.Log($"{TAG} OnReconnectPressed | DONE success={success}");

        if (!success)
            statusText.text = "Reconnect failed — try Connect for fresh authorization";

        SetAllInteractable(true);
    }

    private void OnAuthorized(string pubkey)
    {
        Debug.Log($"{TAG} OnAuthorized | pubkey={pubkey} wallet_type={MWAManager.Instance.ConnectedWalletType} loading Home scene");
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

    private void SetWalletButtonsActive(bool active)
    {
        if (seedVaultButton != null) seedVaultButton.gameObject.SetActive(active);
        if (phantomButton != null) phantomButton.gameObject.SetActive(active);
        if (solflareButton != null) solflareButton.gameObject.SetActive(active);
        if (jupiterButton != null) jupiterButton.gameObject.SetActive(active);
        if (backpackButton != null) backpackButton.gameObject.SetActive(active);
    }

    private void SetAllInteractable(bool interactable)
    {
        connectButton.interactable = interactable;
        reconnectButton.interactable = interactable;
        if (seedVaultButton != null) seedVaultButton.interactable = interactable;
        if (phantomButton != null) phantomButton.interactable = interactable;
        if (solflareButton != null) solflareButton.interactable = interactable;
        if (jupiterButton != null) jupiterButton.interactable = interactable;
        if (backpackButton != null) backpackButton.interactable = interactable;
    }
}
