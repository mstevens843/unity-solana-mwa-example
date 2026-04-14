# Known Issues — Unity Solana SDK MWA Integration

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

## Low: Solflare signMessage Broken on MWA

**Status:** Wallet bug — affects all MWA implementations

Solflare's MWA implementation does not correctly handle `signMessage` requests. The delete account flow routes Solflare through re-authorization (`LoginWalletAdapter()`) instead of sign-message for confirmation.

**Affected flows:** Delete Account (Solflare only)
**Unaffected:** All other wallets, all other flows

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
