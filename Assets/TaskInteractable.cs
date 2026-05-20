using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class TaskInteractable : MonoBehaviour
{
    [Header("Task")]
    [SerializeField] string taskId;
    [SerializeField] string taskDisplayName;
    [SerializeField] string taskName = "Fix Wiring";
    [SerializeField] bool isExitScanTask;
    [SerializeField] bool completed;

    [Header("Exit Scan")]
    [SerializeField, Min(0.1f)] float exitScanDuration = 5f;
    [SerializeField] bool resetExitScanOnRelease = true;
    [SerializeField] Slider exitScanProgressSlider;
    [SerializeField] TMP_Text exitScanProgressText;
    [SerializeField] TMP_Text exitScanPromptText;
    [SerializeField] bool showExitScanDebugLogs = true;

    [Header("Wire Task")]
    [SerializeField] bool useWireMinigame = true;
    [SerializeField] WireTaskMinigame wireTaskMinigame;

    [Header("UI")]
    [SerializeField] TMP_Text interactionPrompt;

    [Header("Debug")]
    [SerializeField] bool enableDebugTaskHotkeys = true;

    [SerializeField] Color completedColor = Color.green;

    SpriteRenderer spriteRenderer;
    Collider2D taskCollider;
    PlayerIdentity localPlayerInRange;
    Color originalColor;

    bool isScanningExit;
    float exitScanProgress;
    bool exitScanUnlocked;

    readonly System.Collections.Generic.HashSet<Collider2D> localPlayerCollidersInside = new System.Collections.Generic.HashSet<Collider2D>();

    // Mobile helper: current task the local player can interact with
    public static TaskInteractable CurrentLocalInteractable { get; private set; }

    public string TaskId => !string.IsNullOrWhiteSpace(taskId) ? taskId : gameObject.name;
    public string TaskDisplayName => !string.IsNullOrWhiteSpace(taskDisplayName) ? taskDisplayName : taskName;
    public string TaskName => TaskDisplayName;
    public bool IsExitScanTask => isExitScanTask;
    public bool IsScanningExit => isScanningExit;
    public bool isCompleted => completed;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        taskCollider = GetComponent<Collider2D>();

        if (taskCollider == null)
        {
            Debug.LogWarning($"[TASK DEBUG] {TaskDisplayName} missing Collider2D.", this);
        }
        else if (!taskCollider.isTrigger)
        {
            Debug.LogWarning($"[TASK DEBUG] {TaskDisplayName} Collider2D is not trigger.", this);
            taskCollider.isTrigger = true;
        }

        if (spriteRenderer == null)
        {
            Debug.LogWarning($"[TASK DEBUG] {TaskDisplayName} missing SpriteRenderer.", this);
        }

        UpdateVisualState();
        SetPromptVisible(false);
    }

    void OnEnable()
    {
        RegisterWithTaskManager();
    }

    void Start()
    {
        RegisterWithTaskManager();
    }

    void Update()
    {
        if (enableDebugTaskHotkeys && Input.GetKeyDown(KeyCode.Y))
        {
            CompleteTaskForDebug();
        }

        if (isExitScanTask)
        {
            UpdateExitScanHoldState();
            return;
        }

        if (completed || localPlayerInRange == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            Interact();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (!IsValidTaskPlayer(identity, out string invalidReason))
        {
            if (identity != null)
            {
                Debug.Log($"[TASK DEBUG] Cannot use {TaskDisplayName}: {invalidReason}", this);
            }

            return;
        }

        bool isFirstCollider = localPlayerCollidersInside.Count == 0;
        localPlayerCollidersInside.Add(other);

        if (!isFirstCollider)
        {
            return;
        }

        localPlayerInRange = identity;
        CurrentLocalInteractable = this;
        SetPromptVisible(true);
        Debug.Log($"[TASK DEBUG] Player entered task: {TaskDisplayName}", this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (identity == null || localPlayerInRange != identity)
        {
            return;
        }

        localPlayerCollidersInside.Remove(other);

        if (localPlayerCollidersInside.Count > 0)
        {
            return;
        }

        localPlayerInRange = null;
        if (CurrentLocalInteractable == this)
        {
            CurrentLocalInteractable = null;
        }
        if (isExitScanTask)
        {
            StopHoldInteract();
        }
        SetPromptVisible(false);
        Debug.Log($"[TASK DEBUG] Player exited task: {TaskDisplayName}", this);
    }

    public void Interact()
    {
        if (isExitScanTask)
        {
            StartHoldInteract();
            return;
        }

        if (TryOpenWireMinigame())
        {
            return;
        }

        AttemptCompletion(false);
    }

    public void StartTapInteract()
    {
        if (isExitScanTask)
        {
            return;
        }

        // Check if wire minigame is already open/opening before attempting to open
        if (useWireMinigame && IsWireTask() && wireTaskMinigame != null && wireTaskMinigame.IsOpeningOrOpen)
        {
            if (showExitScanDebugLogs)
            {
                Debug.Log("[TASK DEBUG] Interact ignored: wire minigame already open.", this);
            }
            return;
        }

        if (TryOpenWireMinigame())
        {
            if (showExitScanDebugLogs)
            {
                Debug.Log("[TASK DEBUG] Wire task tap accepted: " + TaskId, this);
            }
            return;
        }

        AttemptCompletion(false);
    }

    public void StartHoldInteract()
    {
        if (!isExitScanTask)
        {
            Interact();
            return;
        }

        if (showExitScanDebugLogs)
        {
            Debug.Log("[TASK DEBUG] Exit scan hold started.", this);
        }

        if (completed)
        {
            return;
        }

        exitScanUnlocked = IsExitScanUnlocked();
        if (!CanUseExitScan(out string reason))
        {
            if (showExitScanDebugLogs && !string.IsNullOrWhiteSpace(reason))
            {
                Debug.Log($"[TASK DEBUG] {reason}", this);
            }

            RefreshExitScanUi();
            return;
        }

        isScanningExit = true;
        RefreshExitScanUi();
    }

    public void StopHoldInteract()
    {
        if (!isExitScanTask || completed)
        {
            return;
        }

        if (!isScanningExit)
        {
            RefreshExitScanUi();
            return;
        }

        isScanningExit = false;

        if (resetExitScanOnRelease && exitScanProgress > 0f && exitScanProgress < exitScanDuration)
        {
            exitScanProgress = 0f;
            RefreshExitScanUi();
            if (showExitScanDebugLogs)
            {
                Debug.Log("[TASK DEBUG] Exit Scan interrupted. Progress reset.", this);
            }
            return;
        }

        RefreshExitScanUi();
    }

    public static bool TryInteractCurrent()
    {
        if (CurrentLocalInteractable == null)
        {
            return false;
        }

        CurrentLocalInteractable.Interact();
        return true;
    }

    public void CompleteTaskForDebug()
    {
        AttemptCompletion(true);
    }

    public void CompleteFromMinigame()
    {
        if (completed)
        {
            return;
        }

        if (isExitScanTask)
        {
            return;
        }

        AttemptCompletion(false);
    }

    public void ResetTaskForDebug()
    {
        completed = false;
        isScanningExit = false;
        exitScanProgress = 0f;
        exitScanUnlocked = false;
        UpdateVisualState();
        SetPromptVisible(localPlayerInRange != null);
    }

    public bool IsExitScanUnlocked()
    {
        TaskManager taskManager = ResolveTaskManager();
        return taskManager != null && taskManager.IsExitScanUnlocked;
    }

    public float GetExitScanProgress01()
    {
        if (exitScanDuration <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp01(exitScanProgress / exitScanDuration);
    }

    void AttemptCompletion(bool debugForce)
    {
        if (completed)
        {
            Debug.Log($"[TASK DEBUG] Task already completed: {TaskId} {TaskDisplayName}", this);
            return;
        }

        if (!debugForce)
        {
            if (IsBlockedByGamePhase(out string phaseReason))
            {
                Debug.Log($"[TASK DEBUG] {phaseReason}", this);
                return;
            }

            if (localPlayerInRange == null)
            {
                Debug.Log($"[TASK DEBUG] Cannot complete {TaskDisplayName}: no player in range.", this);
                return;
            }

            if (!IsValidTaskPlayer(localPlayerInRange, out string playerReason))
            {
                Debug.Log($"[TASK DEBUG] Cannot complete {TaskDisplayName}: {playerReason}", this);
                return;
            }

            if (FinalHuntIsActive())
            {
                Debug.Log($"[TASK DEBUG] Final Hunt task allowed for {localPlayerInRange.playerName}.", this);
            }
        }

        TaskManager manager = ResolveTaskManager();
        if (manager != null && !manager.CanCompleteTask(this, debugForce, out string managerReason))
        {
            if (!string.IsNullOrWhiteSpace(managerReason))
            {
                Debug.Log($"[TASK DEBUG] {managerReason}", this);
            }

            return;
        }

        completed = true;
        localPlayerInRange = null;

        UpdateVisualState();
        SetPromptVisible(false);

        if (manager != null)
        {
            manager.CompleteTask(this, debugForce);
        }
        else
        {
            Debug.Log($"[TASK DEBUG] Task completed: {TaskId} {TaskDisplayName}", this);
        }
    }

    bool IsBlockedByGamePhase(out string reason)
    {
        GamePhase phase = GameManager.CurrentPhase;
        if (phase == GamePhase.Meeting || phase == GamePhase.Voting)
        {
            reason = $"Phase blocked: {phase}.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    bool FinalHuntIsActive()
    {
        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        return finalHuntManager != null && finalHuntManager.IsFinalHuntActive;
    }

    bool IsValidTaskPlayer(PlayerIdentity identity, out string reason)
    {
        if (identity == null)
        {
            reason = "player identity is missing.";
            return false;
        }

        if (!identity.isAlive)
        {
            reason = $"{identity.playerName} is dead.";
            return false;
        }

        if (!identity.isLocalPlayer)
        {
            reason = $"{identity.playerName} is not the local player.";
            return false;
        }

        if (identity.isInfected)
        {
            reason = $"{identity.playerName} is infected.";
            return false;
        }

        if (identity.isAIControlled)
        {
            reason = $"{identity.playerName} is AI-controlled.";
            return false;
        }

        if (identity.isFrozen)
        {
            reason = $"{identity.playerName} is frozen.";
            return false;
        }

        Rigidbody2D body = identity.GetComponentInParent<Rigidbody2D>();
        if (body == null)
        {
            Debug.LogWarning($"[TASK DEBUG] {identity.playerName} has no Rigidbody2D. Trigger detection may fail.", identity);
        }

        Collider2D collider = identity.GetComponentInParent<Collider2D>();
        if (collider == null)
        {
            Debug.LogWarning($"[TASK DEBUG] {identity.playerName} has no Collider2D. Trigger detection may fail.", identity);
        }

        reason = string.Empty;
        return true;
    }

    void RegisterWithTaskManager()
    {
        TaskManager taskManager = ResolveTaskManager();
        if (taskManager == null)
        {
            Debug.LogWarning($"[TASK DEBUG] No TaskManager found for {TaskDisplayName}", this);
            return;
        }

        taskManager.RegisterTask(this);
    }

    TaskManager ResolveTaskManager()
    {
        return TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
    }

    bool IsWireTask()
    {
        return TaskId.StartsWith("wire_", StringComparison.OrdinalIgnoreCase);
    }

    WireTaskMinigame ResolveWireTaskMinigame()
    {
        if (wireTaskMinigame != null)
        {
            return wireTaskMinigame;
        }

        // First, try to find the active WireTaskSystem controller
        GameObject wireSysGO = GameObject.Find("WireTaskSystem");
        if (wireSysGO != null && wireSysGO.activeInHierarchy)
        {
            WireTaskMinigame controller = wireSysGO.GetComponent<WireTaskMinigame>();
            if (controller != null)
            {
                if (showExitScanDebugLogs)
                {
                    Debug.Log("[WIRE TASK] Using WireTaskMinigame controller: WireTaskSystem panelRoot=WireTaskPanel", this);
                }
                wireTaskMinigame = controller;
                return wireTaskMinigame;
            }
        }

        // Fallback: find any WireTaskMinigame, preferring active ones
        WireTaskMinigame[] allMinigames = FindObjectsByType<WireTaskMinigame>(FindObjectsInactive.Include);
        if (allMinigames == null || allMinigames.Length == 0)
        {
            return null;
        }

        // Prefer active controller
        for (int i = 0; i < allMinigames.Length; i++)
        {
            if (allMinigames[i].gameObject.activeInHierarchy)
            {
                wireTaskMinigame = allMinigames[i];
                if (showExitScanDebugLogs)
                {
                    Debug.Log($"[WIRE TASK] Using active WireTaskMinigame controller: {wireTaskMinigame.gameObject.name}", this);
                }
                return wireTaskMinigame;
            }
        }

        // Fallback: use first found
        wireTaskMinigame = allMinigames[0];
        if (showExitScanDebugLogs)
        {
            Debug.Log($"[WIRE TASK] Using WireTaskMinigame controller: {wireTaskMinigame.gameObject.name} (no active found)", this);
        }
        return wireTaskMinigame;
    }

    bool TryOpenWireMinigame()
    {
        if (!useWireMinigame || !IsWireTask() || isExitScanTask)
        {
            return false;
        }

        if (completed)
        {
            return true;
        }

        if (IsBlockedByGamePhase(out string phaseReason))
        {
            Debug.Log($"[TASK DEBUG] {phaseReason}", this);
            return true;
        }

        if (localPlayerInRange == null)
        {
            Debug.Log($"[TASK DEBUG] Cannot complete {TaskDisplayName}: no player in range.", this);
            return true;
        }

        if (!IsValidTaskPlayer(localPlayerInRange, out string playerReason))
        {
            Debug.Log($"[TASK DEBUG] Cannot complete {TaskDisplayName}: {playerReason}", this);
            return true;
        }

        WireTaskMinigame minigame = ResolveWireTaskMinigame();
        if (minigame == null)
        {
            Debug.LogWarning("[WIRE TASK] WireTaskMinigame missing. Falling back to instant completion.", this);
            return false;
        }

        // Guard: if minigame is already open or opening, don't try to open again
        if (minigame.IsOpeningOrOpen)
        {
            if (showExitScanDebugLogs)
            {
                Debug.Log("[TASK DEBUG] Interact ignored: wire minigame already open.", this);
            }
            return true;
        }

        Debug.Log($"[WIRE TASK] Opening minigame instance: {minigame.gameObject.name}", this);
        minigame.Open(this);
        return true;
    }

    void UpdateExitScanHoldState()
    {
        if (completed)
        {
            return;
        }

        if (localPlayerInRange == null)
        {
            StopHoldInteract();
            SetPromptVisible(false);
            return;
        }

        exitScanUnlocked = IsExitScanUnlocked();

        if (IsBlockedByGamePhase(out string phaseReason))
        {
            isScanningExit = false;
            RefreshExitScanUi(phaseReason);
            return;
        }

        if (!IsValidTaskPlayer(localPlayerInRange, out string playerReason))
        {
            isScanningExit = false;
            RefreshExitScanUi(playerReason);
            return;
        }

        if (!exitScanUnlocked)
        {
            isScanningExit = false;
            RefreshExitScanUi();
            return;
        }

        bool holdActive = Input.GetKey(KeyCode.E) || (MobileActionButtonsController.Instance != null && MobileActionButtonsController.Instance.IsInteractHeld);

        if (!holdActive)
        {
            if (isScanningExit)
            {
                StopHoldInteract();
            }
            else
            {
                RefreshExitScanUi();
            }

            return;
        }

        isScanningExit = true;
        exitScanProgress += Time.deltaTime;
        RefreshExitScanUi();

        if (exitScanProgress >= exitScanDuration)
        {
            CompleteExitScan();
        }
    }

    void CompleteExitScan()
    {
        if (completed)
        {
            return;
        }

        isScanningExit = false;
        exitScanProgress = exitScanDuration;
        completed = true;

        if (CurrentLocalInteractable == this)
        {
            CurrentLocalInteractable = null;
        }

        UpdateVisualState();
        SetPromptVisible(false);

        TaskManager manager = ResolveTaskManager();
        if (manager != null)
        {
            manager.CompleteTask(this);
        }
        else
        {
            AgentTracePanel.Trace("OBJECTIVE", "Exit Scan completed. Escape route opened.");
        }

        if (showExitScanDebugLogs)
        {
            Debug.Log("[TASK DEBUG] Exit Scan completed.", this);
        }
    }

    bool CanUseExitScan(out string reason)
    {
        if (completed)
        {
            reason = "Exit Scan already completed.";
            return false;
        }

        TaskManager taskManager = ResolveTaskManager();
        if (taskManager == null)
        {
            reason = "TaskManager missing for Exit Scan.";
            return false;
        }

        if (!taskManager.IsExitScanUnlocked)
        {
            reason = "Exit Scan locked until all wire tasks are complete.";
            return false;
        }

        if (localPlayerInRange == null)
        {
            reason = "Exit Scan requires a player in range.";
            return false;
        }

        if (IsBlockedByGamePhase(out string phaseReason))
        {
            reason = phaseReason;
            return false;
        }

        if (!IsValidTaskPlayer(localPlayerInRange, out string playerReason))
        {
            reason = playerReason;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    void RefreshExitScanUi(string overridePrompt = null)
    {
        TMP_Text promptText = ResolveExitScanPromptText();
        bool showUi = localPlayerInRange != null && !completed;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(showUi);

            if (showUi)
            {
                if (!string.IsNullOrWhiteSpace(overridePrompt))
                {
                    promptText.text = overridePrompt;
                }
                else if (!exitScanUnlocked)
                {
                    promptText.text = "Exit Scan locked\nComplete wire tasks";
                }
                else if (isScanningExit)
                {
                    promptText.text = $"Scanning exit... {Mathf.RoundToInt(GetExitScanProgress01() * 100f)}%";
                }
                else
                {
                    promptText.text = "Hold INTERACT to scan exit";
                }

                promptText.raycastTarget = false;
            }
        }

        if (exitScanProgressSlider != null)
        {
            exitScanProgressSlider.gameObject.SetActive(showUi);
            exitScanProgressSlider.minValue = 0f;
            exitScanProgressSlider.maxValue = 1f;
            exitScanProgressSlider.value = GetExitScanProgress01();
        }

        if (exitScanProgressText != null)
        {
            exitScanProgressText.gameObject.SetActive(showUi);
            exitScanProgressText.text = showUi
                ? $"Exit Scan: {Mathf.RoundToInt(GetExitScanProgress01() * 100f)}%"
                : string.Empty;
            exitScanProgressText.raycastTarget = false;
        }
    }

    TMP_Text ResolveExitScanPromptText()
    {
        return exitScanPromptText != null ? exitScanPromptText : interactionPrompt;
    }

    void SetPromptVisible(bool visible)
    {
        if (isExitScanTask)
        {
            RefreshExitScanUi();
            return;
        }

        if (interactionPrompt == null)
        {
            return;
        }

        interactionPrompt.gameObject.SetActive(visible && !isCompleted);
        if (visible && !isCompleted)
        {
            // Mobile-first prompt
            interactionPrompt.text = $"INTERACT: {TaskDisplayName}";
            interactionPrompt.raycastTarget = false;
        }
    }

    void UpdateVisualState()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.color = completed ? completedColor : originalColor;
    }

}
