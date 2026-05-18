using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AgentTracePanel : MonoBehaviour
{
    public static AgentTracePanel Instance { get; private set; }

    [Header("UI")]
    [SerializeField] GameObject panelRoot;
    [SerializeField] TMP_Text traceText;
    [SerializeField] TMP_Text titleText;
    [SerializeField, Min(1)] int maxEntries = 10;
    [SerializeField] bool showTimestamps = true;
    [SerializeField] bool autoShowOnTrace = true;
    [SerializeField] bool startVisible = true;
    [SerializeField] bool enableDebugHotkeys = true;

    readonly Queue<string> traceEntries = new Queue<string>();
    bool isVisible;
    CanvasGroup canvasGroup;

    void Awake()
    {
        // Keep the first valid instance and avoid destroying duplicates unless there is no safe choice.
        if (Instance != null && Instance != this)
        {
            if (IsValidInstance(Instance))
            {
                Debug.LogWarning("[AGENT TRACE] Duplicate AgentTracePanel detected. Disabling this instance.", this);
                enabled = false;
                return;
            }

            Instance = null;
        }

        Instance = this;

        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        canvasGroup = panelRoot != null ? panelRoot.GetComponent<CanvasGroup>() : null;
        if (canvasGroup == null && panelRoot != null)
        {
            canvasGroup = panelRoot.AddComponent<CanvasGroup>();
        }

        // The panel is informational only, so it must not block touches or joystick input.
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (titleText != null)
        {
            titleText.text = "ANTIGRAVITY AGENT TRACE";
            titleText.raycastTarget = false;
        }

        if (traceText != null)
        {
            traceText.raycastTarget = false;
        }

        isVisible = startVisible;
        ApplyVisibility(isVisible);
        RefreshText();
    }

    void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
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
        if (!enableDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            TogglePanel();
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            AddTrace("DEMO", "Sample agent trace entry.");
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            ClearTrace();
        }
    }

    public void AddTrace(string message)
    {
        AddTraceInternal(null, message);
    }

    public void AddTrace(string category, string message)
    {
        AddTraceInternal(category, message);
    }

    public void ClearTrace()
    {
        traceEntries.Clear();
        RefreshText();
    }

    public void ShowPanel()
    {
        SetVisible(true);
    }

    public void HidePanel()
    {
        SetVisible(false);
    }

    public void TogglePanel()
    {
        SetVisible(!isVisible);
    }

    public static void Trace(string message)
    {
        if (Instance != null)
        {
            Instance.AddTrace(message);
            return;
        }

        Debug.Log($"[AGENT TRACE] {message}");
    }

    public static void Trace(string category, string message)
    {
        if (Instance != null)
        {
            Instance.AddTrace(category, message);
            return;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            Debug.Log($"[AGENT TRACE] {message}");
        }
        else
        {
            Debug.Log($"[AGENT TRACE] [{category}] {message}");
        }
    }

    void AddTraceInternal(string category, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string entry = FormatEntry(category, message);
        traceEntries.Enqueue(entry);

        while (traceEntries.Count > maxEntries)
        {
            traceEntries.Dequeue();
        }

        Debug.Log($"[AGENT TRACE] {entry}", this);

        if (autoShowOnTrace)
        {
            ShowPanel();
        }

        RefreshText();
    }

    string FormatEntry(string category, string message)
    {
        string prefix = showTimestamps ? $"[{Time.unscaledTime:0.0}s] " : string.Empty;

        if (string.IsNullOrWhiteSpace(category))
        {
            return $"{prefix}{message}";
        }

        return $"{prefix}[{category}] {message}";
    }

    void RefreshText()
    {
        if (traceText == null)
        {
            return;
        }

        if (traceEntries.Count == 0)
        {
            traceText.text = "Waiting for agent events...";
            return;
        }

        traceText.text = string.Join("\n", traceEntries);
    }

    void SetVisible(bool visible)
    {
        isVisible = visible;
        ApplyVisibility(visible);
    }

    void ApplyVisibility(bool visible)
    {
        if (panelRoot != null && panelRoot != gameObject)
        {
            panelRoot.SetActive(visible);
        }
        else if (panelRoot != null)
        {
            // If the controller sits on the same object as the root, keep the script alive.
            // The panel is hidden by alpha instead of deactivating the whole GameObject.
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    static bool IsValidInstance(AgentTracePanel instance)
    {
        try
        {
            return instance != null && instance.gameObject != null;
        }
        catch
        {
            return false;
        }
    }
}