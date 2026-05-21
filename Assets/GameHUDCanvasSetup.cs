using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class GameHUDCanvasSetup : MonoBehaviour
{
    static readonly Color32 PanelColor = new Color32(16, 24, 32, 230);
    static readonly Color32 PrimaryTextColor = new Color32(242, 253, 255, 255);
    static readonly Color32 SecondaryTextColor = new Color32(169, 214, 221, 255);
    static readonly Color32 WarningTextColor = new Color32(255, 90, 95, 255);

    [Header("Controller")]
    [SerializeField] GameHUDController hudController;
    [SerializeField] ObjectiveHUDController objectiveHUDController;

    [Header("Named Text Fields")]
    [SerializeField] string phaseTextName = "PhaseText";
    [SerializeField] string waveTextName = "WaveText";
    [SerializeField] string timerTextName = "TimerText";
    [SerializeField] string topRightObjectiveTextName = "TopRightObjectiveText";
    [SerializeField] string bottomRightFinalHuntTextName = "BottomRightFinalHuntText";

    void Start()
    {
        BuildHUD();
    }

    void BuildHUD()
    {
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject go = new GameObject("Canvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
        }

        ConfigureCanvas(canvas);

        if (hudController == null)
        {
            hudController = canvas.GetComponentInChildren<GameHUDController>(true);
        }

        if (hudController == null)
        {
            GameObject hudRoot = new GameObject("HUD");
            hudRoot.transform.SetParent(canvas.transform, false);
            hudController = hudRoot.AddComponent<GameHUDController>();
        }

        Transform topLeftPanel = FindOrCreatePanel(
            canvas.transform,
            "TopLeftStatusPanel",
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(40f, -40f),
            new Vector2(500f, 240f),
            PanelColor);

        TMP_Text phaseText = FindOrCreateText(topLeftPanel, phaseTextName, 0);
        TMP_Text waveText = FindOrCreateText(topLeftPanel, waveTextName, 1);
        TMP_Text timerText = FindOrCreateText(topLeftPanel, timerTextName, 2);

        ConfigureStatusText(phaseText, new Vector2(24f, -34f), new Vector2(440f, 48f), 38f, TextAlignmentOptions.TopLeft, PrimaryTextColor, true);
        ConfigureStatusText(waveText, new Vector2(24f, -100f), new Vector2(440f, 44f), 32f, TextAlignmentOptions.TopLeft, SecondaryTextColor, true);
        ConfigureStatusText(timerText, new Vector2(24f, -164f), new Vector2(440f, 52f), 38f, TextAlignmentOptions.TopLeft, SecondaryTextColor, true);

        phaseText.text = "PHASE: EXPLORATION";
        waveText.text = "WAVE: 0/3";
        timerText.text = "TIME: 60";

        hudController.SetPhaseText(phaseText);
        hudController.SetWaveText(waveText);
        hudController.SetTimerText(timerText);

        Transform objectivePanel = FindOrCreatePanel(
            canvas.transform,
            "ObjectivePanel",
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-40f, -40f),
            new Vector2(620f, 220f),
            new Color32(13, 27, 34, 230));

        TMP_Text objectiveTitleText = FindOrCreateText(objectivePanel, "ObjectiveTitleText", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(220f, 34f), TextAlignmentOptions.TopRight, 26f);
        ConfigureStatusText(objectiveTitleText, new Vector2(-24f, -24f), new Vector2(220f, 34f), 26f, TextAlignmentOptions.TopRight, SecondaryTextColor, true);
        objectiveTitleText.text = "OBJECTIVE";

        TMP_Text topRightObjectiveText = FindOrCreateText(objectivePanel, topRightObjectiveTextName, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -86f), new Vector2(560f, 120f), TextAlignmentOptions.TopRight, 36f);
        ConfigureStatusText(topRightObjectiveText, new Vector2(-24f, -86f), new Vector2(560f, 120f), 36f, TextAlignmentOptions.TopRight, PrimaryTextColor, false);
        topRightObjectiveText.text = "Tasks Remaining: 0";
        topRightObjectiveText.raycastTarget = false;

        TMP_Text bottomRightFinalHuntText = FindOrCreateText(canvas.transform, bottomRightFinalHuntTextName, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 180f), new Vector2(420f, 84f), TextAlignmentOptions.BottomRight, 40f);
        ConfigureStatusText(bottomRightFinalHuntText, new Vector2(-40f, 180f), new Vector2(420f, 84f), 40f, TextAlignmentOptions.BottomRight, WarningTextColor, true);
        bottomRightFinalHuntText.text = "FINAL HUNT";
        bottomRightFinalHuntText.raycastTarget = false;

        CanvasGroup bottomRightCanvasGroup = bottomRightFinalHuntText.GetComponent<CanvasGroup>();
        if (bottomRightCanvasGroup == null)
        {
            bottomRightCanvasGroup = bottomRightFinalHuntText.gameObject.AddComponent<CanvasGroup>();
        }

        bottomRightCanvasGroup.blocksRaycasts = false;
        bottomRightCanvasGroup.interactable = false;
        bottomRightCanvasGroup.alpha = 0f;
        bottomRightFinalHuntText.gameObject.SetActive(false);

        // Check if serialized field is still valid (not destroyed)
        if (objectiveHUDController != null)
        {
            try
            {
                if (objectiveHUDController.gameObject == null)
                {
                    objectiveHUDController = null;
                }
            }
            catch
            {
                objectiveHUDController = null;
            }
        }

        // Try to find existing ObjectiveHUDController in scene
        if (objectiveHUDController == null)
        {
            objectiveHUDController = FindControllerByExactName(canvas.transform, "ObjectiveHUDController");
        }

        // If still not found, check for an existing instance via FindAnyObjectByType
        if (objectiveHUDController == null)
        {
            objectiveHUDController = FindAnyObjectByType<ObjectiveHUDController>(FindObjectsInactive.Include);
            if (objectiveHUDController != null)
            {
                Debug.Log("[HUD SETUP] Reused existing ObjectiveHUDController from scene.");
            }
        }

        // Create a new one if none exists
        if (objectiveHUDController == null)
        {
            GameObject objectiveRoot = new GameObject("ObjectiveHUDController");
            objectiveRoot.transform.SetParent(canvas.transform, false);
            objectiveHUDController = objectiveRoot.AddComponent<ObjectiveHUDController>();
            Debug.Log("[HUD SETUP] Created new ObjectiveHUDController.");
        }
        else
        {
            Debug.Log("[HUD SETUP] Reused existing ObjectiveHUDController.");
        }

        objectiveHUDController.Initialize(topRightObjectiveText, bottomRightFinalHuntText);
        objectiveHUDController.RefreshObjectiveText();
        Debug.Log("[HUD SETUP] Objective HUD wired successfully.");
        Debug.Log("[INGAME UI] HUD canvas configured.");
        Debug.Log("[INGAME UI] Objective HUD polished.");
    }

    void ConfigureCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    TMP_Text FindOrCreateText(Transform parent, string childName, int lineIndex)
    {
        return FindOrCreateText(parent, childName, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f - (28f * lineIndex)), new Vector2(360f, 30f), TextAlignmentOptions.Left, 22f);
    }

    Transform FindOrCreatePanel(Transform parent, string childName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        Transform existing = FindChildRecursive(parent, childName);
        GameObject panelObject = existing != null ? existing.gameObject : new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));

        if (existing == null)
        {
            panelObject.transform.SetParent(parent, false);
        }

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image image = panelObject.GetComponent<Image>();
        if (image == null)
        {
            image = panelObject.AddComponent<Image>();
        }

        image.color = color;
        image.raycastTarget = false;

        CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = panelObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        return panelObject.transform;
    }

    static void ConfigureStatusText(TMP_Text text, Vector2 anchoredPosition, Vector2 sizeDelta, float fontSize, TextAlignmentOptions alignment, Color color, bool bold)
    {
        if (text == null)
        {
            return;
        }

        RectTransform rect = text.rectTransform;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        text.alignment = alignment;
        text.fontSize = fontSize;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
    }

    ObjectiveHUDController FindControllerByExactName(Transform parent, string childName)
    {
        Transform existing = FindChildRecursive(parent, childName);
        if (existing == null)
        {
            return null;
        }

        ObjectiveHUDController controller = existing.GetComponent<ObjectiveHUDController>();
        if (controller == null)
        {
            controller = existing.gameObject.AddComponent<ObjectiveHUDController>();
        }

        return controller;
    }

    TMP_Text FindOrCreateText(Transform parent, string childName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, TextAlignmentOptions alignment, float fontSize)
    {
        Transform existing = FindChildRecursive(parent, childName);
        if (existing != null)
        {
            TMP_Text existingText = existing.GetComponent<TMP_Text>();
            if (existingText != null)
            {
                RectTransform existingRect = existingText.rectTransform;
                existingRect.anchorMin = anchorMin;
                existingRect.anchorMax = anchorMax;
                existingRect.pivot = pivot;
                existingRect.anchoredPosition = anchoredPosition;
                existingRect.sizeDelta = sizeDelta;
                existingText.alignment = alignment;
                existingText.fontSize = fontSize;
                existingText.raycastTarget = false;
                return existingText;
            }

            TMP_Text addedText = existing.gameObject.AddComponent<TextMeshProUGUI>();
            RectTransform addedRect = addedText.rectTransform;
            addedRect.anchorMin = anchorMin;
            addedRect.anchorMax = anchorMax;
            addedRect.pivot = pivot;
            addedRect.anchoredPosition = anchoredPosition;
            addedRect.sizeDelta = sizeDelta;
            addedText.alignment = alignment;
            addedText.fontSize = fontSize;
            addedText.textWrappingMode = TextWrappingModes.NoWrap;
            addedText.text = childName;
            addedText.color = Color.white;
            addedText.raycastTarget = false;

            CanvasGroup addedCanvasGroup = existing.gameObject.GetComponent<CanvasGroup>();
            if (addedCanvasGroup == null)
            {
                addedCanvasGroup = existing.gameObject.AddComponent<CanvasGroup>();
            }

            addedCanvasGroup.alpha = 1f;
            addedCanvasGroup.blocksRaycasts = false;
            addedCanvasGroup.interactable = false;
            return addedText;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TMP_Text text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = childName;
        text.color = Color.white;
        text.raycastTarget = false;

        CanvasGroup canvasGroup = go.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = go.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        return text;
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}