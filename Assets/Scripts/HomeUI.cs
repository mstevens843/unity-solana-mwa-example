using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Rpc.Models;

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

        // Display pubkey and wallet type
        string truncated = _mwa.TruncatePubkey(_mwa.ConnectedPubkey);
        string walletName = MWAManager.WalletTypeName(_mwa.ConnectedWalletType);
        if (string.IsNullOrEmpty(truncated))
            pubkeyText.text = "Not connected";
        else
            pubkeyText.text = $"{truncated} ({walletName})";
        Debug.Log($"{TAG} Start | pubkey={_mwa.ConnectedPubkey} truncated={truncated} is_connected={_mwa.IsConnected} wallet={walletName} (type={_mwa.ConnectedWalletType})");

        // Wire buttons
        signMessageButton.onClick.AddListener(OnSignMessage);
        signTxButton.onClick.AddListener(OnSignTransaction);
        signSendButton.onClick.AddListener(OnSignAndSend);
        // TODO: Re-enable when SDK GetCapabilities is available
        // capabilitiesButton.onClick.AddListener(OnGetCapabilities);
        disconnectButton.onClick.AddListener(OnDisconnect);
        if (reconnectButton != null) reconnectButton.gameObject.SetActive(false);
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
        try
        {
            string sig = await _mwa.SignMessage("Hello from MWA Example App!");
            if (string.IsNullOrEmpty(sig))
            {
                Debug.Log($"{TAG} OnSignMessage | FAIL empty signature");
                statusText.text = "Sign message failed — wallet may not support message signing";
            }
            else
            {
                Debug.Log($"{TAG} OnSignMessage | SUCCESS sig={sig[..Mathf.Min(20, sig.Length)]}");
                statusText.text = $"Signed: {sig[..Mathf.Min(20, sig.Length)]}...";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} OnSignMessage | EXCEPTION {ex.GetType().Name}: {ex.Message}");
            if (ex.Message.Contains("NOT_SUPPORTED") || ex.Message.Contains("METHOD_NOT_FOUND"))
            {
                statusText.text = "This wallet does not support message signing via MWA";
                AndroidToast.Show("Message signing not supported by this wallet");
            }
            else
            {
                statusText.text = $"Sign failed: {ex.Message}";
            }
        }
        SetButtonsInteractable(true);
        Debug.Log($"{TAG} OnSignMessage | DONE");
    }

    private async void OnSignTransaction()
    {
        Debug.Log($"{TAG} OnSignTransaction | START Web3.Wallet={Web3.Wallet != null} Web3.Rpc={Web3.Rpc != null}");
        SetButtonsInteractable(false);
        try
        {
            if (Web3.Wallet == null || Web3.Rpc == null)
            {
                Debug.Log($"{TAG} OnSignTransaction | session_check wallet_null={Web3.Wallet == null} rpc_null={Web3.Rpc == null}");
                statusText.text = "Establishing session...";
                var sig0 = await _mwa.SignMessage("session check");
                Debug.Log($"{TAG} OnSignTransaction | session_check result sig_empty={string.IsNullOrEmpty(sig0)} wallet_after={Web3.Wallet != null} rpc_after={Web3.Rpc != null}");
                if (string.IsNullOrEmpty(sig0) && Web3.Wallet == null)
                {
                    statusText.text = "No active wallet session — tap Connect first";
                    SetButtonsInteractable(true);
                    return;
                }
            }

            Debug.Log($"{TAG} OnSignTransaction | fetching blockhash rpc_cluster={Web3.Instance.rpcCluster}");
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            Debug.Log($"{TAG} OnSignTransaction | blockhash_result successful={blockHashResult.WasSuccessful} blockhash={blockHashResult.Result?.Value?.Blockhash ?? "null"} reason={blockHashResult.Reason ?? "none"}");
            if (!blockHashResult.WasSuccessful)
            {
                statusText.text = $"Failed to get blockhash: {blockHashResult.Reason}";
                SetButtonsInteractable(true);
                return;
            }

            var fromPubkey = Web3.Wallet.Account.PublicKey;
            var blockhash = blockHashResult.Result.Value.Blockhash;
            Debug.Log($"{TAG} OnSignTransaction | building tx fee_payer={fromPubkey} blockhash={blockhash}");

            // Build memo instruction manually — MemoProgram.NewMemoV2 causes NullRef
            var memoData = System.Text.Encoding.UTF8.GetBytes("Hello from MWA Example App!");
            var memoProgramId = new PublicKey("MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr");
            var memoInstruction = new TransactionInstruction
            {
                ProgramId = memoProgramId.KeyBytes,
                Keys = new List<AccountMeta> { AccountMeta.Writable(fromPubkey, true) },
                Data = memoData
            };

            var tx = new Transaction
            {
                RecentBlockHash = blockhash,
                FeePayer = fromPubkey
            };
            tx.Add(memoInstruction);

            Debug.Log($"{TAG} OnSignTransaction | tx_built instructions={tx.Instructions.Count} calling SignTransaction");
            var signedTx = await _mwa.SignTransaction(tx);
            Debug.Log($"{TAG} OnSignTransaction | RESULT signed_tx_null={signedTx == null}");
            statusText.text = signedTx != null ? "Transaction signed successfully!" : "Sign transaction failed";
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} OnSignTransaction | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            statusText.text = $"Error: {ex.Message}";
        }
        SetButtonsInteractable(true);
    }

    private async void OnSignAndSend()
    {
        Debug.Log($"{TAG} OnSignAndSend | START Web3.Wallet={Web3.Wallet != null} Web3.Rpc={Web3.Rpc != null}");
        SetButtonsInteractable(false);
        try
        {
            if (Web3.Wallet == null || Web3.Rpc == null)
            {
                Debug.Log($"{TAG} OnSignAndSend | session_check wallet_null={Web3.Wallet == null} rpc_null={Web3.Rpc == null}");
                statusText.text = "Establishing session...";
                var sig0 = await _mwa.SignMessage("session check");
                Debug.Log($"{TAG} OnSignAndSend | session_check result sig_empty={string.IsNullOrEmpty(sig0)} wallet_after={Web3.Wallet != null} rpc_after={Web3.Rpc != null}");
                if (string.IsNullOrEmpty(sig0) && Web3.Wallet == null)
                {
                    statusText.text = "No active wallet session — tap Connect first";
                    SetButtonsInteractable(true);
                    return;
                }
            }

            Debug.Log($"{TAG} OnSignAndSend | fetching blockhash rpc_cluster={Web3.Instance.rpcCluster}");
            var blockHashResult = await Web3.Rpc.GetLatestBlockHashAsync();
            Debug.Log($"{TAG} OnSignAndSend | blockhash_result successful={blockHashResult.WasSuccessful} blockhash={blockHashResult.Result?.Value?.Blockhash ?? "null"} reason={blockHashResult.Reason ?? "none"}");
            if (!blockHashResult.WasSuccessful)
            {
                statusText.text = $"Failed to get blockhash: {blockHashResult.Reason}";
                SetButtonsInteractable(true);
                return;
            }

            var fromPubkey = Web3.Wallet.Account.PublicKey;
            var blockhash = blockHashResult.Result.Value.Blockhash;
            Debug.Log($"{TAG} OnSignAndSend | building tx fee_payer={fromPubkey} blockhash={blockhash}");

            var memoData = System.Text.Encoding.UTF8.GetBytes("MWA Example: Sign & Send test");
            var memoProgramId = new PublicKey("MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr");
            var memoInstruction = new TransactionInstruction
            {
                ProgramId = memoProgramId.KeyBytes,
                Keys = new List<AccountMeta> { AccountMeta.Writable(fromPubkey, true) },
                Data = memoData
            };

            var tx = new Transaction
            {
                RecentBlockHash = blockhash,
                FeePayer = fromPubkey
            };
            tx.Add(memoInstruction);

            Debug.Log($"{TAG} OnSignAndSend | tx_built instructions={tx.Instructions.Count} calling SignAndSendTransaction");
            var sig = await _mwa.SignAndSendTransaction(tx);
            Debug.Log($"{TAG} OnSignAndSend | RESULT sig_empty={string.IsNullOrEmpty(sig)} sig={sig ?? "null"}");

            if (string.IsNullOrEmpty(sig))
            {
                // Branch on MWAManager.LastErrorCode so the user gets a
                // truthful message. INSUFFICIENT_FUNDS_FOR_RENT is by far
                // the most common non-wallet failure (Seed Vault's wrapper
                // injects priority fees that push low-balance fee-payers
                // below the rent-exempt minimum). See KNOWN_ISSUES.md.
                if (_mwa.LastErrorCode == "INSUFFICIENT_FUNDS_FOR_RENT")
                {
                    statusText.text = $"Fee-payer underfunded — send ≥0.001 SOL to {_mwa.TruncatePubkey(_mwa.ConnectedPubkey)} and retry";
                    AndroidToast.Show($"Fee-payer underfunded — send ≥0.001 SOL and retry", longDuration: true);
                }
                else
                {
                    statusText.text = "Sign & send failed";
                }
            }
            else
                statusText.text = $"Sent! Sig:\n{sig}";
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} OnSignAndSend | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            statusText.text = $"Error: {ex.Message}";
        }
        SetButtonsInteractable(true);
    }

    // TODO: Re-enable when SDK GetCapabilities is available on this branch
    /*
    private async void OnGetCapabilities()
    {
        Debug.Log($"{TAG} OnGetCapabilities | START");
        SetButtonsInteractable(false);
        var caps = await _mwa.GetCapabilities();
        Debug.Log($"{TAG} OnGetCapabilities | RESULT caps_null={caps == null} max_txs={caps?.MaxTransactionsPerRequest ?? -1} max_msgs={caps?.MaxMessagesPerRequest ?? -1} versions={string.Join(",", caps?.SupportedTransactionVersions ?? new string[0])}");
        if (caps == null)
        {
            statusText.text = "Failed to get capabilities";
        }
        else
        {
            statusText.text = $"get_capabilities:\n" +
                $"  Max Txs/Request: {caps.MaxTransactionsPerRequest}\n" +
                $"  Max Msgs/Request: {caps.MaxMessagesPerRequest}\n" +
                $"  Tx Versions: {string.Join(", ", caps.SupportedTransactionVersions)}";
        }
        SetButtonsInteractable(true);
    }
    */

    private async void OnDisconnect()
    {
        Debug.Log($"{TAG} OnDisconnect | START");
        await _mwa.Deauthorize();
        Debug.Log($"{TAG} OnDisconnect | DONE");
    }

    private async void OnDeleteAccount()
    {
        Debug.Log($"{TAG} OnDeleteAccount | START");
        SetButtonsInteractable(false);
        bool deleted = await _mwa.DeleteAccount();
        Debug.Log($"{TAG} OnDeleteAccount | DONE deleted={deleted}");
        if (!deleted)
        {
            SetButtonsInteractable(true);
        }
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
        disconnectButton.interactable = interactable;
        deleteButton.interactable = interactable;
    }
}
