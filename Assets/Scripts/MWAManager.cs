using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;

public class MWAManager : MonoBehaviour
{
    private const string TAG = "[MWAManager]";

    // Wallet type IDs (from godot-solana-sdk WalletAdapterUI)
    public const int WALLET_PHANTOM = 20;
    public const int WALLET_SOLFLARE = 25;
    public const int WALLET_BACKPACK = 36;
    public const int WALLET_JUPITER = 40;

    public static MWAManager Instance { get; private set; }

    public event Action<string> OnAuthorized;
    public event Action<string> OnAuthorizationFailed;
    public event Action OnDisconnected;
    public event Action<string> OnStatusUpdated;

    public string ConnectedPubkey { get; private set; } = "";
    public int ConnectedWalletType { get; private set; } = -1;
    public string AuthToken { get; private set; } = "";
    public bool IsConnected { get; private set; } = false;

    public IMwaAuthCache Cache { get; set; }

    private readonly HashSet<string> _deletedPubkeys = new();

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

        if (Web3.Instance != null)
        {
            Web3.Instance.rpcCluster = AppConfig.SdkCluster;
            var mwaOpts = Web3.Instance.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions;
            mwaOpts.siwsDomain = AppConfig.SiwsDomain;
            mwaOpts.siwsStatement = AppConfig.SiwsStatement;
            Debug.Log($"{TAG} Awake | set Web3.rpcCluster={Web3.Instance.rpcCluster} ({AppConfig.Cluster}) SIWS domain={mwaOpts.siwsDomain} statement={mwaOpts.siwsStatement}");
        }
        else
        {
            Debug.Log($"{TAG} Awake | WARN Web3.Instance is null — cluster will be set on first Authorize");
        }

        Debug.Log($"{TAG} Awake | DONE singleton established cache_type={Cache.GetType().Name}");
        Debug.Log($"{TAG} Awake | platform={Application.platform} version={Application.version} app={AppConfig.AppName} cluster={AppConfig.Cluster} use_os_picker={AppConfig.UseOsPicker}");
    }

    // ─── AUTHORIZE ───────────────────────────────────────────────────────

    public async Task<bool> Authorize(int walletTypeId = -1)
    {
        Debug.Log($"{TAG} Authorize | START is_connected={IsConnected} wallet_type_id={walletTypeId} ({WalletTypeName(walletTypeId)}) deleted_keys={_deletedPubkeys.Count}");
        UpdateStatus("Requesting wallet authorization...");

        if (_deletedPubkeys.Count > 0)
        {
            Debug.Log($"{TAG} Authorize | clearing {_deletedPubkeys.Count} deleted keys (connect = clean slate)");
            _deletedPubkeys.Clear();
        }

        try
        {
            Web3.Instance.rpcCluster = AppConfig.SdkCluster;
            var mwaOpts = Web3.Instance.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions;
            if (string.IsNullOrEmpty(mwaOpts.siwsDomain))
            {
                mwaOpts.siwsDomain = AppConfig.SiwsDomain;
                mwaOpts.siwsStatement = AppConfig.SiwsStatement;
                Debug.Log($"{TAG} Authorize | SIWS configured (late) domain={mwaOpts.siwsDomain}");
            }
            Debug.Log($"{TAG} Authorize | calling LoginWalletAdapter() cluster={Web3.Instance.rpcCluster} siwsDomain={mwaOpts.siwsDomain}");
            var account = await Web3.Instance.LoginWalletAdapter();

            Debug.Log($"{TAG} Authorize | LoginWalletAdapter returned account={account != null} pubkey={account?.PublicKey?.Key ?? "null"}");

            if (account == null)
            {
                Debug.Log($"{TAG} Authorize | RESULT=FAIL reason=account_null");
                UpdateStatus("Authorization cancelled");
                OnAuthorizationFailed?.Invoke("Wallet returned null — user cancelled or wallet error");
                return false;
            }

            var pubkey = account.PublicKey.Key;
            ConnectedPubkey = pubkey;
            ConnectedWalletType = walletTypeId;
            AuthToken = "";
            IsConnected = true;

            Debug.Log($"{TAG} Authorize | RESULT=SUCCESS pubkey={ConnectedPubkey} pubkey_len={ConnectedPubkey.Length} wallet_type={ConnectedWalletType} ({WalletTypeName(ConnectedWalletType)})");

            // SIWS handles sign-in for ALL wallets at the SDK level.
            // The SDK throws if SIWS was requested but wallet didn't return SignInResult,
            // so reaching here means SIWS succeeded (or keepConnectionAlive returned cached pk).
            var walletAdapter = Web3.Wallet as SolanaWalletAdapter;
            var signInResult = walletAdapter?.LastSignInResult;
            Debug.Log($"{TAG} Authorize | SIWS_RESULT adapter_null={walletAdapter == null} result_null={signInResult == null} address={signInResult?.Address ?? "null"} sig_type={signInResult?.SignatureType ?? "null"}");

            if (signInResult != null)
            {
                Debug.Log($"{TAG} Authorize | SIWS_VERIFIED address={signInResult.Address} sig={signInResult.Signature ?? "null"}");
                AndroidToast.Show("Sign-in verified (SIWS)");
            }
            else
            {
                Debug.LogWarning($"{TAG} Authorize | SIWS_MISSING — wallet may not support SIWS or keepConnectionAlive returned cached pk");
                AndroidToast.Show($"Connected via {WalletTypeName(ConnectedWalletType)}");
            }

            Cache.Set(ConnectedPubkey, AuthToken, walletType: ConnectedWalletType);
            Debug.Log($"{TAG} Authorize | CACHED pubkey={ConnectedPubkey} auth_token_len={AuthToken.Length} wallet_type={ConnectedWalletType}");

            UpdateStatus($"Connected: {TruncatePubkey(ConnectedPubkey)}");
            OnAuthorized?.Invoke(ConnectedPubkey);
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} Authorize | EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            UpdateStatus($"Authorization failed: {CategorizeError(ex)}");
            OnAuthorizationFailed?.Invoke(CategorizeError(ex));
            return false;
        }
    }

    // ─── REAUTHORIZE ─────────────────────────────────────────────────────

    public async Task<bool> Reauthorize()
    {
        Debug.Log($"{TAG} Reauthorize | START is_connected={IsConnected}");
        var cached = Cache.GetLatest();

        Debug.Log($"{TAG} Reauthorize | cache_result found={cached != null} pubkey={cached?.pubkey ?? "null"} wallet_type={cached?.walletType ?? -99} token_len={cached?.authToken?.Length ?? 0}");

        if (cached == null)
        {
            Debug.Log($"{TAG} Reauthorize | RESULT=FAIL reason=no_cached_auth");
            UpdateStatus("No cached authorization found");
            await Task.CompletedTask;
            return false;
        }

        var pubkey = cached.pubkey;
        var walletType = cached.walletType;

        if (string.IsNullOrEmpty(pubkey))
        {
            Debug.Log($"{TAG} Reauthorize | RESULT=FAIL reason=cached_pubkey_empty");
            UpdateStatus("Cached authorization invalid");
            OnAuthorizationFailed?.Invoke("Cached pubkey is empty");
            await Task.CompletedTask;
            return false;
        }

        ConnectedPubkey = pubkey;
        ConnectedWalletType = walletType;
        AuthToken = cached.authToken ?? "";
        IsConnected = true;

        Debug.Log($"{TAG} Reauthorize | RESULT=SUCCESS pubkey={ConnectedPubkey} wallet_type={ConnectedWalletType} ({WalletTypeName(ConnectedWalletType)}) Web3.Wallet={Web3.Wallet != null}");
        AndroidToast.Show($"Reconnected: {TruncatePubkey(ConnectedPubkey)}");
        UpdateStatus($"Connected: {TruncatePubkey(ConnectedPubkey)}");
        OnAuthorized?.Invoke(ConnectedPubkey);
        await Task.CompletedTask;
        return true;
    }

    // ─── DEAUTHORIZE ─────────────────────────────────────────────────────

    public async Task Deauthorize()
    {
        Debug.Log($"{TAG} Deauthorize | START pubkey={ConnectedPubkey} is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null}");
        UpdateStatus("Deauthorizing...");

        Web3.Instance.Logout();

        string oldPubkey = ConnectedPubkey;
        ConnectedPubkey = "";
        ConnectedWalletType = -1;
        AuthToken = "";
        IsConnected = false;

        Debug.Log($"{TAG} Deauthorize | RESULT=DONE old_pubkey={oldPubkey} state_cleared=true Web3.Wallet={Web3.Wallet != null}");
        AndroidToast.Show("Wallet disconnected — session cleared");
        UpdateStatus("Disconnected");
        OnDisconnected?.Invoke();
        await Task.CompletedTask;
    }

    // ─── ENSURE WALLET SESSION ──────────────────────────────────────────

    private async Task<bool> EnsureWalletSession()
    {
        Debug.Log($"{TAG} EnsureWalletSession | START Web3.Wallet={Web3.Wallet != null} Web3.Rpc={Web3.Rpc != null} is_connected={IsConnected}");

        if (Web3.Wallet != null)
        {
            Debug.Log($"{TAG} EnsureWalletSession | RESULT=ALREADY_ACTIVE wallet_type={Web3.Wallet.GetType().Name}");
            return true;
        }

        if (!IsConnected)
        {
            Debug.Log($"{TAG} EnsureWalletSession | RESULT=FAIL reason=not_connected");
            return false;
        }

        UpdateStatus("Establishing wallet session...");

        try
        {
            Web3.Instance.rpcCluster = AppConfig.SdkCluster;
            Debug.Log($"{TAG} EnsureWalletSession | calling LoginWalletAdapter() cluster={Web3.Instance.rpcCluster}");
            var account = await Web3.Instance.LoginWalletAdapter();

            Debug.Log($"{TAG} EnsureWalletSession | LoginWalletAdapter returned account={account != null} pubkey={account?.PublicKey?.Key ?? "null"} Web3.Wallet={Web3.Wallet != null} Web3.Rpc={Web3.Rpc != null}");

            if (account != null)
            {
                AndroidToast.Show("Wallet session established");
                return true;
            }
            Debug.Log($"{TAG} EnsureWalletSession | RESULT=FAIL reason=account_null");
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} EnsureWalletSession | RESULT=FAIL reason=exception type={ex.GetType().Name} msg={ex.Message}");
        }

        UpdateStatus("Wallet session failed — please reconnect");
        IsConnected = false;
        ConnectedPubkey = "";
        ConnectedWalletType = -1;
        OnDisconnected?.Invoke();
        return false;
    }

    // ─── SIGN MESSAGE ────────────────────────────────────────────────────

    public async Task<string> SignMessage(string message)
    {
        Debug.Log($"{TAG} SignMessage | START message=\"{message}\" message_len={message.Length} is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null}");

        if (!IsConnected)
        {
            Debug.Log($"{TAG} SignMessage | RESULT=FAIL reason=not_connected");
            UpdateStatus("Not connected");
            return "";
        }

        if (Web3.Wallet == null && !await EnsureWalletSession())
        {
            Debug.Log($"{TAG} SignMessage | RESULT=FAIL reason=ensure_session_failed");
            return "";
        }

        UpdateStatus("Signing message...");

        try
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(message);
            Debug.Log($"{TAG} SignMessage | calling Web3.Wallet.SignMessage() payload_bytes={payload.Length} payload_hex={BitConverter.ToString(payload).Replace("-", "").ToLower()}");
            var signatureBytes = await Web3.Wallet.SignMessage(payload);

            Debug.Log($"{TAG} SignMessage | Web3.Wallet.SignMessage returned sig_null={signatureBytes == null} sig_bytes={signatureBytes?.Length ?? 0}");

            if (signatureBytes == null || signatureBytes.Length == 0)
            {
                Debug.Log($"{TAG} SignMessage | RESULT=FAIL reason=empty_signature sig_null={signatureBytes == null} sig_len={signatureBytes?.Length ?? 0}");
                UpdateStatus("Sign message failed — empty signature");
                return "";
            }
            var sig = Convert.ToBase64String(signatureBytes);
            Debug.Log($"{TAG} SignMessage | RESULT=SUCCESS sig_bytes={signatureBytes.Length} sig_base64_len={sig.Length} sig_base64={sig}");
            AndroidToast.Show($"Message signed: {sig[..Math.Min(16, sig.Length)]}...");
            UpdateStatus($"Signed: {sig[..Math.Min(20, sig.Length)]}...");
            return sig;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignMessage | RESULT=EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            UpdateStatus($"Sign message failed: {CategorizeError(ex)}");
            return "";
        }
    }

    // ─── SIGN TRANSACTION ────────────────────────────────────────────────

    public async Task<Transaction> SignTransaction(Transaction transaction)
    {
        Debug.Log($"{TAG} SignTransaction | START is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null} tx_null={transaction == null}");

        if (!IsConnected)
        {
            Debug.Log($"{TAG} SignTransaction | RESULT=FAIL reason=not_connected");
            UpdateStatus("Not connected");
            return null;
        }

        if (Web3.Wallet == null && !await EnsureWalletSession())
        {
            Debug.Log($"{TAG} SignTransaction | RESULT=FAIL reason=ensure_session_failed");
            return null;
        }

        UpdateStatus("Signing transaction...");

        try
        {
            Debug.Log($"{TAG} SignTransaction | calling Web3.Wallet.SignTransaction() blockhash={transaction.RecentBlockHash} fee_payer={transaction.FeePayer} instructions={transaction.Instructions?.Count ?? 0}");
            var signedTx = await Web3.Wallet.SignTransaction(transaction);
            Debug.Log($"{TAG} SignTransaction | RESULT=SUCCESS signed_tx_null={signedTx == null}");
            AndroidToast.Show("Transaction signed successfully");
            UpdateStatus("Transaction signed");
            return signedTx;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignTransaction | RESULT=EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            UpdateStatus($"Sign transaction failed: {CategorizeError(ex)}");
            return null;
        }
    }

    // ─── SIGN AND SEND TRANSACTION ──────────────────────────────────────

    public async Task<string> SignAndSendTransaction(Transaction transaction)
    {
        Debug.Log($"{TAG} SignAndSendTransaction | START is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null} tx_null={transaction == null}");

        if (!IsConnected)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=FAIL reason=not_connected");
            UpdateStatus("Not connected");
            return "";
        }

        if (Web3.Wallet == null && !await EnsureWalletSession())
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=FAIL reason=ensure_session_failed");
            return "";
        }

        UpdateStatus("Signing and sending transaction...");

        try
        {
            Debug.Log($"{TAG} SignAndSendTransaction | calling Web3.Wallet.SignAndSendTransaction() blockhash={transaction.RecentBlockHash} fee_payer={transaction.FeePayer} instructions={transaction.Instructions?.Count ?? 0} skipPreflight=false commitment=Confirmed");
            var result = await Web3.Wallet.SignAndSendTransaction(
                transaction,
                skipPreflight: false,
                commitment: Commitment.Confirmed
            );

            Debug.Log($"{TAG} SignAndSendTransaction | RPC returned successful={result.WasSuccessful} result={result.Result ?? "null"} reason={result.Reason ?? "null"} error_code={result.ServerErrorCode}");

            if (result.WasSuccessful)
            {
                Debug.Log($"{TAG} SignAndSendTransaction | RESULT=SUCCESS tx_sig={result.Result} sig_len={result.Result.Length}");
                AndroidToast.Show($"Transaction sent: {result.Result[..Math.Min(16, result.Result.Length)]}...", longDuration: true);
                UpdateStatus($"Sent! Sig: {result.Result[..Math.Min(20, result.Result.Length)]}...");
                return result.Result;
            }
            else
            {
                Debug.Log($"{TAG} SignAndSendTransaction | RESULT=RPC_FAIL reason={result.Reason} error_code={result.ServerErrorCode}");
                UpdateStatus($"Send failed: {result.Reason}");
                return "";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            UpdateStatus($"Sign & send failed: {CategorizeError(ex)}");
            return "";
        }
    }

    // ─── GET CAPABILITIES ────────────────────────────────────────────────

    public async Task<WalletCapabilities> GetCapabilities()
    {
        Debug.Log($"{TAG} GetCapabilities | START is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null}");
        UpdateStatus("Querying wallet capabilities...");

        // get_capabilities is a real MWA 2.0 non-privileged method.
        // We implemented it in the SDK (CapabilitiesResult). Try the real call first.
        try
        {
            Debug.Log($"{TAG} GetCapabilities | attempting real SDK get_capabilities call");

            // get_capabilities opens its own MWA session (non-privileged, no auth needed)
            var adapter = new SolanaMobileWalletAdapter(
                new SolanaMobileWalletAdapterOptions(),
                AppConfig.SdkCluster
            );
            var capsResult = await adapter.GetCapabilities();

            Debug.Log($"{TAG} GetCapabilities | SDK RESULT caps_null={capsResult == null} max_txs={capsResult?.MaxTransactionsPerRequest} max_msgs={capsResult?.MaxMessagesPerRequest} versions={string.Join(",", capsResult?.SupportedTransactionVersions ?? new System.Collections.Generic.List<string>())} features={string.Join(",", capsResult?.Features ?? new System.Collections.Generic.List<string>())}");

            var caps = new WalletCapabilities
            {
                MaxTransactionsPerRequest = capsResult?.MaxTransactionsPerRequest ?? 0,
                MaxMessagesPerRequest = capsResult?.MaxMessagesPerRequest ?? 0,
                SupportedTransactionVersions = capsResult?.SupportedTransactionVersions?.ToArray() ?? new[] { "legacy", "0" }
            };

            AndroidToast.Show($"Capabilities: max_txs={caps.MaxTransactionsPerRequest} max_msgs={caps.MaxMessagesPerRequest}");
            Debug.Log($"{TAG} GetCapabilities | RESULT=SUCCESS source=SDK max_txs={caps.MaxTransactionsPerRequest} max_msgs={caps.MaxMessagesPerRequest} versions={string.Join(",", caps.SupportedTransactionVersions)}");
            return caps;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} GetCapabilities | SDK call failed type={ex.GetType().Name} msg={ex.Message} — falling back to spec defaults");

            // Fallback to MWA spec defaults if SDK call fails (wallet may not support it)
            var fallback = new WalletCapabilities
            {
                MaxTransactionsPerRequest = 10,
                MaxMessagesPerRequest = 10,
                SupportedTransactionVersions = new[] { "legacy", "0" }
            };

            AndroidToast.Show("get_capabilities not supported by wallet — showing spec defaults");
            Debug.Log($"{TAG} GetCapabilities | RESULT=FALLBACK source=spec_defaults max_txs={fallback.MaxTransactionsPerRequest} max_msgs={fallback.MaxMessagesPerRequest}");
            return fallback;
        }
    }

    // ─── DELETE ACCOUNT ──────────────────────────────────────────────────

    public async Task<bool> DeleteAccount()
    {
        Debug.Log($"{TAG} DeleteAccount | START pubkey={ConnectedPubkey} wallet_type={ConnectedWalletType} ({WalletTypeName(ConnectedWalletType)}) is_connected={IsConnected}");

        if (!IsConnected)
        {
            Debug.Log($"{TAG} DeleteAccount | RESULT=FAIL reason=not_connected");
            UpdateStatus("Not connected");
            return false;
        }

        bool walletApproved = false;
        string routeReason;

        if (ConnectedWalletType == WALLET_SOLFLARE)
        {
            // Solflare: signMessage broken on MWA — re-auth for confirmation
            routeReason = "solflare_sign_broken";
            Debug.Log($"{TAG} DeleteAccount | ROUTE=re-auth reason={routeReason} wallet_type={ConnectedWalletType}");
            try
            {
                UpdateStatus("Approve in Solflare to confirm deletion...");
                Web3.Instance.rpcCluster = AppConfig.SdkCluster;
                var account = await Web3.Instance.LoginWalletAdapter();
                walletApproved = account != null;
                Debug.Log($"{TAG} DeleteAccount | re-auth returned account={account != null} approved={walletApproved}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{TAG} DeleteAccount | re-auth EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            }
        }
        else if (Web3.Wallet != null)
        {
            // Wallet session is live — use SignMessage so user sees approval in wallet (matches Godot)
            routeReason = "sign_message_wallet_live";
            var confirmMsg = $"Confirm account deletion for {AppConfig.AppName}";
            Debug.Log($"{TAG} DeleteAccount | ROUTE=sign_message reason={routeReason} wallet_type={ConnectedWalletType} Web3.Wallet=True message=\"{confirmMsg}\"");
            try
            {
                UpdateStatus("Confirm deletion in your wallet...");
                var sig = await SignMessage(confirmMsg);
                walletApproved = !string.IsNullOrEmpty(sig);
                Debug.Log($"{TAG} DeleteAccount | sign_message returned sig_empty={string.IsNullOrEmpty(sig)} sig_len={sig?.Length ?? 0} approved={walletApproved}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{TAG} DeleteAccount | sign_message EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            }
        }
        else
        {
            // No live wallet session (cache reconnect) — re-auth as fallback
            routeReason = "no_wallet_session_reauth";
            Debug.Log($"{TAG} DeleteAccount | ROUTE=re-auth reason={routeReason} wallet_type={ConnectedWalletType} Web3.Wallet=False");
            try
            {
                UpdateStatus("Approve in your wallet to confirm deletion...");
                Web3.Instance.rpcCluster = AppConfig.SdkCluster;
                var account = await Web3.Instance.LoginWalletAdapter();
                walletApproved = account != null;
                Debug.Log($"{TAG} DeleteAccount | re-auth returned account={account != null} approved={walletApproved}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{TAG} DeleteAccount | re-auth EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
            }
        }

        Debug.Log($"{TAG} DeleteAccount | APPROVAL wallet_approved={walletApproved} route={routeReason}");

        if (!walletApproved)
        {
            Debug.Log($"{TAG} DeleteAccount | RESULT=ABORTED reason=wallet_did_not_confirm");
            UpdateStatus("Delete cancelled — confirmation required");
            return false;
        }

        if (!string.IsNullOrEmpty(ConnectedPubkey))
            _deletedPubkeys.Add(ConnectedPubkey);

        Debug.Log($"{TAG} DeleteAccount | PRE_CLEANUP pubkey={ConnectedPubkey} deleted_keys={_deletedPubkeys.Count}");

        await Deauthorize();
        Cache.ClearAll();

        try { Web3.Instance.Logout(); } catch (Exception) { }
        Debug.Log($"{TAG} DeleteAccount | RESULT=SUCCESS cache_cleared=true session_destroyed=true deleted_keys={_deletedPubkeys.Count}");
        AndroidToast.Show("Account deleted — all cached data cleared", longDuration: true);
        UpdateStatus("Account deleted — all cached data cleared");
        return true;
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    private string CategorizeError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("AUTHORIZATION_NOT_GRANTED")) return "User denied authorization";
        if (msg.Contains("NOT_SIGNED") || msg.Contains("USER_DECLINED")) return "User declined to sign";
        if (msg.Contains("TOO_MANY_PAYLOADS")) return "Too many payloads for this wallet";
        if (msg.Contains("NOT_SUPPORTED") || msg.Contains("METHOD_NOT_FOUND")) return "Operation not supported by this wallet";
        if (msg.Contains("timeout") || msg.Contains("Timeout")) return "Wallet connection timed out";
        return msg;
    }

    public string TruncatePubkey(string pubkey)
    {
        if (string.IsNullOrEmpty(pubkey) || pubkey.Length <= 8) return pubkey ?? "";
        return pubkey[..4] + "..." + pubkey[^4..];
    }

    public static string WalletTypeName(int walletType)
    {
        return walletType switch
        {
            WALLET_PHANTOM => "Phantom",
            WALLET_SOLFLARE => "Solflare",
            WALLET_BACKPACK => "Backpack",
            WALLET_JUPITER => "Jupiter",
            _ => "Seed Vault/Other"
        };
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
