using System;
using UnityEngine;
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
    [SerializeField] public bool disableGameplayUntilMatchStart = true;
    [SerializeField] public bool debugLogs = true;
    [SerializeField] public string defaultRoomCode = "ROOM123";
    [SerializeField] public string defaultPlayerId = "player_1";
    [SerializeField] public string defaultPlayerName = "Player 1";

    [SerializeField] public GameObject mainMenuPanel;
    [SerializeField] public GameObject createJoinPanel;
    [SerializeField] public GameObject lobbyPanel;
    [SerializeField] public GameObject settingsPanel;
    [SerializeField] public GameObject gameplayHudRoot;
    [SerializeField] public GameObject demoStartOverlayRoot;

    [SerializeField] public MonoBehaviour gameManagerToPause;
    [SerializeField] public PlayerMovement localPlayerMovement;
    [SerializeField] public FirebaseMultiplayerClient firebaseClient;

    public AppFlowState CurrentState { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (firebaseClient == null)
        {
            firebaseClient = FirebaseMultiplayerClient.TryGetActiveClient();
        }

        if (localPlayerMovement == null)
        {
            PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
            if (players != null && players.Length > 0)
            {
                foreach (var p in players)
                {
                    if (p != null && p.IsLocalPlayer)
                    {
                        localPlayerMovement = p.GetComponent<PlayerMovement>();
                        break;
                    }
                }
            }
        }

        if (gameManagerToPause == null)
        {
            var gm = FindAnyObjectByType<GameManager>();
            if (gm != null) gameManagerToPause = gm as MonoBehaviour;
        }

        if (usePlaceholderUi)
        {
            EnsurePlaceholderPanels();
        }
    }

    void Start()
    {
        ShowMainMenu();
        if (disableGameplayUntilMatchStart)
        {
            SetGameplayActive(false);
        }
    }

    void EnsurePlaceholderPanels()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        Transform root = canvas.transform;

        if (mainMenuPanel == null) mainMenuPanel = CreatePanel(root, "MainMenuPanel", "THE INFECTED", ShowCreateJoinRoom, ShowSettings);
        if (createJoinPanel == null) createJoinPanel = CreatePanel(root, "CreateJoinPanel", "CREATE / JOIN ROOM", CreateRoom, JoinRoom, ShowMainMenu);
        if (lobbyPanel == null) lobbyPanel = CreatePanel(root, "LobbyPanel", "LOBBY", StartMatchAsHost, LeaveRoom);
        if (settingsPanel == null) settingsPanel = CreatePanel(root, "SettingsPanel", "SETTINGS", HideSettings);
    }

    GameObject CreatePanel(Transform parent, string name, string title, params Action[] buttonActions)
    {
        GameObject panel = GameObject.Find(name);
        if (panel != null) return panel;

        panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1080, 1920);
        Image img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.5f);

        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panel.transform, false);
        Text titleText = titleGO.AddComponent<Text>();
        titleText.text = title;
        titleText.alignment = TextAnchor.UpperCenter;
        titleText.fontSize = 48;

        for (int i = 0; i < buttonActions.Length; i++)
        {
            GameObject btnGO = new GameObject($"Button_{i}");
            btnGO.transform.SetParent(panel.transform, false);
            Button btn = btnGO.AddComponent<Button>();
            Image bimg = btnGO.AddComponent<Image>();
            bimg.color = new Color(1f, 1f, 1f, 0.9f);
            Text bt = new GameObject("Text").AddComponent<Text>();
            bt.transform.SetParent(btnGO.transform, false);
            bt.text = buttonActions[i].Method.Name;
            bt.alignment = TextAnchor.MiddleCenter;
            bt.fontSize = 28;
            int idx = i;
            btn.onClick.AddListener(() => { buttonActions[idx].Invoke(); });
        }

        panel.SetActive(false);
        return panel;
    }

    void SetState(AppFlowState state)
    {
        CurrentState = state;
        if (debugLogs) Debug.Log($"[FLOW] State -> {state}");
    }

    public void ShowMainMenu()
    {
        SetActivePanel(mainMenuPanel);
        SetState(AppFlowState.MainMenu);
    }

    public void ShowCreateJoinRoom()
    {
        SetActivePanel(createJoinPanel);
        SetState(AppFlowState.CreateJoinRoom);
    }

    public void CreateRoom()
    {
        if (firebaseClient != null)
        {
            firebaseClient.CreateRoom(defaultRoomCode);
        }
        if (debugLogs) Debug.Log($"[FLOW] Create room requested: {defaultRoomCode}");
        ShowLobby();
    }

    public void JoinRoom()
    {
        if (firebaseClient != null)
        {
            firebaseClient.JoinRoom(defaultRoomCode, defaultPlayerId);
        }
        if (debugLogs) Debug.Log($"[FLOW] Join room requested: {defaultRoomCode} {defaultPlayerId}");
        ShowLobby();
    }

    public void ShowLobby()
    {
        SetActivePanel(lobbyPanel);
        SetState(AppFlowState.Lobby);
    }

    public void ShowSettings()
    {
        SetActivePanel(settingsPanel);
        SetState(AppFlowState.Settings);
    }

    public void HideSettings()
    {
        ShowMainMenu();
    }

    public void LeaveRoom()
    {
        if (firebaseClient != null)
        {
            firebaseClient.LeaveRoom();
        }
        ShowMainMenu();
    }

    public void StartMatchAsHost()
    {
        if (debugLogs) Debug.Log("[FLOW] Match started.");
        StartInGame();
    }

    public void StartInGame()
    {
        SetGameplayActive(true);
        SetState(AppFlowState.InGame);
    }

    public void ShowResults(string result)
    {
        if (debugLogs) Debug.Log($"[FLOW] Results: {result}");
        SetState(AppFlowState.Results);
    }

    void SetActivePanel(GameObject active)
    {
        GameObject[] panels = { mainMenuPanel, createJoinPanel, lobbyPanel, settingsPanel };
        foreach (var p in panels)
        {
            if (p == null) continue;
            p.SetActive(p == active);
        }
    }

    void SetGameplayActive(bool active)
    {
        if (localPlayerMovement != null)
        {
            localPlayerMovement.enabled = active;
        }

        if (gameplayHudRoot != null)
        {
            gameplayHudRoot.SetActive(active);
        }

        if (demoStartOverlayRoot != null)
        {
            demoStartOverlayRoot.SetActive(active);
        }

        if (gameManagerToPause != null)
        {
            gameManagerToPause.enabled = active;
        }
    }
}
