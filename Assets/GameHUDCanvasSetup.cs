using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class GameHUDCanvasSetup : MonoBehaviour
{
    [Header("Controller")]
    [SerializeField] GameHUDController hudController;

    [Header("Named Text Fields")]
    [SerializeField] string phaseTextName = "PhaseText";
    [SerializeField] string waveTextName = "WaveText";
    [SerializeField] string timerTextName = "TimerText";

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
    }

    TMP_Text FindOrCreateText(Transform parent, string childName, int lineIndex)
    {
        Transform existing = FindChildRecursive(parent, childName);
        if (existing != null)
        {
            TMP_Text existingText = existing.GetComponent<TMP_Text>();
            if (existingText != null)
            {
                return existingText;
            }
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -20f - (28f * lineIndex));
        rect.sizeDelta = new Vector2(360f, 30f);

        TMP_Text text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Left;
        text.fontSize = 22f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = childName;
        text.color = Color.white;
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