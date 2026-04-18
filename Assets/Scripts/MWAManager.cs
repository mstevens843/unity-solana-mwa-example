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

    /// <summary>
    /// Last error code from the most recent MWA operation. Populated on
    /// failure paths (cleared on successful call start). Lets UI branch on
    /// specific conditions — e.g. `INSUFFICIENT_FUNDS_FOR_RENT` so we can
    /// show a "fund the account" toast instead of a generic "send failed".
    /// See KNOWN_ISSUES.md for the taxonomy.
    /// </summary>
    public string LastErrorCode { get; private set; } = "";

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

        // Pass 14 note: `Web3.Instance` is typically null during Awake on cold
        // start (Web3 MonoBehaviour hasn't finished its own Awake yet), so
        // cluster + SIWS options must be set lazily inside `Authorize()` /
        // `Reauthorize()` right before `LoginWalletAdapter()`. If
        // `Web3.Instance` IS available here we still set the cluster eagerly
        // for any code that reads it before the first Connect.
        if (Web3.Instance != null)
        {
            Web3.Instance.rpcCluster = AppConfig.SdkCluster;
            Debug.Log($"{TAG} Awake | set Web3.rpcCluster={Web3.Instance.rpcCluster} ({AppConfig.Cluster}) — SIWS options applied in Authorize()");
        }
        else
        {
            Debug.Log($"{TAG} Awake | Web3.Instance null (expected on cold start) — cluster + SIWS options applied on first Authorize()");
        }

        Debug.Log($"{TAG} Awake | DONE singleton established cache_type={Cache.GetType().Name}");
        Debug.Log($"{TAG} Awake | platform={Application.platform} version={Application.version} app={AppConfig.AppName} cluster={AppConfig.Cluster} use_os_picker={AppConfig.UseOsPicker}");
        Debug.Log($"{TAG} Awake | feature_flags use_mwa_sign_and_send={AppConfig.UseMwaSignAndSend} use_siws={AppConfig.UseSiws} use_auth_cache={AppConfig.UseAuthCache}");
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

            // Pass 14 fix: apply SIWS config HERE (not in Awake()) because
            // `Web3.Instance` is null during Awake on cold start, so the
            // Awake-time block is a no-op — `_walletOptions.siwsDomain` stays
            // null and `_Login` takes the LEGACY path, ignoring
            // `AppConfig.UseSiws`. Setting the options right before
            // `LoginWalletAdapter()` guarantees the SDK sees the current flag
            // value every time. Non-null domain → SDK's
            // `SolanaMobileWalletAdapter._Login` takes the SIWS path
            // (`authorize` + `sign_in_payload`, with an in-session
            // `sign_messages` fallback for wallets that don't return
            // `sign_in_result` natively). Null domain → plain MWA 1.x
            // `authorize`.
            var mwaOpts = Web3.Instance.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions;
            if (AppConfig.UseSiws)
            {
                mwaOpts.siwsDomain = AppConfig.SiwsDomain;
                mwaOpts.siwsStatement = AppConfig.SiwsStatement;
            }
            else
            {
                mwaOpts.siwsDomain = null;
                mwaOpts.siwsStatement = null;
            }

            Debug.Log($"{TAG} Authorize | calling LoginWalletAdapter() cluster={Web3.Instance.rpcCluster} mode={(AppConfig.UseSiws ? "siws" : "plain_authorize")} siwsDomain={mwaOpts.siwsDomain ?? "(null)"} siwsStatement={mwaOpts.siwsStatement ?? "(null)"}");
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

            var walletAdapter = Web3.Wallet as SolanaWalletAdapter;
            if (AppConfig.UseSiws)
            {
                var signInResult = walletAdapter?.LastSignInResult;
                Debug.Log($"{TAG} Authorize | SIWS_RESULT adapter_null={walletAdapter == null} result_null={signInResult == null} address={signInResult?.Address ?? "null"} sig_type={signInResult?.SignatureType ?? "null"}");
                if (signInResult != null)
                {
                    Debug.Log($"{TAG} Authorize | SIWS_VERIFIED address={signInResult.Address} sig_len={signInResult.Signature?.Length ?? 0}");
                    AndroidToast.Show($"Signed in with Solana: {TruncatePubkey(ConnectedPubkey)}");
                }
                else
                {
                    Debug.LogWarning($"{TAG} Authorize | SIWS_MISSING wallet didn't return sign_in_result (Phantom/Solflare degrade here)");
                    AndroidToast.Show($"Connected via {WalletTypeName(ConnectedWalletType)}");
                }
            }
            else
            {
                AndroidToast.Show($"Connected via {WalletTypeName(ConnectedWalletType)}");
            }

            // Pass 14: cache write is flag-gated. UseAuthCache=false keeps the
            // demo SIWS/Connect flow working but skips PlayerPrefs persistence
            // so cold-start auto-sign-in doesn't fire.
            if (AppConfig.UseAuthCache)
            {
                Cache.Set(ConnectedPubkey, AuthToken, walletType: ConnectedWalletType);
                Debug.Log($"{TAG} Authorize | CACHED pubkey={ConnectedPubkey} auth_token_len={AuthToken.Length} wallet_type={ConnectedWalletType}");
            }
            else
            {
                Debug.Log($"{TAG} Authorize | CACHE_SKIPPED UseAuthCache=false");
            }

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
        Debug.Log($"{TAG} Reauthorize | START is_connected={IsConnected} use_auth_cache={AppConfig.UseAuthCache}");

        // Pass 14: when AuthCache is disabled, there's nothing to restore —
        // Reconnect (cached) becomes a no-op that surfaces "no cached auth".
        if (!AppConfig.UseAuthCache)
        {
            Debug.Log($"{TAG} Reauthorize | RESULT=FAIL reason=auth_cache_disabled");
            UpdateStatus("Auth cache disabled — use Connect instead");
            await Task.CompletedTask;
            return false;
        }

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
        Debug.Log($"{TAG} SignAndSendTransaction | START is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null} tx_null={transaction == null} wallet_type={ConnectedWalletType} ({WalletTypeName(ConnectedWalletType)})");
        LastErrorCode = "";

        if (!IsConnected)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=FAIL reason=not_connected");
            LastErrorCode = "NOT_CONNECTED";
            UpdateStatus("Not connected");
            return "";
        }

        if (Web3.Wallet == null && !await EnsureWalletSession())
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=FAIL reason=ensure_session_failed");
            LastErrorCode = "NO_SESSION";
            return "";
        }

        // Pre-broadcast balance check — short-circuit when we know the
        // fee-payer can't cover Solana's rent-exempt minimum + tx fee +
        // priority-fee buffer. Seed Vault's Solflare-wrapper injects
        // ComputeBudget priority-fee instructions before signing which pushes
        // the required balance higher than a bare memo-tx fee alone. Surfaces
        // a specific INSUFFICIENT_FUNDS_FOR_RENT code so the UI can show a
        // truthful "fund the account" toast instead of "send failed".
        // See KNOWN_ISSUES.md "Insufficient funds for rent".
        const ulong RentExemptBufferLamports = 1_000_000UL; // 890_880 rent + ~5000 fee + ~100_000 priority-fee buffer
        try
        {
            var balResult = await Web3.Rpc.GetBalanceAsync(Web3.Wallet.Account.PublicKey, Commitment.Confirmed);
            ulong lamports = balResult?.Result?.Value ?? 0UL;
            if (balResult == null || !balResult.WasSuccessful)
            {
                Debug.Log($"{TAG} SignAndSendTransaction | STEP_PREFLIGHT_BALANCE_UNKNOWN rpc_call_failed reason={balResult?.Reason} — proceeding anyway");
            }
            else if (lamports < RentExemptBufferLamports)
            {
                Debug.Log($"{TAG} SignAndSendTransaction | STEP_PREFLIGHT_FAIL balance={lamports} required=~{RentExemptBufferLamports} pubkey={ConnectedPubkey}");
                LastErrorCode = "INSUFFICIENT_FUNDS_FOR_RENT";
                UpdateStatus($"Fee-payer underfunded — send ≥0.001 SOL to {TruncatePubkey(ConnectedPubkey)} and retry");
                return "";
            }
            else
            {
                Debug.Log($"{TAG} SignAndSendTransaction | STEP_PREFLIGHT_BALANCE_OK balance={lamports} threshold={RentExemptBufferLamports}");
            }
        }
        catch (Exception bex)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | STEP_PREFLIGHT_BALANCE_EXCEPTION type={bex.GetType().Name} msg={bex.Message} — proceeding anyway");
        }

        // Backpack's native `sign_and_send_transactions` MWA handler crashes
        // with a Kotlin JsonDecodingException ("Class discriminator was
        // missing…"). Their `sign_transactions` handler works fine. Route
        // Backpack through sign-only + our own RPC broadcast — one wallet
        // intent, one approval, same UX as the native path.
        // See KNOWN_ISSUES.md "Backpack sign_and_send crashes".
        if (ConnectedWalletType == WALLET_BACKPACK)
        {
            return await SignAndBroadcastViaRpc(transaction, "backpack_sign_and_send_bug");
        }

        // Pass 14: `UseMwaSignAndSend=false` routes every other wallet through
        // the same sign-then-RPC path Backpack uses. One wallet intent, one
        // approval, app-side broadcast. Flag ON (default) keeps the native
        // MWA `sign_and_send_transactions` path.
        if (!AppConfig.UseMwaSignAndSend)
        {
            return await SignAndBroadcastViaRpc(transaction, "use_mwa_sign_and_send_flag_off");
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
                if (IsInsufficientRentError(result.Reason))
                {
                    LastErrorCode = "INSUFFICIENT_FUNDS_FOR_RENT";
                    UpdateStatus($"Fee-payer underfunded — send ≥0.001 SOL to {TruncatePubkey(ConnectedPubkey)} and retry");
                }
                else
                {
                    LastErrorCode = "RPC_BROADCAST_FAILED";
                    UpdateStatus($"Send failed: {result.Reason}");
                }
                return "";
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignAndSendTransaction | RESULT=EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            LastErrorCode = "EXCEPTION";
            UpdateStatus($"Sign & send failed: {CategorizeError(ex)}");
            return "";
        }
    }

    // ─── SIGN + RPC BROADCAST (Backpack fallback path) ──────────────────
    //
    // Sign the tx via MWA `sign_transactions` (which Backpack DOES implement
    // correctly), then broadcast to the Solana JSON-RPC endpoint ourselves.
    // One wallet intent, one user approval, identical UX to the native
    // sign_and_send. Used for Backpack because its native sign_and_send
    // handler crashes.
    private async Task<string> SignAndBroadcastViaRpc(Transaction transaction, string reason)
    {
        Debug.Log($"{TAG} SignAndBroadcastViaRpc | START reason={reason} blockhash={transaction.RecentBlockHash} fee_payer={transaction.FeePayer} instructions={transaction.Instructions?.Count ?? 0}");
        UpdateStatus("Signing and sending transaction...");

        try
        {
            // STEP 1: sign via MWA (one wallet intent)
            Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_1_MWA_SIGN_START");
            var signedTx = await Web3.Wallet.SignTransaction(transaction);
            if (signedTx == null)
            {
                Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_1_FAIL signed_tx_null");
                UpdateStatus("Sign & send failed — wallet returned no signed transaction");
                return "";
            }

            // STEP 2: serialize for RPC
            byte[] serialized = signedTx.Serialize();
            string base64 = Convert.ToBase64String(serialized);
            Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_2_SERIALIZED raw_bytes={serialized.Length} base64_len={base64.Length}");

            // STEP 3: broadcast via Solana JSON-RPC
            Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_3_RPC_SEND_START skipPreflight=false preflightCommitment=Confirmed");
            var sendResult = await Web3.Rpc.SendTransactionAsync(base64, skipPreflight: false, preFlightCommitment: Commitment.Confirmed);
            Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_3_RPC_SEND_DONE successful={sendResult.WasSuccessful} result={sendResult.Result ?? "null"} reason={sendResult.Reason ?? "null"} error_code={sendResult.ServerErrorCode}");

            if (!sendResult.WasSuccessful || string.IsNullOrEmpty(sendResult.Result))
            {
                if (IsInsufficientRentError(sendResult.Reason))
                {
                    Debug.Log($"{TAG} SignAndBroadcastViaRpc | STEP_3_RPC_SEND_FAIL INSUFFICIENT_FUNDS_FOR_RENT reason={sendResult.Reason}");
                    LastErrorCode = "INSUFFICIENT_FUNDS_FOR_RENT";
                    UpdateStatus($"Fee-payer underfunded — send ≥0.001 SOL to {TruncatePubkey(ConnectedPubkey)} and retry");
                }
                else
                {
                    LastErrorCode = "RPC_BROADCAST_FAILED";
                    UpdateStatus($"RPC broadcast failed: {sendResult.Reason ?? "empty signature"}");
                }
                return "";
            }

            AndroidToast.Show($"Transaction sent: {sendResult.Result[..Math.Min(16, sendResult.Result.Length)]}...", longDuration: true);
            UpdateStatus($"Sent! Sig: {sendResult.Result[..Math.Min(20, sendResult.Result.Length)]}...");
            return sendResult.Result;
        }
        catch (Exception ex)
        {
            Debug.Log($"{TAG} SignAndBroadcastViaRpc | EXCEPTION type={ex.GetType().Name} msg={ex.Message} stack={ex.StackTrace}");
            LastErrorCode = "EXCEPTION";
            UpdateStatus($"Sign & send failed: {CategorizeError(ex)}");
            return "";
        }
    }

    /// <summary>
    /// Detects Solana RPC `InsufficientFundsForRent` preflight simulation
    /// errors from a sendTransaction `Reason` string. The Solana JSON-RPC
    /// endpoint returns code -32002 with a structured err payload
    /// `{"InsufficientFundsForRent":{"account_index":N}}` which the Unity
    /// SDK surfaces through `RequestResult.Reason`. Matches both the
    /// structured err name and the plain-English version.
    /// See KNOWN_ISSUES.md "Insufficient funds for rent".
    /// </summary>
    private static bool IsInsufficientRentError(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return false;
        return reason.IndexOf("InsufficientFundsForRent", StringComparison.Ordinal) >= 0
            || reason.IndexOf("insufficient funds for rent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ─── GET CAPABILITIES ────────────────────────────────────────────────
    // TODO: Re-enable when SDK GetCapabilities is available on this branch
    /*
    public async Task<WalletCapabilities> GetCapabilities()
    {
        Debug.Log($"{TAG} GetCapabilities | START is_connected={IsConnected} Web3.Wallet={Web3.Wallet != null}");
        UpdateStatus("Querying wallet capabilities...");

        try
        {
            Debug.Log($"{TAG} GetCapabilities | attempting real SDK get_capabilities call");

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
    */

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

        // Phantom and Solflare do NOT implement `sign_messages` over MWA on
        // Android — their `get_capabilities` feature lists omit it. Calling
        // sign_messages hangs or drops the WebSocket. Gate delete on a
        // throwaway memo-only `sign_transactions` call (which both DO
        // implement) and treat a successful signature as user confirmation.
        // The signed tx is NEVER broadcast — no lamports spent, no memo
        // hits chain. See KNOWN_ISSUES.md "Phantom/Solflare sign_messages".
        if (ConnectedWalletType == WALLET_PHANTOM || ConnectedWalletType == WALLET_SOLFLARE)
        {
            routeReason = ConnectedWalletType == WALLET_PHANTOM ? "phantom_no_sign_messages" : "solflare_no_sign_messages";
            Debug.Log($"{TAG} DeleteAccount | ROUTE=sign_transactions_memo reason={routeReason} wallet_type={ConnectedWalletType}");
            try
            {
                UpdateStatus("Confirm deletion in your wallet...");
                walletApproved = await ConfirmDeleteViaMemoTx();
                Debug.Log($"{TAG} DeleteAccount | memo_tx_gate returned approved={walletApproved}");
            }
            catch (Exception ex)
            {
                Debug.Log($"{TAG} DeleteAccount | memo_tx_gate EXCEPTION type={ex.GetType().Name} msg={ex.Message}");
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

    // ─── MEMO-TX CONFIRMATION GATE (Phantom/Solflare delete) ────────────
    //
    // Build a throwaway memo-only transaction with ownership-proof wording
    // and submit it through `sign_transactions`. The signature proves user
    // consent; the signed tx is NEVER broadcast — the blockhash will
    // harmlessly expire. No lamports spent, no memo on chain. Used ONLY by
    // Phantom and Solflare delete flows because they don't implement
    // `sign_messages` over MWA Android.
    private async Task<bool> ConfirmDeleteViaMemoTx()
    {
        if (Web3.Wallet == null || Web3.Rpc == null)
        {
            Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | FAIL wallet_or_rpc_null wallet_null={Web3.Wallet == null} rpc_null={Web3.Rpc == null}");
            return false;
        }

        Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_1_BLOCKHASH_START");
        var blockhashResult = await Web3.Rpc.GetLatestBlockHashAsync();
        if (!blockhashResult.WasSuccessful)
        {
            Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_1_BLOCKHASH_FAIL reason={blockhashResult.Reason}");
            UpdateStatus("Delete failed — could not reach Solana RPC");
            return false;
        }
        var blockhash = blockhashResult.Result.Value.Blockhash;
        var fromPubkey = Web3.Wallet.Account.PublicKey;
        Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_1_BLOCKHASH_OK blockhash={blockhash.Substring(0, 12)}... fee_payer={fromPubkey}");

        // 16-char alphanumeric nonce; makes the memo text unique so the user
        // sees a fresh confirmation request (not a cached preview).
        var nonce = Guid.NewGuid().ToString("N").Substring(0, 16);
        var memoText = $"{AppConfig.AppName}: wallet ownership proof, nonce={nonce}";
        var memoData = System.Text.Encoding.UTF8.GetBytes(memoText);
        var memoProgramId = new PublicKey("MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr");
        var memoInstruction = new TransactionInstruction
        {
            ProgramId = memoProgramId.KeyBytes,
            Keys = new List<AccountMeta> { AccountMeta.Writable(fromPubkey, true) },
            Data = memoData,
        };
        var tx = new Transaction
        {
            RecentBlockHash = blockhash,
            FeePayer = fromPubkey,
        };
        tx.Add(memoInstruction);
        Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_2_TX_BUILT memo=\"{memoText}\" memo_bytes={memoData.Length}");

        Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_3_SIGN_START");
        var signedTx = await Web3.Wallet.SignTransaction(tx);
        Debug.Log($"{TAG} ConfirmDeleteViaMemoTx | STEP_3_SIGN_DONE signed_tx_null={signedTx == null}");

        // Signature present = user approved. We intentionally skip RPC
        // broadcast: the signature alone is proof of ownership.
        return signedTx != null;
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
