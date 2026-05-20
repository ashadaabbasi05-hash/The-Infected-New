using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public sealed class DemoToolsController : MonoBehaviour
{
    public static DemoToolsController Instance { get; private set; }

    [Header("Demo Tools")]
    [SerializeField] bool enableDemoTools = true;
    [SerializeField] bool showPanelOnStart = false;
    [SerializeField] bool enableKeyboardShortcuts = true;
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool forceGasWaveInfectionForDemo = false;
    [SerializeField] bool showGasEffectsForDemo = true;
    [SerializeField] float demoGasVisualDuration = 3f;

    [Header("UI")]
    [SerializeField] GameObject demoPanelRoot;
    [SerializeField] CanvasGroup demoCanvasGroup;
    [SerializeField] Button resetMatchButton;
    [SerializeField] Button infectPlayer2Button;
    [SerializeField] Button forceGasWaveButton;
    [SerializeField] Button forceMeetingButton;
    [SerializeField] Button completeTasksButton;
    [SerializeField] Button finalHuntDemoButton;
    [SerializeField] Button hideDemoPanelButton;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DEMO] Duplicate DemoToolsController disabled.", this);
            enabled = false;
            return;
        }
        Instance = this;

        if (!enableDemoTools)
        {
            Log("Demo tools disabled.");
            return;
        }

        AutoFindReferences();
        WireButtons();

        if (showPanelOnStart)
            ShowDemoPanel();
        else
            HideDemoPanel();

        Log("DemoToolsController ready.");
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!enableDemoTools || !enableKeyboardShortcuts)
            return;

        if (Input.GetKeyDown(KeyCode.F1))
            ToggleDemoPanel();
        if (Input.GetKeyDown(KeyCode.F2))
            ResetLocalMatch();
        if (Input.GetKeyDown(KeyCode.F3))
            DemoInfectPlayer2();
        if (Input.GetKeyDown(KeyCode.F4))
            DemoForceGasWave();
        if (Input.GetKeyDown(KeyCode.F5))
            DemoForceMeetingOrVoting();
        if (Input.GetKeyDown(KeyCode.F6))
            DemoCompleteAllTasks();
        if (Input.GetKeyDown(KeyCode.F7))
            DemoStartFinalHunt();
    }

    void AutoFindReferences()
    {
        if (demoPanelRoot == null)
            demoPanelRoot = GameObject.Find("DemoToolsPanel");

        if (demoPanelRoot != null && demoCanvasGroup == null)
            demoCanvasGroup = demoPanelRoot.GetComponent<CanvasGroup>();

        if (demoCanvasGroup == null && demoPanelRoot != null)
            demoCanvasGroup = demoPanelRoot.AddComponent<CanvasGroup>();

        if (resetMatchButton == null)
            resetMatchButton = FindButton("DemoResetButton");

        if (infectPlayer2Button == null)
            infectPlayer2Button = FindButton("DemoInfectPlayer2Button");

        if (forceGasWaveButton == null)
            forceGasWaveButton = FindButton("DemoGasWaveButton");

        if (forceMeetingButton == null)
            forceMeetingButton = FindButton("DemoMeetingButton");

        if (completeTasksButton == null)
            completeTasksButton = FindButton("DemoCompleteTasksButton");

        if (finalHuntDemoButton == null)
            finalHuntDemoButton = FindButton("DemoFinalHuntButton");

        if (hideDemoPanelButton == null)
            hideDemoPanelButton = FindButton("DemoHideButton");
    }

    Button FindButton(string objectName)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null)
            return null;

        return go.GetComponent<Button>();
    }

    void WireButtons()
    {
        WireButton(resetMatchButton, ResetLocalMatch);
        WireButton(infectPlayer2Button, DemoInfectPlayer2);
        WireButton(forceGasWaveButton, DemoForceGasWave);
        WireButton(forceMeetingButton, DemoForceMeetingOrVoting);
        WireButton(completeTasksButton, DemoCompleteAllTasks);
        WireButton(finalHuntDemoButton, DemoStartFinalHunt);
        WireButton(hideDemoPanelButton, HideDemoPanel);
    }

    void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    public void ToggleDemoPanel()
    {
        if (demoPanelRoot == null)
            return;

        if (demoPanelRoot.activeSelf)
            HideDemoPanel();
        else
            ShowDemoPanel();
    }

    public void ShowDemoPanel()
    {
        if (demoPanelRoot == null)
            return;

        demoPanelRoot.SetActive(true);

        if (demoCanvasGroup != null)
        {
            demoCanvasGroup.alpha = 1f;
            demoCanvasGroup.interactable = true;
            demoCanvasGroup.blocksRaycasts = true;
        }

        Log("Demo panel shown.");
    }

    public void HideDemoPanel()
    {
        if (demoCanvasGroup != null)
        {
            demoCanvasGroup.alpha = 0f;
            demoCanvasGroup.interactable = false;
            demoCanvasGroup.blocksRaycasts = false;
        }

        if (demoPanelRoot != null)
            demoPanelRoot.SetActive(false);

        Log("Demo panel hidden.");
    }

    public void ResetLocalMatch()
    {
        if (!enableDemoTools)
        {
            Debug.LogWarning("[DEMO] Reset local match skipped because demo tools are disabled.", this);
            return;
        }

        Log("Reset local match requested.");

        HideKnownPanels();
        ResetPlayersSafely();

        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        if (finalHuntManager != null)
        {
            InvokeOptional(finalHuntManager, "ResetFinalHuntForDemo", null);
        }

        MeetingController meetingController = FindAnyObjectByType<MeetingController>(FindObjectsInactive.Include);
        if (meetingController != null)
        {
            InvokeOptional(meetingController, "ResetMeetingForDemo", null);
        }

        ObjectiveHUDController objectiveHudController = FindAnyObjectByType<ObjectiveHUDController>(FindObjectsInactive.Include);
        if (objectiveHudController != null)
        {
            if (!InvokeOptional(objectiveHudController, "ResetHudForDemo", null))
            {
                InvokeOptional(objectiveHudController, "ForceHideFinalHuntHud", null);
                InvokeOptional(objectiveHudController, "RefreshObjectiveText", null);
            }
        }

        GasWaveEffectsController gasWaveEffectsController = FindAnyObjectByType<GasWaveEffectsController>(FindObjectsInactive.Include);
        if (gasWaveEffectsController != null)
        {
            InvokeOptional(gasWaveEffectsController, "ForceStopGasEffects", null);
        }

        EscapeDoor escapeDoor = FindAnyObjectByType<EscapeDoor>(FindObjectsInactive.Include);
        if (escapeDoor != null)
        {
            InvokeOptional(escapeDoor, "ResetDoorForDemo", null);
        }

        GameEndManager gameEndManager = FindAnyObjectByType<GameEndManager>(FindObjectsInactive.Include);
        if (gameEndManager != null)
        {
            InvokeOptional(gameEndManager, "ResetGameEndForDemo", null);
        }

        HideNamedObject("FinalHuntPanel");
        HideNamedObject("FinalHuntText");
        HideNamedObject("BottomRightFinalHuntText");
        HideNamedObject("GasOverlay");
        HideNamedObject("GasOverlayImage");
        HideNamedObject("gasOverlayImage");

        bool tasksReset = ResetTasksSafely();

        GameManager gameManager = GameManager.Instance != null ? GameManager.Instance : FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager != null)
        {
            gameManager.enabled = true;
            InvokeOptional(gameManager, "EnterExploration", null);
            InvokeOptional(gameManager, "ResetGameForDebug", null);
        }

        Trace("DEMO", tasksReset ? "UI state reset complete." : "UI state reset complete.");
    }

    public void DemoInfectPlayer2()
    {
        if (!enableDemoTools)
            return;

        Log("Demo infect Player 2.");

        PlayerIdentity player2 = FindPlayerById(2);
        if (player2 != null && player2.isInfected)
        {
            Log("Player 2 is already infected; skipping demo infect.");
            return;
        }

        InfectionSystem infectionSystem = FindAnyObjectByType<InfectionSystem>();
        if (infectionSystem != null)
        {
            bool invoked = InvokeOptional(infectionSystem, "TryInfectPlayerById", new object[] { 2, 1, "DEMO_FORCE_PLAYER_2" });
            if (!invoked)
                invoked = InvokeOptional(infectionSystem, "DebugInfectPlayer2", null);

            if (invoked)
            {
                Trace("DEMO", "Player 2 infected for demo.");
                return;
            }
        }

        if (player2 != null)
        {
            if (InvokeOptional(player2, "Infect", null))
            {
                Trace("DEMO", "Player 2 infected for demo by fallback.");
            }
            else
            {
                Debug.LogWarning("[DEMO] Could not infect Player 2 using fallback.", this);
            }
        }
        else
        {
            Debug.LogWarning("[DEMO] Could not find Player 2.", this);
        }
    }

    public void DemoForceGasWave()
    {
        if (!enableDemoTools)
            return;

        Log("Force gas wave requested.");

        if (!forceGasWaveInfectionForDemo)
        {
            bool invoked =
                InvokeOptionalOnAny("GameManager", "EnterGasWave") ||
                InvokeOptionalOnAny("GameManager", "EnterGasWavePhase") ||
                InvokeOptionalOnAny("GameManager", "StartGasWave");

            if (!invoked)
            {
                Debug.LogWarning("[DEMO] No safe gas wave method found.", this);
            }

            Trace("DEMO", "Gas wave demo used normal phase only.");
            return;
        }

        if (showGasEffectsForDemo)
        {
            GasWaveEffectsController gasWaveEffectsController = FindAnyObjectByType<GasWaveEffectsController>(FindObjectsInactive.Include);
            if (gasWaveEffectsController != null)
            {
                gasWaveEffectsController.PlayDemoGasWaveEffects(demoGasVisualDuration);
                Debug.Log("[DEMO] Gas wave visual effects shown.", this);
            }
            else
            {
                Debug.LogWarning("[DEMO] Gas wave visual effects requested but no GasWaveEffectsController was found.", this);
            }
        }

        if (TryForceSafeGasWaveInfection(out int infectedPlayerId))
        {
            Trace("DEMO", "Gas wave demo forced safe infection: Player " + infectedPlayerId + ".");
            return;
        }

        Debug.LogWarning("[DEMO] Gas wave demo could not find a safe non-local infection target.", this);
    }

    public void DemoForceMeetingOrVoting()
    {
        if (!enableDemoTools)
            return;

        Log("Force meeting/voting requested.");

        MeetingController meetingController = FindAnyObjectByType<MeetingController>(FindObjectsInactive.Include);
        bool invoked = false;

        if (meetingController != null)
        {
            invoked = InvokeOptional(meetingController, "StartMeeting", null);
            if (invoked)
            {
                Trace("DEMO", "Meeting flow forced through MeetingController.");
                return;
            }
        }

        GameManager gameManager = GameManager.Instance != null ? GameManager.Instance : FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager != null)
        {
            invoked = InvokeOptional(gameManager, "EnterMeeting", null);
        }

        if (!invoked)
        {
            Debug.LogWarning("[DEMO] No safe meeting/voting method found.", this);
        }
    }

    public void DemoCompleteAllTasks()
    {
        if (!enableDemoTools)
            return;

        Log("Complete all tasks requested.");

        bool invoked =
            InvokeOptionalOnAny("TaskManager", "ForceCompleteAllTasks") ||
            InvokeOptionalOnAny("TaskManager", "CompleteAllTasksForDebug") ||
            InvokeOptionalOnAny("TaskManager", "DebugCompleteAllTasks");

        if (!invoked)
        {
            TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Exclude);
            foreach (TaskInteractable task in tasks)
            {
                if (task == null)
                    continue;

                InvokeOptional(task, "CompleteTaskForDebug", null);
            }
        }

        Trace("DEMO", "All tasks completed for demo.");
    }

    public void DemoStartFinalHunt()
    {
        if (!enableDemoTools)
            return;

        Log("Start Final Hunt demo requested.");

        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>();
        bool invoked = false;

        if (finalHuntManager != null)
        {
            invoked =
                InvokeOptional(finalHuntManager, "StartFinalHuntDemo", null) ||
                InvokeOptional(finalHuntManager, "CreateFinalHuntDebugState", null) ||
                InvokeOptional(finalHuntManager, "ForceStartFinalHuntDebug", null);
        }

        if (!invoked)
        {
            Debug.LogWarning("[DEMO] No safe Final Hunt demo method found.", this);
        }

        Trace("DEMO", "Final Hunt demo started.");
    }

    void HideKnownPanels()
    {
        string[] panelNames = new string[]
        {
            "MeetingPanel",
            "VotingUI",
            "WinPanel",
            "GameOverPanel",
            "FinalHuntPanel",
            "FinalHuntText",
            "BottomRightFinalHuntText",
            "PersonalAntidotePanel",
            "PublicAntidoteStatusPanel",
            "WireTaskPanel",
            "ChatPanel",
            "GasOverlay",
            "GasOverlayImage",
            "gasOverlayImage"
        };

        for (int i = 0; i < panelNames.Length; i++)
        {
            string panelName = panelNames[i];
            GameObject panel = GameObject.Find(panelName);
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }
    }

    void HideNamedObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return;

        GameObject target = GameObject.Find(objectName);
        if (target != null)
        {
            target.SetActive(false);
        }
    }

    void ResetPlayersSafely()
    {
        PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();

        foreach (PlayerIdentity player in players)
        {
            if (player == null)
                continue;

            bool shouldBeLocal = player.playerId == 1;
            SetPlayerStateForDemo(player, shouldBeLocal);
        }
    }

    bool ResetTasksSafely()
    {
        TaskManager taskManager = FindTaskManager();
        if (taskManager != null)
        {
            if (TryGetBoolMember(taskManager, "allowDebugResetTasks", out bool allowDebugResetTasks) && !allowDebugResetTasks)
            {
                Debug.LogWarning("[DEMO] Task reset is disabled in TaskManager (allowDebugResetTasks=false).", this);
                return false;
            }

            if (InvokeOptional(taskManager, "ResetAllTasksForDebug", null))
            {
                return true;
            }

            Debug.LogWarning("[DEMO] TaskManager reset method was unavailable. Resetting tasks directly.", this);
        }
        else
        {
            Debug.LogWarning("[DEMO] No TaskManager found. Resetting tasks directly.", this);
        }

        TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Include);
        bool resetAnyTask = false;

        if (tasks != null)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskInteractable task = tasks[i];
                if (task == null)
                    continue;

                task.ResetTaskForDebug();
                resetAnyTask = true;
            }
        }

        if (taskManager != null)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo completedTaskIdsField = typeof(TaskManager).GetField("completedTaskIds", flags);
            if (completedTaskIdsField != null)
            {
                object completedTaskIds = completedTaskIdsField.GetValue(taskManager);
                if (completedTaskIds != null)
                {
                    MethodInfo clearMethod = completedTaskIds.GetType().GetMethod("Clear", flags);
                    if (clearMethod != null)
                    {
                        clearMethod.Invoke(completedTaskIds, null);
                        resetAnyTask = true;
                    }
                }
            }

            TrySetBoolMember(taskManager, "hasRaisedAllTasksCompleted", false);
            TrySetBoolMember(taskManager, "hasTracedExitScanUnlocked", false);
            TrySetBoolMember(taskManager, "hasWarnedNoTasks", false);
            TrySetBoolMember(taskManager, "hasWarnedExpectedV4TaskCount", false);
            TrySetBoolMember(taskManager, "hasCompletedV4TaskCountCheck", false);

            InvokeOptional(taskManager, "RecalculateProgress", null);
        }

        return resetAnyTask;
    }

    TaskManager FindTaskManager()
    {
        return TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
    }

    void SetPlayerStateForDemo(PlayerIdentity player, bool isLocal)
    {
        if (player == null)
            return;

        TrySetBoolMember(player, "isAlive", true);
        TrySetBoolMember(player, "isFrozen", false);
        TrySetBoolMember(player, "isInfected", false);
        TrySetBoolMember(player, "isAIControlled", false);
        TrySetBoolMember(player, "isLocalPlayer", isLocal);

        InvokeOptional(player, "Revive", null);
        InvokeOptional(player, "UnfreezePlayer", null);
        InvokeOptional(player, "Cure", null);
        InvokeOptional(player, "ApplyControlState", null);
    }

    bool TryGetBoolMember(object target, string memberName, out bool value)
    {
        value = false;

        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        try
        {
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(target);
                return true;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(bool))
            {
                MethodInfo getter = property.GetGetMethod(true);
                if (getter != null)
                {
                    value = (bool)getter.Invoke(target, null);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DEMO] Failed to read " + memberName + " from " + target + ": " + ex.Message);
        }

        return false;
    }

    bool TrySetBoolMember(object target, string memberName, bool value)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        try
        {
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(target, value);
                return true;
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(bool))
            {
                MethodInfo setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(target, new object[] { value });
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DEMO] Failed to set " + memberName + " on " + target + ": " + ex.Message);
        }

        return false;
    }

    bool TryForceSafeGasWaveInfection(out int infectedPlayerId)
    {
        infectedPlayerId = -1;

        int[] preferredPlayerIds = new int[] { 2, 3, 4 };
        InfectionSystem infectionSystem = FindAnyObjectByType<InfectionSystem>();

        for (int i = 0; i < preferredPlayerIds.Length; i++)
        {
            int playerId = preferredPlayerIds[i];
            PlayerIdentity target = FindPlayerById(playerId);

            if (target == null || !target.isAlive || target.isInfected)
            {
                continue;
            }

            bool infected = false;

            if (infectionSystem != null)
            {
                InvokeOptional(infectionSystem, "TryInfectPlayerById", new object[] { playerId, 1, "DEMO_GAS_WAVE_SAFE" });
            }

            if (!infected)
            {
                InvokeOptional(target, "Infect", null);
            }

            infected = target.isInfected;
            if (infected)
            {
                infectedPlayerId = playerId;
                return true;
            }
        }

        return false;
    }

    PlayerIdentity FindPlayerById(int id)
    {
        try
        {
            PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
            foreach (PlayerIdentity player in players)
            {
                if (player != null && player.playerId == id)
                    return player;
            }
        }
        catch { }

        return null;
    }

    bool InvokeOptionalOnAny(string typeName, string methodName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
            return false;

        try
        {
            MonoBehaviour[] allMonoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

            foreach (MonoBehaviour obj in allMonoBehaviours)
            {
                if (obj == null)
                    continue;

                Type objType = obj.GetType();
                if (objType.Name == typeName)
                {
                    if (InvokeOptional(obj, methodName, null))
                        return true;
                }
            }
        }
        catch { }

        return false;
    }

    bool InvokeOptional(UnityEngine.Object target, string methodName, object[] args)
    {
        if (target == null || string.IsNullOrWhiteSpace(methodName))
            return false;

        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = target.GetType().GetMethod(methodName, flags);

            if (method == null)
                return false;

            method.Invoke(target, args);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DEMO] Optional invoke failed: " + target.name + "." + methodName + " => " + ex.Message, this);
            return false;
        }
    }

    void Trace(string eventType, string message)
    {
        try
        {
            AgentTracePanel.Trace(eventType, message);
        }
        catch
        {
            Debug.Log("[DEMO TRACE] " + eventType + ": " + message);
        }
    }

    void Log(string message)
    {
        if (debugLogs)
            Debug.Log("[DEMO] " + message, this);
    }
}
