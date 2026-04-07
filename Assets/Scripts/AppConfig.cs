using UnityEngine;

/// <summary>
/// App identity sent to wallets during MWA authorization.
/// Attach to a GameObject or use as a ScriptableObject.
/// </summary>
public static class AppConfig
{
    public const string AppName = "MWA Example App";
    public const string AppUri = "https://example.com";
    public const string AppIconPath = "/icon.png";
    public const string Cluster = "devnet"; // "devnet", "testnet", or "mainnet-beta"
}
