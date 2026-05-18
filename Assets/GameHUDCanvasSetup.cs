using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class GameHUDCanvasSetup : MonoBehaviour
{
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

        TMP_Text phaseText = FindOrCreateText(canvas.transform, phaseTextName, 0);
        TMP_Text waveText = FindOrCreateText(canvas.transform, waveTextName, 1);
        TMP_Text timerText = FindOrCreateText(canvas.transform, timerTextName, 2);

        hudController.SetPhaseText(phaseText);
        hudController.SetWaveText(waveText);
        hudController.SetTimerText(timerText);

        TMP_Text topRightObjectiveText = FindOrCreateText(canvas.transform, topRightObjectiveTextName, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -80f), new Vector2(420f, 60f), TextAlignmentOptions.TopRight, 24f);
        topRightObjectiveText.text = "Tasks Remaining: 0";
        topRightObjectiveText.raycastTarget = false;

        TMP_Text bottomRightFinalHuntText = FindOrCreateText(canvas.transform, bottomRightFinalHuntTextName, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 160f), new Vector2(360f, 48f), TextAlignmentOptions.BottomRight, 24f);
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
    }

    TMP_Text FindOrCreateText(Transform parent, string childName, int lineIndex)
    {
        return FindOrCreateText(parent, childName, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f - (28f * lineIndex)), new Vector2(360f, 30f), TextAlignmentOptions.Left, 22f);
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