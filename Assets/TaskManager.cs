using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    [Header("Task Totals")]
    [SerializeField] bool useV4TaskTotal = true;
    [SerializeField] int v4TotalTasks = 8;

    public int totalTasks => TotalTasks;
    public int completedTasks => CompletedTasks;
    public float taskProgress => TotalTasks <= 0 ? 0f : (float)CompletedTasks / TotalTasks;
    public int TotalTasks => useV4TaskTotal ? Mathf.Max(0, v4TotalTasks) : registeredTasksById.Count;
    public int CompletedTasks => completedTaskIds.Count;
    public int RemainingTasks => Mathf.Max(0, TotalTasks - CompletedTasks);
    public bool AreAllTasksCompleted => TotalTasks > 0 && CompletedTasks >= TotalTasks;
    public int ExitScanUnlockThreshold => useV4TaskTotal ? Mathf.Max(0, v4TotalTasks - 1) : Mathf.Max(0, TotalTasks - 1);
    public bool IsExitScanUnlocked => CompletedTasks >= ExitScanUnlockThreshold;

    public event Action OnAllTasksCompleted;
    public event Action<float, int, int> OnTaskProgressChanged;

    [Header("UI")]
    [SerializeField] Slider progressBar;
    [SerializeField] TMP_Text progressText;

    [Header("Debug")]
    [SerializeField] bool enableTaskDebugHotkeys = true;
    [SerializeField] bool allowDebugResetTasks;

    readonly Dictionary<string, TaskInteractable> registeredTasksById = new Dictionary<string, TaskInteractable>();
    readonly List<string> registrationOrder = new List<string>();
    readonly HashSet<string> completedTaskIds = new HashSet<string>();

    bool hasRaisedAllTasksCompleted;
    bool hasWarnedNoTasks;
    bool hasWarnedExpectedV4TaskCount;
    bool hasCompletedV4TaskCountCheck;
    bool hasTracedExitScanUnlocked;
    float lastProgressValue = -1f;
    int lastLoggedCompleted = -1;
    int lastLoggedTotal = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("[TASK DEBUG] TaskManager started.", this);
        RefreshUi();
    }

    void Start()
    {
        RefreshProgressAndEvents(false);

        if (useV4TaskTotal)
        {
            StartCoroutine(ValidateV4TaskCountAfterStart());
        }
        else
        {
            hasCompletedV4TaskCountCheck = true;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        HandleDebugHotkeys();
    }

    public void RegisterTask(TaskInteractable task)
    {
        if (task == null)
        {
            return;
        }

        string taskId = task.TaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            taskId = task.gameObject.name;
        }

        if (registeredTasksById.TryGetValue(taskId, out TaskInteractable existingTask))
        {
            if (existingTask == task)
            {
                if (task.isCompleted)
                {
                    completedTaskIds.Add(taskId);
                }

                return;
            }

            Debug.LogWarning($"[TASK DEBUG] Duplicate taskId ignored: {taskId}", task);
            return;
        }

        registeredTasksById.Add(taskId, task);
        registrationOrder.Add(taskId);

        if (task.isCompleted)
        {
            completedTaskIds.Add(taskId);
        }

        Debug.Log($"[TASK DEBUG] Registered task: {taskId} {task.TaskDisplayName}", task);
        RefreshProgressAndEvents();
    }

    public bool CanCompleteTask(TaskInteractable task, bool debugForce, out string reason)
    {
        if (task == null)
        {
            reason = "Task missing.";
            return false;
        }

        string taskId = task.TaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            reason = "Task id missing.";
            return false;
        }

        if (!registeredTasksById.TryGetValue(taskId, out TaskInteractable registeredTask) || registeredTask != task)
        {
            reason = $"Task not registered: {taskId}";
            return false;
        }

        if (completedTaskIds.Contains(taskId))
        {
            reason = $"Task already completed: {taskId}";
            return false;
        }

        if (!debugForce && task.IsExitScanTask && !IsExitScanUnlocked)
        {
            reason = "Exit Scan locked until all wire tasks are complete.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CompleteTask(TaskInteractable task, bool debugForce = false)
    {
        if (!CanCompleteTask(task, debugForce, out string reason))
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (reason == "Exit Scan locked until all wire tasks are complete.")
                {
                    Debug.Log($"[TASK DEBUG] {reason}", task);
                }
                else
                {
                    Debug.LogWarning($"[TASK DEBUG] {reason}", task);
                }
            }

            return false;
        }

        string taskId = task.TaskId;
        completedTaskIds.Add(taskId);

        Debug.Log($"[TASK DEBUG] Task completed: {taskId} {task.TaskDisplayName} completed={CompletedTasks}/{TotalTasks}", task);

        if (task.IsExitScanTask)
        {
            AgentTracePanel.Trace("OBJECTIVE", "Exit Scan completed. Escape route opened.");
        }
        else
        {
            AgentTracePanel.Trace("OBJECTIVE", $"{task.TaskDisplayName} completed.");
        }

        RefreshProgressAndEvents();

        if (!hasTracedExitScanUnlocked && IsExitScanUnlocked)
        {
            hasTracedExitScanUnlocked = true;
            AgentTracePanel.Trace("OBJECTIVE", "Exit Scan unlocked.");
        }

        if (AreAllTasksCompleted && !hasRaisedAllTasksCompleted)
        {
            hasRaisedAllTasksCompleted = true;
            Debug.Log("[TASK DEBUG] All tasks completed.", this);
            OnAllTasksCompleted?.Invoke();
        }

        return true;
    }

    public void ResetAllTasksForDebug()
    {
        if (!allowDebugResetTasks)
        {
            Debug.LogWarning("[TASK DEBUG] Debug reset is disabled.", this);
            return;
        }

        TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Include);
        if (tasks != null)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskInteractable task = tasks[i];
                if (task != null)
                {
                    task.ResetTaskForDebug();
                }
            }
        }

        completedTaskIds.Clear();
        hasRaisedAllTasksCompleted = false;
        hasTracedExitScanUnlocked = false;
        RefreshProgressAndEvents();
    }

    public void CompleteAllTasksForDebug()
    {
        TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Include);
        if (tasks == null)
        {
            return;
        }

        for (int i = 0; i < tasks.Length; i++)
        {
            TaskInteractable task = tasks[i];
            if (task != null)
            {
                task.CompleteTaskForDebug();
            }
        }
    }

    void RefreshProgressAndEvents(bool allowEventInvoke = true)
    {
        RefreshUi();

        if (allowEventInvoke)
        {
            OnTaskProgressChanged?.Invoke(taskProgress, completedTasks, totalTasks);
        }

        if (TotalTasks <= 0)
        {
            if (!hasWarnedNoTasks)
            {
                hasWarnedNoTasks = true;
                Debug.LogWarning("[TASK DEBUG] No tasks registered.", this);
            }
        }
        else
        {
            hasWarnedNoTasks = false;
        }

        if (TotalTasks > 0 && CompletedTasks < TotalTasks)
        {
            hasRaisedAllTasksCompleted = false;
        }

        if (hasCompletedV4TaskCountCheck)
        {
            WarnIfExpectedV4TaskCountMissing();
        }
    }

    void RefreshUi()
    {
        float progress = taskProgress;

        if (progressBar != null)
        {
            progressBar.minValue = 0f;
            progressBar.maxValue = 1f;
            progressBar.value = progress;
        }

        if (progressText != null)
        {
            progressText.text = $"Tasks: {CompletedTasks}/{TotalTasks}";
        }

        if (Mathf.Abs(progress - lastProgressValue) > 0.0001f || CompletedTasks != lastLoggedCompleted || TotalTasks != lastLoggedTotal)
        {
            lastProgressValue = progress;
            lastLoggedCompleted = CompletedTasks;
            lastLoggedTotal = TotalTasks;
        }
    }

    void HandleDebugHotkeys()
    {
        if (!enableTaskDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            PrintTaskSummary();
        }

        if (Input.GetKeyDown(KeyCode.U))
        {
            CompleteAllTasksForDebug();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetAllTasksForDebug();
        }
    }

    public void PrintTaskSummary()
    {
        Debug.Log($"[TASK DEBUG] Summary completed={CompletedTasks}/{TotalTasks} remaining={RemainingTasks} exitScan={(IsExitScanUnlocked ? "unlocked" : "locked")}", this);
        Debug.Log($"[TASK DEBUG] Registered tasks={registeredTasksById.Count} expectedV4={v4TotalTasks} useV4={useV4TaskTotal}", this);

        if (registrationOrder.Count == 0)
        {
            Debug.Log("[TASK DEBUG] No task objects found.", this);
            return;
        }

        for (int i = 0; i < registrationOrder.Count; i++)
        {
            string taskId = registrationOrder[i];
            if (!registeredTasksById.TryGetValue(taskId, out TaskInteractable task) || task == null)
            {
                continue;
            }

            bool exitScanLocked = task.IsExitScanTask && !IsExitScanUnlocked;
            Debug.Log($"[TASK DEBUG] Task: {taskId} {task.TaskDisplayName} completed={completedTaskIds.Contains(taskId)} exitScanLocked={exitScanLocked}", task);
        }
    }

    public void RecalculateProgress()
    {
        RefreshProgressAndEvents();
    }

    IEnumerator ValidateV4TaskCountAfterStart()
    {
        yield return null;
        hasCompletedV4TaskCountCheck = true;
        WarnIfExpectedV4TaskCountMissing();
    }

    void WarnIfExpectedV4TaskCountMissing()
    {
        if (!useV4TaskTotal || hasWarnedExpectedV4TaskCount)
        {
            return;
        }

        if (registeredTasksById.Count < v4TotalTasks)
        {
            hasWarnedExpectedV4TaskCount = true;
            Debug.LogWarning($"[TASK DEBUG] Expected 8 V4 tasks but found {registeredTasksById.Count}.", this);
        }
    }
}
