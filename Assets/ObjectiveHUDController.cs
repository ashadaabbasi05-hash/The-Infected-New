using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ObjectiveHUDController : MonoBehaviour
{
    public static ObjectiveHUDController Instance { get; private set; }

    [Header("UI")]
    [SerializeField] TMP_Text topRightObjectiveText;
    [SerializeField] TMP_Text bottomRightFinalHuntText;
    [SerializeField] bool flickerFinalHuntText = true;
    [SerializeField, Min(0.1f)] float flickerSpeed = 4f;

    TaskManager taskManager;
    FinalHuntManager finalHuntManager;
    GameEndManager gameEndManager;

    CanvasGroup bottomRightCanvasGroup;
    Color bottomRightBaseColor = Color.white;
    bool hasWarnedMissingTopRight;
    bool hasWarnedMissingBottomRight;
    bool isFinalHuntActive;
    bool bottomRightVisible;
    bool isInitialized;
    Coroutine finalHuntFlickerRoutine;
    bool finalHuntFlickerActive;
    bool finalHuntHudVisible;
    bool wasFinalHuntActiveLastFrame;

    void Awake()
    {
        // Check if Instance exists and is valid (not destroyed)
        if (Instance != null && Instance != this)
        {
            // If Instance is destroyed, clear it
            if (!IsValidInstance(Instance))
            {
                Instance = null;
            }
        }

        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[OBJECTIVE HUD] Instance assigned.", this);
        }
        else if (Instance != this)
        {
            Debug.LogWarning("[OBJECTIVE HUD] Duplicate controller detected. Destroying this instance.", this);
            Destroy(gameObject);
            return;
        }
    }

    static bool IsValidInstance(ObjectiveHUDController instance)
    {
        // Check if instance exists and is not destroyed
        try
        {
            return instance != null && instance.gameObject != null;
        }
        catch
        {
            return false;
        }
    }

    void OnEnable()
    {
        if (HasRequiredReferences())
        {
            ConfigureTopRightText(topRightObjectiveText);
            ConfigureBottomRightText(bottomRightFinalHuntText);
            SubscribeToTaskManager();
            SubscribeToFinalHuntManager();

            if (isInitialized)
            {
                RefreshObjectiveText();
            }
        }
    }

    void Start()
    {
        if (isInitialized)
        {
            return;
        }

        if (HasRequiredReferences())
        {
            Initialize(topRightObjectiveText, bottomRightFinalHuntText);
            return;
        }

        if (!hasWarnedMissingTopRight)
        {
            hasWarnedMissingTopRight = true;
            Debug.LogWarning("[TASK DEBUG] Top-right objective text is missing.", this);
        }

        if (!hasWarnedMissingBottomRight)
        {
            hasWarnedMissingBottomRight = true;
            Debug.LogWarning("[TASK DEBUG] Bottom-right FINAL HUNT text is missing.", this);
        }
    }

    void OnDisable()
    {
        StopFinalHuntFlickerRoutine();
        UnsubscribeFromTaskManager();
        UnsubscribeFromFinalHuntManager();
        Debug.Log("[OBJECTIVE HUD] Unsubscribed events.", this);
    }

    void OnDestroy()
    {
        StopFinalHuntFlickerRoutine();
        UnsubscribeFromTaskManager();
        UnsubscribeFromFinalHuntManager();

        if (Instance == this)
        {
            Instance = null;
            Debug.Log("[OBJECTIVE HUD] Instance cleared.", this);
        }
    }

    void Update()
    {
        RefreshBindings();

        if (gameEndManager != null && gameEndManager.IsGameEnded)
        {
            HideAllObjectiveUi();
            return;
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            RefreshObjectiveText();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            flickerFinalHuntText = !flickerFinalHuntText;
            RefreshObjectiveText();
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            ForceShowFinalHuntText();
        }

        // Handle debug hotkeys only

        // Detect Final Hunt state changes and update HUD only on transitions
        bool currentFinalHuntState = finalHuntManager != null && finalHuntManager.IsFinalHuntActive;
        if (currentFinalHuntState != wasFinalHuntActiveLastFrame)
        {
            if (currentFinalHuntState)
            {
                ShowFinalHuntFlicker();
            }
            else
            {
                HideFinalHuntFlicker();
            }

            wasFinalHuntActiveLastFrame = currentFinalHuntState;
            isFinalHuntActive = currentFinalHuntState;
        }
    }

    public void ResetHudForDemo()
    {
        HideFinalHuntFlicker();
        RefreshBindings();

        isFinalHuntActive = false;
        wasFinalHuntActiveLastFrame = false;

        RefreshObjectiveText();
        Debug.Log("[OBJECTIVE HUD] Reset for demo.", this);
    }

    public void ForceHideFinalHuntHud()
    {
        HideFinalHuntFlicker();

        isFinalHuntActive = false;
        wasFinalHuntActiveLastFrame = false;

        if (bottomRightFinalHuntText != null)
        {
            bottomRightFinalHuntText.text = string.Empty;
            bottomRightFinalHuntText.alpha = 0f;
            bottomRightFinalHuntText.gameObject.SetActive(false);
        }

        if (bottomRightCanvasGroup != null)
        {
            bottomRightCanvasGroup.alpha = 0f;
        }

        RefreshObjectiveText();
    }

    public void Initialize(TMP_Text topRightText, TMP_Text bottomRightText)
    {
        topRightObjectiveText = topRightText;
        bottomRightFinalHuntText = bottomRightText;

        ConfigureTopRightText(topRightObjectiveText);
        ConfigureBottomRightText(bottomRightFinalHuntText);
        SubscribeToTaskManager();
        SubscribeToFinalHuntManager();

        isInitialized = true;
        RefreshObjectiveText();
        Debug.Log("[OBJECTIVE HUD] Initialized.", this);
    }

    public bool HasRequiredReferences()
    {
        return topRightObjectiveText != null && bottomRightFinalHuntText != null;
    }

    public void HandleTaskProgressChanged(float progress, int completed, int total)
    {
        RefreshObjectiveText();
    }

    public void HandleAllTasksCompleted()
    {
        HandleTasksCompleted();
    }

    public void HandleTasksCompleted()
    {
        Debug.Log("[OBJECTIVE HUD] Escape objective active.", this);
        RefreshObjectiveText();
    }

    public void HandleFinalHuntStarted()
    {
        isFinalHuntActive = true;
        ResolveBottomRightTextIfNeeded();
        ForceConfigureBottomRightText();

        Debug.Log($"[OBJECTIVE HUD] Final Hunt objective active. bottomRightNull={(bottomRightFinalHuntText == null)}", this);
        Debug.Log($"[OBJECTIVE HUD] Final Hunt bottom-right state activeSelf={(bottomRightFinalHuntText != null && bottomRightFinalHuntText.gameObject.activeSelf)} canvasAlpha={(bottomRightCanvasGroup != null ? bottomRightCanvasGroup.alpha : -1f)} colorAlpha={(bottomRightFinalHuntText != null ? bottomRightFinalHuntText.color.a : -1f)} anchoredPosition={(bottomRightFinalHuntText != null ? bottomRightFinalHuntText.rectTransform.anchoredPosition : Vector2.zero)}", this);

        ShowFinalHuntFlicker();
        RefreshObjectiveText();
    }

    public void HandleEscapeDoorUnlocked()
    {
        RefreshObjectiveText();
    }

    public void HandleHumanWinTriggered()
    {
        HandleGameEnded();
    }

    public void HandleGameEnded()
    {
        HideFinalHuntFlicker();
        HideAllObjectiveUi();
    }

    public void RefreshObjectiveText()
    {
        RefreshBindings();

        if (gameEndManager != null && gameEndManager.IsGameEnded)
        {
            HideAllObjectiveUi();
            return;
        }

        if (taskManager == null)
        {
            SetTopRightText("Tasks Remaining: 0");
            ShowBottomRightFinalHuntText(isFinalHuntActive);
            return;
        }

        int remainingTasks = taskManager.RemainingTasks;
        bool allTasksComplete = taskManager.AreAllTasksCompleted;
        bool exitScanReady = taskManager.IsExitScanUnlocked && !allTasksComplete;
        TaskInteractable currentTask = TaskInteractable.CurrentLocalInteractable;
        bool isScanningExit = currentTask != null && currentTask.IsExitScanTask && currentTask.IsScanningExit;

        if (isScanningExit)
        {
            SetTopRightText("SCANNING EXIT...");
        }
        else if (allTasksComplete)
        {
            SetTopRightText("ESCAPE DOOR OPEN\nRUN TO THE EXIT");
        }
        else if (exitScanReady)
        {
            SetTopRightText("EXIT SCAN READY\nGO TO EXIT GATE");
        }
        else if (isFinalHuntActive)
        {
            SetTopRightText("FINAL OBJECTIVE\nCOMPLETE REMAINING TASKS");
        }
        else
        {
            SetTopRightText($"Tasks Remaining: {remainingTasks}");
        }

        if (isFinalHuntActive)
        {
            ShowFinalHuntFlicker();
        }
        else
        {
            HideFinalHuntFlicker();
        }
    }

    void SubscribeToTaskManager()
    {
        if (taskManager == null)
        {
            return;
        }

        taskManager.OnTaskProgressChanged -= HandleTaskProgressChanged;
        taskManager.OnTaskProgressChanged += HandleTaskProgressChanged;
        taskManager.OnAllTasksCompleted -= HandleAllTasksCompleted;
        taskManager.OnAllTasksCompleted += HandleAllTasksCompleted;
        Debug.Log("[OBJECTIVE HUD] Subscribed to TaskManager events.", this);
    }

    void UnsubscribeFromTaskManager()
    {
        if (taskManager == null)
        {
            return;
        }

        taskManager.OnTaskProgressChanged -= HandleTaskProgressChanged;
        taskManager.OnAllTasksCompleted -= HandleAllTasksCompleted;
    }

    void SubscribeToFinalHuntManager()
    {
        if (finalHuntManager == null)
        {
            return;
        }

        finalHuntManager.OnFinalHuntStarted -= HandleFinalHuntStarted;
        finalHuntManager.OnFinalHuntStarted += HandleFinalHuntStarted;
        Debug.Log("[OBJECTIVE HUD] Subscribed to FinalHuntManager events.", this);

        if (finalHuntManager.IsFinalHuntActive)
        {
            HandleFinalHuntStarted();
        }
        // Ensure Update doesn't treat current state as a transition
        wasFinalHuntActiveLastFrame = finalHuntManager.IsFinalHuntActive;
    }

    void UnsubscribeFromFinalHuntManager()
    {
        if (finalHuntManager == null)
        {
            return;
        }

        finalHuntManager.OnFinalHuntStarted -= HandleFinalHuntStarted;
    }

    void RefreshBindings()
    {
        if (taskManager == null)
        {
            taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
            if (taskManager != null)
            {
                SubscribeToTaskManager();
            }
        }

        if (finalHuntManager == null)
        {
            finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
            if (finalHuntManager != null)
            {
                SubscribeToFinalHuntManager();
            }
        }

        if (gameEndManager == null)
        {
            gameEndManager = GameEndManager.Instance != null ? GameEndManager.Instance : FindAnyObjectByType<GameEndManager>(FindObjectsInactive.Include);
        }

        if (!isInitialized && HasRequiredReferences())
        {
            ConfigureTopRightText(topRightObjectiveText);
            ConfigureBottomRightText(bottomRightFinalHuntText);
            SubscribeToTaskManager();
            SubscribeToFinalHuntManager();
            isInitialized = true;
            RefreshObjectiveText();
            Debug.Log("[OBJECTIVE HUD] Initialized.", this);
        }
    }

    void ConfigureTopRightText(TMP_Text text)
    {
        if (text == null)
            return;

        text.raycastTarget = false;
        text.gameObject.SetActive(true);
    }

    void ConfigureBottomRightText(TMP_Text text)
    {
        if (text == null)
            return;

        text.raycastTarget = false;
        text.gameObject.SetActive(false);

        bottomRightCanvasGroup = text.GetComponent<CanvasGroup>();
        if (bottomRightCanvasGroup == null)
        {
            bottomRightCanvasGroup = text.gameObject.AddComponent<CanvasGroup>();
        }

        bottomRightCanvasGroup.blocksRaycasts = false;
        bottomRightCanvasGroup.interactable = false;
        bottomRightCanvasGroup.alpha = 0f;
        bottomRightBaseColor = text.color;
    }

    void SetTopRightText(string text)
    {
        if (topRightObjectiveText == null)
        {
            return;
        }

        topRightObjectiveText.gameObject.SetActive(true);
        topRightObjectiveText.text = text;
        topRightObjectiveText.raycastTarget = false;
    }

    public void ShowFinalHuntFlicker()
    {
        if (!this || !gameObject)
        {
            Debug.LogWarning("[OBJECTIVE HUD] ShowFinalHuntFlicker called on destroyed object.");
            return;
        }

        // If already visible and flicker active, do nothing
        if (finalHuntFlickerActive && finalHuntHudVisible && finalHuntFlickerRoutine != null)
        {
            return;
        }

        ResolveBottomRightTextIfNeeded();
        ShowBottomRightFinalHuntText(true);
        finalHuntHudVisible = true;

        // Start routine (StartFinalHuntFlickerRoutine will set finalHuntFlickerActive)
        StartFinalHuntFlickerRoutine();

        // Ensure flag is set
        finalHuntFlickerActive = finalHuntFlickerActive || finalHuntFlickerRoutine != null;
    }

    public void HideFinalHuntFlicker()
    {
        // Idempotent hide: do nothing if already hidden and not active
        bool wasActive = finalHuntFlickerActive;
        bool wasVisible = finalHuntHudVisible;
        bool hadRoutine = finalHuntFlickerRoutine != null;

        if (!wasActive && !wasVisible && !hadRoutine)
        {
            return;
        }

        StopFinalHuntFlickerRoutine();
        finalHuntFlickerActive = false;
        finalHuntHudVisible = false;
        ShowBottomRightFinalHuntText(false);

        if (bottomRightCanvasGroup != null)
        {
            bottomRightCanvasGroup.alpha = 0f;
        }

        if (bottomRightFinalHuntText != null)
        {
            Color color = bottomRightFinalHuntText.color;
            color.a = 0f;
            bottomRightFinalHuntText.color = color;
        }

        // Only log when we actually changed state
        if (wasActive || wasVisible || hadRoutine)
        {
            Debug.Log("[OBJECTIVE HUD] Flicker stopped.", this);
        }
    }

    public void ForceShowFinalHuntText()
    {
        ResolveBottomRightTextIfNeeded();
        ForceConfigureBottomRightText();
        ShowBottomRightFinalHuntText(true);

        if (bottomRightCanvasGroup != null)
        {
            bottomRightCanvasGroup.alpha = 1f;
            bottomRightCanvasGroup.blocksRaycasts = false;
            bottomRightCanvasGroup.interactable = false;
        }

        if (bottomRightFinalHuntText != null)
        {
            Color color = bottomRightFinalHuntText.color;
            color.a = 1f;
            bottomRightFinalHuntText.color = color;
        }

        StartFinalHuntFlickerRoutine(true);
        Debug.Log("[OBJECTIVE HUD] ForceShowFinalHuntText.", this);
    }

    void ShowBottomRightFinalHuntText(bool visible)
    {
        bottomRightVisible = visible;

        if (bottomRightFinalHuntText == null)
        {
            return;
        }

        ForceConfigureBottomRightText();
        bottomRightFinalHuntText.gameObject.SetActive(visible);
        bottomRightFinalHuntText.enabled = true;
        bottomRightFinalHuntText.text = "FINAL HUNT";
        bottomRightFinalHuntText.raycastTarget = false;

        if (bottomRightCanvasGroup != null)
        {
            bottomRightCanvasGroup.blocksRaycasts = false;
            bottomRightCanvasGroup.interactable = false;
            bottomRightCanvasGroup.alpha = visible ? 1f : 0f;
        }

        if (bottomRightFinalHuntText != null)
        {
            Color color = bottomRightFinalHuntText.color;
            color.a = visible ? 1f : 0f;
            bottomRightFinalHuntText.color = color;
        }
    }

    void StartFinalHuntFlickerRoutine(bool forceRestart = false)
    {
        // Safety checks before starting coroutine
        if (!this || !gameObject || !isActiveAndEnabled)
        {
            Debug.LogWarning("[OBJECTIVE HUD] Cannot start flicker: object is invalid or disabled.");
            finalHuntFlickerRoutine = null;
            return;
        }

        if (bottomRightFinalHuntText == null)
        {
            Debug.LogWarning("[OBJECTIVE HUD] Cannot start flicker: BottomRightFinalHuntText is null.", this);
            return;
        }

        // If already active and not forcing a restart, do nothing
        if (finalHuntFlickerActive && finalHuntFlickerRoutine != null && !forceRestart)
        {
            return;
        }

        if (finalHuntFlickerRoutine != null && forceRestart)
        {
            try { StopCoroutine(finalHuntFlickerRoutine); } catch { }
            finalHuntFlickerRoutine = null;
        }

        // Mark active before starting so Update/Refresh won't restart it
        finalHuntFlickerActive = true;
        finalHuntFlickerRoutine = StartCoroutine(FinalHuntFlickerRoutine());
        Debug.Log("[OBJECTIVE HUD] Flicker started.", this);
    }

    void StopFinalHuntFlickerRoutine()
    {
        if (finalHuntFlickerRoutine == null)
        {
            return;
        }

        try
        {
            if (this && gameObject)
            {
                StopCoroutine(finalHuntFlickerRoutine);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[OBJECTIVE HUD] Error stopping flicker coroutine: {ex.Message}", this);
        }

        finalHuntFlickerRoutine = null;
        finalHuntFlickerActive = false;
    }

    IEnumerator FinalHuntFlickerRoutine()
    {
        while (this && isFinalHuntActive && bottomRightFinalHuntText != null)
        {
            if (!this)
            {
                finalHuntFlickerRoutine = null;
                yield break;
            }

            if (bottomRightFinalHuntText == null)
            {
                finalHuntFlickerRoutine = null;
                yield break;
            }

            if (bottomRightFinalHuntText.gameObject.activeSelf)
            {
                bottomRightFinalHuntText.enabled = true;
                bottomRightFinalHuntText.raycastTarget = false;

                float alpha = flickerFinalHuntText
                    ? Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * flickerSpeed))
                    : 1f;

                if (bottomRightCanvasGroup == null)
                {
                    bottomRightCanvasGroup = bottomRightFinalHuntText.GetComponent<CanvasGroup>();
                    if (bottomRightCanvasGroup == null)
                    {
                        bottomRightCanvasGroup = bottomRightFinalHuntText.gameObject.AddComponent<CanvasGroup>();
                    }
                }

                bottomRightCanvasGroup.alpha = alpha;
                bottomRightCanvasGroup.blocksRaycasts = false;
                bottomRightCanvasGroup.interactable = false;

                Color color = bottomRightFinalHuntText.color;
                color.a = 1f;
                bottomRightFinalHuntText.color = color;
            }

            yield return null;
        }

        finalHuntFlickerRoutine = null;
        finalHuntFlickerActive = false;
    }

    void ResolveBottomRightTextIfNeeded()
    {
        if (bottomRightFinalHuntText != null)
        {
            return;
        }

        GameObject foundObject = GameObject.Find("BottomRightFinalHuntText");
        if (foundObject == null)
        {
            return;
        }

        bottomRightFinalHuntText = foundObject.GetComponent<TMP_Text>();
        if (bottomRightFinalHuntText == null)
        {
            return;
        }

        ConfigureBottomRightText(bottomRightFinalHuntText);
    }

    void ForceConfigureBottomRightText()
    {
        if (bottomRightFinalHuntText == null)
        {
            return;
        }

        RectTransform rectTransform = bottomRightFinalHuntText.rectTransform;
        rectTransform.anchorMin = new Vector2(1f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 0f);
        rectTransform.pivot = new Vector2(1f, 0f);
        rectTransform.anchoredPosition = new Vector2(-40f, 120f);
        rectTransform.sizeDelta = new Vector2(300f, 80f);

        bottomRightFinalHuntText.enabled = true;
        bottomRightFinalHuntText.gameObject.SetActive(true);
        bottomRightFinalHuntText.text = "FINAL HUNT";
        bottomRightFinalHuntText.raycastTarget = false;

        Color color = bottomRightFinalHuntText.color;
        color.a = 1f;
        bottomRightFinalHuntText.color = color;

        bottomRightCanvasGroup = bottomRightFinalHuntText.GetComponent<CanvasGroup>();
        if (bottomRightCanvasGroup == null)
        {
            bottomRightCanvasGroup = bottomRightFinalHuntText.gameObject.AddComponent<CanvasGroup>();
        }

        bottomRightCanvasGroup.alpha = 1f;
        bottomRightCanvasGroup.blocksRaycasts = false;
        bottomRightCanvasGroup.interactable = false;
    }

    void HideAllObjectiveUi()
    {
        if (topRightObjectiveText != null)
        {
            topRightObjectiveText.gameObject.SetActive(false);
        }

        ShowBottomRightFinalHuntText(false);
    }
}