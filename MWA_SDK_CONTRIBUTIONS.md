# Solana.Unity-SDK MWA Contributions

**Issue:** [magicblock-labs/Solana.Unity-SDK#273](https://github.com/magicblock-labs/Solana.Unity-SDK/issues/273)
**Filed by:** @mstevens843
**Working Example App:** [unity-solana-mwa-example](https://github.com/mstevens843/unity-solana-mwa-example)
**Tested on:** Solana Seeker hardware, Seed Vault wallet, Unity 6000.4.1f1, IL2CPP, Android API 35

---

## Issue Filed — 7 Points

| # | Issue | Status |
|---|-------|--------|
| 1 | `LoginWalletAdapter()` shows wallet picker UI when only one MWA adapter available — should auto-select | **Open — PR opportunity** |
| 2 | `Web3.Instance.Logout()` only clears local state — no way to revoke MWA authorization | Covered by PR #269 |
| 3 | No built-in auth token cache for MWA sessions — had to build custom `IMwaAuthCache` with PlayerPrefs | Tracked in issue #271 |
| 4 | `GetCapabilities` not exposed in the SDK | Covered by PR #269 |
| 5 | `WebSocket Error: An exception has occurred during an OnMessage event` fires on every MWA handshake — non-blocking but pollutes logs | **Open — PR opportunity** |
| 6 | No `SignMessage(string)` overload — requires manual `Encoding.UTF8.GetBytes()` | **Open — PR opportunity** |
| 7 | No way to access MWA auth token from `LoginWalletAdapter()` return value — `Account` only exposes `PublicKey` | **Open — PR opportunity** |

---

## Maintainer Response (from PR #269 author)

> Hey @mstevens843 great writeup, and it's really useful validation that you hit the same gaps independently.
>
> Several of these are already addressed in PR #269 (currently open and awaiting merge):
>
> - **Point 2**: `Logout()` now calls `Deauthorize()` RPC before clearing local state
> - **Point 3**: `IMwaAuthCache` abstraction is tracked in issue #271, with PlayerPrefs as the default fallback
> - **Point 4**: `GetCapabilities()` is implemented and exposed
> - **Point 2/reconnect**: `DisconnectWallet()` and `ReconnectWallet()` lifecycle helpers added with `OnWalletDisconnected` / `OnWalletReconnected` events
>
> All of these are tested on Solana Seeker hardware inside a gameplay loop, screen recording attached to the PR.
>
> **Points 1, 5, 6, and 7 are not covered by #269 and would make good follow-up issues or PRs.** Happy to collaborate if you want to pick any of those up after #269 merges.

---

## PR Opportunities — What We Can Contribute

### PR 1: Auto-select wallet adapter (Point 1)
**Problem:** `LoginWalletAdapter()` shows a wallet picker even when only one adapter is available on the platform.
**Fix:** In `SolanaWalletAdapter` constructor or `Login()`, check if only one adapter is resolved and auto-select it. Skip the `WalletAdapterUI` prefab instantiation.
**File:** `Runtime/codebase/SolanaWalletAdapter.cs`
**Priority:** High — improves UX on every Android MWA connect

### PR 2: Fix WebSocket OnMessage exception (Point 5)
**Problem:** `WebSocket Error: An exception has occurred during an OnMessage event` logs on every MWA handshake.
**Fix:** Add proper exception handling in the WebSocket message callback, or suppress the non-blocking error during MWA session establishment.
**File:** `Runtime/codebase/WalletBase.cs` or wherever `WebSocketSharp` handlers are configured
**Priority:** Medium — log noise, not a functional issue

### PR 3: SignMessage(string) overload (Point 6)
**Problem:** `SignMessage()` only accepts `byte[]`. Every caller has to do `Encoding.UTF8.GetBytes()` manually.
**Fix:** Add convenience overload: `public async Task<byte[]> SignMessage(string message)` that handles the encoding internally.
**File:** `Runtime/codebase/WalletBase.cs`
**Priority:** Low — small QoL improvement, easy PR

### PR 4: Expose MWA auth token (Point 7)
**Problem:** `LoginWalletAdapter()` returns `Account` which only has `PublicKey`. The MWA auth token is internal to `SolanaMobileWalletAdapter` and not accessible for external caching.
**Fix:** Either expose `AuthToken` on the `Account` return value, or provide a getter on `Web3.Instance.Wallet` after login.
**File:** `Runtime/codebase/SolanaMobileWalletAdapter.cs`, `Runtime/codebase/data/Account.cs`
**Priority:** High — needed for proper auth cache parity with React Native

---

## Next Steps

1. **Wait for PR #269 to merge** — points 2, 3, 4 are handled there
2. **Fork Solana.Unity-SDK** — `git clone https://github.com/magicblock-labs/Solana.Unity-SDK.git`
3. **Create branch per PR** — `feat/auto-select-adapter`, `fix/websocket-mwa-error`, `feat/sign-message-string`, `feat/expose-auth-token`
4. **Start with Point 6 (SignMessage string overload)** — smallest, easiest, good first PR to establish contributor relationship
5. **Then Point 1 (auto-select)** — highest UX impact
6. **Then Point 7 (auth token)** — needed for cache parity
7. **Then Point 5 (WebSocket)** — investigate root cause before fixing

---

## Timeline

- PR #269 merge: waiting on maintainer
- Our PRs: start after #269 merges (to avoid conflicts)
- Grant submission: example app is complete and working, PRs are bonus contributions

---
---

# Part 2: Bug Fixes & SDK Implementation

Found and fixed during hardware testing on Solana Seeker with Phantom, Solflare, Seed Vault, and Jupiter wallets.

---

## App-Level Bug Fixes (10 issues found + fixed)

### Bug #8: RPC Cluster Defaulting to DevNet
**Severity:** Critical — blocked ALL non-Seed-Vault wallets
**Symptom:** Phantom and Backpack showed "Incorrect mode — solana.unity-sdk.gg is trying to use a testnet." Jupiter hung on connecting. Solflare redirect flow broke.
**Root cause:** `Web3.rpcCluster` defaults to `RpcCluster.DevNet` in the SDK. `AppConfig.Cluster = "mainnet-beta"` was declared but never passed to the SDK. The Godot version calls `wallet_adapter.set_mobile_blockchain(1)` at startup.
**Fix:** Set `Web3.Instance.rpcCluster = AppConfig.SdkCluster` at runtime before every `LoginWalletAdapter()` call. Added `SdkCluster` property to `AppConfig` that maps the cluster string to the SDK's `RpcCluster` enum.
**Files:** `MWAManager.cs`, `AppConfig.cs`, `SceneBuilder.cs`

### Bug #9: Sign-In Chained for All Wallets in OS Picker Mode
**Severity:** Critical — Phantom connect hung forever
**Symptom:** User selected Phantom via OS picker, connect succeeded, then `SignMessage("Sign in to...")` was called and hung indefinitely. The MWA session opened for signing never completed.
**Root cause:** `ConnectedWalletType` stays -1 in OS picker mode (Unity SDK doesn't update it after connect, unlike Godot where `wallet_adapter.wallet_type` is updated by the SDK). Code checked `ConnectedWalletType < 0` and treated ALL OS picker connections as Seed Vault, chaining SignMessage.
**Fix:** Changed check to `ConnectedWalletType < 0 && !AppConfig.UseOsPicker`. In OS picker mode, skip sign for all wallets. In in-app picker mode, chain sign only for Seed Vault button.
**Files:** `MWAManager.cs`

### Bug #10: Deleted Keys Cleared at Wrong Time
**Severity:** High — user could never reconnect with same wallet after delete
**Symptom:** After delete account then connect, the SDK returned the same pubkey but it was rejected by the `_deletedPubkeys.Contains()` check. User permanently locked out of that wallet.
**Root cause:** `_deletedPubkeys.Clear()` was called AFTER successful auth (line 160), but the rejection check ran BEFORE (line 91). Godot clears `_deleted_keys` at the START of `authorize()` (line 186-188) — connect is a fresh slate.
**Fix:** Moved `_deletedPubkeys.Clear()` to the start of `Authorize()`, before `LoginWalletAdapter()`. Removed the mid-flow rejection check.
**Files:** `MWAManager.cs`

### Bug #11: Reauthorize Opened Wallet Picker Instead of Using Cache
**Severity:** High — reconnect was not instant
**Symptom:** Tapping "Reconnect" opened the wallet picker again instead of instantly restoring the session from cache. Godot's `reauthorize()` is pure cache — no SDK call, no picker.
**Root cause:** `Reauthorize()` delegated to `Authorize(cached.walletType)` which called `Web3.Instance.LoginWalletAdapter()`.
**Fix:** Rewrote `Reauthorize()` as pure cache-based reconnect: load pubkey + wallet_type from `AuthCache`, set state, emit `OnAuthorized` immediately. Added `EnsureWalletSession()` helper for lazy SDK session init when the user performs their first wallet operation after reconnect.
**Files:** `MWAManager.cs`

### Bug #12: Delete Account Blocked by Two-Tap UI Gate
**Severity:** High — delete never reached wallet confirmation
**Symptom:** User tapped Delete, saw "Tap Delete again within 3s to confirm", but the 3-second window expired before the second tap registered. Every attempt showed "first tap" then timed out. `DeleteAccount()` never ran, wallet sign confirmation never opened.
**Root cause:** Unity-only two-tap confirmation with 3-second window. Godot has no two-tap — `home.gd` calls `MWAManager.delete_account()` directly. The wallet-side sign/re-auth IS the confirmation.
**Fix:** Removed two-tap gate. Single tap disables buttons and calls `DeleteAccount()` directly, matching Godot. The MWA sign prompt in the wallet is the user's confirmation.
**Files:** `HomeUI.cs`

### Bug #13: Delete Account Had Re-Auth Fallback (Godot Doesn't)
**Severity:** Medium — different behavior from Godot reference
**Symptom:** If SignMessage failed during delete, Unity fell back to `LoginWalletAdapter()` for re-auth confirmation. Godot has no fallback — sign fails = delete cancelled.
**Fix:** Removed the re-auth fallback in the else branch. Sign fails = delete cancelled, matching Godot exactly.
**Files:** `MWAManager.cs`

### Bug #14: Delete Routed OS Picker to Re-Auth Instead of SignMessage
**Severity:** Medium — wrong confirmation flow for OS picker deletes
**Symptom:** OS picker connections (`ConnectedWalletType == -1`) were routed to `LoginWalletAdapter()` for delete confirmation instead of `SignMessage("Confirm account deletion...")`. The re-auth just reopened the wallet picker without a meaningful confirmation prompt.
**Root cause:** Condition `ConnectedWalletType < 0 && AppConfig.UseOsPicker` was added to route OS picker to re-auth (same as Solflare), but the Godot version uses sign_message for all wallets except Solflare regardless of how they connected.
**Fix:** Removed OS picker condition from re-auth branch. Only Solflare routes to re-auth. All others (including OS picker unknown) use SignMessage for confirmation.
**Files:** `MWAManager.cs`

### Bug #15: Reconnect Button Showed "Seed Vault/Other"
**Severity:** Low — cosmetic
**Symptom:** After disconnecting, the Reconnect button on Landing showed "Reconnect (Seed Vault/Other)" because `ConnectedWalletType = -1` in OS picker mode. Godot shows fixed text "Reconnect (Cached)".
**Fix:** Changed button text to static `"Reconnect (Cached)"` and toast to `"Cached session found: {pubkey}..."` matching Godot.
**Files:** `LandingUI.cs`

### Bug #16: Delete Status Text Hardcoded "Solflare"
**Severity:** Low — cosmetic
**Symptom:** Re-auth confirmation branch showed "Approve in Solflare to confirm deletion..." and logged "confirmed via Solflare re-authorization" even though the branch could theoretically handle other wallet types.
**Fix:** Changed to generic "Approve in your wallet to confirm deletion..." and added `wallet_type=` to log.
**Files:** `MWAManager.cs`

### Bug #17: Transaction.Serialize() NullRef in SignTransaction/SignAndSendTransaction
**Severity:** Critical — both Sign Transaction and Sign & Send crashed
**Symptom:** Tapping Sign Transaction or Sign & Send showed "Error: Object reference not set to an instance of an object." The wallet was never prompted.
**Root cause:** Deterministic logging called `transaction.Serialize()` before passing the tx to the wallet. MWA accounts are public-key-only (no local private key), so `Serialize()` threw NullReferenceException because the internal `Signatures` list was null.
**Fix:** Removed `transaction.Serialize()` from the logging lines in `MWAManager.SignTransaction()` and `MWAManager.SignAndSendTransaction()`. The SDK handles serialization internally when sending to the wallet.
**Files:** `MWAManager.cs`

---

## SDK Implementation: `get_capabilities`

### What It Is

`get_capabilities` is a **real MWA 2.0 non-privileged method** defined in the [official spec](https://solana-mobile.github.io/mobile-wallet-adapter/spec/spec.html). It queries a wallet's capabilities and limits without requiring authorization.

### Why It Was Missing

The Unity SDK (`Solana.Unity-SDK`) did not implement this method. The `IAdapterOperations` interface only had: `Authorize`, `Reauthorize`, `SignTransactions`, `SignMessages`. The React Native SDK supports it via `transact()`.

### What We Implemented

**4 files modified in `Solana.Unity-SDK`:**

#### 1. NEW: `CapabilitiesResult.cs`
**Path:** `Runtime/codebase/SolanaMobileStack/JsonRpcClient/Responses/CapabilitiesResult.cs`

Response class matching the MWA 2.0 spec:
```csharp
public class CapabilitiesResult
{
    public int? MaxTransactionsPerRequest { get; set; }     // optional
    public int? MaxMessagesPerRequest { get; set; }         // optional
    public List<string> SupportedTransactionVersions { get; set; }  // ["legacy", "0"]
    public List<string> Features { get; set; }              // optional wallet features
}
```

#### 2. EDIT: `IAdapterOperations.cs`
**Path:** `Runtime/codebase/SolanaMobileStack/Interfaces/IAdapterOperations.cs`

Added method signature to the interface:
```csharp
public Task<CapabilitiesResult> GetCapabilities();
```

#### 3. EDIT: `MobileWalletAdapterClient.cs`
**Path:** `Runtime/codebase/SolanaMobileStack/MobileWalletAdapterClient.cs`

JSON-RPC implementation following the exact same pattern as `Authorize`, `SignTransactions`, etc.:
```csharp
public Task<CapabilitiesResult> GetCapabilities()
{
    var request = new JsonRequest
    {
        JsonRpc = "2.0",
        Method = "get_capabilities",    // MWA spec method name
        Params = new JsonRequest.JsonRequestParams(),  // no params (non-privileged)
        Id = NextMessageId()
    };
    return SendRequest<CapabilitiesResult>(request);
}
```

#### 4. EDIT: `SolanaMobileWalletAdapter.cs`
**Path:** `Runtime/codebase/SolanaMobileStack/SolanaMobileWalletAdapter.cs`

High-level method that opens an MWA session and calls `client.GetCapabilities()`. Since it's non-privileged, NO authorize/reauthorize is queued:
```csharp
public async Task<CapabilitiesResult> GetCapabilities()
{
    CapabilitiesResult capabilities = null;
    var localAssociationScenario = new LocalAssociationScenario();
    var result = await localAssociationScenario.StartAndExecute(
        new List<Action<IAdapterOperations>>
        {
            async client => { capabilities = await client.GetCapabilities(); }
        }
    );
    if (!result.WasSuccessful) throw new Exception(result.Error.Message);
    return capabilities;
}
```

### JSON-RPC Wire Format

**Request:**
```json
{"jsonrpc": "2.0", "method": "get_capabilities", "params": {}, "id": 1}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "max_transactions_per_request": 10,
    "max_messages_per_request": 10,
    "supported_transaction_versions": ["legacy", "0"],
    "features": []
  }
}
```

### Integration in Example App

`MWAManager.GetCapabilities()` attempts the real SDK call first. If the wallet doesn't support `get_capabilities` (throws an error), it falls back to MWA spec defaults with a clear log: `RESULT=FALLBACK source=spec_defaults`.

### Hardware Test Results — Confirmed Working

Tested on Solana Seeker with Phantom wallet. All results from deterministic logs:

```
[MWAManager] GetCapabilities | RESULT=SUCCESS source=SDK max_txs=10 max_msgs=1 versions=legacy,0
```

**`max_msgs=1`** confirms this is real wallet data — our fallback uses `10`. Phantom reports it supports only 1 message per request.

### All MWA API Methods Confirmed Working (Phantom + Seeker)

| Method | Log Result | Data |
|--------|-----------|------|
| `authorize` | `RESULT=SUCCESS pubkey=DDck...97is` | Phantom connected on mainnet |
| `sign_messages` | `RESULT=SUCCESS sig_bytes=64 sig_base64_len=88` | 64-byte Ed25519 signature |
| `sign_transactions` | `RESULT=SUCCESS signed_tx_null=False` | Phantom signed memo tx |
| `sign_and_send_transactions` | `RESULT=SUCCESS tx_sig=HGzedb2FS4ng...` | Tx broadcast to mainnet, confirmed |
| `get_capabilities` | `RESULT=SUCCESS source=SDK max_txs=10 max_msgs=1` | Real wallet data via our SDK implementation |
| `reauthorize` (cache) | `RESULT=SUCCESS Web3.Wallet=False` | Instant cache reconnect |
| `deauthorize` | `RESULT=DONE state_cleared=true` | Session cleared |
