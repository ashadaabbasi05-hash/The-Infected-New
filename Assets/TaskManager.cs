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

    public event Action OnAllTasksCompleted;
    public event Action<float, int, int> OnTaskProgressChanged;

    [Header("UI")]
    [SerializeField] Slider progressBar;
    [SerializeField] TMP_Text progressText;

    readonly HashSet<TaskInteractable> registeredTasks = new HashSet<TaskInteractable>();
    readonly HashSet<TaskInteractable> completedTaskSet = new HashSet<TaskInteractable>();

    bool hasRaisedAllTasksCompleted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RefreshUi();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RegisterTask(TaskInteractable task)
    {
        if (task == null)
        {
            return;
        }

        if (!registeredTasks.Add(task))
        {
            return;
        }

        if (task.isCompleted)
        {
            completedTaskSet.Add(task);
        }

        RefreshProgressAndEvents();
    }

    public void CompleteTask(TaskInteractable task)
    {
        if (task == null)
        {
            return;
        }

        registeredTasks.Add(task);

        if (!completedTaskSet.Add(task))
        {
            return;
        }

        RefreshProgressAndEvents();
    }

    void RefreshProgressAndEvents()
    {
        RefreshUi();
        OnTaskProgressChanged?.Invoke(taskProgress, completedTasks, totalTasks);

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }

        if (totalTasks > 0 && completedTasks >= totalTasks)
        {
            if (!hasRaisedAllTasksCompleted)
            {
                hasRaisedAllTasksCompleted = true;
                OnAllTasksCompleted?.Invoke();
            }
        }
        else
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
    }
}
