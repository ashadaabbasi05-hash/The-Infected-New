using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DemoHelpPanel : MonoBehaviour
{
    [SerializeField] GameObject panelRoot;
    [SerializeField] TMP_Text helpText;
    [SerializeField] bool startVisible = true;
    [SerializeField] bool enableDebugHotkeys = true;

    const string defaultHelp =
        "OBJECTIVE:\n" +
        "Complete lab tasks.\n" +
        "Survive gas waves.\n" +
        "Use antidote voting during meetings.\n" +
        "Escape before the infected take over.\n\n" +
        "MOBILE CONTROLS:\n" +
        "Left Joystick = Move\n" +
        "INTERACT = Tasks / Door\n" +
        "TRACE = AI Agent Trace\n" +
        "HELP = Hide / Show Help\n\n" +
        "DEMO:\n" +
        "FINAL HUNT = Start chase showcase";

    CanvasGroup canvasGroup;

    void Awake()
    {
        if (panelRoot == null)
        {
            GameObject found = GameObject.Find("DemoHelpPanel");
            if (found != null)
            {
                panelRoot = found;
            }
        }

        if (panelRoot != null)
        {
            canvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = panelRoot.AddComponent<CanvasGroup>();
            }

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0.85f;
        }

        if (helpText == null && panelRoot != null)
        {
            helpText = panelRoot.GetComponentInChildren<TMP_Text>(true);
        }

        if (helpText != null)
        {
            helpText.raycastTarget = false;
            helpText.text = defaultHelp;
        }

        if (startVisible)
        {
            ShowPanel();
        }
        else
        {
            HidePanel();
        }
    }

    void Update()
    {
        if (!enableDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.H))
        {
            TogglePanel();
        }
    }

    public void ShowPanel()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0.85f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    public void HidePanel()
    {
        if (panelRoot != null)
        {
            // keep object active to avoid destroying components references
            panelRoot.SetActive(false);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    public void TogglePanel()
    {
        bool active = panelRoot != null && panelRoot.activeSelf;
        if (active)
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }
}
