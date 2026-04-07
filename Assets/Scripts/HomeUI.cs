using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solana.Unity.SDK;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Programs;

public class HomeUI : MonoBehaviour
{
    private const string TAG = "[HomeUI]";

    [Header("UI References")]
    public TextMeshProUGUI pubkeyText;
    public TextMeshProUGUI statusText;

    [Header("Action Buttons")]
    public Button signMessageButton;
    public Button signTxButton;
    public Button signSendButton;
    public Button capabilitiesButton;
    public Button reconnectButton;

    [Header("Account Buttons")]
    public Button disconnectButton;
    public Button deleteButton;

    private MWAManager _mwa;

    private void Start()
    {
        Debug.Log($"{TAG} Start | BEGIN");
        _mwa = MWAManager.Instance;

        // Display pubkey
        string truncated = _mwa.TruncatePubkey(_mwa.ConnectedPubkey);
        pubkeyText.text = string.IsNullOrEmpty(truncated) ? "Not connected" : truncated;
        Debug.Log($"{TAG} Start | pubkey={_mwa.ConnectedPubkey} truncated={truncated} is_connected={_mwa.IsConnected}");

        // Wire buttons
        signMessageButton.onClick.AddListener(OnSignMessage);
        signTxButton.onClick.AddListener(OnSignTransaction);
        signSendButton.onClick.AddListener(OnSignAndSend);
        capabilitiesButton.onClick.AddListener(OnGetCapabilities);
        reconnectButton.onClick.AddListener(OnReconnect);
        disconnectButton.onClick.AddListener(OnDisconnect);
        deleteButton.onClick.AddListener(OnDeleteAccount);

        // Listen for events
        _mwa.OnStatusUpdated += OnStatusUpdated;
        _mwa.OnDisconnected += OnDisconnected;

        statusText.text = "Connected — choose an action";
        Debug.Log($"{TAG} Start | DONE buttons wired, signals connected");
    }

    private void OnDestroy()
    {
        Debug.Log($"{TAG} OnDestroy | cleaning up event subscriptions");
        if (_mwa != null)
        {
            _mwa.OnStatusUpdated -= OnStatusUpdated;
            _mwa.OnDisconnected -= OnDisconnected;
        }
    }

    // ─── BUTTON HANDLERS ─────────────────────────────────────────────────

    private async void OnSignMessage()
    {
        Debug.Log($"{TAG} OnSignMessage | START");
        SetButtonsInteractable(false);
        string sig = await _mwa.SignMessage("Hello from MWA Example App!");
        if (string.IsNullOrEmpty(sig))
        {
            Debug.Log($"{TAG} OnSignMessage | FAIL empty signature");
            statusText.text = "Sign message failed";
        }
        else
        {
            Debug.Log($"{TAG} OnSignMessage | SUCCESS sig={sig[..Mathf.Min(20, sig.Length)]}");
            statusText.text = $"Signed: {sig[..Mathf.Min(20, sig.Length)]}...";
        }
        SetButtonsInteractable(true);
        Debug.Log($"{TAG} OnSignMessage | DONE");
    }

    private async void OnSignTransaction()
    {
        Debug.Log($"{TAG} OnSignTransaction | START");
        SetButtonsInteractable(false);
        try
        {
            Debug.Log($"{TAG} OnSignTransaction | fetching recent blockhash");
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            if (!blockHashResult.WasSuccessful)
            {
                Debug.Log($"{TAG} OnSignTransaction | FAIL could not get blockhash");
                statusText.text = "Failed to get recent blockhash";
                SetButtonsInteractable(true);
                return;
            }

            var fromPubkey = Web3.Wallet.Account.PublicKey;
            var tx = new Transaction
            {
                RecentBlockHash = blockHashResult.Result.Value.Blockhash,
                FeePayer = fromPubkey
            };
            tx.Add(MemoProgram.NewMemoV2("Hello from MWA Example App!"));

            Debug.Log($"{TAG} OnSignTransaction | built memo tx, calling SignTransaction");
            var signedTx = await _mwa.SignTransaction(tx);
            if (signedTx == null)
            {
                Debug.Log($"{TAG} OnSignTransaction | FAIL null result");
                statusText.text = "Sign transaction failed";
            }
            else
            {
                Debug.Log($"{TAG} OnSignTransaction | SUCCESS signed");
                statusText.text = "Transaction signed successfully!";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} OnSignTransaction | EXCEPTION {ex.Message}");
            statusText.text = $"Error: {ex.Message}";
        }
        SetButtonsInteractable(true);
        Debug.Log($"{TAG} OnSignTransaction | DONE");
    }

    private async void OnSignAndSend()
    {
        Debug.Log($"{TAG} OnSignAndSend | START");
        SetButtonsInteractable(false);
        try
        {
            Debug.Log($"{TAG} OnSignAndSend | fetching recent blockhash");
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            if (!blockHashResult.WasSuccessful)
            {
                Debug.Log($"{TAG} OnSignAndSend | FAIL could not get blockhash");
                statusText.text = "Failed to get recent blockhash";
                SetButtonsInteractable(true);
                return;
            }

            var fromPubkey = Web3.Wallet.Account.PublicKey;
            var tx = new Transaction
            {
                RecentBlockHash = blockHashResult.Result.Value.Blockhash,
                FeePayer = fromPubkey
            };
            tx.Add(MemoProgram.NewMemoV2("MWA Example: Sign & Send test"));

            Debug.Log($"{TAG} OnSignAndSend | built memo tx, calling SignAndSendTransaction");
            var sig = await _mwa.SignAndSendTransaction(tx);
            if (string.IsNullOrEmpty(sig))
            {
                Debug.Log($"{TAG} OnSignAndSend | FAIL empty sig");
                statusText.text = "Sign & send failed";
            }
            else
            {
                Debug.Log($"{TAG} OnSignAndSend | SUCCESS sig={sig}");
                statusText.text = $"Sent! Sig:\n{sig[..Math.Min(20, sig.Length)]}...";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} OnSignAndSend | EXCEPTION {ex.Message}");
            statusText.text = $"Error: {ex.Message}";
        }
        SetButtonsInteractable(true);
        Debug.Log($"{TAG} OnSignAndSend | DONE");
    }

    private async void OnGetCapabilities()
    {
        Debug.Log($"{TAG} OnGetCapabilities | START");
        SetButtonsInteractable(false);
        var caps = await _mwa.GetCapabilities();
        if (caps == null)
        {
            Debug.Log($"{TAG} OnGetCapabilities | FAIL null result");
            statusText.text = "Failed to get capabilities";
        }
        else
        {
            Debug.Log($"{TAG} OnGetCapabilities | SUCCESS max_txs={caps.MaxTransactionsPerRequest} max_msgs={caps.MaxMessagesPerRequest}");
            statusText.text = $"Capabilities:\n" +
                $"  Max Txs: {caps.MaxTransactionsPerRequest}\n" +
                $"  Max Msgs: {caps.MaxMessagesPerRequest}\n" +
                $"  Versions: {string.Join(", ", caps.SupportedTransactionVersions)}";
        }
        SetButtonsInteractable(true);
        Debug.Log($"{TAG} OnGetCapabilities | DONE");
    }

    private async void OnReconnect()
    {
        Debug.Log($"{TAG} OnReconnect | START");
        SetButtonsInteractable(false);
        bool ok = await _mwa.Reauthorize();
        Debug.Log($"{TAG} OnReconnect | DONE success={ok}");
        statusText.text = ok ? "Reconnected successfully" : "Reconnect failed";
        SetButtonsInteractable(true);
    }

    private async void OnDisconnect()
    {
        Debug.Log($"{TAG} OnDisconnect | START");
        await _mwa.Deauthorize();
        Debug.Log($"{TAG} OnDisconnect | DONE");
    }

    private async void OnDeleteAccount()
    {
        Debug.Log($"{TAG} OnDeleteAccount | START");
        await _mwa.DeleteAccount();
        Debug.Log($"{TAG} OnDeleteAccount | DONE");
    }

    // ─── EVENTS ──────────────────────────────────────────────────────────

    private void OnDisconnected()
    {
        Debug.Log($"{TAG} OnDisconnected | SIGNAL RECEIVED — loading Landing scene");
        SceneManager.LoadScene("Landing");
    }

    private void OnStatusUpdated(string message)
    {
        Debug.Log($"{TAG} OnStatusUpdated | {message}");
        statusText.text = message;
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    private void SetButtonsInteractable(bool interactable)
    {
        Debug.Log($"{TAG} SetButtonsInteractable | interactable={interactable}");
        signMessageButton.interactable = interactable;
        signTxButton.interactable = interactable;
        signSendButton.interactable = interactable;
        capabilitiesButton.interactable = interactable;
        reconnectButton.interactable = interactable;
        disconnectButton.interactable = interactable;
        deleteButton.interactable = interactable;
    }
}
