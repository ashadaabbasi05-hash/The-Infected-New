using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    public int totalTasks => registeredTasks.Count;
    public int completedTasks => completedTaskSet.Count;
    public float taskProgress => totalTasks <= 0 ? 0f : (float)completedTasks / totalTasks;
    public int TotalTasks => totalTasks;
    public int CompletedTasks => completedTasks;
    public int RemainingTasks => Mathf.Max(0, totalTasks - completedTasks);

    public event Action OnAllTasksCompleted;
    public event Action<float, int, int> OnTaskProgressChanged;

    [Header("UI")]
    [SerializeField] Slider progressBar;
    [SerializeField] TMP_Text progressText;

    [Header("Debug")]
    [SerializeField] bool enableTaskDebugHotkeys = true;
    [SerializeField] bool allowDebugResetTasks;

    readonly HashSet<TaskInteractable> registeredTasks = new HashSet<TaskInteractable>();
    readonly HashSet<TaskInteractable> completedTaskSet = new HashSet<TaskInteractable>();

    bool hasRaisedAllTasksCompleted;
    bool hasWarnedNoTasks;
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

        if (!registeredTasks.Add(task))
        {
            if (task.isCompleted)
            {
                completedTaskSet.Add(task);
            }

            return;
        }

        if (task.isCompleted)
        {
            completedTaskSet.Add(task);
        }

        Debug.Log($"[TASK DEBUG] Registered task: {task.TaskName}", task);
        RefreshProgressAndEvents();
    }

    public void CompleteTask(TaskInteractable task)
    {
        if (task == null)
        {
            return;
        }

        if (!registeredTasks.Contains(task))
        {
            registeredTasks.Add(task);
            Debug.Log($"[TASK DEBUG] Registered task: {task.TaskName}", task);
        }

        if (!completedTaskSet.Add(task))
        {
            return;
        }

        RefreshProgressAndEvents();
        Debug.Log($"[TASK DEBUG] Completed task: {task.TaskName} completed={completedTasks}/{totalTasks}", task);

        if (totalTasks > 0 && completedTasks >= totalTasks && !hasRaisedAllTasksCompleted)
        {
            hasRaisedAllTasksCompleted = true;
            Debug.Log("[TASK DEBUG] All tasks completed.", this);
            OnAllTasksCompleted?.Invoke();
        }
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

        completedTaskSet.Clear();
        hasRaisedAllTasksCompleted = false;
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

        if (totalTasks <= 0)
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

        if (totalTasks > 0 && completedTasks < totalTasks)
        {
            hasRaisedAllTasksCompleted = false;
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
            progressText.text = $"Tasks: {completedTasks}/{totalTasks}";
        }

        if (Mathf.Abs(progress - lastProgressValue) > 0.0001f || completedTasks != lastLoggedCompleted || totalTasks != lastLoggedTotal)
        {
            lastProgressValue = progress;
            lastLoggedCompleted = completedTasks;
            lastLoggedTotal = totalTasks;
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
        TaskInteractable[] tasks = FindObjectsByType<TaskInteractable>(FindObjectsInactive.Include);
        Debug.Log($"[TASK DEBUG] Summary totalTasks={totalTasks} completedTasks={completedTasks} progress01={taskProgress:0.000}", this);

        if (tasks == null || tasks.Length == 0)
        {
            Debug.Log("[TASK DEBUG] No task objects found.", this);
            return;
        }

        for (int i = 0; i < tasks.Length; i++)
        {
            TaskInteractable task = tasks[i];
            if (task == null)
            {
                continue;
            }

            Debug.Log($"[TASK DEBUG] Task: {task.TaskName} completed={task.isCompleted}", task);
        }
    }

    public void RecalculateProgress()
    {
        RefreshProgressAndEvents();
    }
}
