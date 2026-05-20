using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MeetingController : MonoBehaviour
{
    enum MeetingFlowState
    {
        Idle = 0,
        Discussion = 1,
        Voting = 2,
        Curing = 3
    }

    const string VoteButtonNamePrefix = "VoteButton_Player";
    const int MaxVoteSlots = 4;

    [Header("References")]
    [SerializeField] GameManager gameManager;
    [SerializeField] GameObject meetingPanel;
    [SerializeField] GameObject votingPanel;
    [Tooltip("Assign vote buttons named VoteButton_Player1..4, or leave empty to auto-find under meetingPanel.")]
    [SerializeField] List<Button> voteButtons = new List<Button>(4);
    [SerializeField] TMP_Text timerText;

    [Header("Auto Setup")]
    [SerializeField] bool autoFindUiReferences = true;

    [Header("Timing")]
    [SerializeField, Min(1f)] float discussionDuration = 20f;
    [SerializeField, Min(1f)] float votingDuration = 20f;
    [SerializeField, Min(1f)] float antidoteFreezeDuration = 10f;

    [Header("Antidote UI")]
    [SerializeField, Tooltip("Personal full-screen overlay — local antidote target only.")]
    GameObject personalAntidotePanel;
    [SerializeField] TMP_Text personalAntidoteText;
    [SerializeField] TMP_Text personalAntidoteTimerText;

    [Header("Public Antidote Status (non-blocking banner)")]
    [SerializeField] GameObject publicAntidoteStatusPanel;
    [SerializeField] TMP_Text publicAntidoteStatusText;
    [SerializeField] TMP_Text publicAntidoteTimerText;
    [SerializeField] CanvasGroup publicAntidoteCanvasGroup;
    [SerializeField] bool createPublicAntidoteBannerIfMissing = true;

    [Header("Behavior")]
    [SerializeField] bool autoAdvanceGameManagerPhases = true;
    [SerializeField] bool enablePrototypeAIVotes = true;
    [SerializeField] bool disableAIVotesForManualTesting = false;
    [SerializeField] bool useTeamAVoteApi = false;
    [SerializeField] float voteApiResponseTimeoutSeconds = 3f;
    [SerializeField] bool allowNonLocalHumansToAutoVote = true;
    [SerializeField] bool enableDebugHotkeys = true;
    [SerializeField] bool showPrivateAntidoteDebugTrace = false;

    [Header("UI Feedback")]
    [SerializeField] TMP_Text resultText;

    readonly List<PlayerIdentity> alivePlayers = new List<PlayerIdentity>(4);
    readonly Dictionary<int, int> votesByVoterId = new Dictionary<int, int>(4);
    readonly HashSet<int> playersWhoVoted = new HashSet<int>();

    MeetingFlowState flowState = MeetingFlowState.Idle;
    float phaseTimer;
    bool votingResolved;
    bool localVoteCast;
    bool personalAntidotePanelMissingWarningLogged;
    bool personalAntidoteUiWiredLogged;

    PlayerIdentity localVoter;
    PlayerIdentity activeAntidoteTarget;
    PlayerIdentity activePersonalAntidoteTarget;
    Coroutine activeAntidoteRoutine;
    bool personalAntidoteOverlayHiddenLogged;

    void Awake()
    {
        AutoAssignReferences();
        AutoAssignPersonalAntidoteReferences();
        AutoAssignPublicAntidoteReferences();
        EnsurePublicAntidoteStatusUi();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        if (votingPanel != null)
        {
            votingPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }

        HidePersonalAntidoteOverlay();
        HidePublicAntidoteStatus();
        ResolveGameManagerReference();
        LogLocalPlayerStrict("Awake");
    }

    void Start()
    {
        AutoAssignReferences();
        AutoAssignPersonalAntidoteReferences();
        AutoAssignPublicAntidoteReferences();
        EnsurePublicAntidoteStatusUi();
        LogLocalPlayerStrict("Start");

        if (gameManager == null)
        {
            return;
        }

        if (GameManager.CurrentPhase == GamePhase.Meeting)
        {
            StartMeeting();
        }
        else if (GameManager.CurrentPhase == GamePhase.Voting)
        {
            StartVoting();
        }
    }

    void OnEnable()
    {
        AutoAssignReferences();
        ResolveGameManagerReference();
        SubscribeToGameManagerEvents();
    }

    void OnValidate()
    {
        if (!autoFindUiReferences)
        {
            SanitizeVoteButtonList();
        }
        else
        {
            AutoAssignReferences();
        }

        AutoAssignPersonalAntidoteReferences();
        AutoAssignPublicAntidoteReferences();
        SanitizeVoteButtonList();
    }

    void OnDisable()
    {
        UnsubscribeFromGameManagerEvents();
    }

    void Update()
    {
        HandleDebugHotkeys();

        if (flowState == MeetingFlowState.Idle)
        {
            return;
        }

        bool antidoteRoutineRunning = flowState == MeetingFlowState.Curing && activeAntidoteRoutine != null;
        if (!antidoteRoutineRunning)
        {
            phaseTimer -= Time.deltaTime;
            if (phaseTimer < 0f)
            {
                phaseTimer = 0f;
            }
        }

        UpdateTimerText();

        if (antidoteRoutineRunning)
        {
            return;
        }

        if (phaseTimer > 0f)
        {
            return;
        }

        if (flowState == MeetingFlowState.Discussion)
        {
            if (autoAdvanceGameManagerPhases && gameManager != null && GameManager.CurrentPhase != GamePhase.Voting)
            {
                gameManager.EnterVoting();
                return;
            }

            StartVoting();
            return;
        }

        if (flowState == MeetingFlowState.Curing)
        {
            FinalizeMeetingFlow();
            TryEnterExplorationAfterMeeting();
            return;
        }

        EndVoting();
    }

    public void StartMeeting()
    {
        RefreshAlivePlayers();

        flowState = MeetingFlowState.Discussion;
        phaseTimer = discussionDuration;
        votingResolved = false;
        activeAntidoteTarget = null;

        localVoter = GetLocalPlayerStrict();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

        ApplyMeetingVotingMovementLock();
        EnsureVoteButtonsSortedBySlot();
        ConfigureVoteButtons(false);
        ClearResultText();
        HidePersonalAntidoteOverlay();
        UpdateTimerText();

        Debug.Log("Meeting started.", this);
    }

    public void StartVoting()
    {
        if (flowState == MeetingFlowState.Voting)
        {
            return;
        }

        AgentTracePanel.Trace("MEETING", "Voting started. Antidote target selection active.");

        RefreshAlivePlayers();

        votesByVoterId.Clear();
        playersWhoVoted.Clear();
        localVoteCast = false;
        votingResolved = false;
        activeAntidoteTarget = null;
        HidePersonalAntidoteOverlay();
        HidePublicAntidoteStatus();

        flowState = MeetingFlowState.Voting;
        phaseTimer = votingDuration;

        localVoter = GetLocalPlayerStrict();
        LogLocalPlayerStrict("StartVoting");

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

        EnsureVoteButtonsSortedBySlot();
        ConfigureVoteButtons(true);

        if (disableAIVotesForManualTesting)
        {
            Debug.Log("[VOTE DEBUG] AI votes disabled for manual testing.", this);
        }
        else
        {
            CastAIVotes();
        }

        UpdateTimerText();

        int eligibleVoters = CountEligibleVoters();
        Debug.Log($"[VOTE DEBUG] Voting started. Eligible voters={eligibleVoters}", this);
        Debug.Log("[VOTE DEBUG] Vote buttons configured.", this);
    }

    public void EndVoting()
    {
        if (flowState == MeetingFlowState.Idle)
        {
            return;
        }

        if (!votingResolved && flowState == MeetingFlowState.Voting)
        {
            CastMissingVotesRandomly();
            ResolveVotingOutcome();
        }
    }

    void OnVoteButtonPressedForPlayerId(int targetPlayerId)
    {
        Debug.Log($"[VOTE DEBUG] Vote button pressed targetPlayerId={targetPlayerId}", this);

        if (flowState != MeetingFlowState.Voting || votingResolved)
        {
            Debug.LogWarning("Vote button pressed outside active voting phase.", this);
            return;
        }

        if (localVoteCast)
        {
            Debug.Log("[VOTE DEBUG] Local player already voted. Duplicate vote rejected.", this);
            return;
        }

        PlayerIdentity localPlayer = GetLocalPlayerStrict();
        if (localPlayer == null || !localPlayer.isAlive || localPlayer.isFrozen)
        {
            Debug.LogWarning("No valid local voter found or voter is frozen.", this);
            return;
        }

        localVoter = localPlayer;

        PlayerIdentity target = FindPlayerById(targetPlayerId);
        if (target == null || !target.isAlive || target.isFrozen)
        {
            Debug.LogWarning($"[VOTE DEBUG] Vote target playerId {targetPlayerId} not found, dead, or frozen.", this);
            return;
        }

        string localName = GetPlayerDisplayName(localPlayer);
        string targetName = GetPlayerDisplayName(target);
        Debug.Log($"[VOTE DEBUG] Vote button resolved target: {targetName} playerId={target.playerId}", this);
        Debug.Log($"[VOTE DEBUG] CONFIRMED LOCAL VOTE: {localName} will vote for {targetName} targetPlayerId={target.playerId}", this);

        CastVote(localPlayer, target);
    }

    void ResolveGameManagerReference()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    [ContextMenu("Auto Assign Meeting References")]
    void AutoAssignReferences()
    {
        if (!autoFindUiReferences)
        {
            return;
        }

        ResolveGameManagerReference();

        if (meetingPanel == null)
        {
            Transform panelTransform = FindSceneTransformByName("meetingPanel");
            if (panelTransform == null)
            {
                panelTransform = FindSceneTransformByName("MeetingPanel");
            }

            if (panelTransform != null)
            {
                meetingPanel = panelTransform.gameObject;
            }
        }

        if (meetingPanel != null)
        {
            if (voteButtons == null)
            {
                voteButtons = new List<Button>(4);
            }

            if (voteButtons.Count == 0)
            {
                Button[] foundButtons = meetingPanel.GetComponentsInChildren<Button>(true);
                Array.Sort(foundButtons, (a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.Ordinal));
                for (int i = 0; i < foundButtons.Length; i++)
                {
                    Button foundButton = foundButtons[i];
                    if (foundButton != null && foundButton.gameObject.name.StartsWith(VoteButtonNamePrefix, StringComparison.Ordinal))
                    {
                        voteButtons.Add(foundButton);
                    }
                }
            }

            if (timerText == null)
            {
                TMP_Text[] texts = meetingPanel.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text text = texts[i];
                    if (text == null)
                    {
                        continue;
                    }

                    string nameLower = text.gameObject.name.ToLowerInvariant();
                    if (nameLower.Contains("timer"))
                    {
                        timerText = text;
                        break;
                    }
                }

                if (timerText == null && texts.Length > 0)
                {
                    timerText = texts[0];
                }
            }
        }

        SanitizeVoteButtonList();
    }

    void SanitizeVoteButtonList()
    {
        if (voteButtons == null)
        {
            voteButtons = new List<Button>(4);
            return;
        }

        for (int i = voteButtons.Count - 1; i >= 0; i--)
        {
            if (voteButtons[i] == null)
            {
                voteButtons.RemoveAt(i);
            }
        }

        while (voteButtons.Count > 4)
        {
            voteButtons.RemoveAt(voteButtons.Count - 1);
        }
    }

    void AutoAssignPersonalAntidoteReferences()
    {
        if (personalAntidotePanel == null)
        {
            GameObject panelObject = GameObject.Find("PersonalAntidotePanel");
            if (panelObject == null)
            {
                Transform panelTransform = FindSceneTransformByName("PersonalAntidotePanel");
                if (panelTransform != null)
                {
                    panelObject = panelTransform.gameObject;
                }
            }

            personalAntidotePanel = panelObject;
        }

        if (personalAntidotePanel != null)
        {
            TMP_Text[] texts = personalAntidotePanel.GetComponentsInChildren<TMP_Text>(true);

            if (personalAntidoteText == null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text text = texts[i];
                    if (text != null && string.Equals(text.gameObject.name, "PersonalAntidoteText", StringComparison.Ordinal))
                    {
                        personalAntidoteText = text;
                        break;
                    }
                }
            }

            if (personalAntidoteTimerText == null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    TMP_Text text = texts[i];
                    if (text != null && string.Equals(text.gameObject.name, "PersonalAntidoteTimerText", StringComparison.Ordinal))
                    {
                        personalAntidoteTimerText = text;
                        break;
                    }
                }
            }

            if (!personalAntidoteUiWiredLogged)
            {
                personalAntidoteUiWiredLogged = true;
                Debug.Log("[ANTIDOTE UI] Personal antidote panel wired.", this);
            }
        }
        else if (!personalAntidotePanelMissingWarningLogged)
        {
            personalAntidotePanelMissingWarningLogged = true;
            Debug.LogWarning("[ANTIDOTE UI] Personal antidote panel missing.", this);
        }
    }

    void AutoAssignPublicAntidoteReferences()
    {
        if (publicAntidoteStatusPanel == null)
        {
            GameObject panelObject = GameObject.Find("PublicAntidoteStatusPanel");
            if (panelObject == null)
            {
                Transform panelTransform = FindSceneTransformByName("PublicAntidoteStatusPanel");
                if (panelTransform != null)
                {
                    panelObject = panelTransform.gameObject;
                }
            }

            publicAntidoteStatusPanel = panelObject;
        }

        if (publicAntidoteStatusPanel == null)
        {
            return;
        }

        TMP_Text[] texts = publicAntidoteStatusPanel.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
            {
                continue;
            }

            if (publicAntidoteStatusText == null && string.Equals(text.gameObject.name, "PublicAntidoteStatusText", StringComparison.Ordinal))
            {
                publicAntidoteStatusText = text;
            }

            if (publicAntidoteTimerText == null && string.Equals(text.gameObject.name, "PublicAntidoteTimerText", StringComparison.Ordinal))
            {
                publicAntidoteTimerText = text;
            }
        }

        if (publicAntidoteCanvasGroup == null)
        {
            publicAntidoteCanvasGroup = publicAntidoteStatusPanel.GetComponent<CanvasGroup>();
        }

        ConfigurePublicAntidoteStatusAsNonBlocking();
    }

    void EnsurePublicAntidoteStatusUi()
    {
        AutoAssignPublicAntidoteReferences();

        if (publicAntidoteStatusPanel != null || !createPublicAntidoteBannerIfMissing)
        {
            return;
        }

        Transform canvasRoot = ResolveHudCanvasRoot();
        if (canvasRoot == null)
        {
            Debug.LogWarning("[ANTIDOTE UI] Cannot create PublicAntidoteStatusPanel: no Canvas found.", this);
            return;
        }

        GameObject panelObject = new GameObject("PublicAntidoteStatusPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        panelObject.transform.SetParent(canvasRoot, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -90f);
        panelRect.sizeDelta = new Vector2(520f, 90f);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.82f);
        panelImage.raycastTarget = false;

        publicAntidoteStatusPanel = panelObject;
        publicAntidoteCanvasGroup = panelObject.GetComponent<CanvasGroup>();

        publicAntidoteStatusText = CreateBannerText(panelObject.transform, "PublicAntidoteStatusText", new Vector2(0f, 18f), 22f, "ANTIDOTE ADMINISTERED");
        publicAntidoteTimerText = CreateBannerText(panelObject.transform, "PublicAntidoteTimerText", new Vector2(0f, -18f), 18f, "Target frozen: 10s");

        panelObject.SetActive(false);
        ConfigurePublicAntidoteStatusAsNonBlocking();
        Debug.Log("[ANTIDOTE UI] Created runtime PublicAntidoteStatusPanel banner.", this);
    }

    static TMP_Text CreateBannerText(Transform parent, string objectName, Vector2 anchoredPosition, float fontSize, string defaultText)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(480f, 36f);

        TMP_Text text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = defaultText;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    Transform ResolveHudCanvasRoot()
    {
        if (meetingPanel != null)
        {
            return meetingPanel.transform.parent;
        }

        if (personalAntidotePanel != null)
        {
            return personalAntidotePanel.transform.parent;
        }

        Canvas canvas = FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        return canvas != null ? canvas.transform : null;
    }

    void ShowPublicAntidoteStatus(PlayerIdentity target, float secondsRemaining)
    {
        string targetName = GetPlayerDisplayName(target);
        string message = $"ANTIDOTE ADMINISTERED TO {targetName}";

        if (publicAntidoteStatusPanel != null)
        {
            publicAntidoteStatusPanel.SetActive(true);
        }
        else if (resultText != null)
        {
            resultText.text = message;
        }

        if (publicAntidoteStatusText != null)
        {
            publicAntidoteStatusText.text = message;
        }

        if (publicAntidoteTimerText != null)
        {
            publicAntidoteTimerText.text = $"Target frozen: {Mathf.CeilToInt(secondsRemaining)}s";
        }

        ConfigurePublicAntidoteStatusAsNonBlocking();
    }

    void UpdatePublicAntidoteStatus(PlayerIdentity target, float secondsRemaining)
    {
        if (publicAntidoteTimerText != null)
        {
            publicAntidoteTimerText.text = $"Target frozen: {Mathf.CeilToInt(secondsRemaining)}s";
            return;
        }

        if (timerText != null && publicAntidoteStatusPanel == null)
        {
            timerText.text = $"Target frozen: {Mathf.CeilToInt(secondsRemaining)}s";
        }
    }

    void HidePublicAntidoteStatus()
    {
        if (publicAntidoteStatusPanel != null)
        {
            publicAntidoteStatusPanel.SetActive(false);
        }
    }

    void ConfigurePublicAntidoteStatusAsNonBlocking()
    {
        if (publicAntidoteStatusPanel == null)
        {
            return;
        }

        if (publicAntidoteCanvasGroup == null)
        {
            publicAntidoteCanvasGroup = publicAntidoteStatusPanel.GetComponent<CanvasGroup>();
            if (publicAntidoteCanvasGroup == null)
            {
                publicAntidoteCanvasGroup = publicAntidoteStatusPanel.AddComponent<CanvasGroup>();
            }
        }

        publicAntidoteCanvasGroup.interactable = false;
        publicAntidoteCanvasGroup.blocksRaycasts = false;

        Image panelImage = publicAntidoteStatusPanel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.raycastTarget = false;
        }

        if (publicAntidoteStatusText != null)
        {
            publicAntidoteStatusText.raycastTarget = false;
        }

        if (publicAntidoteTimerText != null)
        {
            publicAntidoteTimerText.raycastTarget = false;
        }
    }

    void HideVotingUiForAntidoteSequence()
    {
        SetVoteButtonsInteractable(false);

        if (votingPanel != null)
        {
            votingPanel.SetActive(false);
        }

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        ClearResultText();

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }
    }

    Transform FindSceneTransformByName(string objectName)
    {
        Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform transformItem = allTransforms[i];
            if (transformItem == null)
            {
                continue;
            }

            if (transformItem.hideFlags != HideFlags.None)
            {
                continue;
            }

            if (!transformItem.gameObject.scene.IsValid())
            {
                continue;
            }

            if (transformItem.name == objectName)
            {
                return transformItem;
            }
        }

        return null;
    }

    void SubscribeToGameManagerEvents()
    {
        if (gameManager == null)
        {
            return;
        }

        gameManager.OnMeetingStarted -= HandleMeetingStarted;
        gameManager.OnMeetingStarted += HandleMeetingStarted;

        gameManager.OnVotingStarted -= HandleVotingStarted;
        gameManager.OnVotingStarted += HandleVotingStarted;

        gameManager.OnExplorationStarted -= HandleExplorationStarted;
        gameManager.OnExplorationStarted += HandleExplorationStarted;
    }

    void UnsubscribeFromGameManagerEvents()
    {
        if (gameManager == null)
        {
            return;
        }

        gameManager.OnMeetingStarted -= HandleMeetingStarted;
        gameManager.OnVotingStarted -= HandleVotingStarted;
        gameManager.OnExplorationStarted -= HandleExplorationStarted;
    }

    void HandleMeetingStarted()
    {
        StartMeeting();
    }

    void HandleVotingStarted()
    {
        StartVoting();
    }

    void HandleExplorationStarted()
    {
        if (activeAntidoteRoutine != null || (activeAntidoteTarget != null && activeAntidoteTarget.isFrozen))
        {
            return;
        }

        if (flowState == MeetingFlowState.Curing)
        {
            return;
        }

        if (flowState == MeetingFlowState.Voting && !votingResolved)
        {
            CastMissingVotesRandomly();
            ResolveVotingOutcome();
            return;
        }

        if (flowState != MeetingFlowState.Idle)
        {
            FinalizeMeetingFlow();
        }
    }

    void RefreshAlivePlayers()
    {
        alivePlayers.Clear();

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            alivePlayers.Add(player);
        }

        alivePlayers.Sort((a, b) => a.playerId.CompareTo(b.playerId));
    }

    void ConfigureVoteButtons(bool interactable)
    {
        EnsureVoteButtonsSortedBySlot();

        int buttonCount = voteButtons != null ? voteButtons.Count : 0;
        for (int slot = 0; slot < buttonCount && slot < MaxVoteSlots; slot++)
        {
            Button button = voteButtons[slot];
            if (button == null)
            {
                continue;
            }

            int expectedPlayerId = slot + 1;
            PlayerIdentity target = FindPlayerById(expectedPlayerId);
            bool hasTarget = target != null && target.isAlive && !target.isFrozen;

            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(hasTarget);
            button.interactable = interactable && hasTarget && !localVoteCast;

            if (!hasTarget)
            {
                TMP_Text hiddenLabel = button.GetComponentInChildren<TMP_Text>(true);
                if (hiddenLabel != null)
                {
                    hiddenLabel.text = $"Player {expectedPlayerId}";
                }

                continue;
            }

            string labelText = GetVoteButtonLabel(target, expectedPlayerId);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = labelText;
            }

            int capturedPlayerId = target.playerId;
            button.onClick.AddListener(() => OnVoteButtonPressedForPlayerId(capturedPlayerId));

            Debug.Log($"[VOTE DEBUG] Button slot {slot} label='{labelText}' target={GetPlayerDisplayName(target)} playerId={target.playerId}", this);
        }
    }

    void SetVoteButtonsInteractable(bool interactable)
    {
        if (voteButtons == null)
        {
            return;
        }

        for (int i = 0; i < voteButtons.Count; i++)
        {
            Button button = voteButtons[i];
            if (button == null || !button.gameObject.activeSelf)
            {
                continue;
            }

            button.interactable = interactable && !localVoteCast;
        }
    }

    void EnsureVoteButtonsSortedBySlot()
    {
        if (voteButtons == null || voteButtons.Count <= 1)
        {
            return;
        }

        voteButtons.Sort((a, b) =>
        {
            int idA = TryParsePlayerIdFromButton(a) ?? 999;
            int idB = TryParsePlayerIdFromButton(b) ?? 999;
            return idA.CompareTo(idB);
        });
    }

    static string GetVoteButtonLabel(PlayerIdentity target, int expectedPlayerId)
    {
        if (target == null)
        {
            return $"Player {expectedPlayerId}";
        }

        if (!string.IsNullOrWhiteSpace(target.playerName))
        {
            return target.playerName;
        }

        return $"Player {expectedPlayerId}";
    }

    static int? TryParsePlayerIdFromButton(Button button)
    {
        if (button == null)
        {
            return null;
        }

        string objectName = button.gameObject.name;
        if (!objectName.StartsWith(VoteButtonNamePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string suffix = objectName.Substring(VoteButtonNamePrefix.Length);
        if (int.TryParse(suffix, out int playerId))
        {
            return playerId;
        }

        return null;
    }

    PlayerIdentity GetLocalPlayerStrict()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        PlayerIdentity soleLocal = null;
        int localCount = 0;

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.IsLocalPlayer)
            {
                continue;
            }

            localCount++;
            soleLocal = player;
        }

        if (localCount == 1)
        {
            return soleLocal;
        }

        if (localCount == 0)
        {
            PlayerIdentity fallback = FindPlayerById(1);
            Debug.LogWarning("[VOTE DEBUG] No isLocalPlayer found. Falling back to Player 1.", this);
            if (fallback != null)
            {
                fallback.SetLocalPlayerForPrototype(true);
            }

            return fallback;
        }

        Debug.LogError("[VOTE DEBUG] Multiple local players found. Auto-fixing: only Player 1 remains local.", this);
        PlayerIdentity playerOne = FindPlayerById(1);
        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null)
            {
                continue;
            }

            player.SetLocalPlayerForPrototype(player == playerOne);
        }

        return playerOne;
    }

    void LogLocalPlayerStrict(string context)
    {
        PlayerIdentity local = GetLocalPlayerStrict();
        if (local == null)
        {
            Debug.LogWarning($"[VOTE DEBUG] Local player strict ({context}) = NONE", this);
            return;
        }

        Debug.Log($"[VOTE DEBUG] Local player strict ({context}) = {GetPlayerDisplayName(local)} playerId={local.playerId}", this);
    }

    int CountEligibleVoters()
    {
        int count = 0;
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter != null && voter.isAlive && !voter.isFrozen)
            {
                count++;
            }
        }

        return count;
    }

    void CastAIVotes()
    {
        if (disableAIVotesForManualTesting)
        {
            Debug.Log("[VOTE DEBUG] AI votes disabled for manual testing.", this);
            return;
        }

        if (!enablePrototypeAIVotes)
        {
            return;
        }

        if (useTeamAVoteApi && TeamAApiClient.Instance != null && TeamAApiClient.Instance.EnableApiCalls)
        {
            StartCoroutine(CastAIVotesViaTeamA());
            return;
        }

        // Fallback to local behavior
        CastAIVotesLocally();
    }

    void CastMissingVotesRandomly()
    {
        if (disableAIVotesForManualTesting)
        {
            return;
        }

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive || voter.isFrozen)
            {
                continue;
            }

            if (playersWhoVoted.Contains(voter.playerId))
            {
                continue;
            }

            PlayerIdentity target = GetRandomVoteTarget(voter);
            if (target != null && target != voter)
            {
                CastVote(voter, target, isAiVote: true);
            }
        }
    }

    // Preserve existing local AI vote behavior here
    void CastAIVotesLocally()
    {
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive || voter.isFrozen)
            {
                continue;
            }

            if (voter == localVoter)
            {
                continue;
            }

            bool isAI = voter.isAIControlled;
            bool isNonLocalHuman = !isAI && !voter.IsLocalPlayer;
            bool shouldVote = isAI || (isNonLocalHuman && allowNonLocalHumansToAutoVote);

            if (!shouldVote)
            {
                continue;
            }

            PlayerIdentity target = GetRandomVoteTarget(voter);
            if (target != null && target != voter)
            {
                Debug.Log($"[VOTE DEBUG] Local fallback AI vote: {GetPlayerDisplayName(voter)} -> {GetPlayerDisplayName(target)}", this);
                CastVote(voter, target, isAiVote: true);
            }
        }
    }

    IEnumerator CastAIVotesViaTeamA()
    {
        if (disableAIVotesForManualTesting)
        {
            Debug.Log("[VOTE DEBUG] AI votes disabled for manual testing.", this);
            yield break;
        }

        if (TeamAApiClient.Instance == null)
        {
            Debug.Log("[VOTE DEBUG] TeamAApiClient missing. Falling back to local AI votes.", this);
            CastAIVotesLocally();
            yield break;
        }

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive || voter.isFrozen)
            {
                continue;
            }

            if (voter == localVoter)
            {
                continue;
            }

            bool isAI = voter.isAIControlled;
            bool isNonLocalHuman = !isAI && !voter.IsLocalPlayer;
            bool shouldVote = isAI || (isNonLocalHuman && allowNonLocalHumansToAutoVote);

            if (!shouldVote)
            {
                continue;
            }

            // Build vote request snapshot for this voter
            VoteRequest request = BuildVoteRequestForVoter(voter);

            bool callbackInvoked = false;
            bool ok = false;
            VoteResponse response = null;

            if (!TeamAApiClient.Instance.EnableApiCalls)
            {
                Debug.Log("[VOTE DEBUG] Team A API disabled. Using local fallback.", this);
                AgentTracePanel.Trace("API", $"Vote fallback for {GetPlayerDisplayName(voter)}.");
                PlayerIdentity fallbackTarget = GetRandomVoteTarget(voter);
                if (fallbackTarget != null && fallbackTarget != voter)
                {
                    Debug.Log($"[VOTE DEBUG] Local fallback AI vote: {GetPlayerDisplayName(voter)} -> {GetPlayerDisplayName(fallbackTarget)}", this);
                    CastVote(voter, fallbackTarget, isAiVote: false);
                }
                continue;
            }

            // Start API call
            StartCoroutine(TeamAApiClient.Instance.Vote(request, (s, r) =>
            {
                callbackInvoked = true;
                ok = s;
                response = r;
            }));

            float waited = 0f;
            while (!callbackInvoked && waited < voteApiResponseTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!callbackInvoked)
            {
                Debug.Log($"[VOTE DEBUG] Team A vote timeout for {GetPlayerDisplayName(voter)}. Using local fallback.", this);
                AgentTracePanel.Trace("API", $"Vote fallback for {GetPlayerDisplayName(voter)}.");
                PlayerIdentity fallback = GetRandomVoteTarget(voter);
                if (fallback != null && fallback != voter)
                {
                    Debug.Log($"[VOTE DEBUG] Local fallback AI vote: {GetPlayerDisplayName(voter)} -> {GetPlayerDisplayName(fallback)}", this);
                    CastVote(voter, fallback, isAiVote: false);
                }
                continue;
            }

            if (ok && response != null && !string.IsNullOrWhiteSpace(response.voteTarget))
            {
                PlayerIdentity apiTarget = FindPlayerByApiIdOrName(response.voteTarget);
                if (apiTarget != null && apiTarget.isAlive && !apiTarget.isFrozen)
                {
                    Debug.Log($"[VOTE DEBUG] Team A vote: {GetPlayerDisplayName(voter)} -> {GetPlayerDisplayName(apiTarget)}", this);
                    AgentTracePanel.Trace("VOTE", $"{GetPlayerDisplayName(voter)} chose antidote target {GetPlayerDisplayName(apiTarget)}.");
                    CastVote(voter, apiTarget, isAiVote: false);
                    continue;
                }
            }

            // Fallback if response invalid
            Debug.Log($"[VOTE DEBUG] Team A vote fallback: {GetPlayerDisplayName(voter)} -> local random target.", this);
            AgentTracePanel.Trace("API", $"Vote fallback for {GetPlayerDisplayName(voter)}.");
            PlayerIdentity fb = GetRandomVoteTarget(voter);
            if (fb != null && fb != voter)
            {
                Debug.Log($"[VOTE DEBUG] Local fallback AI vote: {GetPlayerDisplayName(voter)} -> {GetPlayerDisplayName(fb)}", this);
                CastVote(voter, fb, isAiVote: false);
            }
        }
    }

    VoteRequest BuildVoteRequestForVoter(PlayerIdentity voter)
    {
        VoteRequest req = new VoteRequest
        {
            matchId = InfectionSystem.Instance != null ? InfectionSystem.Instance.MatchId : "ROOM123",
            phase = GameManager.CurrentPhase.ToString(),
            wave = GameManager.CurrentWave,
            cycle = 1,
            botId = InfectionSystem.GetApiPlayerId(voter),
            botName = GetPlayerDisplayName(voter),
            alivePlayers = GetAlivePlayerApiIds(),
            humanPlayers = GetHumanPlayerApiIds(),
            infectedPlayers = GetInfectedPlayerApiIds(),
            recentChat = new ChatMessageDto[0]
        };

        return req;
    }

    string GetApiPlayerId(PlayerIdentity player)
    {
        return InfectionSystem.GetApiPlayerId(player);
    }

    string[] GetAlivePlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isAlive) ids.Add(GetApiPlayerId(p));
        }
        return ids.ToArray();
    }

    string[] GetHumanPlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isAlive && !p.isInfected) ids.Add(GetApiPlayerId(p));
        }
        return ids.ToArray();
    }

    string[] GetInfectedPlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isInfected) ids.Add(GetApiPlayerId(p));
        }
        return ids.ToArray();
    }

    PlayerIdentity FindPlayerByApiIdOrName(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        string s = idOrName.Trim();
        // Try formats: player_1, player 1, Player 1, 1
        string lower = s.ToLowerInvariant();

        // player_N
        if (lower.StartsWith("player_"))
        {
            string num = lower.Substring("player_".Length);
            if (int.TryParse(num, out int pid))
            {
                return FindPlayerById(pid);
            }
        }

        // Player N (with space)
        if (lower.StartsWith("player "))
        {
            string num = lower.Substring("player ".Length);
            if (int.TryParse(num, out int pid2))
            {
                return FindPlayerById(pid2);
            }
        }

        // numeric id
        if (int.TryParse(lower, out int pid3))
        {
            return FindPlayerById(pid3);
        }

        // match by display name (case-insensitive)
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            string display = GetPlayerDisplayName(p);
            if (string.Equals(display, s, StringComparison.OrdinalIgnoreCase)) return p;
            if (string.Equals(p.playerName, s, StringComparison.OrdinalIgnoreCase)) return p;
        }

        return null;
    }

    void CastVote(PlayerIdentity voter, PlayerIdentity target, bool isAiVote = false)
    {
        if (voter == null || target == null)
        {
            return;
        }

        if (flowState != MeetingFlowState.Voting || votingResolved)
        {
            return;
        }

        if (!voter.isAlive || !target.isAlive || voter.isFrozen || target.isFrozen)
        {
            return;
        }

        if (playersWhoVoted.Contains(voter.playerId))
        {
            Debug.LogWarning($"[VOTE DEBUG] {GetPlayerDisplayName(voter)} already voted. Duplicate vote rejected.", this);
            return;
        }

        votesByVoterId[voter.playerId] = target.playerId;
        playersWhoVoted.Add(voter.playerId);

        string voterName = GetPlayerDisplayName(voter);
        string targetName = GetPlayerDisplayName(target);

        Debug.Log($"[VOTE DEBUG] STORED VOTE voterId={voter.playerId} targetId={target.playerId}", this);
        AgentTracePanel.Trace("VOTE", $"{voterName} voted antidote for {targetName}.");

        if (isAiVote)
        {
            Debug.Log($"[VOTE DEBUG] AI vote cast: {voterName} -> {targetName}", this);
        }

        if (voter == localVoter || voter.IsLocalPlayer)
        {
            localVoteCast = true;
            SetVoteButtonsInteractable(false);
            Debug.Log("[VOTE DEBUG] Local player vote cast. Vote buttons disabled without rebind.", this);
        }

        TryResolveVotingEarly();
    }

    void TryResolveVotingEarly()
    {
        if (flowState != MeetingFlowState.Voting || votingResolved)
        {
            return;
        }

        int eligibleVoters = CountEligibleVoters();
        if (eligibleVoters > 0 && playersWhoVoted.Count >= eligibleVoters)
        {
            Debug.Log("[VOTE DEBUG] All eligible votes received. Resolving early.", this);
            ResolveVotingOutcome();
        }
    }

    PlayerIdentity GetRandomVoteTarget(PlayerIdentity voter)
    {
        if (voter == null)
        {
            return null;
        }

        List<PlayerIdentity> validTargets = new List<PlayerIdentity>(alivePlayers.Count);
        List<PlayerIdentity> preferredHumanTargets = new List<PlayerIdentity>(alivePlayers.Count);

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity candidate = alivePlayers[i];
            if (candidate == null || !candidate.isAlive || candidate.isFrozen || candidate == voter)
            {
                continue;
            }

            validTargets.Add(candidate);
            if (!candidate.isInfected)
            {
                preferredHumanTargets.Add(candidate);
            }
        }

        if (validTargets.Count == 0)
        {
            return null;
        }

        List<PlayerIdentity> pool = preferredHumanTargets.Count > 0 ? preferredHumanTargets : validTargets;
        int index = UnityEngine.Random.Range(0, pool.Count);
        return pool[index];
    }

    void ResolveVotingOutcome()
    {
        if (votingResolved)
        {
            return;
        }

        votingResolved = true;
        SetVoteButtonsInteractable(false);
        ClearResultText();

        if (votesByVoterId.Count == 0)
        {
            const string noVotesMsg = "No antidote used.";
            AgentTracePanel.Trace("VOTE", noVotesMsg);
            Debug.Log($"[VOTE DEBUG] {noVotesMsg}", this);
            SetResultText(noVotesMsg);
            EndVotingWithoutAntidote();
            return;
        }

        Dictionary<int, int> tallyByTargetId = new Dictionary<int, int>(votesByVoterId.Count);
        foreach (KeyValuePair<int, int> vote in votesByVoterId)
        {
            int targetId = vote.Value;
            if (!tallyByTargetId.ContainsKey(targetId))
            {
                tallyByTargetId[targetId] = 0;
            }

            tallyByTargetId[targetId]++;
        }

        Debug.Log("[VOTE DEBUG] Vote tally by targetId:", this);
        int topPlayerId = -1;
        int topVotes = 0;
        int tieCount = 0;

        for (int slot = 1; slot <= MaxVoteSlots; slot++)
        {
            int votes = tallyByTargetId.TryGetValue(slot, out int count) ? count : 0;
            PlayerIdentity player = FindPlayerById(slot);
            string playerName = player != null ? GetPlayerDisplayName(player) : $"Player {slot}";
            Debug.Log($"[VOTE DEBUG]   targetId={slot} {playerName} votes={votes}", this);
        }

        foreach (KeyValuePair<int, int> entry in tallyByTargetId)
        {
            if (entry.Value <= 0)
            {
                continue;
            }

            if (entry.Value > topVotes)
            {
                topVotes = entry.Value;
                topPlayerId = entry.Key;
                tieCount = 1;
            }
            else if (entry.Value == topVotes)
            {
                tieCount++;
            }
        }

        if (topVotes <= 0)
        {
            const string noVotesMsg = "No antidote used.";
            SetResultText(noVotesMsg);
            EndVotingWithoutAntidote();
            return;
        }

        if (tieCount > 1)
        {
            const string tieMsg = "No consensus. No antidote used.";
            AgentTracePanel.Trace("VOTE", tieMsg);
            Debug.Log($"[VOTE DEBUG] TIE DETECTED. {tieCount} players tied with {topVotes} votes. {tieMsg}", this);
            SetResultText(tieMsg);
            EndVotingWithoutAntidote();
            return;
        }

        PlayerIdentity winner = FindPlayerById(topPlayerId);
        if (winner == null || !winner.isAlive)
        {
            Debug.LogWarning($"[VOTE DEBUG] Winner playerId {topPlayerId} not found or dead.", this);
            EndVotingWithoutAntidote();
            return;
        }

        if (winner.isFrozen)
        {
            Debug.Log($"[VOTE DEBUG] Winner {GetPlayerDisplayName(winner)} already frozen. Skipping antidote.", this);
            EndVotingWithoutAntidote();
            return;
        }

        Debug.Log($"[VOTE DEBUG] WINNER TARGET CONFIRMED: {GetPlayerDisplayName(winner)} playerId={topPlayerId}", this);
        Debug.Log("[MOVE LOCK] Voting resolved. Restoring non-frozen players.", this);
        StartAntidoteSequence(winner);
    }

    void EndVotingWithoutAntidote()
    {
        flowState = MeetingFlowState.Curing;
        phaseTimer = 0f;
        Debug.Log("[MOVE LOCK] Voting resolved with no antidote. Restoring non-frozen players.", this);
        RestoreControlForNonFrozenPlayersAfterVoting();
        FinalizeMeetingFlow();
        TryEnterExplorationAfterMeeting();
    }

    void StartAntidoteSequence(PlayerIdentity target)
    {
        if (target == null || !target.isAlive)
        {
            Debug.LogWarning("[ANTIDOTE] StartAntidoteSequence called with invalid target.", this);
            EndVotingWithoutAntidote();
            return;
        }

        StopActiveAntidoteRoutineIfNeeded();

        activeAntidoteTarget = target;
        flowState = MeetingFlowState.Curing;
        phaseTimer = antidoteFreezeDuration;

        string playerName = GetPlayerDisplayName(target);
        AgentTracePanel.Trace("VOTE", $"Antidote administered to {playerName}.");
        Debug.Log($"[ANTIDOTE DEBUG] ApplyAntidote target={playerName} playerId={target.playerId} isLocalTarget={target.IsLocalPlayer}", this);
        Debug.Log($"[ANTIDOTE] Antidote sequence started for {playerName} (playerId={target.playerId}). duration={antidoteFreezeDuration:0.0}s.", this);

        HideVotingUiForAntidoteSequence();
        RestoreControlForNonFrozenPlayersAfterVoting();

        activeAntidoteRoutine = StartCoroutine(AntidoteFreezeRoutine(target));
    }

    void StopActiveAntidoteRoutineIfNeeded()
    {
        if (activeAntidoteRoutine == null)
        {
            return;
        }

        StopCoroutine(activeAntidoteRoutine);
        activeAntidoteRoutine = null;

        if (activeAntidoteTarget != null && activeAntidoteTarget.isFrozen)
        {
            activeAntidoteTarget.UnfreezePlayer();
        }

        HidePersonalAntidoteOverlay();
        HidePublicAntidoteStatus();
    }

    IEnumerator AntidoteFreezeRoutine(PlayerIdentity target)
    {
        if (target == null || !target.isAlive)
        {
            activeAntidoteRoutine = null;
            yield break;
        }

        int frozenTargetPlayerId = target.playerId;
        bool isLocalTarget = ShouldShowPersonalAntidoteOverlay(target);
        string targetName = GetPlayerDisplayName(target);

        HidePersonalAntidoteOverlay();
        HideVotingUiForAntidoteSequence();

        if (InfectionSystem.Instance != null)
        {
            InfectionSystem.Instance.CancelLegacyAntidoteFreeze(target);
        }

        target.FreezePlayer("Antidote");
        Debug.Log($"[ANTIDOTE] Freeze started for {targetName} duration={antidoteFreezeDuration:0.0}", this);

        ShowPublicAntidoteStatus(target, antidoteFreezeDuration);

        if (isLocalTarget)
        {
            ShowPersonalAntidoteOverlay(target, antidoteFreezeDuration);
        }
        else
        {
            HidePersonalAntidoteOverlay();
            Debug.Log("[ANTIDOTE UI] Non-local target. Personal overlay hidden.", this);
        }

        float remaining = antidoteFreezeDuration;
        while (remaining > 0f)
        {
            if (target == null || !target.isAlive || target.playerId != frozenTargetPlayerId)
            {
                break;
            }

            remaining -= Time.deltaTime;
            if (remaining < 0f)
            {
                remaining = 0f;
            }

            phaseTimer = remaining;
            UpdatePublicAntidoteStatus(target, remaining);

            if (isLocalTarget)
            {
                UpdatePersonalAntidoteCountdown(target, remaining);
            }

            yield return null;
        }

        if (target != null && target.isAlive && target.playerId == frozenTargetPlayerId)
        {
            bool cured = ResolveCureSilently(target);
            Debug.Log($"[ANTIDOTE DEBUG] Silent cure resolved for {targetName}. cured={cured}", this);
            target.UnfreezePlayer();
            Debug.Log($"[ANTIDOTE] Freeze ended for {targetName}", this);
        }

        HidePersonalAntidoteOverlay();
        HidePublicAntidoteStatus();

        activeAntidoteTarget = null;
        activeAntidoteRoutine = null;
        phaseTimer = 0f;

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }

        FinalizeMeetingFlow();
        TryEnterExplorationAfterMeeting();
    }

    bool ResolveCureSilently(PlayerIdentity target)
    {
        if (target == null || !target.isAlive || !CanBeCured(target))
        {
            return false;
        }

        if (InfectionSystem.Instance != null)
        {
            InfectionSystem.Instance.ApplyVoteCure(target);
        }
        else
        {
            target.Cure();
            target.RefreshInfectionVisual();

            BotMovement botMovement = target.GetComponent<BotMovement>();
            if (botMovement != null)
            {
                botMovement.enabled = false;
            }

            target.RefreshControlState();
        }

        if (InfectionSystem.Instance != null)
        {
            string botId = InfectionSystem.GetApiPlayerId(target);
            InfectionSystem.Instance.RemoveRegisteredBotApiId(botId);
        }

        TryUnregisterBotWithTeamA(target);

        if (showPrivateAntidoteDebugTrace)
        {
            AgentTracePanel.Trace("VOTE_DEBUG", $"{GetPlayerDisplayName(target)} cure resolved silently.");
        }

        return true;
    }

    bool CanBeCured(PlayerIdentity target)
    {
        return target != null && target.isInfected;
    }

    bool ShouldShowPersonalAntidoteOverlay(PlayerIdentity target)
    {
        if (target == null || !target.IsLocalPlayer)
        {
            return false;
        }

        PlayerIdentity strictLocal = GetLocalPlayerStrict();
        return strictLocal != null && strictLocal.playerId == target.playerId;
    }

    void ShowPersonalAntidoteOverlay(PlayerIdentity target, float freezeDuration)
    {
        HidePersonalAntidoteOverlay();

        string targetName = GetPlayerDisplayName(target);
        if (!ShouldShowPersonalAntidoteOverlay(target))
        {
            Debug.Log($"[ANTIDOTE UI] Personal overlay not shown because target is not local: {targetName}", this);
            return;
        }

        if (personalAntidotePanel == null)
        {
            if (!personalAntidotePanelMissingWarningLogged)
            {
                personalAntidotePanelMissingWarningLogged = true;
                Debug.LogWarning("[ANTIDOTE UI] personalAntidotePanel not assigned. Cannot show personal overlay.", this);
            }

            return;
        }

        activePersonalAntidoteTarget = target;
        personalAntidotePanel.SetActive(true);
        personalAntidoteOverlayHiddenLogged = false;

        if (personalAntidoteText != null)
        {
            personalAntidoteText.text = "ANTIDOTE ADMINISTERED\nPLEASE WAIT";
        }

        if (personalAntidoteTimerText != null)
        {
            personalAntidoteTimerText.text = Mathf.CeilToInt(freezeDuration).ToString();
        }

        Debug.Log($"[ANTIDOTE UI] Personal antidote overlay shown for {targetName}.", this);
    }

    void HidePersonalAntidoteOverlay()
    {
        if (personalAntidotePanel == null)
        {
            activePersonalAntidoteTarget = null;
            return;
        }

        bool wasActive = personalAntidotePanel.activeSelf;
        personalAntidotePanel.SetActive(false);
        activePersonalAntidoteTarget = null;

        if (wasActive && !personalAntidoteOverlayHiddenLogged)
        {
            personalAntidoteOverlayHiddenLogged = true;
            Debug.Log("[ANTIDOTE UI] Personal antidote overlay hidden.", this);
        }
    }

    void UpdatePersonalAntidoteCountdown(PlayerIdentity target, float remainingSeconds)
    {
        if (!ShouldShowPersonalAntidoteOverlay(target))
        {
            return;
        }

        if (activePersonalAntidoteTarget != target)
        {
            return;
        }

        if (personalAntidotePanel == null || !personalAntidotePanel.activeInHierarchy)
        {
            return;
        }

        if (personalAntidoteTimerText != null)
        {
            personalAntidoteTimerText.text = Mathf.CeilToInt(remainingSeconds).ToString();
        }
    }

    PlayerIdentity FindPlayerById(int playerId)
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player != null && player.playerId == playerId)
            {
                return player;
            }
        }

        return null;
    }

    void FinalizeMeetingFlow()
    {
        if (activeAntidoteTarget != null && activeAntidoteTarget.isFrozen)
        {
            return;
        }

        flowState = MeetingFlowState.Idle;

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }

        HidePersonalAntidoteOverlay();
        HidePublicAntidoteStatus();
        RestoreMovementAfterMeeting();
    }

    void TryEnterExplorationAfterMeeting()
    {
        if (!autoAdvanceGameManagerPhases || gameManager == null)
        {
            return;
        }

        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
        if (finalHuntManager != null && finalHuntManager.IsFinalHuntActive)
        {
            return;
        }

        if (GameManager.CurrentPhase == GamePhase.Voting || GameManager.CurrentPhase == GamePhase.Meeting)
        {
            gameManager.EnterExploration();
        }
    }

    void ApplyMeetingVotingMovementLock()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            PlayerMovement playerMvmt = player.GetComponent<PlayerMovement>();
            if (playerMvmt != null)
            {
                playerMvmt.enabled = false;
            }

            BotMovement botMovement = player.GetComponent<BotMovement>();
            if (botMovement != null)
            {
                botMovement.enabled = false;
            }

            LogPlayerMovementState(player, "[MOVE LOCK] Meeting/Voting lock applied to");
        }
    }

    void RestoreControlForNonFrozenPlayersAfterVoting()
    {
        PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < players.Length; i++)
        {
            PlayerIdentity player = players[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            if (player.isFrozen)
            {
                Debug.Log($"[MOVE LOCK] Not restoring {GetPlayerDisplayName(player)}: frozen.", this);
                continue;
            }

            player.ApplyControlState();
            LogPlayerMovementState(player, "[MOVE LOCK] Restore check");
            Debug.Log($"[MOVE LOCK] Restored control for {GetPlayerDisplayName(player)}. frozen={player.isFrozen} infected={player.isInfected} ai={player.isAIControlled} local={player.isLocalPlayer}", this);
        }
    }

    void RestoreMovementAfterMeeting()
    {
        RestoreControlForNonFrozenPlayersAfterVoting();
    }

    static void LogPlayerMovementState(PlayerIdentity player, string prefix)
    {
        if (player == null)
        {
            return;
        }

        PlayerMovement playerMovement = player.GetComponent<PlayerMovement>();
        BotMovement botMovement = player.GetComponent<BotMovement>();
        bool playerMovementEnabled = playerMovement != null && playerMovement.enabled;
        bool botMovementEnabled = botMovement != null && botMovement.enabled;

        Debug.Log(
            $"{prefix} {GetPlayerDisplayName(player)} movementEnabled={playerMovementEnabled} frozen={player.isFrozen} infected={player.isInfected} ai={player.isAIControlled} local={player.isLocalPlayer} playerMovementEnabled={playerMovementEnabled} botMovementEnabled={botMovementEnabled}",
            player);
    }

    void SetResultText(string message)
    {
        if (resultText != null)
        {
            resultText.text = message;
        }
    }

    void ClearResultText()
    {
        if (resultText != null)
        {
            resultText.text = string.Empty;
        }
    }

    static string GetPlayerDisplayName(PlayerIdentity player)
    {
        if (player == null)
        {
            return "Unknown";
        }

        return string.IsNullOrWhiteSpace(player.playerName) ? player.gameObject.name : player.playerName;
    }

    void HandleDebugHotkeys()
    {
        if (!enableDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.M))
        {
            StartMeeting();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            StartVoting();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            EndVoting();
        }

        if (flowState != MeetingFlowState.Voting)
        {
            return;
        }

        PlayerIdentity localPlayer = GetLocalPlayerStrict();
        if (localPlayer == null)
        {
            return;
        }

        localVoter = localPlayer;

        for (int slot = 0; slot < MaxVoteSlots; slot++)
        {
            KeyCode voteKey = KeyCode.Alpha1 + slot;
            if (!Input.GetKeyDown(voteKey))
            {
                continue;
            }

            int targetPlayerId = slot + 1;
            OnVoteButtonPressedForPlayerId(targetPlayerId);
        }
    }

    void UpdateTimerText()
    {
        if (timerText == null)
        {
            return;
        }

        if (flowState == MeetingFlowState.Curing && activeAntidoteTarget != null)
        {
            return;
        }

        string phaseLabel = flowState == MeetingFlowState.Voting ? "Voting" : flowState == MeetingFlowState.Curing ? "Curing" : "Discussion";
        timerText.text = $"{phaseLabel} : {Mathf.CeilToInt(phaseTimer)}";
    }

    void TryUnregisterBotWithTeamA(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        if (TeamAApiClient.Instance == null)
        {
            Debug.Log("[ANTIDOTE] unregister_bot skipped: TeamAApiClient missing.");
            return;
        }

        string botId = InfectionSystem.GetApiPlayerId(target);
        if (string.IsNullOrWhiteSpace(botId))
        {
            return;
        }

        UnregisterBotRequest request = new UnregisterBotRequest
        {
            matchId = InfectionSystem.Instance != null ? InfectionSystem.Instance.MatchId : "ROOM123",
            botId = botId,
            reason = "antidote_cure"
        };

        StartCoroutine(TeamAApiClient.Instance.UnregisterBot(request, (ok, response) =>
        {
            if (!showPrivateAntidoteDebugTrace)
            {
                return;
            }

            string playerName = InfectionSystem.GetDisplayName(target);
            if (ok && response != null && response.ok)
            {
                AgentTracePanel.Trace("VOTE_DEBUG", $"{playerName} bot unregister succeeded.");
            }
            else
            {
                AgentTracePanel.Trace("VOTE_DEBUG", $"{playerName} bot unregister fallback.");
            }
        }));
    }
}
