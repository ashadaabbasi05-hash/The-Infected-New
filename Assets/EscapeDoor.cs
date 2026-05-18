using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class EscapeDoor : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_Text winMessageText;

    [Header("References")]
    [SerializeField] GameEndManager gameEndManager;

    [Header("Visual")]
    [SerializeField] Color unlockedColor = Color.cyan;

    bool isUnlocked;
    SpriteRenderer spriteRenderer;
    TaskManager subscribedTaskManager;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        TryResolveGameEndManager();

        Collider2D trigger = GetComponent<Collider2D>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }

        isUnlocked = false;

        if (winMessageText != null)
        {
            winMessageText.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        TrySubscribeToTaskManager();
    }

    void OnDisable()
    {
        if (subscribedTaskManager != null)
        {
            subscribedTaskManager.OnAllTasksCompleted -= HandleAllTasksCompleted;
            subscribedTaskManager = null;
        }
    }

    void Start()
    {
        TryResolveGameEndManager();
        TrySubscribeToTaskManager();
        TryUnlockFromCurrentProgress();
    }

    void TryResolveGameEndManager()
    {
        if (gameEndManager != null)
        {
            return;
        }

        gameEndManager = FindAnyObjectByType<GameEndManager>(FindObjectsInactive.Include);
    }

    void TrySubscribeToTaskManager()
    {
        TaskManager taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
        if (taskManager == null)
        {
            return;
        }

        if (subscribedTaskManager == taskManager)
        {
            return;
        }

        if (subscribedTaskManager != null)
        {
            subscribedTaskManager.OnAllTasksCompleted -= HandleAllTasksCompleted;
        }

        subscribedTaskManager = taskManager;
        subscribedTaskManager.OnAllTasksCompleted -= HandleAllTasksCompleted;
        subscribedTaskManager.OnAllTasksCompleted += HandleAllTasksCompleted;
    }

    void TryUnlockFromCurrentProgress()
    {
        if (subscribedTaskManager == null)
        {
            return;
        }

        if (subscribedTaskManager.totalTasks > 0 && subscribedTaskManager.completedTasks >= subscribedTaskManager.totalTasks)
        {
            HandleAllTasksCompleted();
        }
    }

    void HandleAllTasksCompleted()
    {
        if (isUnlocked)
        {
            return;
        }

        isUnlocked = true;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = unlockedColor;
        }

        AgentTracePanel.Trace("OBJECTIVE", "Escape door opened.");
        ObjectiveHUDController.Instance?.HandleEscapeDoorUnlocked();
        Debug.Log("[TASK DEBUG] Escape door unlocked.", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (identity == null || !identity.isLocalPlayer || !identity.isAlive)
        {
            return;
        }

        if (identity.isInfected || identity.isAIControlled)
        {
            return;
        }

        if (!isUnlocked)
        {
            Debug.Log("[TASK DEBUG] Door locked. Complete all tasks.", this);
            ObjectiveHUDController.Instance?.RefreshObjectiveText();
            return;
        }

        if (winMessageText != null)
        {
            winMessageText.gameObject.SetActive(true);
            winMessageText.text = "Humans escaped. You win.";
        }

        TryResolveGameEndManager();

        if (gameEndManager != null)
        {
            gameEndManager.TriggerHumanWin();
        }

        AgentTracePanel.Trace("OBJECTIVE", "Humans escaped.");
        ObjectiveHUDController.Instance?.HandleHumanWinTriggered();

        Debug.Log("Humans escaped. You win.", this);
    }
}
