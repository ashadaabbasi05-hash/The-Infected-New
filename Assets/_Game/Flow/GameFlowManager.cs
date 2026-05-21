using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public enum AppFlowState
{
    Boot,
    MainMenu,
    Settings,
    CreateJoinRoom,
    Lobby,
    LoadingMatch,
    InGame,
    Meeting,
    Voting,
    Results
}

[DisallowMultipleComponent]
public sealed class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [SerializeField] public bool usePlaceholderUi = true;
    [SerializeField] public bool useTeamAEntryUi = true;
    [SerializeField] public bool showDevClientSwitcher = true;
    [SerializeField] public bool disableGameplayUntilMatchStart = true;
    [SerializeField] public bool debugLogs = true;
    [SerializeField] public string defaultRoomCode = "ROOM123";
    [SerializeField] public string defaultPlayerId = "player_1";
    [SerializeField] public string defaultPlayerName = "Player 1";

    [Header("Team A UI Prefabs")]
    [SerializeField] public MainMenuUI mainMenuPrefab;
    [SerializeField] public CreateJoinRoomUI createJoinRoomPrefab;

    [Header("Placeholder References")]
    [SerializeField] public GameObject mainMenuPanel;
    [SerializeField] public GameObject createJoinPanel;
    [SerializeField] public GameObject lobbyPanel;
    [SerializeField] public GameObject settingsPanel;
    [SerializeField] public GameObject gameplayHudRoot;
    [SerializeField] public GameObject demoStartOverlayRoot;

    [SerializeField] public MonoBehaviour gameManagerToPause;
    [SerializeField] public PlayerMovement localPlayerMovement;
    [SerializeField] public FirebaseMultiplayerClient firebaseClient;
    [SerializeField] public MultiplayerClientConfig clientConfig;

    [Header("Lobby UI")]
    [SerializeField] TMP_Text lobbyRoomCodeText;
    [SerializeField] TMP_Text lobbyStatusText;
    [SerializeField] TMP_Text lobbyPlayerListText;
    [SerializeField] Button readyButton;
    [SerializeField] Button startMatchButton;
    [SerializeField] Button leaveButton;
    [SerializeField] TMP_Text readyButtonLabel;
    [SerializeField] TMP_Text startMatchButtonLabel;
    [SerializeField] TMP_Text leaveButtonLabel;

    [Header("Dev Client Switcher")]
    [SerializeField] GameObject devClientSwitcherRoot;
    [SerializeField] TMP_Text devClientSwitcherStatusText;

    // Instance references for Team A UI
    MainMenuUI mainMenuInstance;
    CreateJoinRoomUI createJoinRoomInstance;

    // Dedicated GameFlowCanvas for placeholder fallback
    GameObject gameFlowCanvasObject;
    Canvas gameFlowCanvas;
    CanvasScaler gameFlowScaler;
    GraphicRaycaster gameFlowRaycaster;

    // Event wiring guards
    bool mainMenuEventsWired;
    bool createJoinEventsWired;

    public AppFlowState CurrentState { get; private set; }

    public bool IsMatchActive
    {
        get { return CurrentState == AppFlowState.InGame || CurrentState == AppFlowState.Meeting || CurrentState == AppFlowState.Voting; }
    }

    public static bool IsMatchActiveNow()
    {
        return Instance != null && Instance.IsMatchActive;
    }

    public static bool ShouldBlockGameplayStartup()
    {
        return Instance != null && !Instance.IsMatchActive;
    }

    bool localReady;
    bool firebaseEventsSubscribed;
    string activeRoomCode;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RefreshSceneReferences();
        ApplyClientConfigIfAvailable();

        if (showDevClientSwitcher)
        {
            EnsureDevClientSwitcherPanel();
        }

        if (usePlaceholderUi && !ShouldUseTeamAEntry())
        {
            EnsureGameFlowCanvas();
            EnsurePlaceholderPanels();
        }

        SubscribeFirebaseEvents();
    }

    void OnEnable()
    {
        RefreshSceneReferences();
        ApplyClientConfigIfAvailable();
        if (showDevClientSwitcher)
        {
            EnsureDevClientSwitcherPanel();
        }
        SubscribeFirebaseEvents();
    }

    void Start()
    {
        ShowMainMenu();

        if (disableGameplayUntilMatchStart)
        {
            SetGameplayActive(false);
        }

        StartCoroutine(HideGameplayUiNextFrame());
    }

    void OnDestroy()
    {
        UnsubscribeFirebaseEvents();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    System.Collections.IEnumerator HideGameplayUiNextFrame()
    {
        yield return null;
        HideGameplayUiForAppFlow();
        if (debugLogs) Debug.Log("[FLOW UI] Deferred HUD hide completed.");
    }

    // ========================================================================
    //  HELPERS
    // ========================================================================

    bool ShouldUseTeamAEntry()
    {
        return useTeamAEntryUi && mainMenuPrefab != null;
    }

    string GetOrCreateDisplayName()
    {
        if (!PlayerPrefs.HasKey("DisplayName"))
            PlayerPrefs.SetString("DisplayName", "Player" + UnityEngine.Random.Range(1000, 9999));
        return PlayerPrefs.GetString("DisplayName");
    }

    void RefreshSceneReferences()
    {
        if (firebaseClient == null)
        {
            firebaseClient = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (clientConfig == null)
        {
            clientConfig = FindAnyObjectByType<MultiplayerClientConfig>();
        }

        if (localPlayerMovement == null)
        {
            PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
            if (players != null)
            {
                foreach (PlayerIdentity player in players)
                {
                    if (player != null && player.IsLocalPlayer)
                    {
                        localPlayerMovement = player.GetComponent<PlayerMovement>();
                        break;
                    }
                }
            }
        }

        if (gameManagerToPause == null)
        {
            GameManager gameManager = FindAnyObjectByType<GameManager>();
            if (gameManager != null)
            {
                gameManagerToPause = gameManager;
            }
        }

        if (demoStartOverlayRoot == null)
        {
            DemoStartOverlayController overlay = FindAnyObjectByType<DemoStartOverlayController>(FindObjectsInactive.Include);
            if (overlay != null)
            {
                demoStartOverlayRoot = overlay.gameObject;
            }
        }
    }

    void ApplyClientConfigIfAvailable()
    {
        if (clientConfig == null)
        {
            return;
        }

        defaultPlayerId = string.IsNullOrWhiteSpace(clientConfig.LocalPlayerId) ? defaultPlayerId : clientConfig.LocalPlayerId;
        defaultPlayerName = string.IsNullOrWhiteSpace(clientConfig.DisplayName) ? defaultPlayerName : clientConfig.DisplayName;
        defaultRoomCode = string.IsNullOrWhiteSpace(clientConfig.RoomCode) ? defaultRoomCode : clientConfig.RoomCode.ToUpperInvariant();

        if (firebaseClient != null)
        {
            firebaseClient.SetLocalPlayerId(defaultPlayerId);
            firebaseClient.SetDisplayName(defaultPlayerName);
            firebaseClient.SetRoomCode(defaultRoomCode);
        }

        if (createJoinRoomInstance != null)
        {
            createJoinRoomInstance.SetPlayerName(defaultPlayerName);
            createJoinRoomInstance.SetRoomCode(defaultRoomCode);
        }

        RefreshDevClientSwitcherStatus();
    }

    void PushCurrentIdentityToFirebase()
    {
        if (firebaseClient == null)
        {
            firebaseClient = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (firebaseClient == null)
        {
            return;
        }

        firebaseClient.SetLocalPlayerId(defaultPlayerId);
        firebaseClient.SetDisplayName(defaultPlayerName);
        firebaseClient.SetRoomCode(defaultRoomCode);
        RefreshDevClientSwitcherStatus();
    }

    void EnsureDevClientSwitcherPanel()
    {
        if (!showDevClientSwitcher)
        {
            return;
        }

        if (devClientSwitcherRoot != null)
        {
            RefreshDevClientSwitcherStatus();
            return;
        }

        GameObject canvasObject = new GameObject("DevClientSwitcherCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("DevClientSwitcherPanel");
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-24f, -24f);
        panelRect.sizeDelta = new Vector2(360f, 260f);

        Image background = panel.AddComponent<Image>();
        background.color = new Color32(11, 22, 28, 235);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 16, 16);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text title = CreateText(panel.transform, "DEV CLIENT SWITCHER", 28, TextAlignmentOptions.Center, true, new Color32(242, 253, 255, 255));
        title.rectTransform.sizeDelta = new Vector2(320f, 50f);

        devClientSwitcherStatusText = CreateText(panel.transform, string.Empty, 18, TextAlignmentOptions.Center, false, new Color32(169, 216, 221, 255));
        devClientSwitcherStatusText.rectTransform.sizeDelta = new Vector2(320f, 72f);

        CreateButton(panel.transform, "PLAYER 1 HOST", new Color32(22, 65, 74, 255), UsePlayer1HostPreset);
        CreateButton(panel.transform, "PLAYER 2 CLIENT", new Color32(18, 50, 58, 255), UsePlayer2ClientPreset);
        CreateButton(panel.transform, "APPLY TO GAME", new Color32(255, 90, 95, 255), ApplyDevClientPreset);

        devClientSwitcherRoot = canvasObject;
        RefreshDevClientSwitcherStatus();
    }

    void RefreshDevClientSwitcherStatus()
    {
        if (devClientSwitcherStatusText == null)
        {
            return;
        }

        string configName = clientConfig != null ? clientConfig.LocalPlayerId : defaultPlayerId;
        string configDisplay = clientConfig != null ? clientConfig.DisplayName : defaultPlayerName;
        string configRoom = clientConfig != null ? clientConfig.RoomCode : defaultRoomCode;
        bool host = clientConfig != null ? clientConfig.IsHostClient : string.Equals(defaultPlayerId, "player_1", StringComparison.OrdinalIgnoreCase);

        devClientSwitcherStatusText.text =
            $"Current: {configName}\n" +
            $"Name: {configDisplay}\n" +
            $"Room: {configRoom}\n" +
            $"Mode: {(host ? "Host" : "Client")}";
    }

    void UsePlayer1HostPreset()
    {
        if (clientConfig != null)
        {
            clientConfig.UsePlayer1Host();
        }

        defaultPlayerId = "player_1";
        defaultPlayerName = "Player 1";
        defaultRoomCode = "ROOM123";

        ApplyClientConfigIfAvailable();
        PushCurrentIdentityToFirebase();
    }

    void UsePlayer2ClientPreset()
    {
        if (clientConfig != null)
        {
            clientConfig.UsePlayer2Client();
        }

        defaultPlayerId = "player_2";
        defaultPlayerName = "Player 2";
        defaultRoomCode = "ROOM123";

        ApplyClientConfigIfAvailable();
        PushCurrentIdentityToFirebase();
    }

    void ApplyDevClientPreset()
    {
        if (clientConfig != null)
        {
            clientConfig.ApplyToFirebaseAndFlow();
        }

        ApplyClientConfigIfAvailable();
        PushCurrentIdentityToFirebase();

        if (debugLogs)
        {
            Debug.Log($"[FLOW] Dev client preset applied: {defaultPlayerId} / {defaultPlayerName} / {defaultRoomCode}");
        }
    }

    void SubscribeFirebaseEvents()
    {
        if (firebaseEventsSubscribed || firebaseClient == null)
        {
            return;
        }

        firebaseClient.OnLobbyPlayersChanged += HandleLobbyPlayersChanged;
        firebaseClient.OnRoomJoined += HandleRoomJoined;
        firebaseClient.OnRoomLeft += HandleRoomLeft;
        firebaseClient.OnMatchStartedFromFirebase += HandleMatchStartedFromFirebase;
        firebaseEventsSubscribed = true;
    }

    void UnsubscribeFirebaseEvents()
    {
        if (!firebaseEventsSubscribed || firebaseClient == null)
        {
            firebaseEventsSubscribed = false;
            return;
        }

        firebaseClient.OnLobbyPlayersChanged -= HandleLobbyPlayersChanged;
        firebaseClient.OnRoomJoined -= HandleRoomJoined;
        firebaseClient.OnRoomLeft -= HandleRoomLeft;
        firebaseClient.OnMatchStartedFromFirebase -= HandleMatchStartedFromFirebase;
        firebaseEventsSubscribed = false;
    }

    // ========================================================================
    //  TEAM A UI INTEGRATION
    // ========================================================================

    void ConfigureTeamACanvas(GameObject root)
    {
        if (root == null) return;

        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        if (root.GetComponent<GraphicRaycaster>() == null)
            root.AddComponent<GraphicRaycaster>();

        root.transform.SetAsLastSibling();
        EnsureEventSystem();
    }

    void EnsureTeamAMainMenu()
    {
        if (mainMenuInstance != null)
        {
            mainMenuInstance.gameObject.SetActive(true);
            ConfigureTeamACanvas(mainMenuInstance.gameObject);
            return;
        }

        if (mainMenuPrefab == null)
        {
            if (debugLogs) Debug.LogWarning("[FLOW UI] mainMenuPrefab not assigned, cannot create Team A MainMenu.");
            return;
        }

        mainMenuInstance = Instantiate(mainMenuPrefab, transform);
        mainMenuInstance.gameObject.name = "TeamA_MainMenuCanvas";
        ConfigureTeamACanvas(mainMenuInstance.gameObject);

        if (!mainMenuEventsWired)
        {
            mainMenuInstance.OnStartClicked += ShowCreateJoinRoom;
            mainMenuInstance.OnSettingsClicked += ShowSettings;
            mainMenuInstance.OnQuitClicked += QuitGame;
            mainMenuEventsWired = true;
        }

        if (debugLogs) Debug.Log("[FLOW UI] Team A Main Menu instantiated.");
    }

    void EnsureTeamACreateJoin()
    {
        if (createJoinRoomInstance != null)
        {
            createJoinRoomInstance.gameObject.SetActive(true);
            ConfigureTeamACanvas(createJoinRoomInstance.gameObject);
            return;
        }

        if (createJoinRoomPrefab == null)
        {
            if (debugLogs) Debug.LogWarning("[FLOW UI] createJoinRoomPrefab not assigned, cannot create Team A CreateJoin.");
            return;
        }

        createJoinRoomInstance = Instantiate(createJoinRoomPrefab, transform);
        createJoinRoomInstance.gameObject.name = "TeamA_CreateJoinRoomCanvas";
        ConfigureTeamACanvas(createJoinRoomInstance.gameObject);

        if (!createJoinEventsWired)
        {
            createJoinRoomInstance.OnCreateRoomClicked += HandleTeamACreateRoom;
            createJoinRoomInstance.OnJoinRoomClicked += HandleTeamAJoinRoom;
            createJoinRoomInstance.OnBackClicked += ShowMainMenu;
            createJoinEventsWired = true;
        }

        if (debugLogs) Debug.Log("[FLOW UI] Team A Create/Join instantiated.");
    }

    void HideTeamAEntryScreens()
    {
        if (mainMenuInstance != null) mainMenuInstance.gameObject.SetActive(false);
        if (createJoinRoomInstance != null) createJoinRoomInstance.gameObject.SetActive(false);
    }

    void HandleTeamACreateRoom(string playerName)
    {
        defaultPlayerName = playerName;
        PushCurrentIdentityToFirebase();
        if (createJoinRoomInstance != null) createJoinRoomInstance.SetLoading(true);
        if (createJoinRoomInstance != null) createJoinRoomInstance.SetError("");
        ShowLobby();
        CreateRoom();
    }

    void HandleTeamAJoinRoom(string playerName, string roomCode)
    {
        defaultPlayerName = playerName;
        defaultRoomCode = string.IsNullOrWhiteSpace(roomCode) ? "ROOM123" : roomCode.Trim().ToUpperInvariant();
        PushCurrentIdentityToFirebase();
        if (createJoinRoomInstance != null) createJoinRoomInstance.SetLoading(true);
        if (createJoinRoomInstance != null) createJoinRoomInstance.SetError("");
        ShowLobby();
        JoinRoom();
    }

    public void QuitGame()
    {
        Debug.Log("[FLOW] Quit requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ========================================================================
    //  GAME FLOW CANVAS (placeholder fallback)
    // ========================================================================

    void EnsureGameFlowCanvas()
    {
        gameFlowCanvasObject = GameObject.Find("GameFlowCanvas");
        if (gameFlowCanvasObject != null)
        {
            gameFlowCanvas = gameFlowCanvasObject.GetComponent<Canvas>();
            gameFlowScaler = gameFlowCanvasObject.GetComponent<CanvasScaler>();
            gameFlowRaycaster = gameFlowCanvasObject.GetComponent<GraphicRaycaster>();
        }
        else
        {
            gameFlowCanvasObject = new GameObject("GameFlowCanvas");
            gameFlowCanvas = gameFlowCanvasObject.AddComponent<Canvas>();
            gameFlowCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameFlowScaler = gameFlowCanvasObject.AddComponent<CanvasScaler>();
            gameFlowRaycaster = gameFlowCanvasObject.AddComponent<GraphicRaycaster>();
        }

        gameFlowCanvas.sortingOrder = 100;
        gameFlowScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        gameFlowScaler.referenceResolution = new Vector2(1080f, 1920f);
        gameFlowScaler.matchWidthOrHeight = 0.5f;
        gameFlowCanvasObject.transform.SetAsLastSibling();
        EnsureEventSystem();

        if (debugLogs) Debug.Log("[FLOW UI] GameFlowCanvas configured sortingOrder=100");
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    void ConfigureMenuPanelCanvasGroup(GameObject panel, bool active)
    {
        if (panel == null) return;
        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = active ? 1f : 0f;
        cg.interactable = active;
        cg.blocksRaycasts = active;
    }

    // ========================================================================
    //  PLACEHOLDER PANELS (fallback)
    // ========================================================================

    void EnsurePlaceholderPanels()
    {
        Transform root = gameFlowCanvasObject.transform;
        if (mainMenuPanel == null) mainMenuPanel = CreateMainMenuPanel(root);
        if (createJoinPanel == null) createJoinPanel = CreateCreateJoinPanel(root);
        if (lobbyPanel == null) lobbyPanel = CreateLobbyPanel(root);
        if (settingsPanel == null) settingsPanel = CreateSettingsPanel(root);
        foreach (GameObject panel in new[] { mainMenuPanel, createJoinPanel, lobbyPanel, settingsPanel })
        {
            if (panel != null) ConfigureMenuPanelCanvasGroup(panel, false);
        }
    }

    GameObject CreateMainMenuPanel(Transform parent)
    {
        GameObject panel = CreateScreenPanel(parent, "MainMenuPanel");
        TMP_Text title = CreateText(panel.transform, "THE INFECTED", 72, TextAlignmentOptions.Center, true, new Color32(242, 253, 255, 255));
        title.rectTransform.anchoredPosition = new Vector2(0f, 420f);
        TMP_Text subtitle = CreateText(panel.transform, "TRUST NO ONE", 36, TextAlignmentOptions.Center, false, new Color32(169, 216, 221, 255));
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 360f);
        CreateButton(panel.transform, "START", new Color32(22, 65, 74, 255), () => ShowCreateJoinRoom());
        CreateButton(panel.transform, "SETTINGS", new Color32(18, 50, 58, 255), () => ShowSettings());
        return panel;
    }

    GameObject CreateCreateJoinPanel(Transform parent)
    {
        GameObject panel = CreateScreenPanel(parent, "CreateJoinPanel");
        CreateText(panel.transform, "CREATE / JOIN ROOM", 52, TextAlignmentOptions.Center, true, new Color32(242, 253, 255, 255)).rectTransform.anchoredPosition = new Vector2(0f, 430f);
        CreateText(panel.transform, $"Prototype room code: {defaultRoomCode}", 30, TextAlignmentOptions.Center, false, new Color32(169, 216, 221, 255)).rectTransform.anchoredPosition = new Vector2(0f, 330f);
        CreateButton(panel.transform, "CREATE ROOM", new Color32(22, 65, 74, 255), () => CreateRoom());
        CreateButton(panel.transform, "JOIN ROOM", new Color32(18, 50, 58, 255), () => JoinRoom());
        CreateButton(panel.transform, "BACK", new Color32(13, 27, 34, 255), () => ShowMainMenu());
        return panel;
    }

    GameObject CreateLobbyPanel(Transform parent)
    {
        GameObject panel = CreateScreenPanel(parent, "LobbyPanel");
        CreateText(panel.transform, "LOBBY", 56, TextAlignmentOptions.Center, true, new Color32(242, 253, 255, 255)).rectTransform.anchoredPosition = new Vector2(0f, 430f);
        TMP_Text roomCodeText = CreateText(panel.transform, $"ROOM: {defaultRoomCode}", 34, TextAlignmentOptions.Center, false, new Color32(169, 216, 221, 255));
        roomCodeText.rectTransform.anchoredPosition = new Vector2(0f, 330f);
        lobbyRoomCodeText = roomCodeText;
        TMP_Text statusText = CreateText(panel.transform, "WAITING FOR SURVIVORS", 32, TextAlignmentOptions.Center, false, new Color32(242, 253, 255, 255));
        statusText.rectTransform.anchoredPosition = new Vector2(0f, 260f);
        lobbyStatusText = statusText;
        TMP_Text playerListText = CreateText(panel.transform, "No players yet", 30, TextAlignmentOptions.TopLeft, false, new Color32(169, 216, 221, 255));
        playerListText.rectTransform.sizeDelta = new Vector2(860f, 420f);
        playerListText.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        playerListText.textWrappingMode = TextWrappingModes.Normal;
        lobbyPlayerListText = playerListText;
        readyButton = CreateButton(panel.transform, "READY", new Color32(22, 65, 74, 255), ToggleReady);
        readyButtonLabel = readyButton.GetComponentInChildren<TMP_Text>(true);
        startMatchButton = CreateButton(panel.transform, "START MATCH", new Color32(255, 90, 95, 255), StartMatchAsHost);
        startMatchButtonLabel = startMatchButton.GetComponentInChildren<TMP_Text>(true);
        leaveButton = CreateButton(panel.transform, "LEAVE", new Color32(13, 27, 34, 255), LeaveRoom);
        leaveButtonLabel = leaveButton.GetComponentInChildren<TMP_Text>(true);
        UpdateReadyButtonLabel();
        UpdateLobbyButtons();
        return panel;
    }

    GameObject CreateSettingsPanel(Transform parent)
    {
        GameObject panel = CreateScreenPanel(parent, "SettingsPanel");
        CreateText(panel.transform, "SETTINGS", 56, TextAlignmentOptions.Center, true, new Color32(242, 253, 255, 255)).rectTransform.anchoredPosition = new Vector2(0f, 430f);
        CreateText(panel.transform, "Settings placeholder", 30, TextAlignmentOptions.Center, false, new Color32(169, 216, 221, 255)).rectTransform.anchoredPosition = new Vector2(0f, 320f);
        CreateButton(panel.transform, "BACK", new Color32(13, 27, 34, 255), () => HideSettings());
        return panel;
    }

    GameObject CreateScreenPanel(Transform parent, string name)
    {
        GameObject panel = GameObject.Find(name);
        if (panel != null) return panel;
        panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image background = panel.AddComponent<Image>();
        background.color = new Color32(5, 7, 10, 245);
        GameObject card = new GameObject("Card");
        card.transform.SetParent(panel.transform, false);
        RectTransform cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.08f, 0.1f);
        cardRect.anchorMax = new Vector2(0.92f, 0.9f);
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        Image cardImage = card.AddComponent<Image>();
        cardImage.color = new Color32(13, 27, 34, 245);
        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(54, 54, 60, 54);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ContentSizeFitter fitter = card.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return card;
    }

    TMP_Text CreateText(Transform parent, string text, int fontSize, TextAlignmentOptions alignment, bool upper, Color color)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(860f, 80f);
        TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = upper ? text.ToUpperInvariant() : text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = Mathf.Max(72f, fontSize + 24f);
        layout.minHeight = 60f;
        return tmp;
    }

    Button CreateButton(Transform parent, string label, Color color, Action action)
    {
        GameObject go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(760f, 120f);
        Image image = go.AddComponent<Image>();
        image.color = color;
        Button button = go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Brighten(color, 0.12f);
        colors.pressedColor = Darken(color, 0.12f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, 0.75f);
        button.colors = colors;
        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TMP_Text tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label.ToUpperInvariant();
        tmp.fontSize = 40;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color32(242, 253, 255, 255);
        tmp.raycastTarget = false;
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 120f;
        layout.minHeight = 110f;
        button.onClick.AddListener(() => action?.Invoke());
        return button;
    }

    Color Brighten(Color color, float amount)
    {
        return new Color(Mathf.Clamp01(color.r + amount), Mathf.Clamp01(color.g + amount), Mathf.Clamp01(color.b + amount), color.a);
    }

    Color Darken(Color color, float amount)
    {
        return new Color(Mathf.Clamp01(color.r - amount), Mathf.Clamp01(color.g - amount), Mathf.Clamp01(color.b - amount), color.a);
    }

    // ========================================================================
    //  HIDE / RESTORE GAMEPLAY HUD
    // ========================================================================

    void HideGameplayUiForAppFlow()
    {
        string[] hudNames = new string[]
        {
            "HUDSystem",
            "PhaseText",
            "WaveText",
            "TimerText",
            "MobileButtonsPanel",
            "Fixed Joystick",
            "Joystick",
            "DemoStartOverlay",
            "DemoToolsPanel",
            "gasOverlayImage",
            "GasOverlay",
            "MeetingPanel",
            "ChatPanel",
            "VotingPanel",
            "PublicAntidoteStatusPanel",
            "PersonalAntidotePanel",
            "FinalHuntPanel",
            "GameOverPanel",
            "WinPanel"
        };

        foreach (string hudName in hudNames)
        {
            GameObject obj = GameObject.Find(hudName);
            if (obj != null && obj.activeSelf)
            {
                obj.SetActive(false);
                if (debugLogs) Debug.Log($"[FLOW UI] Hidden HUD: {hudName}");
            }
        }

        if (gameplayHudRoot != null && gameplayHudRoot.activeSelf)
        {
            gameplayHudRoot.SetActive(false);
            if (debugLogs) Debug.Log("[FLOW UI] Hidden gameplayHudRoot");
        }

        if (demoStartOverlayRoot != null && demoStartOverlayRoot.activeSelf)
        {
            demoStartOverlayRoot.SetActive(false);
        }

        if (debugLogs) Debug.Log("[FLOW UI] Gameplay HUD hidden for app flow.");
    }

    void RestoreGameplayHudForMatch()
    {
        string[] hudNames = new string[]
        {
            "HUDSystem",
            "PhaseText",
            "WaveText",
            "TimerText",
            "MobileButtonsPanel",
            "Fixed Joystick",
            "Joystick",
            "DemoStartOverlay",
            "MeetingPanel",
            "ChatPanel",
            "VotingPanel",
            "PublicAntidoteStatusPanel",
            "PersonalAntidotePanel",
            "FinalHuntPanel",
            "GameOverPanel",
            "WinPanel"
        };

        foreach (string hudName in hudNames)
        {
            GameObject obj = GameObject.Find(hudName);
            if (obj != null && !obj.activeSelf)
            {
                obj.SetActive(true);
            }
        }

        if (gameplayHudRoot != null && !gameplayHudRoot.activeSelf)
        {
            gameplayHudRoot.SetActive(true);
        }

        if (debugLogs) Debug.Log("[FLOW UI] Gameplay HUD restored for match.");
    }

    // ========================================================================
    //  STATE MANAGEMENT
    // ========================================================================

    void SetState(AppFlowState state)
    {
        CurrentState = state;
        if (debugLogs) Debug.Log($"[FLOW] State -> {state}");
    }

    public void ShowMainMenu()
    {
        SetGameplayActive(false);

        // Hide Team A entry screens
        if (createJoinRoomInstance != null) createJoinRoomInstance.gameObject.SetActive(false);
        if (mainMenuInstance != null) mainMenuInstance.gameObject.SetActive(false);

        // Hide placeholder flow panels
        if (gameFlowCanvasObject != null) SetActivePanel(null);

        // Hide lobby
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        HideGameplayUiForAppFlow();

        if (ShouldUseTeamAEntry())
        {
            EnsureTeamAMainMenu();
            if (mainMenuInstance != null)
            {
                mainMenuInstance.gameObject.SetActive(true);
                ConfigureTeamACanvas(mainMenuInstance.gameObject);
            }
            SetState(AppFlowState.MainMenu);
            if (debugLogs) Debug.Log("[FLOW UI] Team A Main Menu shown.");
        }
        else
        {
            if (gameFlowCanvasObject == null)
            {
                EnsureGameFlowCanvas();
                EnsurePlaceholderPanels();
            }
            gameFlowCanvasObject.SetActive(true);
            gameFlowCanvasObject.transform.SetAsLastSibling();
            gameFlowCanvas.sortingOrder = 100;
            SetActivePanel(mainMenuPanel);
            SetState(AppFlowState.MainMenu);
            if (debugLogs) Debug.Log("[FLOW UI] MainMenu visible alpha=1 interactable=True");
        }
    }

    public void ShowCreateJoinRoom()
    {
        SetGameplayActive(false);

        // Hide Team A entry screens
        if (mainMenuInstance != null) mainMenuInstance.gameObject.SetActive(false);
        if (createJoinRoomInstance != null) createJoinRoomInstance.gameObject.SetActive(false);

        // Hide placeholder flow panels
        if (gameFlowCanvasObject != null) SetActivePanel(null);

        // Hide lobby
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        HideGameplayUiForAppFlow();

        if (ShouldUseTeamAEntry())
        {
            EnsureTeamACreateJoin();
            if (createJoinRoomInstance != null)
            {
                createJoinRoomInstance.ClearInputs();
                createJoinRoomInstance.SetPlayerName(string.IsNullOrWhiteSpace(defaultPlayerName) ? GetOrCreateDisplayName() : defaultPlayerName);
                createJoinRoomInstance.SetRoomCode(defaultRoomCode);
                createJoinRoomInstance.gameObject.SetActive(true);
                ConfigureTeamACanvas(createJoinRoomInstance.gameObject);
            }
            SetState(AppFlowState.CreateJoinRoom);
            if (debugLogs) Debug.Log("[FLOW UI] Team A Create/Join shown.");
        }
        else
        {
            if (gameFlowCanvasObject != null)
            {
                gameFlowCanvasObject.SetActive(true);
                gameFlowCanvasObject.transform.SetAsLastSibling();
            }
            SetActivePanel(createJoinPanel);
            SetState(AppFlowState.CreateJoinRoom);
        }
    }

    public void CreateRoom()
    {
        RefreshSceneReferences();
        SubscribeFirebaseEvents();
        PushCurrentIdentityToFirebase();
        localReady = false;
        UpdateReadyButtonLabel();
        ShowLobby("Creating room...");

        if (firebaseClient != null)
        {
            firebaseClient.CreateRoom(defaultRoomCode);
        }

        if (debugLogs) Debug.Log($"[FLOW] Create room requested: {defaultRoomCode}");
    }

    public void JoinRoom()
    {
        RefreshSceneReferences();
        SubscribeFirebaseEvents();
        PushCurrentIdentityToFirebase();
        localReady = false;
        UpdateReadyButtonLabel();
        ShowLobby("Joining room...");

        if (firebaseClient != null)
        {
            firebaseClient.JoinRoom(defaultRoomCode, defaultPlayerId);
        }

        if (debugLogs) Debug.Log($"[FLOW] Join room requested: {defaultRoomCode} {defaultPlayerId}");
    }

    public void ShowLobby()
    {
        ShowLobby("WAITING FOR SURVIVORS");
    }

    void ShowLobby(string status)
    {
        SetGameplayActive(false);

        // Hide Team A entry screens
        if (mainMenuInstance != null) mainMenuInstance.gameObject.SetActive(false);
        if (createJoinRoomInstance != null) createJoinRoomInstance.gameObject.SetActive(false);

        HideGameplayUiForAppFlow();

        // Ensure placeholder lobby canvas
        if (gameFlowCanvasObject == null)
        {
            EnsureGameFlowCanvas();
            EnsurePlaceholderPanels();
        }
        gameFlowCanvasObject.SetActive(true);
        gameFlowCanvasObject.transform.SetAsLastSibling();
        SetActivePanel(lobbyPanel);

        SetState(AppFlowState.Lobby);
        activeRoomCode = string.IsNullOrWhiteSpace(activeRoomCode) ? defaultRoomCode : activeRoomCode;
        SetLobbyRoomCode(activeRoomCode);
        SetLobbyStatus(status);
        UpdateReadyButtonLabel();
        UpdateLobbyButtons();
    }

    public void ShowSettings()
    {
        SetGameplayActive(false);

        // Hide Team A entry screens
        if (mainMenuInstance != null) mainMenuInstance.gameObject.SetActive(false);
        if (createJoinRoomInstance != null) createJoinRoomInstance.gameObject.SetActive(false);

        if (gameFlowCanvasObject != null) SetActivePanel(null);

        if (ShouldUseTeamAEntry() && mainMenuPrefab != null)
        {
            // Settings placeholder falls through to GameFlowCanvas
        }

        if (gameFlowCanvasObject != null)
        {
            gameFlowCanvasObject.SetActive(true);
            gameFlowCanvasObject.transform.SetAsLastSibling();
        }
        SetActivePanel(settingsPanel);
        SetState(AppFlowState.Settings);
        HideGameplayUiForAppFlow();
    }

    public void HideSettings()
    {
        ShowMainMenu();
    }

    public void LeaveRoom()
    {
        localReady = false;
        UpdateReadyButtonLabel();
        if (firebaseClient != null) firebaseClient.LeaveRoom();
        ShowMainMenu();
    }

    public void ToggleReady()
    {
        localReady = !localReady;
        if (firebaseClient != null) firebaseClient.SetLocalReady(localReady);
        UpdateReadyButtonLabel();
    }

    public void StartMatchAsHost()
    {
        bool firebaseEnabled = firebaseClient != null && firebaseClient.FirebaseEnabled;
        bool firebaseReady = firebaseClient != null && firebaseClient.IsFirebaseReady;
        bool isJoined = firebaseClient != null && firebaseClient.IsJoined;
        bool isHost = firebaseClient != null && firebaseClient.IsHost;
        string room = firebaseClient != null ? firebaseClient.CurrentRoomCode : defaultRoomCode;
        string localPlayerId = firebaseClient != null ? firebaseClient.LocalPlayerId : defaultPlayerId;

        if (debugLogs)
        {
            Debug.Log($"[FLOW] Host start match requested. isJoined={isJoined} isHost={isHost} firebaseReady={firebaseReady} room={room} localPlayerId={localPlayerId}");
        }

        if (!firebaseEnabled)
        {
            StartInGame();
            return;
        }

        if (firebaseReady && isJoined)
        {
            if (!isHost)
            {
                Debug.LogWarning("[FLOW] StartMatch blocked because local client is not host.");
                return;
            }

            firebaseClient.StartMatch();
            return;
        }

        if (firebaseEnabled && !isJoined)
        {
            Debug.LogWarning("[FLOW] StartMatch blocked because local client is not joined yet.");
            return;
        }

        StartInGame();
    }

    public void StartInGame()
    {
        SetState(AppFlowState.InGame);
        if (debugLogs) Debug.Log("[FLOW] Match active = true");

        // Hide Team A entry screens
        HideTeamAEntryScreens();

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(false);
        }

        // Hide flow canvas
        if (gameFlowCanvasObject != null)
        {
            SetActivePanel(null);
            gameFlowCanvasObject.SetActive(false);
        }

        SetGameplayActive(true);
        RestoreGameplayHudForMatch();

        if (debugLogs) Debug.Log("[FLOW UI] Entry UI hidden for match.");
    }

    public void ShowResults(string result)
    {
        if (debugLogs) Debug.Log($"[FLOW] Results: {result}");
        SetState(AppFlowState.Results);
    }

    void HandleRoomJoined(string roomCode)
    {
        activeRoomCode = string.IsNullOrWhiteSpace(roomCode) ? defaultRoomCode : roomCode;
        SetLobbyRoomCode(activeRoomCode);
        SetLobbyStatus("WAITING FOR SURVIVORS");

        // Clear loading state on CreateJoinRoom if it exists
        if (createJoinRoomInstance != null)
        {
            createJoinRoomInstance.SetLoading(false);
            createJoinRoomInstance.SetError("");
        }

        if (debugLogs) Debug.Log($"[FLOW] Room joined: {activeRoomCode}");
    }

    void HandleRoomLeft(string roomCode)
    {
        if (debugLogs && !string.IsNullOrWhiteSpace(roomCode))
            Debug.Log($"[FLOW] Room left: {roomCode}");
        activeRoomCode = string.Empty;
        UpdateLobbyButtons();
    }

    void HandleLobbyPlayersChanged(List<LobbyPlayerData> players)
    {
        UpdateLobbyPlayersDisplay(players);
        UpdateLobbyButtons();
        if (debugLogs)
        {
            int count = players != null ? players.Count : 0;
            Debug.Log($"[FLOW] Lobby player list updated: {count}");
        }
    }

    void HandleMatchStartedFromFirebase(string phase)
    {
        if (debugLogs) Debug.Log($"[FLOW] Match start received from Firebase. phase={phase}");
        StartInGame();
    }

    void SetLobbyRoomCode(string roomCode)
    {
        if (lobbyRoomCodeText != null)
            lobbyRoomCodeText.text = $"ROOM: {roomCode}";
    }

    void SetLobbyStatus(string status)
    {
        if (lobbyStatusText != null)
            lobbyStatusText.text = status;
    }

    void UpdateLobbyPlayersDisplay(List<LobbyPlayerData> players)
    {
        if (lobbyPlayerListText == null) return;
        if (players == null)
        {
            lobbyPlayerListText.text = localReady ? "READY" : "WAITING";
            UpdateLobbyStatusFromPlayers(0);
            return;
        }

        List<string> lines = new List<string>();
        for (int index = 0; index < players.Count; index++)
        {
            LobbyPlayerData player = players[index];
            if (player == null) continue;
            string name = string.IsNullOrWhiteSpace(player.displayName) ? player.playerId : player.displayName;
            string role = player.isHost ? "HOST " : string.Empty;
            string readyState = player.isReady ? "READY" : "WAITING";
            string botState = player.isBot ? " BOT" : string.Empty;
            lines.Add($"{role}{name}{botState} - {readyState}");
        }

        lobbyPlayerListText.text = lines.Count > 0 ? string.Join("\n", lines) : "No players yet";
        UpdateLobbyStatusFromPlayers(players.Count);
    }

    void UpdateLobbyStatusFromPlayers(int playerCount)
    {
        if (lobbyStatusText != null)
            lobbyStatusText.text = $"WAITING FOR SURVIVORS ({playerCount}/4)";
    }

    void UpdateReadyButtonLabel()
    {
        if (readyButtonLabel != null)
            readyButtonLabel.text = localReady ? "NOT READY" : "READY";
    }

    void UpdateLobbyButtons()
    {
        bool firebaseReady = firebaseClient != null && firebaseClient.IsFirebaseReady;
        bool localIsHost = firebaseClient == null || !firebaseReady || firebaseClient.IsHost;
        bool canHostStart = firebaseClient == null || !firebaseReady || (firebaseClient.IsJoined && firebaseClient.IsHost);

        if (startMatchButton != null)
        {
            startMatchButton.interactable = canHostStart;
            startMatchButton.gameObject.SetActive(localIsHost || !firebaseReady);
        }

        if (startMatchButtonLabel != null)
            startMatchButtonLabel.color = canHostStart ? new Color32(255, 245, 245, 255) : new Color32(185, 185, 190, 255);

        if (!localIsHost && firebaseReady)
        {
            SetLobbyStatus("WAITING FOR HOST TO START");
        }

        if (debugLogs)
        {
            Debug.Log($"[FLOW] Lobby host status localIsHost={localIsHost} startButtonInteractable={canHostStart}");
        }
    }

    void SetActivePanel(GameObject active)
    {
        GameObject[] panels = { mainMenuPanel, createJoinPanel, lobbyPanel, settingsPanel };
        foreach (GameObject panel in panels)
        {
            if (panel == null) continue;
            bool isActive = (panel == active);
            panel.SetActive(isActive);
            ConfigureMenuPanelCanvasGroup(panel, isActive);
        }
    }

    void SetGameplayActive(bool active)
    {
        if (localPlayerMovement != null) localPlayerMovement.enabled = active;
        if (gameplayHudRoot != null) gameplayHudRoot.SetActive(active);
        if (demoStartOverlayRoot != null) demoStartOverlayRoot.SetActive(active);
        if (gameManagerToPause != null) gameManagerToPause.enabled = active;
        if (!active) HideGameplayUiForAppFlow();
    }
}