using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class TaskInteractable : MonoBehaviour
{
    [Header("Task")]
    [SerializeField] string taskName = "Fix Wiring";
    [SerializeField] bool completed;

    [Header("UI")]
    [SerializeField] TMP_Text interactionPrompt;

    [Header("Debug")]
    [SerializeField] bool enableDebugTaskHotkeys = true;

    [SerializeField] Color completedColor = Color.green;

    SpriteRenderer spriteRenderer;
    Collider2D taskCollider;
    PlayerIdentity localPlayerInRange;
    Color originalColor;

    public string TaskName => string.IsNullOrWhiteSpace(taskName) ? gameObject.name : taskName;
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
            Debug.LogWarning($"[TASK DEBUG] {TaskName} missing Collider2D.", this);
        }
        else if (!taskCollider.isTrigger)
        {
            Debug.LogWarning($"[TASK DEBUG] {TaskName} Collider2D is not trigger.", this);
            taskCollider.isTrigger = true;
        }

        if (spriteRenderer == null)
        {
            Debug.LogWarning($"[TASK DEBUG] {TaskName} missing SpriteRenderer.", this);
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
                Debug.Log($"[TASK DEBUG] Cannot use {TaskName}: {invalidReason}", this);
            }

            return;
        }

        localPlayerInRange = identity;
        SetPromptVisible(true);
        Debug.Log($"[TASK DEBUG] Player entered task: {TaskName}", this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        PlayerIdentity identity = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (identity == null || localPlayerInRange != identity)
        {
            return;
        }

        localPlayerInRange = null;
        SetPromptVisible(false);
        Debug.Log($"[TASK DEBUG] Player exited task: {TaskName}", this);
    }

    public void Interact()
    {
        AttemptCompletion(false);
    }

    public void CompleteTaskForDebug()
    {
        AttemptCompletion(true);
    }

    public void ResetTaskForDebug()
    {
        completed = false;
        UpdateVisualState();
        SetPromptVisible(localPlayerInRange != null);
    }

    void AttemptCompletion(bool debugForce)
    {
        if (completed)
        {
            Debug.Log($"[TASK DEBUG] Task already completed: {TaskName}", this);
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
                Debug.Log($"[TASK DEBUG] Cannot complete {TaskName}: no player in range.", this);
                return;
            }

            if (!IsValidTaskPlayer(localPlayerInRange, out string playerReason))
            {
                Debug.Log($"[TASK DEBUG] Cannot complete {TaskName}: {playerReason}", this);
                return;
            }

            if (FinalHuntIsActive())
            {
                Debug.Log($"[TASK DEBUG] Final Hunt task allowed for {localPlayerInRange.playerName}.", this);
            }
        }

        completed = true;
        localPlayerInRange = null;

        UpdateVisualState();
        SetPromptVisible(false);
        Debug.Log($"[TASK DEBUG] Task completed: {TaskName}", this);

        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.CompleteTask(this);
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
        TaskManager taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include);
        if (taskManager == null)
        {
            Debug.LogWarning($"[TASK DEBUG] No TaskManager found for {TaskName}", this);
            return;
        }

        taskManager.RegisterTask(this);
    }

    void SetPromptVisible(bool visible)
    {
        if (interactionPrompt == null)
        {
            return;
        }

        interactionPrompt.gameObject.SetActive(visible && !isCompleted);
        if (visible && !isCompleted)
        {
            interactionPrompt.text = $"Press E: {TaskName}";
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
