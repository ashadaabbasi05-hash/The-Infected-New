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

    [Header("References")]
    [SerializeField] GameManager gameManager;
    [SerializeField] GameObject meetingPanel;
    [Tooltip("Assign up to 4 vote buttons (one per player). If empty, script will auto-find buttons under meetingPanel.")]
    [SerializeField] List<Button> voteButtons = new List<Button>(4);
    [SerializeField] TMP_Text timerText;

    [Header("Auto Setup")]
    [SerializeField] bool autoFindUiReferences = true;

    [Header("Timing")]
    [SerializeField, Min(1f)] float discussionDuration = 20f;
    [SerializeField, Min(1f)] float votingDuration = 20f;
    [SerializeField, Min(1f)] float antidoteFreezeDuration = 10f;

    [Header("Antidote UI")]
    [SerializeField, Tooltip("Personal antidote/curing overlay shown to local player if they are antidote target.")]
    GameObject personalAntidotePanel;
    [SerializeField] TMP_Text personalAntidoteText;
    [SerializeField] TMP_Text personalAntidoteTimerText;

    [Header("Behavior")]
    [Tooltip("If true, this controller tells GameManager to switch Voting/Exploration phases automatically when timers end.")]
    [SerializeField] bool autoAdvanceGameManagerPhases = true;
    [SerializeField] bool enablePrototypeAIVotes = true;
    [SerializeField] bool disableAIVotesForManualTesting = false;
    [SerializeField] bool allowNonLocalHumansToAutoVote = true;
    [SerializeField] bool enableDebugHotkeys = true;
    [SerializeField] bool showPrivateAntidoteDebugTrace = false;

    [Header("UI Feedback")]
    [SerializeField] TMP_Text resultText;

    readonly List<PlayerIdentity> alivePlayers = new List<PlayerIdentity>(4);
    readonly Dictionary<int, int> votesByTargetPlayerId = new Dictionary<int, int>(4);
    readonly HashSet<int> playersWhoVoted = new HashSet<int>();

    MeetingFlowState flowState = MeetingFlowState.Idle;
    float phaseTimer;
    bool hasResolvedVoting;
    bool localVoteCast;
    bool personalAntidotePanelMissingWarningLogged;
    bool personalAntidoteUiWiredLogged;

    PlayerIdentity localVoter;
    PlayerIdentity currentAntidoteTarget;
    Coroutine activeAntidoteRoutine;

    void Awake()
    {
        AutoAssignReferences();
        AutoAssignPersonalAntidoteReferences();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }

        HidePersonalAntidoteOverlay();
        ResolveGameManagerReference();
    }

    void Start()
    {
        AutoAssignReferences();
        AutoAssignPersonalAntidoteReferences();

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

        phaseTimer -= Time.deltaTime;
        if (phaseTimer < 0f)
        {
            phaseTimer = 0f;
        }

        UpdateTimerText();

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

            if (autoAdvanceGameManagerPhases && gameManager != null && GameManager.CurrentPhase == GamePhase.Voting)
            {
                gameManager.EnterExploration();
            }

            return;
        }

        EndVoting();
    }

    // Called by GameManager event or manually for prototype scenes.
    public void StartMeeting()
    {
        RefreshAlivePlayers();

        flowState = MeetingFlowState.Discussion;
        phaseTimer = discussionDuration;
        hasResolvedVoting = false;

        localVoter = FindLocalVoter();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

        // Disable all player movement during discussion/voting.
        DisableAllMovement();
        ConfigureVoteButtons(false);
        ClearResultText();
        UpdateTimerText();

        Debug.Log("Meeting started.", this);
    }

    // Called by GameManager event or manually for prototype scenes.
    public void StartVoting()
    {
        if (flowState == MeetingFlowState.Voting)
        {
            return;
        }

        AgentTracePanel.Trace("MEETING", "Voting started. Antidote target selection active.");

        RefreshAlivePlayers();

        // CRITICAL: Clean state for new voting phase
        votesByTargetPlayerId.Clear();
        playersWhoVoted.Clear();
        localVoteCast = false;  // ← Used here to reset local player vote flag
        currentAntidoteTarget = null;
        HidePersonalAntidoteOverlay();

        flowState = MeetingFlowState.Voting;
        phaseTimer = votingDuration;
        hasResolvedVoting = false;

        localVoter = FindLocalVoter();

        EnsureVoteContainersInitialized();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

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

        Debug.Log($"[VOTE DEBUG] Voting started. Eligible voters={alivePlayers.Count}. {(disableAIVotesForManualTesting ? "AI votes disabled for manual testing." : "AI votes cast.")}", this);
    }

    // Called by timer, GameManager phase transition, or manually.
    public void EndVoting()
    {
        if (flowState == MeetingFlowState.Idle)
        {
            return;
        }

        if (!hasResolvedVoting && flowState == MeetingFlowState.Voting)
        {
            CastMissingVotesRandomly();
            ResolveVotingOutcome();
            hasResolvedVoting = true;

            // Antidote sequence will handle flow transition
            // (sets Curing state, waits 10 seconds, finalizes)
            // Don't call StartCuringPhase here; ApplyAntidote handles it

            return;
        }
    }

    /// <summary>
    /// Called when a vote button is pressed. Routes to vote casting by playerId.
    /// </summary>
    void OnVoteButtonPressedForPlayerId(int targetPlayerId)
    {
        Debug.Log($"[VOTE DEBUG] Vote button pressed targetPlayerId={targetPlayerId}", this);

        if (flowState != MeetingFlowState.Voting)
        {
            Debug.LogWarning("Vote button pressed outside voting phase.", this);
            return;
        }

        // Check if local player already voted (prevents multiple votes)
        if (localVoteCast)  // ← Used here to prevent duplicate local votes
        {
            Debug.Log("[VOTE DEBUG] Local player already voted. Duplicate vote rejected.", this);
            return;
        }

        if (localVoter == null || !localVoter.isAlive || localVoter.isFrozen)
        {
            Debug.LogWarning("No valid local voter found or voter is frozen.", this);
            return;
        }

        // Find target by stable playerId, not list index.
        PlayerIdentity target = FindPlayerById(targetPlayerId);
        if (target == null || !target.isAlive || target.isFrozen)
        {
            Debug.LogWarning($"Vote target playerId {targetPlayerId} not found, dead, or frozen.", this);
            return;
        }

        Debug.Log($"[VOTE DEBUG] Vote button resolved target: {target.playerName} playerId={target.playerId}", this);
        CastVote(localVoter, target);
    }

    void ResolveGameManagerReference()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    // Auto-wires common prototype references so setup is faster in scene.
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
                for (int i = 0; i < foundButtons.Length; i++)
                {
                    Button foundButton = foundButtons[i];
                    if (foundButton != null)
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
                    if (text != null && !string.Equals(text.gameObject.name, "PersonalAntidoteTimerText", StringComparison.Ordinal))
                    {
                        personalAntidoteText = text;
                        break;
                    }
                }

                if (personalAntidoteText == null && texts.Length > 0)
                {
                    personalAntidoteText = texts[0];
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

                if (personalAntidoteTimerText == null)
                {
                    personalAntidoteTimerText = personalAntidoteText;
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
        if (flowState == MeetingFlowState.Curing)
        {
            return;
        }

        if (flowState == MeetingFlowState.Voting && !hasResolvedVoting)
        {
            CastMissingVotesRandomly();
            ResolveVotingOutcome();  // This method calls ApplyAntidote internally
            hasResolvedVoting = true;

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
    }

    void ConfigureVoteButtons(bool interactable)
    {
        int buttonCount = voteButtons != null ? voteButtons.Count : 0;

        for (int i = 0; i < buttonCount; i++)
        {
            Button button = voteButtons[i];
            if (button == null)
            {
                continue;
            }

            // Clear old listeners to prevent stale captures.
            button.onClick.RemoveAllListeners();
            button.onClick = new Button.ButtonClickedEvent();

            bool hasTarget = i < alivePlayers.Count;
            button.gameObject.SetActive(hasTarget);
            button.interactable = interactable && hasTarget;

            if (!hasTarget)
            {
                continue;
            }

            PlayerIdentity target = alivePlayers[i];
            PlayerIdentity capturedTarget = target;
            if (capturedTarget == null)
            {
                Debug.LogWarning($"[VOTE DEBUG] Button {button.gameObject.name} has no target at index {i}.", this);
                continue;
            }

            string capturedName = string.IsNullOrWhiteSpace(capturedTarget.playerName) ? capturedTarget.gameObject.name : capturedTarget.playerName;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = capturedName;
            }

            int capturedPlayerId = capturedTarget.playerId;
            Debug.Log($"[VOTE DEBUG] Button configured: {button.gameObject.name} -> {capturedName} playerId={capturedPlayerId}", this);
            button.onClick.AddListener(() => OnVoteButtonPressedForPlayerId(capturedPlayerId));
        }
    }

    void EnsureVoteContainersInitialized()
    {
        votesByTargetPlayerId.Clear();
        playersWhoVoted.Clear();

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity player = alivePlayers[i];
            if (player == null)
            {
                continue;
            }

            if (!votesByTargetPlayerId.ContainsKey(player.playerId))
            {
                votesByTargetPlayerId.Add(player.playerId, 0);
            }
        }
    }

    PlayerIdentity FindLocalVoter()
    {
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity player = alivePlayers[i];
            if (player != null && player.isLocalPlayer)
            {
                return player;
            }
        }

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity player = alivePlayers[i];
            if (player != null && !player.isAIControlled)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>
    /// AI-controlled players and prototype non-local humans cast random votes.
    /// Frozen players cannot vote.
    /// </summary>
    void CastAIVotes()
    {
        if (disableAIVotesForManualTesting)
        {
            return;
        }

        if (!enablePrototypeAIVotes)
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

            // Skip local voter (they vote through UI).
            if (voter == localVoter)
            {
                continue;
            }

            // In local prototype, allow non-local humans to auto-vote if enabled.
            bool isAI = voter.isAIControlled;
            bool isNonLocalHuman = !isAI && !voter.isLocalPlayer;
            bool shouldVote = isAI || (isNonLocalHuman && allowNonLocalHumansToAutoVote);

            if (!shouldVote)
            {
                continue;
            }

            PlayerIdentity target = GetRandomAliveUnfrozenTarget();
            if (target != null && target != voter)
            {
                CastVote(voter, target);
            }
        }
    }

    /// <summary>
    /// Players who have not voted yet cast random votes (voting deadline).
    /// Frozen players cannot vote.
    /// </summary>
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

            // Skip if already voted.
            if (playersWhoVoted.Contains(voter.playerId))
            {
                continue;
            }

            PlayerIdentity target = GetRandomAliveUnfrozenTarget();
            if (target != null && target != voter)
            {
                CastVote(voter, target);
            }
        }
    }

    /// <summary>
    /// Records a vote from voter for target. Prevents duplicate votes and maintains vote count.
    /// Frozen voters and targets cannot vote or be voted for.
    /// </summary>
    void CastVote(PlayerIdentity voter, PlayerIdentity target)
    {
        if (voter == null || target == null)
        {
            return;
        }

        if (!voter.isAlive || !target.isAlive || voter.isFrozen || target.isFrozen)
        {
            return;
        }

        // Prevent duplicate votes from same voter.
        if (playersWhoVoted.Contains(voter.playerId))
        {
            Debug.LogWarning($"[VOTE DEBUG] {voter.playerName} already voted. Duplicate vote rejected.", this);
            return;
        }

        // Ensure target exists in vote dictionary.
        if (!votesByTargetPlayerId.ContainsKey(target.playerId))
        {
            votesByTargetPlayerId[target.playerId] = 0;
        }

        votesByTargetPlayerId[target.playerId]++;
        playersWhoVoted.Add(voter.playerId);

        // Log vote for debugging.
        string voterName = string.IsNullOrWhiteSpace(voter.playerName) ? voter.gameObject.name : voter.playerName;
        string targetName = string.IsNullOrWhiteSpace(target.playerName) ? target.gameObject.name : target.playerName;
        
        AgentTracePanel.Trace("VOTE", $"{voterName} voted antidote for {targetName}.");
        Debug.Log($"[VOTE DEBUG] {voterName} (playerId={voter.playerId}) -> {targetName} (playerId={target.playerId}). Vote count for target={votesByTargetPlayerId[target.playerId]}", this);

        // Track local player vote.
        if (voter == localVoter)
        {
            localVoteCast = true;
            ConfigureVoteButtons(false);
            Debug.Log($"[VOTE DEBUG] Local player vote cast. Vote buttons disabled.", this);
        }
    }

    PlayerIdentity GetRandomAliveTarget()
    {
        if (alivePlayers.Count == 0)
        {
            return null;
        }

        int index = UnityEngine.Random.Range(0, alivePlayers.Count);
        return alivePlayers[index];
    }

    PlayerIdentity GetRandomAliveUnfrozenTarget()
    {
        List<PlayerIdentity> unfrozen = new List<PlayerIdentity>();
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            if (alivePlayers[i] != null && !alivePlayers[i].isFrozen)
            {
                unfrozen.Add(alivePlayers[i]);
            }
        }

        if (unfrozen.Count == 0)
        {
            return null;
        }

        int index = UnityEngine.Random.Range(0, unfrozen.Count);
        return unfrozen[index];
    }

    /// <summary>
    /// Determines voting outcome: finds winner, detects ties, applies antidote or reports no consensus.
    /// </summary>
    void ResolveVotingOutcome()
    {
        // No votes cast = no consensus.
        if (votesByTargetPlayerId.Count == 0 || playersWhoVoted.Count == 0)
        {
            string noConsensusMsg = "No antidote used.";
            AgentTracePanel.Trace("VOTE", noConsensusMsg);
            Debug.Log($"[VOTE DEBUG] {noConsensusMsg} No votes received.", this);
            SetResultText(noConsensusMsg);
            HandleNoConsensus();
            return;
        }

        // Debug vote summary.
        Debug.Log($"[VOTE DEBUG] Vote tally:", this);
        int topPlayerId = -1;
        int topVotes = 0;
        int tieCount = 0;

        foreach (KeyValuePair<int, int> entry in votesByTargetPlayerId)
        {
            PlayerIdentity player = FindPlayerById(entry.Key);
            string playerName = player != null ? player.playerName : $"Unknown({entry.Key})";
            Debug.Log($"[VOTE DEBUG]   {playerName} = {entry.Value} votes", this);

            if (entry.Value > topVotes)
            {
                topVotes = entry.Value;
                topPlayerId = entry.Key;
                tieCount = 1;
            }
            else if (entry.Value == topVotes && entry.Value > 0)
            {
                tieCount++;
            }
        }

        // Two or more targets share highest vote count = tie.
        if (tieCount > 1)
        {
            string tieMsg = "No consensus. No antidote used.";
            AgentTracePanel.Trace("VOTE", tieMsg);
            Debug.Log($"[VOTE DEBUG] TIE DETECTED. {tieCount} players tied with {topVotes} votes. {tieMsg}", this);
            SetResultText(tieMsg);
            HandleNoConsensus();
            return;
        }

        // Find and apply antidote to winner.
        PlayerIdentity target = FindPlayerById(topPlayerId);
        if (target == null || !target.isAlive)
        {
            Debug.LogWarning($"[VOTE DEBUG] ERROR: Voting winner playerId {topPlayerId} not found or dead.", this);
            HandleNoConsensus();
            return;
        }

        if (target.isFrozen)
        {
            Debug.Log($"[VOTE DEBUG] Target {target.playerName} already frozen. Skipping antidote.", this);
            HandleNoConsensus();
            return;
        }

        Debug.Log($"[VOTE DEBUG] WINNER: {target.playerName} (playerId={topPlayerId}) with {topVotes} votes.", this);
        ApplyAntidote(target);
    }

    /// <summary>
    /// Handles case where no antidote is used (tie or no votes).
    /// Finalizes meeting immediately.
    /// </summary>
    void HandleNoConsensus()
    {
        flowState = MeetingFlowState.Curing;
        FinalizeMeetingFlow();

        if (autoAdvanceGameManagerPhases && gameManager != null && GameManager.CurrentPhase == GamePhase.Voting)
        {
            gameManager.EnterExploration();
        }
    }

    /// <summary>
    /// Starts antidote freeze sequence for target. Public messaging does NOT reveal role.
    /// CRITICAL: This must apply to the VOTED target, not always Player 1.
    /// </summary>
    void ApplyAntidote(PlayerIdentity target)
    {
        if (target == null || !target.isAlive)
        {
            Debug.LogWarning("[ANTIDOTE] ApplyAntidote called with null or dead target.", this);
            return;
        }

        // Stop any existing antidote routine
        if (activeAntidoteRoutine != null)
        {
            StopCoroutine(activeAntidoteRoutine);
            Debug.LogWarning($"[ANTIDOTE] Stopping previous antidote routine for {currentAntidoteTarget.playerName}", this);
        }

        currentAntidoteTarget = target;

        string playerName = string.IsNullOrWhiteSpace(target.playerName) ? target.gameObject.name : target.playerName;
        string antidoteMsg = $"ANTIDOTE ADMINISTERED TO {playerName}";
        SetResultText(antidoteMsg);
        AgentTracePanel.Trace("VOTE", $"Antidote administered to {playerName}.");
        Debug.Log($"[ANTIDOTE] Antidote applied to {playerName} (playerId={target.playerId}). Starting 10-second freeze.", this);

        activeAntidoteRoutine = StartCoroutine(AntidoteFreezeRoutine(target));
    }

    /// <summary>
    /// Freezes target for exactly 10 seconds, resolves cure silently, then unfreezes.
    /// Shows personal overlay ONLY if target is local player.
    /// </summary>
    IEnumerator AntidoteFreezeRoutine(PlayerIdentity target)
    {
        if (target == null || !target.isAlive)
        {
            yield break;
        }

        flowState = MeetingFlowState.Curing;
        bool isLocalTarget = IsLocalTarget(target);

        target.FreezePlayer();
        Debug.Log($"[ANTIDOTE] {target.playerName} frozen. duration=10 seconds. isLocalTarget={isLocalTarget}", this);

        if (isLocalTarget)
        {
            ShowPersonalAntidoteOverlay(target);
        }

        float elapsedTime = 0f;
        float freezeDuration = antidoteFreezeDuration;

        while (elapsedTime < freezeDuration)
        {
            elapsedTime += Time.deltaTime;
            float remaining = Mathf.Max(0f, freezeDuration - elapsedTime);

            if (isLocalTarget)
            {
                UpdatePersonalAntidoteCountdown(remaining);
            }

            yield return null;
        }

        if (target != null && target.isAlive)
        {
            Debug.Log($"[ANTIDOTE] {target.playerName} freeze complete. Resolving cure silently.", this);
            ResolveCureSilently(target);
            target.UnfreezePlayer();
            Debug.Log($"[ANTIDOTE] {target.playerName} unfrozen.", this);
        }

        if (isLocalTarget)
        {
            HidePersonalAntidoteOverlay();
        }

        currentAntidoteTarget = null;
        activeAntidoteRoutine = null;

        // Trigger loss condition check after unfreeze.
        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }

        // Finalize meeting flow after antidote is complete
        FinalizeMeetingFlow();

        if (autoAdvanceGameManagerPhases && gameManager != null && GameManager.CurrentPhase == GamePhase.Voting)
        {
            gameManager.EnterExploration();
        }
    }

    /// <summary>
    /// Cures target silently if eligible. Does NOT show public messages about role or cure.
    /// </summary>
    void ResolveCureSilently(PlayerIdentity target)
    {
        if (target == null || !target.isAlive)
        {
            return;
        }

        if (!CanBeCured(target))
        {
            return;
        }

        bool wasCured = false;

        // Cure silently: no public message.
        if (target.isInfected)
        {
            if (InfectionSystem.Instance != null)
            {
                InfectionSystem.Instance.ApplyAntidote(target);
            }
            else
            {
                target.Cure();
            }

            wasCured = true;
            Debug.Log($"[ANTIDOTE DEBUG] {target.playerName} was infected and was cured silently.", this);
        }

        // Use showPrivateAntidoteDebugTrace to conditionally show private debug info
        if (showPrivateAntidoteDebugTrace)  // ← Used here as a guard for private debug trace
        {
            string targetName = string.IsNullOrWhiteSpace(target.playerName) ? target.gameObject.name : target.playerName;
            AgentTracePanel.Trace("VOTE_DEBUG", $"{targetName} antidote resolved privately. cured={wasCured}");
        }
    }

    /// <summary>
    /// Returns true if target can be cured by antidote.
    /// </summary>
    private bool CanBeCured(PlayerIdentity target)
    {
        return target != null && target.isInfected;
    }

    /// <summary>
    /// Returns true if target is the local player.
    /// </summary>
    private bool IsLocalTarget(PlayerIdentity target)
    {
        return target != null && target.isLocalPlayer;
    }

    /// <summary>
    /// Shows personal antidote/curing overlay ONLY for local player targets.
    /// </summary>
    void ShowPersonalAntidoteOverlay(PlayerIdentity target)
    {
        if (target == null || !IsLocalTarget(target))
        {
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

        personalAntidotePanel.SetActive(true);

        if (personalAntidoteText != null)
        {
            personalAntidoteText.text = "ANTIDOTE ADMINISTERED\nPLEASE WAIT";
        }

        if (personalAntidoteTimerText != null)
        {
            personalAntidoteTimerText.text = Mathf.CeilToInt(antidoteFreezeDuration).ToString();
        }

        Debug.Log($"[ANTIDOTE UI] Personal antidote overlay shown for {target.playerName}.", this);
    }

    /// <summary>
    /// Hides personal antidote/curing overlay.
    /// </summary>
    void HidePersonalAntidoteOverlay()
    {
        if (personalAntidotePanel != null)
        {
            bool wasActive = personalAntidotePanel.activeSelf;
            personalAntidotePanel.SetActive(false);

            if (wasActive)
            {
                Debug.Log("[ANTIDOTE UI] Personal antidote overlay hidden.", this);
            }
        }
    }

    /// <summary>
    /// Updates the countdown timer on personal antidote overlay.
    /// </summary>
    void UpdatePersonalAntidoteCountdown(float remainingSeconds)
    {
        if (personalAntidotePanel == null || !personalAntidotePanel.activeInHierarchy)
        {
            return;
        }

        if (currentAntidoteTarget == null || !currentAntidoteTarget.isLocalPlayer || !currentAntidoteTarget.isAlive)
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
        flowState = MeetingFlowState.Idle;
        phaseTimer = 0f;

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }

        // Restore movement correctly: only local player, preserve AI/bot state.
        RestoreMovementAfterMeeting();
    }

    /// <summary>
    /// Disables movement for all players. Used when meeting starts.
    /// </summary>
    void DisableAllMovement()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            // Disable PlayerMovement.
            PlayerMovement playerMvmt = player.GetComponent<PlayerMovement>();
            if (playerMvmt != null)
            {
                playerMvmt.enabled = false;
            }

            // Disable BotMovement.
            MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
            for (int j = 0; j < behaviours.Length; j++)
            {
                if (behaviours[j] != null && behaviours[j].GetType().Name == "BotMovement")
                {
                    behaviours[j].enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Restores movement after meeting ends:
    /// - Local human player gets PlayerMovement.
    /// - Infected AI players keep BotMovement disabled (they are cured).
    /// - Dead players stay disabled.
    /// - Non-local humans in local prototype do NOT get movement.
    /// </summary>
    void RestoreMovementAfterMeeting()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            // Only local human player gets PlayerMovement.
            if (player.isLocalPlayer && !player.isAIControlled)
            {
                PlayerMovement playerMvmt = player.GetComponent<PlayerMovement>();
                if (playerMvmt != null)
                {
                    playerMvmt.enabled = true;
                }
            }
            // Infected AI players were cured (movement frozen by antidote system).
            // After freeze expires, BotMovement will re-enable in InfectionSystem.
            // For now, leave disabled during meeting.
        }
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

    void HandleDebugHotkeys()
    {
        if (!enableDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.M))
        {
            Debug.Log("[DEBUG] Starting meeting.", this);
            StartMeeting();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            Debug.Log("[DEBUG] Starting voting.", this);
            StartVoting();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("[DEBUG] Ending voting.", this);
            EndVoting();
        }

        // Debug vote hotkeys: Alpha1-4 = vote for players 1-4.
        for (int i = 0; i < 4; i++)
        {
            KeyCode voteKey = KeyCode.Alpha1 + i;
            if (Input.GetKeyDown(voteKey) && flowState == MeetingFlowState.Voting && localVoter != null)
            {
                if (i < alivePlayers.Count)
                {
                    PlayerIdentity target = alivePlayers[i];
                    Debug.Log($"[DEBUG] Local player voting for {target.playerName}.", this);
                    CastVote(localVoter, target);
                }
            }
        }
    }

    void UpdateTimerText()
    {
        if (timerText == null)
        {
            return;
        }

        string phaseLabel = flowState == MeetingFlowState.Voting ? "Voting" : flowState == MeetingFlowState.Curing ? "Curing" : "Discussion";
        timerText.text = $"{phaseLabel} : {Mathf.CeilToInt(phaseTimer)}";
    }
}
