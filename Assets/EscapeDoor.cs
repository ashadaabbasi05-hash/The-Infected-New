using System.Collections.Generic;
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
    bool escapeTriggered;
    readonly HashSet<Collider2D> triggeredColliders = new HashSet<Collider2D>();
    SpriteRenderer spriteRenderer;
    TaskManager subscribedTaskManager;
    public static EscapeDoor CurrentLocalDoor { get; private set; }

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

        if (subscribedTaskManager.AreAllTasksCompleted)
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
        if (escapeTriggered)
        {
            Debug.Log("[TASK DEBUG] Escape door ignored: win already triggered.", this);
            return;
        }

        if (other != null && triggeredColliders.Contains(other))
        {
            return;
        }

        if (other != null)
        {
            triggeredColliders.Add(other);
        }

        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (identity == null || !identity.isLocalPlayer || !identity.isAlive)
        {
            return;
        }

        CurrentLocalDoor = this;

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

        escapeTriggered = true;

        if (gameEndManager != null)
        {
            gameEndManager.TriggerHumanWin();
        }

        AgentTracePanel.Trace("OBJECTIVE", "Humans escaped.");
        ObjectiveHUDController.Instance?.HandleHumanWinTriggered();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (identity == null)
        {
            return;
        }

        if (CurrentLocalDoor == this && identity.isLocalPlayer)
        {
            CurrentLocalDoor = null;
        }
    }

    public void Interact()
    {
        if (escapeTriggered)
        {
            Debug.Log("[TASK DEBUG] Escape door ignored: win already triggered.", this);
            return;
        }

        if (!isUnlocked)
        {
            Debug.Log("[TASK DEBUG] Door locked. Complete all tasks.", this);
            ObjectiveHUDController.Instance?.RefreshObjectiveText();
            return;
        }

        TryResolveGameEndManager();

        if (gameEndManager != null)
        {
            gameEndManager.TriggerHumanWin();
        }

        AgentTracePanel.Trace("OBJECTIVE", "Humans escaped.");
        ObjectiveHUDController.Instance?.HandleHumanWinTriggered();
        escapeTriggered = true;
    }

    public void ResetDoorForDemo()
    {
        escapeTriggered = false;
        triggeredColliders.Clear();
    }

    public static bool TryInteractCurrentDoor()
    {
        if (CurrentLocalDoor == null)
        {
            return false;
        }

        CurrentLocalDoor.Interact();
        return true;
    }
}
