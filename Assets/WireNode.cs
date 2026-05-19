using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum WireColor
{
    Red,
    Blue,
    Yellow,
    Green
}

[DisallowMultipleComponent]
public sealed class WireNode : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] WireColor wireColor;
    [SerializeField] bool isLeftNode;
    [SerializeField] Image nodeImage;
    [SerializeField] TMP_Text labelText;
    [SerializeField] WireTaskMinigame minigame;

    bool isConnected;
    GameObject previewLine;
    Vector2 dragStartPosition;

    public WireColor WireColor => wireColor;
    public bool IsLeftNode => isLeftNode;
    public bool IsConnected => isConnected;
    public RectTransform NodeRect => transform as RectTransform;

    void Awake()
    {
        ApplyVisuals();
    }

    public void Configure(WireTaskMinigame owner, bool leftNode, WireColor color)
    {
        minigame = owner;
        isLeftNode = leftNode;
        wireColor = color;
        isConnected = false;
        previewLine = null;
        ApplyVisuals();
    }

    public void SetConnected(bool connected)
    {
        isConnected = connected;
        ApplyVisuals();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanStartDrag())
        {
            return;
        }

        dragStartPosition = NodeRect != null ? NodeRect.anchoredPosition : Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!CanStartDrag())
        {
            return;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!CanStartDrag())
        {
            return;
        }

        WireNode targetNode = FindTargetNode(eventData);
        if (targetNode == null || targetNode.IsLeftNode)
        {
            ClearPreviewLine();
            return;
        }

        if (targetNode.IsConnected || targetNode.WireColor != wireColor)
        {
            ClearPreviewLine();
            if (minigame != null)
            {
                minigame.LogDebug("[WIRE TASK] Wrong wire match. Snapped back.");
            }
            return;
        }

        minigame?.RegisterCorrectConnection(this, targetNode);
        ClearPreviewLine();
    }

    public static UnityEngine.Color GetVisualColor(WireColor color)
    {
        switch (color)
        {
            case WireColor.Red:
                return UnityEngine.Color.red;
            case WireColor.Blue:
                return UnityEngine.Color.blue;
            case WireColor.Yellow:
                return UnityEngine.Color.yellow;
            case WireColor.Green:
                return UnityEngine.Color.green;
            default:
                return UnityEngine.Color.white;
        }
    }

    bool CanStartDrag()
    {
        return minigame != null && isLeftNode && !isConnected;
    }

    WireNode FindTargetNode(PointerEventData eventData)
    {
        if (EventSystem.current == null || eventData == null)
        {
            return null;
        }

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        for (int i = 0; i < results.Count; i++)
        {
            WireNode node = results[i].gameObject != null
                ? results[i].gameObject.GetComponentInParent<WireNode>()
                : null;

            if (node != null && node != this)
            {
                return node;
            }
        }

        return null;
    }

    void ApplyVisuals()
    {
        if (nodeImage != null)
        {
            nodeImage.color = GetVisualColor(wireColor);
            nodeImage.raycastTarget = true;
        }

        if (labelText != null)
        {
            labelText.text = wireColor.ToString().ToUpperInvariant();
            labelText.raycastTarget = false;
        }
    }

    void ClearPreviewLine()
    {
        if (previewLine != null)
        {
            Destroy(previewLine);
            previewLine = null;
        }
    }
}
