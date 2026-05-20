using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WireTaskMinigame : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] GameObject panelRoot;
    [SerializeField] RectTransform wirePanelRect;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text instructionText;
    [SerializeField] Button closeButton;
    [SerializeField] Image completionFlashImage;

    [Header("Nodes")]
    [SerializeField] WireNode[] leftNodes;
    [SerializeField] WireNode[] rightNodes;

    [Header("Lines")]
    [SerializeField] LineRenderer linePrefab;
    [SerializeField] RectTransform lineContainer;

    [Header("Options")]
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool allowCloseWithoutComplete = true;

    TaskInteractable currentTask;
    readonly Dictionary<WireColor, WireNode> connectedMatches = new Dictionary<WireColor, WireNode>();
    readonly List<GameObject> activeLines = new List<GameObject>();

    bool isOpen;
    bool isCompleted;

    public bool IsOpen => isOpen && panelRoot != null && panelRoot.activeInHierarchy;

    public bool IsOpeningOrOpen => isOpen || (panelRoot != null && panelRoot.activeInHierarchy);

    public bool IsPanelVisibleInHierarchy =>
        panelRoot != null && panelRoot.activeInHierarchy;

    void Awake()
    {
        EnsurePanelReferences();

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        isOpen = false;
    }

    void OnValidate()
    {
        EnsurePanelReferences();
    }

    void EnsurePanelReferences()
    {
        // If panelRoot not assigned, try to find WireTaskPanel separately
        if (panelRoot == null)
        {
            GameObject found = GameObject.Find("WireTaskPanel");
            if (found != null)
            {
                panelRoot = found;
            }
        }

        if (wirePanelRect == null && panelRoot != null)
        {
            wirePanelRect = panelRoot.GetComponent<RectTransform>();
        }

        if (lineContainer == null && panelRoot != null)
        {
            Transform lineContainerTransform = panelRoot.transform.Find("LineContainer");
            if (lineContainerTransform != null)
            {
                lineContainer = lineContainerTransform.GetComponent<RectTransform>();
            }
        }
    }

    public void Open(TaskInteractable task)
    {
        // Early guard: task must be valid
        if (task == null)
        {
            Debug.LogWarning("[WIRE TASK] Open called with null task.", this);
            return;
        }

        // Ensure references
        EnsurePanelReferences();

        // Early guard: panelRoot must be found
        if (panelRoot == null)
        {
            Debug.LogWarning("[WIRE TASK] panelRoot not found. Cannot open minigame.", this);
            return;
        }

        // Early guard: panel must not already be open
        if (IsOpen)
        {
            if (debugLogs)
            {
                Debug.Log("[WIRE TASK] Open ignored because panel is already open.", this);
            }
            return;
        }

        // Check parent hierarchy before activation
        if (panelRoot.transform.parent != null && !panelRoot.transform.parent.gameObject.activeSelf)
        {
            Debug.LogWarning($"[WIRE TASK] Panel parent is inactive: {panelRoot.transform.parent.gameObject.name}", this);
            return;
        }

        // Set state BEFORE activation
        currentTask = task;
        isCompleted = false;
        isOpen = true;

        // Activate panel only (not controller gameObject)
        if (!panelRoot.activeSelf)
        {
            panelRoot.SetActive(true);
        }

        // Validate activation succeeded before proceeding
        if (!panelRoot.activeSelf || !panelRoot.activeInHierarchy)
        {
            currentTask = null;
            isOpen = false;
            Debug.LogWarning($"[WIRE TASK] Panel activation failed. Aborting open. panelRoot={panelRoot.name} activeSelf={panelRoot.activeSelf} activeInHierarchy={panelRoot.activeInHierarchy}", this);
            return;
        }

        panelRoot.transform.SetAsLastSibling();

        if (debugLogs)
        {
            Debug.Log($"[WIRE TASK] Panel activated. panelRoot={panelRoot.name} activeSelf={panelRoot.activeSelf} activeInHierarchy={panelRoot.activeInHierarchy} controllerActive={gameObject.activeInHierarchy}", this);
        }

        ResetPuzzle();

        if (titleText != null)
        {
            titleText.text = "FIX WIRES";
            titleText.raycastTarget = false;
        }

        if (instructionText != null)
        {
            instructionText.text = "Drag each wire to matching color";
            instructionText.raycastTarget = false;
        }

        AgentTracePanel.Trace("OBJECTIVE", $"Fix Wires opened: {task.TaskDisplayName}");
        if (debugLogs)
        {
            Debug.Log($"[WIRE TASK] Opened Fix Wires for {task.TaskId}", this);
        }
    }

    public void Close()
    {
        if (!allowCloseWithoutComplete && !isCompleted)
        {
            return;
        }

        if (!isOpen || (panelRoot != null && !panelRoot.activeSelf))
        {
            if (debugLogs)
            {
                Debug.Log("[WIRE TASK] Close ignored because panel is already closed.", this);
            }
            return;
        }

        CloseInternal();
    }

    public void ResetPuzzle()
    {
        connectedMatches.Clear();
        ClearLines();

        WireColor[] colors =
        {
            WireColor.Red,
            WireColor.Blue,
            WireColor.Yellow,
            WireColor.Green
        };

        for (int i = 0; i < leftNodes.Length && i < colors.Length; i++)
        {
            if (leftNodes[i] != null)
            {
                leftNodes[i].Configure(this, true, colors[i]);
            }
        }

        List<WireColor> shuffled = new List<WireColor>(colors);
        for (int i = 0; i < shuffled.Count; i++)
        {
            int swap = Random.Range(i, shuffled.Count);
            WireColor temp = shuffled[i];
            shuffled[i] = shuffled[swap];
            shuffled[swap] = temp;
        }

        for (int i = 0; i < rightNodes.Length && i < shuffled.Count; i++)
        {
            if (rightNodes[i] != null)
            {
                rightNodes[i].Configure(this, false, shuffled[i]);
            }
        }
    }

    public void RegisterCorrectConnection(WireNode left, WireNode right)
    {
        if (!isOpen || isCompleted || left == null || right == null)
        {
            return;
        }

        if (!left.IsLeftNode || right.IsLeftNode)
        {
            return;
        }

        if (left.IsConnected || right.IsConnected)
        {
            return;
        }

        if (left.WireColor != right.WireColor)
        {
            return;
        }

        if (IsColorAlreadyConnected(left.WireColor))
        {
            return;
        }

        left.SetConnected(true);
        right.SetConnected(true);

        connectedMatches[left.WireColor] = right;
        GameObject line = CreateUILine(left.NodeRect, right.NodeRect, WireNode.GetVisualColor(left.WireColor));
        if (line != null)
        {
            activeLines.Add(line);
        }

        if (debugLogs)
        {
            Debug.Log($"[WIRE TASK] Connected {left.WireColor}.", this);
        }

        if (connectedMatches.Count >= 4)
        {
            CompletePuzzle();
        }
    }

    public bool IsColorAlreadyConnected(WireColor color)
    {
        return connectedMatches.ContainsKey(color);
    }

    public void CompletePuzzle()
    {
        if (!isOpen || isCompleted)
        {
            return;
        }

        isCompleted = true;

        if (completionFlashImage != null)
        {
            completionFlashImage.gameObject.SetActive(true);
            Color flashColor = completionFlashImage.color;
            flashColor.a = 0.85f;
            completionFlashImage.color = flashColor;
            flashColor.a = 0f;
            completionFlashImage.color = flashColor;
            completionFlashImage.gameObject.SetActive(false);
        }

        if (currentTask != null)
        {
            currentTask.CompleteFromMinigame();
            AgentTracePanel.Trace("OBJECTIVE", $"Fix Wires completed: {currentTask.TaskDisplayName}");
        }
        else
        {
            AgentTracePanel.Trace("OBJECTIVE", "Fix Wires completed.");
        }

        if (debugLogs)
        {
            Debug.Log("[WIRE TASK] Puzzle completed.", this);
        }

        CloseInternal();
    }

    public GameObject CreateUILine(RectTransform from, RectTransform to, Color color)
    {
        if (from == null || to == null)
        {
            return null;
        }

        RectTransform parent = lineContainer != null ? lineContainer : wirePanelRect;
        if (parent == null && panelRoot != null)
        {
            parent = panelRoot.GetComponent<RectTransform>();
        }

        if (parent == null)
        {
            return null;
        }

        Vector2 fromPos = GetLocalPoint(parent, from);
        Vector2 toPos = GetLocalPoint(parent, to);

        GameObject lineObject = new GameObject($"WireLine_{color}");
        lineObject.transform.SetParent(parent, false);

        RectTransform lineRect = lineObject.AddComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0f, 0.5f);

        Image image = lineObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        Vector2 direction = toPos - fromPos;
        float length = direction.magnitude;
        lineRect.anchoredPosition = fromPos;
        lineRect.sizeDelta = new Vector2(length, 7f);

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        lineRect.localRotation = Quaternion.Euler(0f, 0f, angle);

        return lineObject;
    }

    public void LogDebug(string message)
    {
        if (!debugLogs || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log(message, this);
    }

    void CloseInternal()
    {
        if (!isOpen)
        {
            return;
        }

        ClearLines();
        connectedMatches.Clear();

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        isOpen = false;
        isCompleted = false;
        currentTask = null;

        if (debugLogs)
        {
            Debug.Log("[WIRE TASK] Panel closed.", this);
        }
    }

    void ClearLines()
    {
        for (int i = 0; i < activeLines.Count; i++)
        {
            if (activeLines[i] != null)
            {
                Destroy(activeLines[i]);
            }
        }

        activeLines.Clear();
    }

    Vector2 GetLocalPoint(RectTransform parent, RectTransform target)
    {
        Canvas parentCanvas = parent.GetComponentInParent<Canvas>();
        Camera eventCamera = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? parentCanvas.worldCamera
            : null;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, target.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, eventCamera, out Vector2 localPoint);
        return localPoint;
    }
}
