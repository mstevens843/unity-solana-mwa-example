# Known Issues — Unity Solana SDK MWA Integration

> **If you're catching up on this app for the first time:** the three sections at the top ("Phantom sign_messages", "Solflare sign_messages", "Backpack sign_and_send_transactions") are the *wallet*-side gotchas that tripped up development and have per-wallet workarounds in `Assets/Scripts/MWAManager.cs`. The later sections are SDK-level / dev-UX notes.

---

## High: Phantom Mobile Does Not Implement `sign_messages` Over MWA

**Status:** Wallet-side gap — worked around in this example app by routing Phantom's **Delete Account** flow through a throwaway memo-only `sign_transactions` call instead of `sign_messages`.
**Severity:** Was High (delete hung indefinitely on Phantom before the workaround).
**Affects:** Phantom Mobile (`app.phantom`). Same gap exists in the Godot and Cocos example apps.

### What actually happens

Connect Phantom via MWA, then call `sign_messages` with any payload. Phantom opens, the user may even see an approve screen — but the response never reaches the dApp. The request either:
- Hangs for 90 seconds, then the MWA client library's internal timer fires a `TimeoutException` wrapped inside an `ExecutionException`, or
- Closes the WebSocket after ~5-10 seconds with a `CancellationException msg=null` (no protocol-level reply).

### Why (evidence)

Phantom Mobile's `get_capabilities` response advertises only MWA 1.x sign-and-send support — it never declares sign_messages:

```
[MWASessionManager] getCapabilities | SUCCESS max_txs=10 max_msgs=1 versions=["legacy","0"] features=["supports_sign_and_send_transactions"]
```

Per the MWA spec, `features[]` is the authoritative list of implemented methods. Phantom simply doesn't have a handler for `sign_messages` on Android MWA. `max_msgs=1` is an advisory per-batch ceiling, not a support flag — it means nothing by itself. Calling the unimplemented method reliably fails with no user-visible explanation because the wallet doesn't know to send a protocol-level error; it just closes the socket or lets the internal timer expire.

### Workaround in this example app

Delete flow for Phantom now uses a throwaway memo-only `sign_transactions` call (see `MWAManager.DeleteAccount` → `ConfirmDeleteViaMemoTx`):

1. Fetch a fresh blockhash via `Web3.Rpc.GetLatestBlockHashAsync()`.
2. Build a memo-only transaction with ownership-proof wording: `"MWA Example App: wallet ownership proof, nonce=<16 alphanum>"`.
3. Ask the wallet to sign it via `Web3.Wallet.SignTransaction(tx)` (which Phantom DOES implement).
4. If a signed tx comes back → user approved → proceed with local cache clear.
5. **NEVER broadcast the signed tx.** The blockhash harmlessly expires. No lamports spent, no memo on chain. The signature alone is proof of ownership.

This same gate is ALSO used for Solflare (see the next section).

### Related Phantom-side issue: Blowfish transaction-warning modals

Even when `sign_transactions` works, Phantom's on-device transaction simulator (Blowfish) evaluates the originating dApp against a reputation/allowlist signal and stacks "this app may be malicious" warning screens ahead of the approve screen when the dApp is unverified. Signals that trigger this:
- `AppUri` uses a placeholder-looking domain (e.g. `https://example.com`).
- `AppName` contains "Example" / "Test" / "Demo".
- `Cluster` is `mainnet-beta` (stricter threshold than devnet).
- dApp is not on Phantom's verified dApp allowlist.

**Mitigation in this example:** `AppConfig.AppUri` points at a real GitHub repo, `AppName` is a dev-tool name. For **production dApps**, register the domain with Phantom's dApp verification program and serve a dApp manifest — Phantom will then skip the warning cascade.

---

## High: Solflare Mobile Does Not Implement `sign_messages` Over MWA

**Status:** Wallet-side gap — worked around in this example app by routing Solflare's **Delete Account** flow through the same memo-only `sign_transactions` gate as Phantom. The older workaround (re-auth via `LoginWalletAdapter`) is superseded.
**Severity:** Was High (delete appeared to "crash" on Solflare before).
**Affects:** Solflare Mobile (`com.solflare.mobile`).

### What actually happens

Call `sign_messages` on a Solflare MWA session. Solflare opens, shows an approve screen, and then closes the WebSocket about 7 seconds later without returning any protocol-level result. The MWA client surfaces this as `CancellationException msg=null` — the same "null-cause" pattern documented in past releases of this app as a Solflare crash.

### Why (evidence)

Solflare's `get_capabilities` response advertises `solana:signTransactions` but NOT `solana:signMessages`:

```
[MWASessionManager] getCapabilities | SUCCESS max_txs=20 max_msgs=20 versions=["legacy","0"] features=["solana:signTransactions"]
```

Like Phantom, Solflare never declares sign_messages support — it simply isn't implemented. `max_msgs=20` is advisory and meaningless for support. The wallet has no handler, so it closes the connection.

### Workaround in this example app

Same memo-only `sign_transactions` gate used for Phantom — see `ConfirmDeleteViaMemoTx` in `MWAManager.cs`. Signing works; nothing is broadcast. Historically the Solflare delete path used a re-authorization loop (`LoginWalletAdapter`), which also worked but was clunkier (second wallet picker flash). The memo-tx gate is the unified path now.

Note: Solflare does NOT run a Blowfish-style transaction simulator. You will not see "this app may be malicious" modals on Solflare for the same transactions Phantom warns about.

---

## High: Backpack Mobile `sign_and_send_transactions` Crashes

**Status:** Wallet-side bug — worked around in this example app by routing Backpack's **Sign & Send** button through MWA `sign_transactions` + a client-side Solana JSON-RPC `sendTransaction` call instead of MWA's native `sign_and_send_transactions`.
**Severity:** High (the Sign & Send button was unusable on Backpack).
**Affects:** Backpack Mobile (`app.backpack`). Same bug exists in the React Native, Cocos, and Godot example apps — Unity's SDK happens to have helpers for the fallback path.

### What actually happens

Call MWA's native `sign_and_send_transactions` with Backpack connected. About 19 seconds pass with no wallet UI response, then the WebSocket closes from Backpack's side. The MWA client library surfaces this as a `CancellationException`. Backpack's own internal logs (not visible to dApps) show:

```
kotlinx.serialization.json.internal.JsonDecodingException:
  Class discriminator was missing in SolanaMobileWalletAdapterWalletLibModule
```

Backpack's Kotlin plugin fails to deserialize the sign_and_send RPC request and silently crashes the handler.

### Workaround in this example app

`MWAManager.SignAndSendTransaction` (`Assets/Scripts/MWAManager.cs`) checks `ConnectedWalletType`. If it's `WALLET_BACKPACK`, the code routes through `SignAndBroadcastViaRpc`:

1. `Web3.Wallet.SignTransaction(tx)` — Backpack's `sign_transactions` handler IS correct; one wallet intent, one user approval.
2. `Convert.ToBase64String(signedTx.Serialize())` — serialize the signed tx.
3. `Web3.Rpc.SendTransactionAsync(base64, skipPreflight: false, Commitment.Confirmed)` — broadcast to the configured Solana RPC endpoint.
4. Return the base58 signature.

Same UX as the native path (one wallet approval), same result (tx lands on chain), works every time. For every wallet OTHER than Backpack, the native `sign_and_send_transactions` path is retained — no behavior change for Phantom, Solflare, Jupiter, or Seed Vault.

### Why not always use sign+RPC-broadcast?

Some wallets (e.g. Seed Vault) have a smoother UX for native sign_and_send because they can submit the transaction from inside the secure enclave. Keeping the native path for non-Backpack wallets preserves that UX.

---

## Connect defaults to plain `authorize` (NOT SIWS)

**Status:** By design for this Grant demo.

The Connect button calls `Web3.Instance.LoginWalletAdapter()` which triggers MWA 1.x `authorize`. SIWS (MWA 2.0 Sign-In-With-Solana, which bundles a signed in-message into the authorize response) is deliberately NOT the default here:

- Not every mobile wallet implements SIWS.
- SIWS adds a second wallet confirmation screen at connect time (the sign-in message), increasing connect-flow friction for a dev/demo app.
- The Grant scope didn't require the signed-in-message.

To opt into SIWS on a branch that exposes `siwsDomain`/`siwsStatement` in `SolanaMobileWalletAdapterOptions`, populate those fields before calling `Authorize`. Tracked in `AppConfig.SiwsDomain` / `AppConfig.SiwsStatement` for future use; currently unused.

---

## Medium: Wallet Type Not Reported by SDK After OS Picker

**Status:** SDK limitation — same as Godot SDK

When using the OS wallet picker (`AppConfig.UseOsPicker = true`), the Unity SDK does not report which wallet the user selected. The `walletTypeId` parameter to `Authorize()` is an INPUT that the app passes in, not an OUTPUT from the SDK.

**Impact:** When using OS picker, `ConnectedWalletType` defaults to -1 (Seed Vault/Other). Sign-in routing treats this as Seed Vault, which chains a SignMessage after connect. If the user actually selected Phantom or Solflare through the OS picker, they may see the wallet picker again for the sign step.

**Workaround:** Use the in-app wallet picker (`AppConfig.UseOsPicker = false`). The app tracks which wallet button the user tapped and stores the wallet type in AuthCache.

---

## Medium: Cache-Only Reconnect Requires Lazy Session Init

**Status:** By design — matches Godot reference app behavior

After `Reauthorize()` (cache-only reconnect), `Web3.Wallet` is null because no `LoginWalletAdapter()` call was made. The user sees the Home screen instantly with their cached pubkey, but the first wallet operation (sign, send) triggers `EnsureWalletSession()` which opens the wallet picker once to establish the SDK session.

**User experience:** Tap Reconnect → instant Home screen → first Sign/Send opens picker → subsequent operations work without picker.

---

## Low: Auth Token Not Used for Reauthorization

**Status:** SDK limitation

The MWA spec supports `reauthorize(auth_token)` for silent session restoration. The Unity SDK's `LoginWalletAdapter()` does not accept an auth token parameter. `Reauthorize()` uses cache-only reconnect instead, with lazy session establishment on first operation.

**Future:** If the Unity SDK adds auth token support, `Reauthorize()` could silently restore a full SDK session without any user interaction.

---

## Low: Solflare signMessage Broken on MWA — SUPERSEDED

**Status:** Superseded. The underlying cause is that Solflare doesn't implement `sign_messages` at all (see the **"Solflare Mobile Does Not Implement `sign_messages`"** section at the top of this file for the evidence-backed explanation). The older re-authorization workaround has been replaced with a memo-only `sign_transactions` gate that's shared with Phantom. Delete Account now works correctly on Solflare with a single approve screen and no double picker flash.

---

## Low: clearState() SDK Fix Needed for Disconnect→Connect Flow

**Status:** Pending — SDK-level fix documented in Godot reference app

The Unity SDK may have the same issue as the Godot SDK where `connectWallet()` has an early return check on cached `myResult`. After disconnect, calling connect again may silently return the cached connection without opening the OS wallet picker.

**Root cause:** In the Kotlin plugin's `connectWallet()`:
```kotlin
if (myResult is TransactionResult.Success) {
    return  // Never opens picker if cached
}
```

**Fix:** Add `myResult = null` to `clearState()` in the Kotlin plugin. This fix has been applied to the Godot SDK and needs to be ported to the Unity SDK's Android plugin.

**Reference:** https://github.com/mstevens843/godot-solana-mwa-example/blob/main/SDK_CLEARSTATE_FIX.md

---

## High: `InsufficientFundsForRent` on Sign & Send — Underfunded Fee-Payer (Especially via Seed Vault)

**Status:** Worked around at the SDK layer — we now do a pre-broadcast balance check and map the RPC-side rent error to a specific `LastErrorCode = "INSUFFICIENT_FUNDS_FOR_RENT"` so the UI tells the user to fund the account. Not a code bug; a funding problem.
**Severity:** Was Medium (user-confusing). The sign succeeded, the user saw the wallet approve, and then the transaction silently failed at RPC broadcast with a generic "Sign & send failed" toast.
**Affects:** Any fee-payer with a balance below `rent_exempt_min + tx_fee + priority_fee_buffer` (~0.001 SOL). Especially visible with **Seed Vault** (the Solana Seeker's default wallet) because its Solflare-built wrapper injects ComputeBudget priority-fee instructions before signing. Also hits **Phantom on Solana Seeker** because Phantom uses the same Seed Vault secure element there (identical keypair).

### What actually happens

Tap Sign & Send with a fee-payer that's just barely above Solana's rent-exempt minimum (890,880 lamports). The tx we build has ~5000 lamports of base fee; add Seed Vault's injected priority fees and the total drops the account below rent. Solana's RPC preflight rejects with:

```
code=-32002 "Transaction simulation failed: Transaction results in an account (0) with insufficient funds for rent"
err={"InsufficientFundsForRent":{"account_index":0}}
```

Signature inflation is the fingerprint — unsigned memo tx is ~203 bytes, Seed Vault returns a signed tx of ~255 bytes (+52 bytes = one `SetComputeUnitLimit` + one `SetComputeUnitPrice` instruction plus the extra account for the `ComputeBudget111…` program). This is undocumented in [seed-vault-sdk](https://github.com/solana-mobile/seed-vault-sdk); zero GitHub issues reference it.

### Why (evidence-backed)

- **Rent-exempt minimum for a zero-data System-owned account is 890,880 lamports** — ~0.00089 SOL. A tx that would drop the fee-payer below this fails preflight. See [Solana accounts docs](https://solana.com/docs/core/accounts), [QuickNode rent guide](https://www.quicknode.com/guides/solana-development/getting-started/understanding-rent-on-solana).
- **Seed Vault is a signing-only secure element behind a Solflare wrapper.** [Seed Vault Wallet blog post](https://blog.solanamobile.com/post/seed-vault-wallet----solana-seekers-native-mobile-wallet) confirms this explicitly. Its MWA `get_capabilities` reply only lists `solana:signTransactions`.
- **MWA 2.0 spec self-contradicts** on `solana:signAndSendTransaction` — listed as "mandatory feature" but carrying the note "implementation of this method by a wallet endpoint is optional." Seed Vault's capabilities reply is spec-compliant given that carve-out. See [MWA 2.0 spec](https://solana-mobile.github.io/mobile-wallet-adapter/spec/spec.html).
- **`skipPreflight: true` does NOT help** — Solana validators recheck rent at execution. The tx lands on-chain, fails at execution, and the fee is burned. Do not use this as a workaround.

### Fix

1. **Pre-broadcast balance check** — `MWAManager.SignAndSendTransaction` calls `Web3.Rpc.GetBalanceAsync(PublicKey)` before opening the wallet intent. If `lamports < 1_000_000` (covers 890_880 rent + ~5_000 fee + ~100_000 priority-fee buffer), short-circuit with `LastErrorCode = "INSUFFICIENT_FUNDS_FOR_RENT"` and no wallet intent opens. The user gets a clear toast immediately.
2. **RPC-side error detection** — if the tx actually reaches the RPC and fails with `InsufficientFundsForRent` (either structured err or plain-English message), `SignAndSendTransaction` and `SignAndBroadcastViaRpc` both map it to the same `LastErrorCode`. Handles the edge case where the balance was just above the threshold at pre-check time but the wallet's priority-fee injection pushed it over.
3. **UI toast** — `HomeUI.OnSignAndSend` branches on `MWAManager.LastErrorCode`. On `INSUFFICIENT_FUNDS_FOR_RENT` it shows "Fee-payer underfunded — send ≥0.001 SOL and retry".

### How to reproduce

1. Connect Seed Vault (on Seeker) or Phantom (using the Seeker's Seed Vault keypair) with a fee-payer balance near the rent-exempt minimum (e.g., fund it with exactly 0.0009 SOL). Tap Sign & Send. Expected: immediate "Fee-payer underfunded" toast, no wallet intent opens. Logcat: `STEP_PREFLIGHT_FAIL balance=890880 required=~1000000`.
2. Send 0.01 SOL to the fee-payer from another wallet. Retry Sign & Send. Balance check passes, sign completes, tx lands on-chain.
3. Regression check: Backpack / Solflare / Jupiter with funded accounts — Sign & Send works unchanged.

### Files

- `Assets/Scripts/MWAManager.cs` — added `LastErrorCode` public property, pre-flight balance check in `SignAndSendTransaction`, rent-error parse in both native RPC_FAIL branch and `SignAndBroadcastViaRpc` (Backpack path), `IsInsufficientRentError` helper.
- `Assets/Scripts/HomeUI.cs` — `OnSignAndSend` toast branch on `INSUFFICIENT_FUNDS_FOR_RENT`.

---

## Low: Jupiter Mobile `get_capabilities` Confirm Modal Renders Blank

**Status:** Wallet-side bug — no dApp-side fix. Documented so contributors don't waste cycles on it.
**Severity:** Cosmetic. The RPC response still makes it back to the app, so Get Capabilities returns the correct result. The modal UI just fails to render content.
**Affects:** Jupiter Mobile wallet (`ag.jup.app`).

### Symptom

Connect Jupiter → tap Get Capabilities → Jupiter opens its own bottom-sheet confirm modal. The modal frame renders but **the content never loads** — no message, no Approve button, no Reject button. Tapping outside dismisses it, and the dApp still receives a correct capabilities response. Nothing actionable on our side.

### Why (hypothesis)

Jupiter's public mobile adapter [TeamRaccoons/jup-mobile-adapter](https://github.com/TeamRaccoons/jup-mobile-adapter) is a WalletConnect/Reown wrapper, not a native MWA protocol wallet. That architectural mismatch likely explains the modal content failing to render. Per MWA spec, `get_capabilities` is a pure query that should not require user interaction at all, but Jupiter is showing a confirm modal anyway (and failing to populate it). Zero GitHub issues filed in `jup-ag/*` or `TeamRaccoons/*` mentioning this.

### Fix

None on our side. Workaround for users: tap outside the modal to dismiss it — the capabilities response still arrives.

---

## Pass 14: Feature Flag Parity with Cocos

**Status:** New. Three demo-app flags in `Assets/Scripts/AppConfig.cs` toggle SDK behaviours that used to be hardcoded. Defaults mirror the Cocos demo, which ships working on every wallet we tested on Seeker.
**Severity:** Feature. No regression to any existing flow — defaults keep the pre-Pass-14 behaviour except for (a) the `AuthCache` is now opt-out-able, and (b) SIWS is routable at runtime without editing code.

### Flags

| Flag | Default | Effect when ON | Effect when OFF |
|---|---|---|---|
| `UseMwaSignAndSend` | `true` | Native MWA `sign_and_send_transactions` (wallet broadcasts). **Backpack is always forced OFF** regardless — its native handler crashes with JsonDecodingException. | Sign via MWA, broadcast via app-side Solana JSON-RPC. Matches the Backpack workaround path, used by every wallet. |
| `UseSiws` | `false` | Configure `solanaWalletAdapterOptions.siwsDomain`/`siwsStatement` before Connect → SDK's `SolanaMobileWalletAdapter._Login` takes the SIWS path (authorize + sign_in_payload, in-session `sign_messages` fallback for wallets that don't return `sign_in_result` natively). | Plain MWA 1.x `authorize` — Connect on every wallet including Solflare, no SIWS crash risk. |
| `UseAuthCache` | `true` | PlayerPrefs-backed `AuthCache.cs` persists pubkey/authToken; cold-start auto-sign-in fires via `Reauthorize()`. | Skip all cache writes; `Reauthorize()` short-circuits to "auth cache disabled — use Connect". Kotlin-side in-memory session still works within one app lifetime. |

### Why these defaults

- **`UseMwaSignAndSend=true`** — native sign-and-send works on Jupiter / Phantom / Solflare / Seed Vault. Backpack's handler is the only broken one and the SDK already routes it to `SignAndBroadcastViaRpc`.
- **`UseSiws=false`** — keeps Connect working on every wallet, including Solflare and Phantom. Two wallets can't complete an SIWS Connect on Unity today:
  - **Phantom** — authorize succeeds, but Phantom closes the WebSocket when `sign_messages` is called as a second RPC inside the same scenario. The Unity SDK's `LocalAssociationScenario.StartAndExecute` propagates that as a failure, so the whole `_Login` throws and Connect fails. (Cocos's Java handler wraps the fallback in a bounded `.get()` + catch so it degrades gracefully to authorize-only — Unity would need the same pattern to match; that's a separate pass.)
  - **Solflare** — crashes on `authorize` with `sign_in_payload` (`Reply already submitted in onActivityResult` Flutter bug). Can't recover on our side.

  Flip to `true` to exercise SIWS with the wallets that DO work: **Backpack** (native `sign_in_result`, one prompt), **Seed Vault** (native, one prompt — confirmed on Seeker with `cofeelme.skr` account), **Jupiter** (fallback `sign_messages` inside the same scenario — two prompts, SIWS verified).
- **`UseAuthCache=true`** — matches Cocos's "session restored on cold start" behaviour. Flip to `false` to simulate cold-start-only UX.

### Wiring

- `Assets/Scripts/AppConfig.cs` — three new `public const bool` flags next to the existing `UseOsPicker`.
- `Assets/Scripts/MWAManager.cs` `Awake()` — gates `siwsDomain`/`siwsStatement` on the flag and logs all three flags.
- `Assets/Scripts/MWAManager.cs` `Authorize()` — only reads `LastSignInResult` and fires the "Signed in with Solana" toast when `UseSiws=true`.
- `Assets/Scripts/MWAManager.cs` `SignAndSendTransaction()` — the Backpack override runs first; after that, `!UseMwaSignAndSend` routes every other wallet through the same sign-then-RPC helper.
- `Assets/Scripts/MWAManager.cs` `Reauthorize()` — short-circuits when `UseAuthCache=false`.

### Cross-reference

Cocos's per-wallet matrix (`../cocos-solana-mwa/WALLET_COMPATIBILITY.md`) documents exact wallet behaviour for each API; Unity produces the same matrix with the defaults above.
