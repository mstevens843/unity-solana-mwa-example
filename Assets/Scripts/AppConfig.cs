using UnityEngine;
using Solana.Unity.SDK;

/// <summary>
/// App identity sent to wallets during MWA authorization.
/// </summary>
public static class AppConfig
{
    public const string AppName = "MWA Example App";
    public const string AppUri = "https://github.com/mstevens843/unity-solana-mwa-example";
    public const string AppIconPath = "/icon.png";
    public const string Cluster = "mainnet-beta"; // "devnet", "testnet", or "mainnet-beta"
    public const bool UseOsPicker = true; // true = OS wallet picker, false = in-app wallet buttons (stores wallet type)

    // SIWS (Sign In With Solana) — Auth 2.0
    // Domain must match the domain portion of the identity URI used by the wallet adapter
    public const string SiwsDomain = "solana.unity-sdk.gg";
    public const string SiwsStatement = "Sign in to MWA Example App";

    // ─── Feature flags (Pass 14 — match Cocos defaults) ──────────────────────

    /// <summary>
    /// If true, use MWA native signAndSendTransactions (wallet signs AND broadcasts).
    /// If false, use sign-only via MWA, then app broadcasts via RPC.
    /// Backpack ALWAYS uses the sign+RPC path regardless of this flag — its
    /// native handler crashes with Kotlin JsonDecodingException.
    /// </summary>
    public const bool UseMwaSignAndSend = true;

    /// <summary>
    /// If true, use SIWS (Sign In With Solana) for connect + prove ownership in one prompt.
    /// If false, use standard Connect (MWA 1.x authorize). Solflare crashes on SIWS
    /// authorize ("Reply already submitted" Flutter bug), so leaving this off keeps
    /// Connect working on every wallet including Solflare.
    /// </summary>
    public const bool UseSiws = false;

    /// <summary>
    /// If true, persist auth tokens via AuthCache so cold-start auto-sign-in works.
    /// If false, rely on in-memory session only (lost on app kill).
    /// </summary>
    public const bool UseAuthCache = true;

    /// <summary>
    /// Maps the Cluster string to the SDK's RpcCluster enum.
    /// Used by SceneBuilder to configure the Web3 component.
    /// </summary>
    public static RpcCluster SdkCluster => Cluster switch
    {
        "mainnet-beta" => RpcCluster.MainNet,
        "devnet" => RpcCluster.DevNet,
        "testnet" => RpcCluster.TestNet,
        _ => RpcCluster.MainNet
    };
}
