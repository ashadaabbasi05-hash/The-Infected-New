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
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.OnAllTasksCompleted -= HandleAllTasksCompleted;
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
        if (TaskManager.Instance == null)
        {
            return;
        }

        TaskManager.Instance.OnAllTasksCompleted -= HandleAllTasksCompleted;
        TaskManager.Instance.OnAllTasksCompleted += HandleAllTasksCompleted;
    }

    void TryUnlockFromCurrentProgress()
    {
        if (TaskManager.Instance == null)
        {
            return;
        }

        if (TaskManager.Instance.totalTasks > 0 && TaskManager.Instance.completedTasks >= TaskManager.Instance.totalTasks)
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

        Debug.Log("Escape door unlocked.", this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerIdentity identity = other.GetComponent<PlayerIdentity>();
        if (identity == null || !identity.isLocalPlayer || !identity.isAlive)
        {
            return;
        }

        if (!isUnlocked)
        {
            Debug.Log("Door locked. Complete all tasks.", this);
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

        Debug.Log("Humans escaped. You win.", this);
    }
}
