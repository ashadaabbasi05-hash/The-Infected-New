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

    public bool IsOpen => isOpen;

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
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (wirePanelRect == null)
        {
            wirePanelRect = GetComponent<RectTransform>();
        }

        if (lineContainer == null)
        {
            Transform lineContainerTransform = transform.Find("LineContainer");
            if (lineContainerTransform != null)
            {
                lineContainer = lineContainerTransform.GetComponent<RectTransform>();
            }
        }
    }

    public void Open(TaskInteractable task)
    {
        if (task == null)
        {
            return;
        }

        EnsurePanelReferences();

        if (panelRoot == null)
        {
            panelRoot = gameObject;
            Debug.LogWarning("[WIRE TASK] panelRoot was missing. Defaulted to WireTaskMinigame GameObject.", this);
        }

        if (wirePanelRect == null)
        {
            wirePanelRect = GetComponent<RectTransform>();
        }

        if (isOpen && panelRoot.activeInHierarchy)
        {
            if (debugLogs)
            {
                Debug.Log("[WIRE TASK] Open ignored because panel is already visible.", this);
            }

            return;
        }

        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        if (debugLogs)
        {
            Debug.Log($"[WIRE TASK] Panel activated. activeSelf={panelRoot.activeSelf} activeInHierarchy={panelRoot.activeInHierarchy}", this);
        }

        currentTask = task;
        isCompleted = false;
        isOpen = true;

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
