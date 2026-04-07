using System;
using System.Threading.Tasks;
using UnityEngine;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;

public class MWAManager : MonoBehaviour
{
    private const string TAG = "[MWAManager]";

    public static MWAManager Instance { get; private set; }

    public event Action<string> OnAuthorized;
    public event Action<string> OnAuthorizationFailed;
    public event Action OnDisconnected;
    public event Action<string> OnStatusUpdated;

    public string ConnectedPubkey { get; private set; } = "";
    public string AuthToken { get; private set; } = "";
    public bool IsConnected { get; private set; } = false;

    public IMwaAuthCache Cache { get; set; }

    private void Awake()
    {
        Debug.Log($"{TAG} Awake | START instance_exists={Instance != null}");

        if (Instance != null && Instance != this)
        {
            Debug.Log($"{TAG} Awake | DUPLICATE destroying self");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Cache = new AuthCache();
        Debug.Log($"{TAG} Awake | DONE singleton established, cache initialized");
        Debug.Log($"{TAG} Awake | platform={Application.platform} version={Application.version} app={AppConfig.AppName} cluster={AppConfig.Cluster}");
    }

    // ─── AUTHORIZE ───────────────────────────────────────────────────────

    public async Task<bool> Authorize()
    {
        Debug.Log($"{TAG} Authorize | START is_connected={IsConnected}");
        UpdateStatus("Requesting wallet authorization...");

        try
        {
            Debug.Log($"{TAG} Authorize | calling Web3.Instance.LoginWalletAdapter()");
            var account = await Web3.Instance.LoginWalletAdapter();
            if (account == null)
            {
                Debug.Log($"{TAG} Authorize | FAIL account is null (user cancelled or wallet error)");
                UpdateStatus("Authorization cancelled");
                OnAuthorizationFailed?.Invoke("Wallet returned null — user cancelled or wallet error");
                return false;
            }

            ConnectedPubkey = account.PublicKey.Key;
            AuthToken = "";
            IsConnected = true;
            Cache.Set(ConnectedPubkey, AuthToken);
            Debug.Log($"{TAG} Authorize | SUCCESS pubkey={ConnectedPubkey}");
            AndroidToast.Show($"Authorized: {TruncatePubkey(ConnectedPubkey)}");

            // Auto sign-in for biometric confirmation (Godot parity)
            Debug.Log($"{TAG} Authorize | CONNECTED — chaining sign-in for biometric confirmation");
            UpdateStatus("Confirming identity...");
            var signInSig = await SignMessage("Sign in to MWA Unity Example App");
            if (string.IsNullOrEmpty(signInSig))
            {
                Debug.Log($"{TAG} Authorize | SIGN_IN_REJECTED — cancelling auth");
                AndroidToast.Show("Sign-in rejected — authorization cancelled");
                await Deauthorize();
                OnAuthorizationFailed?.Invoke("Sign-in confirmation rejected");
                return false;
            }
            Debug.Log($"{TAG} Authorize | SIGNED_IN sig={signInSig[..Math.Min(20, signInSig.Length)]}...");
            AndroidToast.Show("Biometric sign-in verified");

            UpdateStatus($"Connected: {TruncatePubkey(ConnectedPubkey)}");
            OnAuthorized?.Invoke(ConnectedPubkey);
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} Authorize | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            UpdateStatus($"Authorization failed: {ex.Message}");
            OnAuthorizationFailed?.Invoke(ex.Message);
            return false;
        }
    }

    // ─── REAUTHORIZE ─────────────────────────────────────────────────────

    public async Task<bool> Reauthorize()
    {
        Debug.Log($"{TAG} Reauthorize | START");
        var cached = Cache.GetLatest();
        if (cached == null)
        {
            Debug.Log($"{TAG} Reauthorize | FAIL no cached authorization");
            UpdateStatus("No cached authorization found");
            await Task.CompletedTask;
            return false;
        }

        Debug.Log($"{TAG} Reauthorize | cached_pubkey={cached.pubkey} cached_token_len={cached.authToken?.Length ?? 0}");
        UpdateStatus("Reauthorizing with cached token...");

        // SDK handles token reuse internally via SolanaMobileWalletAdapter.
        // LoginWalletAdapter() checks for cached auth and uses reauthorize path when available.
        Debug.Log($"{TAG} Reauthorize | delegating to Authorize() (SDK handles token reuse internally)");
        AndroidToast.Show("Reauthorizing with cached token...");
        return await Authorize();
    }

    // ─── DEAUTHORIZE ─────────────────────────────────────────────────────

    public async Task Deauthorize()
    {
        Debug.Log($"{TAG} Deauthorize | START pubkey={ConnectedPubkey} is_connected={IsConnected}");
        UpdateStatus("Deauthorizing...");

        Debug.Log($"{TAG} Deauthorize | calling Web3.Instance.Logout()");
        Web3.Instance.Logout();

        string oldPubkey = ConnectedPubkey;
        ConnectedPubkey = "";
        AuthToken = "";
        IsConnected = false;

        Debug.Log($"{TAG} Deauthorize | DONE old_pubkey={oldPubkey} state_cleared=true");
        AndroidToast.Show("Wallet disconnected — session cleared");
        UpdateStatus("Disconnected");
        OnDisconnected?.Invoke();
        await Task.CompletedTask;
    }

    // ─── SIGN MESSAGE ────────────────────────────────────────────────────

    public async Task<string> SignMessage(string message)
    {
        Debug.Log($"{TAG} SignMessage | START message_len={message.Length} is_connected={IsConnected}");

        if (!IsConnected || Web3.Wallet == null)
        {
            Debug.Log($"{TAG} SignMessage | FAIL not connected or wallet null");
            UpdateStatus("Not connected");
            if (Web3.Wallet == null && IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
            }
            return "";
        }

        UpdateStatus("Signing message...");

        try
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(message);
            Debug.Log($"{TAG} SignMessage | calling Web3.Wallet.SignMessage() payload_bytes={payload.Length}");
            var signatureBytes = await Web3.Wallet.SignMessage(payload);
            if (signatureBytes == null || signatureBytes.Length == 0)
            {
                Debug.Log($"{TAG} SignMessage | FAIL empty signature returned");
                UpdateStatus("Sign message failed — empty signature");
                return "";
            }
            var sig = Convert.ToBase64String(signatureBytes);
            Debug.Log($"{TAG} SignMessage | SUCCESS sig_len={sig.Length} sig={sig[..Math.Min(20, sig.Length)]}...");
            AndroidToast.Show($"Message signed: {sig[..Math.Min(16, sig.Length)]}...");
            UpdateStatus($"Signed: {sig[..Math.Min(20, sig.Length)]}...");
            return sig;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignMessage | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            UpdateStatus($"Sign message failed: {ex.Message}");
            return "";
        }
    }

    // ─── SIGN TRANSACTION ────────────────────────────────────────────────

    public async Task<Transaction> SignTransaction(Transaction transaction)
    {
        Debug.Log($"{TAG} SignTransaction | START is_connected={IsConnected}");

        if (!IsConnected || Web3.Wallet == null)
        {
            Debug.Log($"{TAG} SignTransaction | FAIL not connected or wallet null");
            UpdateStatus("Not connected");
            if (Web3.Wallet == null && IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
            }
            return null;
        }

        UpdateStatus("Signing transaction...");

        try
        {
            Debug.Log($"{TAG} SignTransaction | calling Web3.Wallet.SignTransaction()");
            var signedTx = await Web3.Wallet.SignTransaction(transaction);
            Debug.Log($"{TAG} SignTransaction | SUCCESS");
            AndroidToast.Show("Transaction signed successfully");
            UpdateStatus("Transaction signed");
            return signedTx;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignTransaction | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            UpdateStatus($"Sign transaction failed: {ex.Message}");
            return null;
        }
    }

    // ─── SIGN AND SEND TRANSACTION ──────────────────────────────────────

    public async Task<string> SignAndSendTransaction(Transaction transaction)
    {
        Debug.Log($"{TAG} SignAndSendTransaction | START is_connected={IsConnected}");

        if (!IsConnected || Web3.Wallet == null)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | FAIL not connected or wallet null");
            UpdateStatus("Not connected");
            if (Web3.Wallet == null && IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
            }
            return "";
        }

        UpdateStatus("Signing and sending transaction...");

        try
        {
            Debug.Log($"{TAG} SignAndSendTransaction | calling Web3.Wallet.SignAndSendTransaction()");
            var result = await Web3.Wallet.SignAndSendTransaction(
                transaction,
                skipPreflight: false,
                commitment: Commitment.Confirmed
            );
            if (result.WasSuccessful)
            {
                Debug.Log($"{TAG} SignAndSendTransaction | SUCCESS sig={result.Result}");
                AndroidToast.Show($"Transaction sent: {result.Result[..Math.Min(16, result.Result.Length)]}...", longDuration: true);
                UpdateStatus($"Sent! Sig: {result.Result[..Math.Min(20, result.Result.Length)]}...");
                return result.Result;
            }
            else
            {
                Debug.Log($"{TAG} SignAndSendTransaction | RPC_FAIL reason={result.Reason}");
                UpdateStatus($"Send failed: {result.Reason}");
                return "";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            UpdateStatus($"Sign & send failed: {ex.Message}");
            return "";
        }
    }

    // ─── GET CAPABILITIES ────────────────────────────────────────────────

    public async Task<WalletCapabilities> GetCapabilities()
    {
        Debug.Log($"{TAG} GetCapabilities | START is_connected={IsConnected}");

        if (!IsConnected)
        {
            Debug.Log($"{TAG} GetCapabilities | FAIL not connected");
            UpdateStatus("Not connected");
            await Task.CompletedTask;
            return null;
        }

        UpdateStatus("Querying wallet capabilities...");

        // GetCapabilities is not yet exposed in Solana.Unity-SDK.
        // Returning sensible MWA 2.0 defaults until SDK adds support.
        Debug.Log($"{TAG} GetCapabilities | returning defaults (not yet exposed in SDK)");

        var placeholder = new WalletCapabilities
        {
            MaxTransactionsPerRequest = 10,
            MaxMessagesPerRequest = 10,
            SupportedTransactionVersions = new[] { "legacy", "0" }
        };

        AndroidToast.Show($"Capabilities: max_txs={placeholder.MaxTransactionsPerRequest} max_msgs={placeholder.MaxMessagesPerRequest}");
        Debug.Log($"{TAG} GetCapabilities | DONE max_txs={placeholder.MaxTransactionsPerRequest} max_msgs={placeholder.MaxMessagesPerRequest}");
        await Task.CompletedTask;
        return placeholder;
    }

    // ─── DELETE ACCOUNT ──────────────────────────────────────────────────

    public async Task DeleteAccount()
    {
        Debug.Log($"{TAG} DeleteAccount | START pubkey={ConnectedPubkey}");

        // Require wallet confirmation via biometric sign (Seed Vault protection)
        if (IsConnected && Web3.Wallet != null)
        {
            UpdateStatus("Confirm deletion in wallet...");
            Debug.Log($"{TAG} DeleteAccount | requesting wallet confirmation via SignMessage");
            var confirmSig = await SignMessage("Confirm account deletion for MWA Example App");
            if (string.IsNullOrEmpty(confirmSig))
            {
                Debug.Log($"{TAG} DeleteAccount | ABORTED user did not confirm");
                UpdateStatus("Delete cancelled — confirmation required");
                return;
            }
            Debug.Log($"{TAG} DeleteAccount | confirmed sig={confirmSig[..Math.Min(20, confirmSig.Length)]}...");
        }

        await Deauthorize();
        Cache.ClearAll();
        Debug.Log($"{TAG} DeleteAccount | DONE cache cleared, session destroyed");
        AndroidToast.Show("Account deleted — all cached data cleared", longDuration: true);
        UpdateStatus("Account deleted — all cached data cleared");
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    public string TruncatePubkey(string pubkey)
    {
        if (string.IsNullOrEmpty(pubkey) || pubkey.Length <= 8) return pubkey ?? "";
        return pubkey[..4] + "..." + pubkey[^4..];
    }

    private void UpdateStatus(string message)
    {
        Debug.Log($"{TAG} STATUS | {message}");
        OnStatusUpdated?.Invoke(message);
    }
}

[Serializable]
public class WalletCapabilities
{
    public int MaxTransactionsPerRequest;
    public int MaxMessagesPerRequest;
    public string[] SupportedTransactionVersions;
}
