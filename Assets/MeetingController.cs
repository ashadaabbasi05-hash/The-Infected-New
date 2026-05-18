using System;
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
    [SerializeField, Min(0f)] float curingDuration = 10f;

    [Header("Behavior")]
    [Tooltip("If true, this controller tells GameManager to switch Voting/Exploration phases automatically when timers end.")]
    [SerializeField] bool autoAdvanceGameManagerPhases = true;
    [SerializeField] bool enablePrototypeAIVotes = true;
    [SerializeField] bool allowNonLocalHumansToAutoVote = true;
    [SerializeField] bool enableDebugHotkeys = true;

    [Header("UI Feedback")]
    [SerializeField] TMP_Text resultText;

    readonly List<PlayerIdentity> alivePlayers = new List<PlayerIdentity>(4);
    readonly Dictionary<int, int> votesByTargetPlayerId = new Dictionary<int, int>(4);
    readonly HashSet<int> playersWhoVoted = new HashSet<int>();

    MeetingFlowState flowState = MeetingFlowState.Idle;
    float phaseTimer;
    bool hasResolvedVoting;

    PlayerIdentity localVoter;

    void Awake()
    {
        AutoAssignReferences();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.text = string.Empty;
        }

        ResolveGameManagerReference();
    }

    void Start()
    {
        AutoAssignReferences();

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
            return;
        }

        AutoAssignReferences();
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

        flowState = MeetingFlowState.Voting;
        phaseTimer = votingDuration;
        hasResolvedVoting = false;

        EnsureVoteContainersInitialized();

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

        ConfigureVoteButtons(true);
        CastAIVotes();
        UpdateTimerText();

        Debug.Log("Voting started.", this);
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

            if (flowState != MeetingFlowState.Curing)
            {
                StartCuringPhase();
            }

            return;
        }
    }

    /// <summary>
    /// Called when a vote button is pressed. Routes to vote casting by playerId.
    /// </summary>
    void OnVoteButtonPressedForPlayerId(int targetPlayerId)
    {
        if (flowState != MeetingFlowState.Voting)
        {
            Debug.LogWarning("Vote button pressed outside voting phase.", this);
            return;
        }

        if (localVoter == null || !localVoter.isAlive)
        {
            Debug.LogWarning("No valid local voter found.", this);
            return;
        }

        // Find target by stable playerId, not list index.
        PlayerIdentity target = FindPlayerById(targetPlayerId);
        if (target == null || !target.isAlive)
        {
            Debug.LogWarning($"Vote target playerId {targetPlayerId} not found or dead.", this);
            return;
        }

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
            ResolveVotingOutcome();
            hasResolvedVoting = true;

            if (flowState != MeetingFlowState.Curing)
            {
                StartCuringPhase();
            }

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

            bool hasTarget = i < alivePlayers.Count;
            button.gameObject.SetActive(hasTarget);
            button.interactable = interactable && hasTarget;

            if (!hasTarget)
            {
                continue;
            }

            PlayerIdentity target = alivePlayers[i];
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = target != null ? target.playerName : "Unknown";
            }

            // Capture playerId instead of index for stable vote targeting.
            int capturedPlayerId = target.playerId;
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
    /// </summary>
    void CastAIVotes()
    {
        if (!enablePrototypeAIVotes)
        {
            return;
        }

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive)
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

            PlayerIdentity target = GetRandomAliveTarget();
            if (target != null && target != voter)
            {
                CastVote(voter, target);
            }
        }
    }

    /// <summary>
    /// Players who have not voted yet cast random votes (voting deadline).
    /// </summary>
    void CastMissingVotesRandomly()
    {
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive)
            {
                continue;
            }

            // Skip if already voted.
            if (playersWhoVoted.Contains(voter.playerId))
            {
                continue;
            }

            PlayerIdentity target = GetRandomAliveTarget();
            if (target != null && target != voter)
            {
                CastVote(voter, target);
            }
        }
    }

    /// <summary>
    /// Records a vote from voter for target. Prevents duplicate votes and maintains vote count.
    /// </summary>
    void CastVote(PlayerIdentity voter, PlayerIdentity target)
    {
        if (voter == null || target == null)
        {
            return;
        }

        if (!voter.isAlive || !target.isAlive)
        {
            return;
        }

        // Prevent duplicate votes from same voter.
        if (playersWhoVoted.Contains(voter.playerId))
        {
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
        Debug.Log($"{voterName} voted antidote for {targetName}.", this);

        // Disable buttons after local player votes.
        if (voter == localVoter)
        {
            ConfigureVoteButtons(false);
        }
    }

    PlayerIdentity GetRandomAliveTarget()
    {
        if (alivePlayers.Count == 0)
        {
            return null;
        }

        int index = UnityEngine.Random. Range(0, alivePlayers.Count);
        return alivePlayers[index];
    }

    /// <summary>
    /// Determines voting outcome: finds winner, detects ties, applies antidote or reports no consensus.
    /// </summary>
    void ResolveVotingOutcome()
    {
        // No votes cast = no consensus.
        if (votesByTargetPlayerId.Count == 0)
        {
            string noConsensusMsg = "No consensus. No antidote used.";
            AgentTracePanel.Trace("VOTE", noConsensusMsg);
            Debug.Log(noConsensusMsg, this);
            SetResultText(noConsensusMsg);
            return;
        }

        int topPlayerId = -1;
        int topVotes = -1;
        int tieCount = 0;

        // Find highest vote count and detect ties.
        foreach (KeyValuePair<int, int> entry in votesByTargetPlayerId)
        {
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
            Debug.Log(tieMsg, this);
            SetResultText(tieMsg);
            return;
        }

        // Find and apply antidote to winner.
        PlayerIdentity target = FindPlayerById(topPlayerId);
        if (target == null || !target.isAlive)
        {
            Debug.LogWarning($"Voting winner playerId {topPlayerId} not found or dead.", this);
            return;
        }

        ApplyAntidote(target);
    }

    /// <summary>
    /// Applies antidote to target: cures if infected, otherwise no effect.
    /// Does NOT kill, eliminate, or disable any player.
    /// </summary>
    void ApplyAntidote(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        if (!target.isAlive)
        {
            return;
        }

        string playerName = string.IsNullOrWhiteSpace(target.playerName) ? target.gameObject.name : target.playerName;
        bool wasInfected = target.isInfected;

        if (wasInfected)
        {
            // Cure the infected player via InfectionSystem if available.
            if (InfectionSystem.Instance != null)
            {
                InfectionSystem.Instance.ApplyAntidote(target);
            }
            else
            {
                // Fallback: directly cure if InfectionSystem unavailable.
                target.Cure();
            }

            string curedMsg = $"{playerName} was infected and was cured.";
            AgentTracePanel.Trace("VOTE", $"{target.playerName} was infected and cured by antidote.");
            Debug.Log(curedMsg, target);
            SetResultText(curedMsg);
        }
        else
        {
            // Target is human: no effect, but remain alive and unaffected.
            string humanMsg = $"{playerName} was human. Antidote had no effect.";
            AgentTracePanel.Trace("VOTE", $"{target.playerName} was human. Antidote had no effect.");
            Debug.Log(humanMsg, target);
            SetResultText(humanMsg);
        }

        // Trigger GameEndManager loss condition check if present.
        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }
    }

    void StartCuringPhase()
    {
        flowState = MeetingFlowState.Curing;

        float freezeDuration = curingDuration;
        if (InfectionSystem.Instance != null)
        {
            freezeDuration = InfectionSystem.Instance.AntidoteFreezeDuration;
        }

        phaseTimer = freezeDuration;

        if (gameManager != null)
        {
            gameManager.AddPhaseTime(freezeDuration);
        }

        if (meetingPanel != null)
        {
            meetingPanel.SetActive(true);
        }

        ConfigureVoteButtons(false);
        UpdateTimerText();
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
