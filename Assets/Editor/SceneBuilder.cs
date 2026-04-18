using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

public class SceneBuilder
{
    [MenuItem("MWA/Build All Scenes")]
    public static void BuildAllScenes()
    {
        BuildLandingScene();
        BuildHomeScene();
        ConfigureBuildSettings();
        Debug.Log("[SceneBuilder] DONE — both scenes created and added to Build Settings");
    }

    [MenuItem("MWA/Build Landing Scene")]
    public static void BuildLandingScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Set camera background
        var cam = GameObject.Find("Main Camera");
        if (cam != null)
        {
            cam.GetComponent<Camera>().backgroundColor = new Color(0.05f, 0.05f, 0.12f);
            cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        }

        // MWAManager
        var mwaGo = new GameObject("MWAManager");
        mwaGo.AddComponent<MWAManager>();

        // Web3 — configure cluster so MWA sends the correct network to wallets
        var web3Go = new GameObject("Web3");
        var web3 = web3Go.AddComponent<Solana.Unity.SDK.Web3>();
        web3.rpcCluster = AppConfig.SdkCluster;
        web3.solanaWalletAdapterOptions ??= new Solana.Unity.SDK.SolanaWalletAdapterOptions();
        web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions ??= new Solana.Unity.SDK.SolanaMobileWalletAdapterOptions();
        // TEMP: SIWS options not present on feat/expose-mwa-auth-token branch
        // web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions.siwsDomain = AppConfig.SiwsDomain;
        // web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions.siwsStatement = AppConfig.SiwsStatement;

        // Canvas
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        canvasGo.AddComponent<GraphicRaycaster>();

        // EventSystem
        var eventGo = new GameObject("EventSystem");
        eventGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // Background panel
        var bgGo = CreateUIElement<Image>(canvasGo, "Background");
        bgGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.12f, 1f);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Container (vertical layout)
        var containerGo = new GameObject("Container");
        containerGo.transform.SetParent(canvasGo.transform, false);
        var containerRect = containerGo.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(800, 800);
        var vlg = containerGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 30;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Title
        var titleGo = CreateTMPText(containerGo, "TitleText", "MWA Example App", 48, TextAlignmentOptions.Center);
        titleGo.GetComponent<LayoutElement>().preferredHeight = 80;

        // Subtitle
        var subtitleGo = CreateTMPText(containerGo, "SubtitleText", "Solana Mobile Wallet Adapter Demo", 24, TextAlignmentOptions.Center);
        subtitleGo.GetComponent<LayoutElement>().preferredHeight = 40;
        subtitleGo.GetComponent<TextMeshProUGUI>().color = new Color(0.7f, 0.7f, 0.7f);

        // Connect Button
        var connectGo = CreateTMPButton(containerGo, "ConnectButton", "Connect Wallet", new Color(0.2f, 0.6f, 1f));
        connectGo.GetComponent<LayoutElement>().preferredHeight = 70;

        // Reconnect Button
        var reconnectGo = CreateTMPButton(containerGo, "ReconnectButton", "Reconnect", new Color(0.3f, 0.7f, 0.4f));
        reconnectGo.GetComponent<LayoutElement>().preferredHeight = 70;
        reconnectGo.SetActive(false);

        // Status Text
        var statusGo = CreateTMPText(containerGo, "StatusText", "Tap Connect to link your wallet", 22, TextAlignmentOptions.Center);
        statusGo.GetComponent<LayoutElement>().preferredHeight = 100;
        statusGo.GetComponent<TextMeshProUGUI>().enableWordWrapping = true;
        statusGo.GetComponent<TextMeshProUGUI>().color = new Color(0.8f, 0.8f, 0.8f);

        // Wallet Picker Buttons (hidden by default — shown when USE_OS_PICKER = false)
        var seedVaultGo = CreateTMPButton(containerGo, "SeedVaultButton", "Seed Vault", new Color(0.2f, 0.5f, 0.8f));
        seedVaultGo.GetComponent<LayoutElement>().preferredHeight = 60;
        seedVaultGo.SetActive(false);

        var phantomGo = CreateTMPButton(containerGo, "PhantomButton", "Phantom", new Color(0.4f, 0.3f, 0.9f));
        phantomGo.GetComponent<LayoutElement>().preferredHeight = 60;
        phantomGo.SetActive(false);

        var solflareGo = CreateTMPButton(containerGo, "SolflareButton", "Solflare", new Color(0.9f, 0.5f, 0.2f));
        solflareGo.GetComponent<LayoutElement>().preferredHeight = 60;
        solflareGo.SetActive(false);

        var jupiterGo = CreateTMPButton(containerGo, "JupiterButton", "Jupiter", new Color(0.2f, 0.8f, 0.6f));
        jupiterGo.GetComponent<LayoutElement>().preferredHeight = 60;
        jupiterGo.SetActive(false);

        var backpackGo = CreateTMPButton(containerGo, "BackpackButton", "Backpack", new Color(0.8f, 0.3f, 0.3f));
        backpackGo.GetComponent<LayoutElement>().preferredHeight = 60;
        backpackGo.SetActive(false);

        // Attach LandingUI to Canvas and wire references
        var landingUI = canvasGo.AddComponent<LandingUI>();
        landingUI.connectButton = connectGo.GetComponent<Button>();
        landingUI.reconnectButton = reconnectGo.GetComponent<Button>();
        landingUI.titleText = titleGo.GetComponent<TextMeshProUGUI>();
        landingUI.subtitleText = subtitleGo.GetComponent<TextMeshProUGUI>();
        landingUI.statusText = statusGo.GetComponent<TextMeshProUGUI>();
        landingUI.seedVaultButton = seedVaultGo.GetComponent<Button>();
        landingUI.phantomButton = phantomGo.GetComponent<Button>();
        landingUI.solflareButton = solflareGo.GetComponent<Button>();
        landingUI.jupiterButton = jupiterGo.GetComponent<Button>();
        landingUI.backpackButton = backpackGo.GetComponent<Button>();

        // Save
        string path = "Assets/Scenes/Landing.unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[SceneBuilder] Landing scene saved to {path}");
    }

    [MenuItem("MWA/Build Home Scene")]
    public static void BuildHomeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Set camera background
        var cam = GameObject.Find("Main Camera");
        if (cam != null)
        {
            cam.GetComponent<Camera>().backgroundColor = new Color(0.05f, 0.05f, 0.12f);
            cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        }

        // Canvas
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        canvasGo.AddComponent<GraphicRaycaster>();

        // EventSystem
        var eventGo = new GameObject("EventSystem");
        eventGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // Background panel
        var bgGo = CreateUIElement<Image>(canvasGo, "Background");
        bgGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.12f, 1f);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Scroll container
        var containerGo = new GameObject("Container");
        containerGo.transform.SetParent(canvasGo.transform, false);
        var containerRect = containerGo.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(800, 1200);
        var vlg = containerGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Header
        var headerGo = CreateTMPText(containerGo, "HeaderText", "MWA Example App", 36, TextAlignmentOptions.Center);
        headerGo.GetComponent<LayoutElement>().preferredHeight = 60;

        // Pubkey
        var pubkeyGo = CreateTMPText(containerGo, "PubkeyText", "Not connected", 22, TextAlignmentOptions.Center);
        pubkeyGo.GetComponent<LayoutElement>().preferredHeight = 40;
        pubkeyGo.GetComponent<TextMeshProUGUI>().color = new Color(0.5f, 0.8f, 1f);

        // Action buttons
        var signMsgGo = CreateTMPButton(containerGo, "SignMessageButton", "Sign Message", new Color(0.2f, 0.6f, 1f));
        signMsgGo.GetComponent<LayoutElement>().preferredHeight = 60;

        var signTxGo = CreateTMPButton(containerGo, "SignTxButton", "Sign Transaction", new Color(0.2f, 0.6f, 1f));
        signTxGo.GetComponent<LayoutElement>().preferredHeight = 60;

        var signSendGo = CreateTMPButton(containerGo, "SignSendButton", "Sign & Send", new Color(0.2f, 0.6f, 1f));
        signSendGo.GetComponent<LayoutElement>().preferredHeight = 60;

        var capsGo = CreateTMPButton(containerGo, "CapabilitiesButton", "Get Capabilities", new Color(0.4f, 0.5f, 0.7f));
        capsGo.GetComponent<LayoutElement>().preferredHeight = 60;

        var reconnGo = CreateTMPButton(containerGo, "ReconnectButton", "Reconnect", new Color(0.3f, 0.7f, 0.4f));
        reconnGo.GetComponent<LayoutElement>().preferredHeight = 60;

        // Status
        var statusGo = CreateTMPText(containerGo, "StatusText", "Connected — choose an action", 20, TextAlignmentOptions.Center);
        statusGo.GetComponent<LayoutElement>().preferredHeight = 80;
        statusGo.GetComponent<TextMeshProUGUI>().enableWordWrapping = true;
        statusGo.GetComponent<TextMeshProUGUI>().color = new Color(0.8f, 0.8f, 0.8f);

        // Disconnect / Delete buttons
        var disconnGo = CreateTMPButton(containerGo, "DisconnectButton", "Disconnect", new Color(0.8f, 0.4f, 0.2f));
        disconnGo.GetComponent<LayoutElement>().preferredHeight = 60;

        var deleteGo = CreateTMPButton(containerGo, "DeleteButton", "Delete Account", new Color(0.8f, 0.2f, 0.2f));
        deleteGo.GetComponent<LayoutElement>().preferredHeight = 60;

        // Attach HomeUI and wire references
        var homeUI = canvasGo.AddComponent<HomeUI>();
        homeUI.pubkeyText = pubkeyGo.GetComponent<TextMeshProUGUI>();
        homeUI.statusText = statusGo.GetComponent<TextMeshProUGUI>();
        homeUI.signMessageButton = signMsgGo.GetComponent<Button>();
        homeUI.signTxButton = signTxGo.GetComponent<Button>();
        homeUI.signSendButton = signSendGo.GetComponent<Button>();
        homeUI.capabilitiesButton = capsGo.GetComponent<Button>();
        homeUI.reconnectButton = reconnGo.GetComponent<Button>();
        homeUI.disconnectButton = disconnGo.GetComponent<Button>();
        homeUI.deleteButton = deleteGo.GetComponent<Button>();

        // Save
        string path = "Assets/Scenes/Home.unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[SceneBuilder] Home scene saved to {path}");
    }

    static void ConfigureBuildSettings()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Landing.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Home.unity", true)
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("[SceneBuilder] Build Settings configured: Landing=0, Home=1");
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    static GameObject CreateUIElement<T>(GameObject parent, string name) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<T>();
        return go;
    }

    static GameObject CreateTMPText(GameObject parent, string name, string text, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<LayoutElement>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        return go;
    }

    static GameObject CreateTMPButton(GameObject parent, string name, string label, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<LayoutElement>();
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // Button text
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go;
    }
}
