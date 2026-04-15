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
