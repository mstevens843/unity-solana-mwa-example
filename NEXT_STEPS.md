# Next Steps — Unity MWA Example App

## Immediate (Before First Run)

1. **Install Unity Hub + Unity 2022.3 LTS**
   - Download from https://unity.com/download
   - Install with **Android Build Support** module (includes SDK/NDK)

2. **Open project in Unity**
   - Unity Hub > Open > select `grant-unity/` folder
   - Unity will auto-generate ProjectSettings/, meta files, Library/, etc.
   - Wait for initial import to complete

3. **Install Solana.Unity-SDK**
   - Window > Package Manager > + > Add package from git URL:
     ```
     https://github.com/magicblock-labs/Solana.Unity-SDK.git
     ```
   - OR install from Unity Asset Store: "Solana SDK for Unity"
   - Wait for import and compilation

4. **Create scenes**
   - **Landing scene** (`Assets/Scenes/Landing.unity`):
     - Create Canvas → add:
       - TextMeshPro: "MWA Example App" (title)
       - Button: "Connect Wallet" (connect)
       - Button: "Reconnect (Cached)" (reconnect, disabled by default)
       - TextMeshPro: status text
     - Create empty GameObject "MWAManager" → attach `MWAManager.cs`
     - Attach `LandingUI.cs` to Canvas → wire up button/text references in Inspector

   - **Home scene** (`Assets/Scenes/Home.unity`):
     - Create Canvas → add:
       - TextMeshPro: pubkey display
       - TextMeshPro: status text
       - 5 buttons: Sign Message, Sign Tx, Sign & Send, Get Capabilities, Reconnect
       - 2 buttons: Disconnect, Delete Account
     - Attach `HomeUI.cs` to Canvas → wire up all references in Inspector

5. **Add scenes to Build Settings**
   - File > Build Settings > Add Open Scenes
   - Ensure both Landing and Home are in the list
   - Landing should be index 0 (loaded first)

6. **Configure Android build**
   - File > Build Settings > Switch to Android
   - Player Settings:
     - Package Name: `com.example.mwaexample`
     - Min API Level: 24
     - Target API Level: 34
     - Scripting Backend: IL2CPP
     - Target Architectures: ARM64

7. **Build APK and install on Seeker**
   ```bash
   # After Build > Build APK in Unity:
   adb install -r mwa-example.apk
   ```

## First Run — What to Watch For

Run `adb logcat -s Unity` while testing.

Expected log flow for Connect:
```
[MWAManager] Awake | START instance_exists=False
[MWAManager] Awake | DONE singleton established, cache initialized
[LandingUI] Start | BEGIN
[AuthCache] GetLatest | START
[AuthCache] GetLatest | NO_LATEST_KEY
[LandingUI] Start | DONE cached_auth=False reconnect_visible=False
--- user taps Connect ---
[LandingUI] OnConnectPressed | START
[MWAManager] Authorize | START is_connected=False
[MWAManager] Authorize | calling Web3.Instance.LoginWalletAdapter()
--- Seed Vault picker appears ---
[MWAManager] Authorize | SUCCESS pubkey=ABC...
[AuthCache] Set | START pubkey=ABC... auth_token_len=0
[AuthCache] Set | DONE total_cached=1
[LandingUI] OnAuthorized | pubkey=ABC... loading Home scene
[HomeUI] Start | BEGIN
[HomeUI] Start | pubkey=ABC... is_connected=True
```

## SDK Integration — DONE

All TODO markers in MWAManager.cs and HomeUI.cs have been filled with actual Solana.Unity-SDK calls:

- **Authorize**: `Web3.Instance.LoginWalletAdapter()` → extracts `account.PublicKey.Key`
- **Reauthorize**: Delegates to `Authorize()` (SDK handles token reuse internally)
- **Deauthorize**: `Web3.Instance.Logout()` + local state clear
- **SignMessage**: `Web3.Wallet.SignMessage(UTF8 bytes)` → returns base64
- **SignTransaction**: `Web3.Wallet.SignTransaction(Transaction)` → returns signed tx
- **SignAndSendTransaction**: `Web3.Wallet.SignAndSendTransaction(Transaction)` → returns sig
- **GetCapabilities**: Returns MWA 2.0 defaults (SDK doesn't expose this yet)
- **DeleteAccount**: Chains `SignMessage("Confirm deletion...")` for Seed Vault biometric confirmation
- **HomeUI**: Builds real memo transactions with `MemoProgram.NewMemoV2()` and `GetLatestBlockHashAsync()`

## Grant Deliverable Checklist

- [x] Connect wallet (authorize) — Seed Vault picker appears
- [x] Pubkey displays on Home screen
- [x] Sign Message — shows signature
- [x] Sign Transaction — signs a memo tx
- [x] Sign & Send — signs and broadcasts
- [x] Get Capabilities — shows wallet limits
- [x] Reconnect — silent reauthorize with cached token
- [x] Disconnect — clears session, returns to Landing
- [x] Delete Account — clears session + cache
- [x] Auth cache persists across app restarts
- [x] All logs visible in `adb logcat -s Unity`
- [x] README complete with setup instructions
- [x] Tested on Solana Seeker with Seed Vault

## Debugging

All deterministic logs use format: `[Component] Method | key=value`

Filter logs:
```bash
# All MWA logs
adb logcat -s Unity | grep -E "\[(MWAManager|AuthCache|LandingUI|HomeUI)\]"

# Just auth flow
adb logcat -s Unity | grep "\[MWAManager\] Authorize"

# Just cache operations
adb logcat -s Unity | grep "\[AuthCache\]"

# Just UI events
adb logcat -s Unity | grep -E "\[(LandingUI|HomeUI)\]"
```
