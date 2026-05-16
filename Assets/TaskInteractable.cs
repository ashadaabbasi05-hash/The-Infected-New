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
    [SerializeField] TMP_Text interactionPromptText;

    SpriteRenderer spriteRenderer;
    bool localPlayerInRange;

    public string TaskName => taskName;
    public bool isCompleted => completed;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        Collider2D trigger = GetComponent<Collider2D>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }

        UpdateVisualState();
        SetPromptVisible(false);
    }

    void Start()
    {
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.RegisterTask(this);
        }
    }

    void Update()
    {
        if (completed || !localPlayerInRange)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            CompleteTask();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (completed)
        {
            return;
        }

        PlayerIdentity identity = other.GetComponent<PlayerIdentity>();
        if (identity == null || !identity.isLocalPlayer || !identity.isAlive)
        {
            return;
        }

        localPlayerInRange = true;
        if (interactionPromptText != null)
        {
            interactionPromptText.text = $"Press E: {taskName}";
        }
        SetPromptVisible(true);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        PlayerIdentity identity = other.GetComponent<PlayerIdentity>();
        if (identity == null || !identity.isLocalPlayer)
        {
            return;
        }

        localPlayerInRange = false;
        SetPromptVisible(false);
    }

    // Mobile UI can call this directly from a button.
    public void CompleteTask()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        localPlayerInRange = false;

        UpdateVisualState();
        SetPromptVisible(false);

        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.CompleteTask(this);
        }
    }

    void SetPromptVisible(bool visible)
    {
        if (interactionPromptText == null)
        {
            return;
        }

        interactionPromptText.gameObject.SetActive(visible && !completed);
    }

    void UpdateVisualState()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (completed)
        {
            spriteRenderer.color = Color.green;
        }
    }
}
