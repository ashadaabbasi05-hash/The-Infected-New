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
        Voting = 2
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

    [Header("Behavior")]
    [Tooltip("If true, this controller tells GameManager to switch Voting/Exploration phases automatically when timers end.")]
    [SerializeField] bool autoAdvanceGameManagerPhases = true;

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

        SetMovementForAlivePlayers(false);
        ConfigureVoteButtons(false);
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
        }

        FinalizeMeetingFlow();

        if (autoAdvanceGameManagerPhases && gameManager != null && GameManager.CurrentPhase == GamePhase.Voting)
        {
            gameManager.EnterExploration();
        }
    }

    public void OnVoteButtonPressed(int buttonIndex)
    {
        if (flowState != MeetingFlowState.Voting)
        {
            return;
        }

        if (buttonIndex < 0 || buttonIndex >= alivePlayers.Count)
        {
            return;
        }

        if (localVoter == null || !localVoter.isAlive)
        {
            return;
        }

        PlayerIdentity target = alivePlayers[buttonIndex];
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
        if (flowState == MeetingFlowState.Voting && !hasResolvedVoting)
        {
            CastMissingVotesRandomly();
            ResolveVotingOutcome();
            hasResolvedVoting = true;
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

    void SetMovementForAlivePlayers(bool enabled)
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.enabled = enabled;
            }
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

            int capturedIndex = i;
            button.onClick.AddListener(() => OnVoteButtonPressed(capturedIndex));
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

    void CastAIVotes()
    {
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive)
            {
                continue;
            }

            if (voter == localVoter)
            {
                continue;
            }

            if (!voter.isAIControlled)
            {
                continue;
            }

            PlayerIdentity target = GetRandomAliveTarget();
            CastVote(voter, target);
        }
    }

    void CastMissingVotesRandomly()
    {
        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerIdentity voter = alivePlayers[i];
            if (voter == null || !voter.isAlive)
            {
                continue;
            }

            if (playersWhoVoted.Contains(voter.playerId))
            {
                continue;
            }

            PlayerIdentity target = GetRandomAliveTarget();
            CastVote(voter, target);
        }
    }

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

        if (playersWhoVoted.Contains(voter.playerId))
        {
            return;
        }

        if (!votesByTargetPlayerId.ContainsKey(target.playerId))
        {
            votesByTargetPlayerId[target.playerId] = 0;
        }

        votesByTargetPlayerId[target.playerId]++;
        playersWhoVoted.Add(voter.playerId);

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

        int index = Random.Range(0, alivePlayers.Count);
        return alivePlayers[index];
    }

    void ResolveVotingOutcome()
    {
        if (votesByTargetPlayerId.Count == 0)
        {
            Debug.Log("Tie vote. No one eliminated.", this);
            return;
        }

        int topPlayerId = -1;
        int topVotes = -1;
        bool isTie = false;

        foreach (KeyValuePair<int, int> entry in votesByTargetPlayerId)
        {
            if (entry.Value > topVotes)
            {
                topVotes = entry.Value;
                topPlayerId = entry.Key;
                isTie = false;
            }
            else if (entry.Value == topVotes)
            {
                isTie = true;
            }
        }

        if (isTie)
        {
            Debug.Log("Tie vote. No one eliminated.", this);
            return;
        }

        PlayerIdentity eliminated = FindPlayerById(topPlayerId);
        if (eliminated == null || !eliminated.isAlive)
        {
            return;
        }

        bool wasInfected = eliminated.isInfected;
        string eliminatedName = eliminated.playerName;

        eliminated.KillPlayer();

        Debug.Log($"{eliminatedName} was eliminated.", eliminated);

        if (wasInfected)
        {
            Debug.Log($"{eliminatedName} was Infected.", eliminated);
        }
        else
        {
            Debug.Log($"{eliminatedName} was NOT Infected.", eliminated);
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

        SetMovementForAlivePlayers(true);
    }

    void UpdateTimerText()
    {
        if (timerText == null)
        {
            return;
        }

        string phaseLabel = flowState == MeetingFlowState.Voting ? "Voting" : "Discussion";
        timerText.text = $"{phaseLabel}: {Mathf.CeilToInt(phaseTimer)}";
    }
}
