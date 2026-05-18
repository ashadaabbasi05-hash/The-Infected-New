using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MobileActionButtonsController : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] public Button interactButton;
    [SerializeField] public Button traceButton;
    [SerializeField] public Button helpButton;
    [SerializeField] public Button finalHuntDemoButton;

    [Header("Labels")]
    [SerializeField] public TMP_Text interactButtonText;
    [SerializeField] public TMP_Text traceButtonText;
    [SerializeField] public TMP_Text helpButtonText;
    [SerializeField] public TMP_Text finalHuntButtonText;

    [Header("References")]
    [SerializeField] public AgentTracePanel agentTracePanel;
    [SerializeField] public DemoHelpPanel demoHelpPanel;
    [SerializeField] public FinalHuntManager finalHuntManager;

    [Header("Options")]
    [SerializeField] public bool autoFindReferences = true;
    [SerializeField] public bool enableDebugLogs = true;

    bool warnedMissingInteract;
    bool warnedMissingTrace;
    bool warnedMissingHelp;
    bool warnedMissingFinalHunt;

    void Awake()
    {
        if (autoFindReferences)
        {
            AutoFindReferences();
        }

        SetupButtonLabels();
        SetupListeners();
    }

    void AutoFindReferences()
    {
        if (interactButton == null)
        {
            GameObject go = GameObject.Find("InteractButton");
            if (go != null) interactButton = go.GetComponent<Button>();
        }

        if (traceButton == null)
        {
            GameObject go = GameObject.Find("TraceButton");
            if (go != null) traceButton = go.GetComponent<Button>();
        }

        if (helpButton == null)
        {
            GameObject go = GameObject.Find("HelpButton");
            if (go != null) helpButton = go.GetComponent<Button>();
        }

        if (finalHuntDemoButton == null)
        {
            GameObject go = GameObject.Find("FinalHuntButton");
            if (go != null) finalHuntDemoButton = go.GetComponent<Button>();
        }

        if (interactButtonText == null && interactButton != null)
        {
            interactButtonText = interactButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (traceButtonText == null && traceButton != null)
        {
            traceButtonText = traceButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (helpButtonText == null && helpButton != null)
        {
            helpButtonText = helpButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (finalHuntButtonText == null && finalHuntDemoButton != null)
        {
            finalHuntButtonText = finalHuntDemoButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (agentTracePanel == null)
        {
            agentTracePanel = AgentTracePanel.Instance ?? FindAnyObjectByType<AgentTracePanel>(FindObjectsInactive.Include);
        }

        if (demoHelpPanel == null)
        {
            demoHelpPanel = FindAnyObjectByType<DemoHelpPanel>(FindObjectsInactive.Include);
        }

        if (finalHuntManager == null)
        {
            finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        }
    }

    void SetupButtonLabels()
    {
        if (interactButtonText != null) interactButtonText.text = "INTERACT";
        if (traceButtonText != null) traceButtonText.text = "TRACE";
        if (helpButtonText != null) helpButtonText.text = "HELP";
        if (finalHuntButtonText != null) finalHuntButtonText.text = "FINAL HUNT";
    }

    void SetupListeners()
    {
        if (interactButton != null)
        {
            interactButton.onClick.RemoveAllListeners();
            interactButton.onClick.AddListener(HandleInteractPressed);
            interactButton.gameObject.SetActive(true);
        }
        else if (!warnedMissingInteract)
        {
            warnedMissingInteract = true;
            if (enableDebugLogs) Debug.LogWarning("[MOBILE BUTTONS] InteractButton missing.");
        }

        if (traceButton != null)
        {
            traceButton.onClick.RemoveAllListeners();
            traceButton.onClick.AddListener(HandleTracePressed);
            traceButton.gameObject.SetActive(true);
        }
        else if (!warnedMissingTrace)
        {
            warnedMissingTrace = true;
            if (enableDebugLogs) Debug.LogWarning("[MOBILE BUTTONS] TraceButton missing.");
        }

        if (helpButton != null)
        {
            helpButton.onClick.RemoveAllListeners();
            helpButton.onClick.AddListener(HandleHelpPressed);
            helpButton.gameObject.SetActive(true);
        }
        else if (!warnedMissingHelp)
        {
            warnedMissingHelp = true;
            if (enableDebugLogs) Debug.LogWarning("[MOBILE BUTTONS] HelpButton missing.");
        }

        if (finalHuntDemoButton != null)
        {
            finalHuntDemoButton.onClick.RemoveAllListeners();
            finalHuntDemoButton.onClick.AddListener(HandleFinalHuntPressed);
            finalHuntDemoButton.gameObject.SetActive(true);
        }
        else if (!warnedMissingFinalHunt)
        {
            warnedMissingFinalHunt = true;
            if (enableDebugLogs) Debug.LogWarning("[MOBILE BUTTONS] FinalHuntButton missing.");
        }
    }

    void HandleInteractPressed()
    {
        bool interacted = false;

        // Priority: Task then Door
        if (TaskInteractable.TryInteractCurrent())
        {
            interacted = true;
        }
        else if (EscapeDoor.TryInteractCurrentDoor())
        {
            interacted = true;
        }

        if (!interacted)
        {
            Debug.Log("[MOBILE BUTTONS] Nothing to interact with.");
        }
    }

    void HandleTracePressed()
    {
        AgentTracePanel panel = agentTracePanel ?? AgentTracePanel.Instance ?? FindAnyObjectByType<AgentTracePanel>(FindObjectsInactive.Include);
        if (panel != null)
        {
            panel.TogglePanel();
            AgentTracePanel.Trace("UI", "Agent Trace toggled from mobile button.");
            return;
        }

        if (!warnedMissingTrace)
        {
            warnedMissingTrace = true;
            Debug.LogWarning("[MOBILE BUTTONS] AgentTracePanel not found for TRACE button.");
        }
    }

    void HandleHelpPressed()
    {
        if (demoHelpPanel != null)
        {
            demoHelpPanel.TogglePanel();
            AgentTracePanel.Trace("UI", "Help panel toggled.");
            return;
        }

        DemoHelpPanel found = FindAnyObjectByType<DemoHelpPanel>(FindObjectsInactive.Include);
        if (found != null)
        {
            demoHelpPanel = found;
            demoHelpPanel.TogglePanel();
            AgentTracePanel.Trace("UI", "Help panel toggled.");
            return;
        }

        if (!warnedMissingHelp)
        {
            warnedMissingHelp = true;
            Debug.LogWarning("[MOBILE BUTTONS] DemoHelpPanel not found for HELP button.");
        }
    }

    void HandleFinalHuntPressed()
    {
        FinalHuntManager mgr = finalHuntManager ?? FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        if (mgr != null)
        {
            mgr.StartFinalHuntDemo();
            AgentTracePanel.Trace("DEMO", "Final Hunt demo button pressed.");
            return;
        }

        if (!warnedMissingFinalHunt)
        {
            warnedMissingFinalHunt = true;
            Debug.LogWarning("[MOBILE BUTTONS] FinalHuntManager not found for FINAL HUNT button.");
        }
    }
}
