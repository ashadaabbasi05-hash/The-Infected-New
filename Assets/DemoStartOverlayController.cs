using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DemoStartOverlayController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] bool showOnStart = true;
    [SerializeField] bool autoFindReferences = true;
    [SerializeField] bool debugLogs = true;

    [Header("UI References")]
    [SerializeField] GameObject overlayRoot;
    [SerializeField] CanvasGroup overlayCanvasGroup;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text bodyText;
    [SerializeField] Button startButton;
    [SerializeField] Button demoToolsButton;
    [SerializeField] Button hideButton;

    [Header("External References")]
    [SerializeField] DemoToolsController demoToolsController;

    const string DefaultTitle = "THE INFECTED";

    const string DefaultBody =
        "OBJECTIVE:\n" +
        "Complete tasks, survive gas waves, use antidote voting, escape.\n\n" +
        "MOBILE CONTROLS:\n" +
        "Joystick = Move\n" +
        "INTERACT = Tasks / Exit Scan\n" +
        "TRACE = Agent decisions\n" +
        "HELP = Instructions\n" +
        "DEMO = Judge tools\n\n" +
        "DEMO FLOW:\n" +
        "1. Complete tasks or use demo tools\n" +
        "2. Survive infection waves\n" +
        "3. Vote antidote during meetings\n" +
        "4. Escape before Final Hunt\n\n" +
        "No hidden role reveal. Infected players become AI agents.";

    CanvasGroup cachedCanvasGroup;
    bool hasWiredButtons;

    void Awake()
    {
        if (autoFindReferences)
        {
            AutoFindReferences();
        }

        EnsureCanvasGroup();
        SetupText();
        WireButtons();
        ConfigureInitialVisibility();
    }

    void AutoFindReferences()
    {
        if (overlayRoot == null)
        {
            GameObject found = GameObject.Find("DemoStartOverlay");
            if (found != null)
            {
                overlayRoot = found;
                Log("Auto-found overlayRoot: DemoStartOverlay");
            }
        }

        if (overlayRoot != null)
        {
            if (overlayCanvasGroup == null)
            {
                overlayCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();
            }

            if (titleText == null)
            {
                GameObject titleObj = FindChildRecursive(overlayRoot.transform, "DemoStartTitleText");
                if (titleObj != null)
                {
                    titleText = titleObj.GetComponent<TMP_Text>();
                }
            }

            if (bodyText == null)
            {
                GameObject bodyObj = FindChildRecursive(overlayRoot.transform, "DemoStartBodyText");
                if (bodyObj != null)
                {
                    bodyText = bodyObj.GetComponent<TMP_Text>();
                }
            }

            if (startButton == null)
            {
                GameObject btnObj = FindChildRecursive(overlayRoot.transform, "DemoStartButton");
                if (btnObj != null)
                {
                    startButton = btnObj.GetComponent<Button>();
                }
            }

            if (demoToolsButton == null)
            {
                GameObject btnObj = FindChildRecursive(overlayRoot.transform, "DemoStartDemoToolsButton");
                if (btnObj != null)
                {
                    demoToolsButton = btnObj.GetComponent<Button>();
                }
            }

            if (hideButton == null)
            {
                GameObject btnObj = FindChildRecursive(overlayRoot.transform, "DemoStartHideButton");
                if (btnObj != null)
                {
                    hideButton = btnObj.GetComponent<Button>();
                }
            }
        }

        if (demoToolsController == null)
        {
            demoToolsController = DemoToolsController.Instance;
            if (demoToolsController != null)
            {
                Log("Auto-found demoToolsController via Instance.");
            }
            else
            {
                GameObject dtcObj = GameObject.Find("DemoToolsSystem");
                if (dtcObj != null)
                {
                    demoToolsController = dtcObj.GetComponent<DemoToolsController>();
                    if (demoToolsController != null)
                    {
                        Log("Auto-found demoToolsController via DemoToolsSystem GameObject.");
                    }
                }
            }

            if (demoToolsController == null)
            {
                demoToolsController = FindAnyObjectByType<DemoToolsController>(FindObjectsInactive.Include);
                if (demoToolsController != null)
                {
                    Log("Auto-found demoToolsController via FindAnyObjectByType.");
                }
            }
        }
    }

    GameObject FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrWhiteSpace(childName))
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child.gameObject;

            GameObject found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    void EnsureCanvasGroup()
    {
        if (overlayCanvasGroup != null)
        {
            cachedCanvasGroup = overlayCanvasGroup;
            return;
        }

        if (overlayRoot != null)
        {
            cachedCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();
            if (cachedCanvasGroup == null)
            {
                cachedCanvasGroup = overlayRoot.AddComponent<CanvasGroup>();
            }
        }
    }

    void SetupText()
    {
        if (titleText != null)
        {
            titleText.text = DefaultTitle;
            titleText.raycastTarget = false;
        }

        if (bodyText != null)
        {
            bodyText.text = DefaultBody;
            bodyText.raycastTarget = false;
        }
    }

    void WireButtons()
    {
        if (hasWiredButtons)
            return;

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(HandleStartPressed);
        }
        else
        {
            LogWarning("startButton is null — DemoStartButton not found in hierarchy.");
        }

        if (demoToolsButton != null)
        {
            demoToolsButton.onClick.RemoveAllListeners();
            demoToolsButton.onClick.AddListener(HandleDemoToolsPressed);
        }
        else
        {
            LogWarning("demoToolsButton is null — DemoStartDemoToolsButton not found in hierarchy.");
        }

        if (hideButton != null)
        {
            hideButton.onClick.RemoveAllListeners();
            hideButton.onClick.AddListener(HandleHidePressed);
        }
        else
        {
            LogWarning("hideButton is null — DemoStartHideButton not found in hierarchy.");
        }

        hasWiredButtons = true;
    }

    void ConfigureInitialVisibility()
    {
        if (showOnStart)
        {
            ShowOverlay();
        }
        else
        {
            HideOverlay();
        }
    }

    void HandleStartPressed()
    {
        Log("Start button pressed — hiding overlay.");
        HideOverlay();
        AgentTracePanel.Trace("DEMO", "Demo started.");
    }

    void HandleDemoToolsPressed()
    {
        Log("Demo Tools button pressed.");
        HideOverlay();

        if (demoToolsController != null)
        {
            demoToolsController.ToggleDemoPanel();
            Log("DemoToolsController.ToggleDemoPanel called.");
        }
        else
        {
            LogWarning("DemoToolsController is null — cannot toggle demo panel.");
        }

        AgentTracePanel.Trace("DEMO", "Demo tools opened.");
    }

    void HandleHidePressed()
    {
        Log("Hide button pressed — hiding overlay.");
        HideOverlay();
    }

    public void ShowOverlay()
    {
        if (overlayRoot != null)
        {
            overlayRoot.SetActive(true);
        }

        if (cachedCanvasGroup != null)
        {
            cachedCanvasGroup.alpha = 1f;
            cachedCanvasGroup.interactable = true;
            cachedCanvasGroup.blocksRaycasts = true;
        }

        Log("DemoStartOverlay shown.");
    }

    public void HideOverlay()
    {
        if (cachedCanvasGroup != null)
        {
            cachedCanvasGroup.alpha = 0f;
            cachedCanvasGroup.interactable = false;
            cachedCanvasGroup.blocksRaycasts = false;
        }

        if (overlayRoot != null)
        {
            overlayRoot.SetActive(false);
        }

        Log("DemoStartOverlay hidden.");
    }

    void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log("[START OVERLAY] " + message, this);
        }
    }

    void LogWarning(string message)
    {
        if (debugLogs)
        {
            Debug.LogWarning("[START OVERLAY] " + message, this);
        }
    }
}