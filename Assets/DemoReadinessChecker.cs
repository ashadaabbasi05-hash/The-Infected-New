using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
/// <summary>
/// DemoReadinessChecker validates the hackathon demo scene without modifying gameplay.
/// Runs once on Play Mode start and prints a clean checklist to Console.
/// Read-only checks only — no state changes.
/// </summary>
public class DemoReadinessChecker : MonoBehaviour
{
    [SerializeField] bool runOnStart = true;
    [SerializeField] bool verboseLogs = true;
    [SerializeField] bool warnOnly = true;
    [SerializeField] int expectedTaskCount = 8;

    int checksRun = 0;
    int checksPassed = 0;
    int failedChecks = 0;
    List<string> issues = new List<string>();

    void Start()
    {
        if (runOnStart)
        {
            StartCoroutine(RunCheckNextFrame());
        }
    }

    IEnumerator RunCheckNextFrame()
    {
        yield return null;

        Debug.Log("[DEMO CHECK] ==============================", this);
        Debug.Log("[DEMO CHECK] The Infected demo readiness scan", this);

        CheckPlayers();
        CheckTasks();
        CheckWireTask();
        CheckUi();
        CheckManagers();
        CheckSceneSafety();

        PrintSummary();

        Debug.Log("[DEMO CHECK] ==============================", this);
    }

    void CheckPlayers()
    {
        bool passed = true;
        string result = "";

        // Find all PlayerIdentity objects
        PlayerIdentity[] allPlayers = FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Include);
        if (allPlayers.Length != 4)
        {
            passed = false;
            result = $"Found {allPlayers.Length} players, expected 4";
        }

        // Find Player 1
        PlayerIdentity player1 = null;
        foreach (PlayerIdentity p in allPlayers)
        {
            if (p != null && p.playerId == 1)
            {
                player1 = p;
                break;
            }
        }

        if (player1 == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "Player 1 not found";
        }
        else
        {
            if (!player1.isAlive)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "Player 1 not alive";
            }

            if (!player1.IsLocalPlayer)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "Player 1 not local";
            }

            PlayerMovement playerMvmt = player1.GetComponent<PlayerMovement>();
            if (playerMvmt == null)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "Player 1 missing PlayerMovement";
            }

            BotMovement botMvmt = player1.GetComponent<BotMovement>();
            if (botMvmt != null && botMvmt.enabled)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "Player 1 BotMovement enabled (should be disabled)";
            }
        }

        if (passed)
        {
            Pass("Players: 4 found, Player 1 local ready");
        }
        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Players check: {result}", this);
    }

    void CheckTasks()
    {
        bool passed = true;
        string result = "";

        TaskManager taskManager = FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
        if (taskManager == null)
        {
            passed = false;
            result = "TaskManager not found";
        }

        TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Include);
        if (tasks.Length != expectedTaskCount)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + $"Found {tasks.Length} tasks, expected {expectedTaskCount}";
        }

        bool hasExitScan = false;
        foreach (TaskInteractable task in tasks)
        {
            if (task == null)
                continue;

            if (IsExitScanTask(task))
            {
                hasExitScan = true;
            }

            Collider2D collider = task.GetComponent<Collider2D>();
            if (collider == null)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + $"Task {task.name} missing Collider2D";
            }
            else if (!collider.isTrigger)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + $"Task {task.name} Collider2D not trigger";
            }
        }

        if (!hasExitScan)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "No Exit Scan task found";
        }

        if (passed)
        {
            Pass($"Tasks: {expectedTaskCount} found");
        }
        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Tasks check: {result}", this);
    }

    void CheckWireTask()
    {
        bool passed = true;
        string result = "";

        GameObject wireTaskSystemObject = FindSceneGameObjectByName("WireTaskSystem");
        GameObject wireTaskPanelObject = FindSceneGameObjectByName("WireTaskPanel");
        WireTaskMinigame wireTaskMinigame = FindAnyObjectByType<WireTaskMinigame>(FindObjectsInactive.Include);

        if (wireTaskSystemObject == null)
        {
            Fail("WireTaskSystem GameObject missing.");
            passed = false;
            result = "WireTaskSystem missing";
        }
        else if (!wireTaskSystemObject.activeInHierarchy)
        {
            Fail("WireTaskSystem exists but is inactive. It should stay active.");
            passed = false;
            result = "WireTaskSystem inactive";
        }

        if (wireTaskPanelObject == null)
        {
            Fail("WireTaskPanel GameObject missing.");
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "WireTaskPanel missing";
        }

        if (wireTaskMinigame == null)
        {
            Fail("WireTaskMinigame component missing.");
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "WireTaskMinigame missing";
        }
        else
        {
            if (wireTaskPanelObject != null && wireTaskMinigame.gameObject == wireTaskPanelObject && !wireTaskPanelObject.activeInHierarchy)
            {
                Warn("WireTaskMinigame is on inactive WireTaskPanel. Prefer active WireTaskSystem controller.");
            }

            if (wireTaskSystemObject != null && wireTaskMinigame.gameObject == wireTaskSystemObject)
            {
                // Preferred placement.
            }
            else if (passed)
            {
                Warn("WireTaskMinigame exists but is not on WireTaskSystem.");
            }
        }

        if (passed)
        {
            Pass("Wire task UI ready");
        }

        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Wire task check: {result}", this);
    }

    void CheckUi()
    {
        bool passed = true;
        string result = "";

        // Canvas
        Canvas canvas = FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            passed = false;
            result = "Canvas not found";
        }

        // Joystick
        FixedJoystick joystick = FindAnyObjectByType<FixedJoystick>(FindObjectsInactive.Include);
        if (joystick == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "FixedJoystick not found";
        }

        // Mobile buttons panel
        GameObject mobileButtonsPanel = FindGameObjectByExactNameIncludingInactive("MobileButtonsPanel");
        if (mobileButtonsPanel == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "MobileButtonsPanel not found";
        }
        else
        {
            Button interactButton = FindButtonInHierarchy(mobileButtonsPanel, "InteractButton");
            if (interactButton == null)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "InteractButton missing";
            }

            Button traceButton = FindButtonInHierarchy(mobileButtonsPanel, "TraceButton");
            if (traceButton == null)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "TraceButton missing";
            }

            Button helpButton = FindButtonInHierarchy(mobileButtonsPanel, "HelpButton");
            if (helpButton == null)
            {
                passed = false;
                result += (result.Length > 0 ? "; " : "") + "HelpButton missing";
            }

            Button demoButton = FindButtonInHierarchy(mobileButtonsPanel, "DemoButton");
            if (demoButton == null && verboseLogs)
            {
                Debug.Log("[DEMO CHECK] ℹ️  DemoButton not found (optional)", this);
            }
        }

        // Chat, voting, antidote panels
        GameObject chatPanel = FindGameObjectByExactNameIncludingInactive("ChatPanel");
        if (chatPanel == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "ChatPanel not found";
        }

        GameObject votingPanel = FindGameObjectByExactNameIncludingInactive("VotingPanel");
        if (votingPanel == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "VotingPanel not found";
        }

        GameObject publicAntidotePanel = FindGameObjectByExactNameIncludingInactive("PublicAntidoteStatusPanel");
        if (publicAntidotePanel == null && verboseLogs)
        {
            Debug.Log("[DEMO CHECK] ℹ️  PublicAntidoteStatusPanel not found (may be created at runtime)", this);
        }

        GameObject personalAntidotePanel = FindGameObjectByExactNameIncludingInactive("PersonalAntidotePanel");
        if (personalAntidotePanel == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "PersonalAntidotePanel not found";
        }

        // Objective HUD
        GameObject objectiveText = FindGameObjectByExactNameIncludingInactive("TopRightObjectiveText");
        if (objectiveText == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "TopRightObjectiveText not found";
        }

        GameObject finalHuntText = FindGameObjectByExactNameIncludingInactive("BottomRightFinalHuntText");
        if (finalHuntText == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "BottomRightFinalHuntText not found";
        }

        if (passed)
        {
            Pass("Mobile UI ready");
        }
        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Mobile UI check: {result}", this);
    }

    void CheckManagers()
    {
        bool passed = true;
        string result = "";

        // Core managers
        GameManager gameManager = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null)
        {
            passed = false;
            result = "GameManager not found";
        }

        InfectionSystem infectionSystem = FindAnyObjectByType<InfectionSystem>(FindObjectsInactive.Include);
        if (infectionSystem == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "InfectionSystem not found";
        }

        MeetingController meetingController = FindAnyObjectByType<MeetingController>(FindObjectsInactive.Include);
        if (meetingController == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "MeetingController not found";
        }

        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        if (finalHuntManager == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "FinalHuntManager not found";
        }

        ObjectiveHUDController objectiveHud = FindAnyObjectByType<ObjectiveHUDController>(FindObjectsInactive.Include);
        if (objectiveHud == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "ObjectiveHUDController not found";
        }

        GasWaveEffectsController gasWave = FindAnyObjectByType<GasWaveEffectsController>(FindObjectsInactive.Include);
        if (gasWave == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "GasWaveEffectsController not found";
        }

        AgentTracePanel agentTrace = FindAnyObjectByType<AgentTracePanel>(FindObjectsInactive.Include);
        if (agentTrace == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "AgentTracePanel not found";
        }

        DemoToolsController demoTools = FindAnyObjectByType<DemoToolsController>(FindObjectsInactive.Include);
        if (demoTools == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "DemoToolsController not found";
        }

        BotChatDirector botChat = FindAnyObjectByType<BotChatDirector>(FindObjectsInactive.Include);
        if (botChat == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "BotChatDirector not found";
        }

        BotDecisionDirector botDecision = FindAnyObjectByType<BotDecisionDirector>(FindObjectsInactive.Include);
        if (botDecision == null && verboseLogs)
        {
            Debug.Log("[DEMO CHECK] ℹ️  BotDecisionDirector not found (optional)", this);
        }

        TeamAApiClient teamA = FindAnyObjectByType<TeamAApiClient>(FindObjectsInactive.Include);
        if (teamA == null && verboseLogs)
        {
            Debug.Log("[DEMO CHECK] ℹ️  TeamAApiClient not found (uses fallback)", this);
        }

        if (passed)
        {
            Pass("Managers ready");
        }
        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Managers check: {result}", this);
    }

    void CheckSceneSafety()
    {
        bool passed = true;
        string result = "";

        // EventSystem
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (eventSystem == null)
        {
            passed = false;
            result = "EventSystem not found";
        }

        // Main Camera
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + "Main Camera not found or not tagged";
        }

        // Audio Listeners
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
        if (listeners.Length > 1 && verboseLogs)
        {
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Found {listeners.Length} AudioListeners (should be 1)", this);
        }

        // Duplicates
        ObjectiveHUDController[] objectiveHuds = FindObjectsByType<ObjectiveHUDController>(FindObjectsInactive.Include);
        if (objectiveHuds.Length > 1)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + $"Duplicate ObjectiveHUDController ({objectiveHuds.Length} found)";
        }

        DemoToolsController[] demoTools = FindObjectsByType<DemoToolsController>(FindObjectsInactive.Include);
        if (demoTools.Length > 1)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + $"Duplicate DemoToolsController ({demoTools.Length} found)";
        }

        TeamAApiClient[] teamAClients = FindObjectsByType<TeamAApiClient>(FindObjectsInactive.Include);
        if (teamAClients.Length > 1)
        {
            passed = false;
            result += (result.Length > 0 ? "; " : "") + $"Duplicate TeamAApiClient ({teamAClients.Length} found)";
        }

        if (passed)
        {
            Pass("Scene safety clean");
        }
        if (!passed && verboseLogs)
            Debug.LogWarning($"[DEMO CHECK] ⚠️  Scene safety check: {result}", this);
    }

    void Pass(string message)
    {
        checksRun++;
        checksPassed++;
        Debug.Log($"[DEMO CHECK] ✅ {message}", this);
    }

    void Warn(string message)
    {
        if (verboseLogs)
        {
            Debug.LogWarning($"[DEMO CHECK] ⚠️  {message}", this);
        }
    }

    void Fail(string message)
    {
        checksRun++;
        failedChecks++;
        issues.Add(message);

        if (warnOnly)
        {
            Debug.LogWarning("[DEMO CHECK] ❌ " + message, this);
        }
        else
        {
            Debug.LogError("[DEMO CHECK] ❌ " + message, this);
        }
    }

    void PrintSummary()
    {
        if (failedChecks == 0)
        {
            Debug.Log("[DEMO CHECK] RESULT: READY", this);
        }
        else
        {
            string issueList = string.Join(", ", issues);
            if (warnOnly)
            {
                Debug.LogWarning($"[DEMO CHECK] RESULT: NEEDS FIX — {issueList}", this);
            }
            else
            {
                Debug.LogError($"[DEMO CHECK] RESULT: NEEDS FIX — {issueList}", this);
            }
        }
    }

    GameObject FindGameObjectByExactNameIncludingInactive(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        foreach (Transform transform in transforms)
        {
            if (transform == null)
            {
                continue;
            }

            if (transform.gameObject.name == exactName)
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    GameObject FindSceneGameObjectByName(string objectName)
    {
        return FindGameObjectByExactNameIncludingInactive(objectName);
    }

    bool IsExitScanTask(TaskInteractable task)
    {
        if (task == null)
        {
            return false;
        }

        if (ContainsExitKeyword(task.gameObject != null ? task.gameObject.name : null))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "taskId")))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "taskDisplayName")))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "taskName")))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "TaskId")))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "TaskDisplayName")))
        {
            return true;
        }

        if (ContainsExitKeyword(GetMemberStringValue(task, "TaskName")))
        {
            return true;
        }

        return ContainsExitKeyword(task.ToString());
    }

    static bool ContainsExitKeyword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string lower = value.ToLowerInvariant();
        return lower.Contains("exit") || lower.Contains("scan");
    }

    static string GetMemberStringValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        System.Type type = target.GetType();
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        System.Reflection.PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.PropertyType == typeof(string))
        {
            object value = property.GetValue(target);
            return value as string;
        }

        System.Reflection.FieldInfo field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(string))
        {
            object value = field.GetValue(target);
            return value as string;
        }

        return null;
    }

    Button FindButtonInHierarchy(GameObject root, string buttonName)
    {
        if (root == null)
            return null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button btn in buttons)
        {
            if (btn != null && btn.gameObject.name == buttonName)
                return btn;
        }

        return null;
    }
}
