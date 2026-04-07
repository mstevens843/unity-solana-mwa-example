# MWA Example App — Unity

Minimal Unity Android app demonstrating all Solana Mobile Wallet Adapter (MWA) 2.0 API methods with Seed Vault integration on Solana Seeker.

Tested and verified on Solana Seeker hardware with Seed Vault.

## Features

- **Authorize** — Connect wallet via MWA with auto sign-in (biometric confirmation)
- **Reauthorize** — Silent reconnect with cached auth token
- **Deauthorize** — Disconnect wallet session
- **Sign Message** — Sign arbitrary text with Seed Vault biometric
- **Sign Transaction** — Sign a serialized memo transaction
- **Sign & Send Transaction** — Sign and broadcast to Solana devnet
- **Get Capabilities** — Query wallet limits and supported versions
- **Auth Cache** — PlayerPrefs-based token persistence across sessions
- **Delete Account** — Biometric-confirmed deauthorize + clear all cached data

## Auth Flow

```
Connect button → Seed Vault wallet picker → user approves
    → auto sign-in message → biometric (double-tap + fingerprint)
    → signature returned → Home screen
```

This matches the Godot reference implementation. The biometric step IS the signing — the private key never leaves Seed Vault.

## Prerequisites

- Unity 2022.3 LTS or newer
- Android Build Support module (includes SDK/NDK)
- Solana Seeker or Android device with a MWA-compatible wallet (Seed Vault, Phantom, Solflare)

## Setup

1. **Open project in Unity**
   - Unity Hub > Open > select this folder
   - Wait for initial import to complete

2. **Install Solana.Unity-SDK** (if not already resolved)
   - Window > Package Manager > + > Add package from git URL:
     ```
     https://github.com/magicblock-labs/Solana.Unity-SDK.git
     ```

3. **Generate scenes**
   - Menu: `MWA > Build All Scenes`
   - This creates both `Landing` and `Home` scenes with all UI wired up

4. **Configure Android build**
   - Menu: `MWA > Configure Android Build`
   - This sets package name, API levels, IL2CPP, ARM64

5. **Build and install**
   ```bash
   # Build APK in Unity: File > Build Settings > Build
   adb install -r mwa-example-unity.apk
   ```

## Project Structure

```
Assets/
├── Scripts/
│   ├── MWAManager.cs          # MWA singleton — authorize, sign, cache, delete
│   ├── AuthCache.cs           # IMwaAuthCache interface + PlayerPrefs implementation
│   ├── LandingUI.cs           # Landing screen — Connect / Reconnect buttons
│   ├── HomeUI.cs              # Home screen — 7 action buttons
│   ├── AppConfig.cs           # App identity (name, URI, cluster)
│   └── StatusDisplay.cs       # Reusable status text component
├── Editor/
│   ├── SceneBuilder.cs        # Auto-generates Landing + Home scenes (MWA menu)
│   ├── AndroidConfigurator.cs # Auto-configures Android build settings (MWA menu)
│   └── ManifestPostProcessor.cs # Auto-patches AndroidManifest during build
├── Scenes/
│   ├── Landing.unity          # First screen — connect wallet
│   └── Home.unity             # Main screen — wallet actions
└── Plugins/
    └── Android/
        ├── AndroidManifest.xml       # <queries> for solana-wallet scheme
        ├── LauncherManifest.xml      # Launcher manifest with tools:replace
        ├── mainTemplate.gradle       # Gradle build config
        ├── gradleTemplate.properties # Gradle properties
        └── settingsTemplate.gradle   # Gradle settings
```

## Architecture

```
C# Scripts (MWAManager.cs)
    ↓ calls
Solana.Unity-SDK (SolanaMobileWalletAdapter)
    ↓ pure C# MWA protocol
WebSocket + ECDH + AES-GCM (BouncyCastle)
    ↓ Android Intent
Wallet App (Seed Vault / Phantom / Solflare)
```

## Debugging

All logs use deterministic format: `[Component] Method | key=value`

```bash
# All MWA logs
adb logcat -s Unity | grep -E "\[(MWAManager|AuthCache|LandingUI|HomeUI)\]"

# Auth flow only
adb logcat -s Unity | grep "\[MWAManager\] Authorize"

# Cache operations
adb logcat -s Unity | grep "\[AuthCache\]"

# UI events
adb logcat -s Unity | grep -E "\[(LandingUI|HomeUI)\]"
```

## License

MIT — see [LICENSE](LICENSE)
