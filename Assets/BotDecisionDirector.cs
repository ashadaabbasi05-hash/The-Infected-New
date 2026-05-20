using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BotDecisionDirector : MonoBehaviour
{
    public static BotDecisionDirector Instance { get; private set; }

    [SerializeField] bool enableDecisionDirector = true;
    [SerializeField] bool useTeamADecideActionApi = false;
    [SerializeField, Min(0.1f)] float decisionTickInterval = 5f;
    [SerializeField, Min(0.1f)] float apiTimeoutSeconds = 4f;
    [SerializeField] string matchId = "ROOM123";
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool traceDecisions = true;

    readonly Dictionary<int, float> nextDecisionTimeByPlayerId = new Dictionary<int, float>();
    readonly HashSet<int> decisionInProgressPlayerIds = new HashSet<int>();

    float decisionTimer;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[BOT DECISION] Duplicate BotDecisionDirector detected. Disabling duplicate.", this);
            }

            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (debugLogs)
        {
            Debug.Log("[BOT DECISION] BotDecisionDirector ready.", this);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (!enableDecisionDirector)
        {
            return;
        }

        if (IsFinalHuntActive())
        {
            return;
        }

        decisionTimer += Time.deltaTime;
        if (decisionTimer < decisionTickInterval)
        {
            return;
        }

        decisionTimer = 0f;
        TickDecisions();
    }

    public void RequestImmediateDecision(PlayerIdentity bot)
    {
        if (!enableDecisionDirector || bot == null)
        {
            return;
        }

        int playerId = bot.playerId;
        nextDecisionTimeByPlayerId[playerId] = 0f;

        if (decisionInProgressPlayerIds.Contains(playerId))
        {
            return;
        }

        if (!IsValidDecisionBot(bot))
        {
            return;
        }

        StartCoroutine(RequestDecisionForBot(bot));
    }

    void TickDecisions()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null || allPlayers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity bot = allPlayers[i];
            if (!IsValidDecisionBot(bot))
            {
                continue;
            }

            int playerId = bot.playerId;
            if (decisionInProgressPlayerIds.Contains(playerId))
            {
                continue;
            }

            if (nextDecisionTimeByPlayerId.TryGetValue(playerId, out float nextDecisionTime) && Time.time < nextDecisionTime)
            {
                continue;
            }

            StartCoroutine(RequestDecisionForBot(bot));
        }
    }

    IEnumerator RequestDecisionForBot(PlayerIdentity bot)
    {
        if (bot == null)
        {
            yield break;
        }

        int playerId = bot.playerId;
        if (decisionInProgressPlayerIds.Contains(playerId))
        {
            yield break;
        }

        decisionInProgressPlayerIds.Add(playerId);

        bool finalHuntActive = IsFinalHuntActive();
        bool apiClientUnavailable = TeamAApiClient.Instance == null || !TeamAApiClient.Instance.EnableApiCalls;
        bool useLocalFallbackOnly = !useTeamADecideActionApi || apiClientUnavailable || finalHuntActive;
        if (useLocalFallbackOnly)
        {
            if (debugLogs)
            {
                if (!useTeamADecideActionApi)
                {
                    Debug.Log("[BOT DECISION] Team A decide_action disabled. Using local fallback.", this);
                }
                else if (finalHuntActive)
                {
                    Debug.Log("[BOT DECISION] Final Hunt active. Using local fallback.", this);
                }
                else if (apiClientUnavailable)
                {
                    Debug.Log("[BOT DECISION] Team A API unavailable or disabled. Using local fallback.", this);
                }
            }

            ApplyLocalDecision(bot);
            decisionInProgressPlayerIds.Remove(playerId);
            yield break;
        }

        DecideActionRequest request = BuildDecideActionRequest(bot);
        bool callbackReceived = false;
        bool apiSuccess = false;
        DecideActionResponse apiResponse = null;

        Coroutine requestCoroutine = TeamAApiClient.Instance.StartCoroutine(TeamAApiClient.Instance.DecideAction(request, (success, response) =>
        {
            callbackReceived = true;
            apiSuccess = success;
            apiResponse = response;
        }));

        float timeoutAt = Time.time + Mathf.Max(0.1f, apiTimeoutSeconds);
        while (!callbackReceived && Time.time < timeoutAt)
        {
            yield return null;
        }

        if (!callbackReceived)
        {
            if (requestCoroutine != null)
            {
                TeamAApiClient.Instance.StopCoroutine(requestCoroutine);
            }

            if (debugLogs)
            {
                Debug.LogWarning($"[BOT DECISION] DecideAction timed out for {GetBotDebugName(bot)}. Using local fallback.", this);
            }

            ApplyLocalDecision(bot);
            decisionInProgressPlayerIds.Remove(playerId);
            yield break;
        }

        if (!IsValidDecisionBot(bot))
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[BOT DECISION] Decision ignored because {GetBotDebugName(bot)} is no longer infected.", this);
            }

            nextDecisionTimeByPlayerId.Remove(playerId);
            decisionInProgressPlayerIds.Remove(playerId);
            yield break;
        }

        if (apiSuccess && apiResponse != null && !string.IsNullOrWhiteSpace(apiResponse.behaviorMode))
        {
            ApplyApiDecision(bot, apiResponse);
        }
        else
        {
            ApplyLocalDecision(bot);
        }

        decisionInProgressPlayerIds.Remove(playerId);
    }

    DecideActionRequest BuildDecideActionRequest(PlayerIdentity bot)
    {
        PlayerIdentity nearestHuman = FindNearestHuman(bot);
        string nearestHumanId = nearestHuman != null ? GetApiPlayerId(nearestHuman) : string.Empty;

        return new DecideActionRequest
        {
            matchId = string.IsNullOrWhiteSpace(matchId) ? "ROOM123" : matchId,
            phase = GetCurrentPhaseName(),
            wave = GetCurrentWave(),
            cycle = GetCurrentCycle(),
            botId = GetApiPlayerId(bot),
            botName = GetDisplayName(bot),
            infectedPlayers = GetInfectedPlayerApiIds(),
            humanPlayers = GetHumanPlayerApiIds(),
            alivePlayers = GetAlivePlayerApiIds(),
            taskProgress = GetTaskProgress(),
            nearestHuman = nearestHumanId,
            botRoom = "unknown",
            nearestHumanRoom = "unknown",
            secondsSinceLastSeenHuman = 0f,
            isFinalChase = IsFinalHuntActive()
        };
    }

    void ApplyApiDecision(PlayerIdentity bot, DecideActionResponse response)
    {
        if (!IsValidDecisionBot(bot))
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[BOT DECISION] API decision ignored because {GetBotDebugName(bot)} is no longer infected.", this);
            }

            if (bot != null)
            {
                nextDecisionTimeByPlayerId.Remove(bot.playerId);
            }

            return;
        }

        BotMovement botMovement = bot.GetComponent<BotMovement>();
        if (botMovement == null)
        {
            ApplyLocalDecision(bot);
            return;
        }

        if (IsFinalHuntActive())
        {
            if (botMovement.CurrentMode != BotBehaviorMode.FinalHunt)
            {
                if (debugLogs)
                {
                    Debug.Log("[BOT DECISION] Final Hunt active. Forcing FinalHunt mode.", this);
                }

                botMovement.SetMode(BotBehaviorMode.FinalHunt);
                TraceDecision(bot, BotBehaviorMode.FinalHunt, true);
            }

            nextDecisionTimeByPlayerId[bot.playerId] = Time.time + decisionTickInterval;
            return;
        }

        if (!TryMapApiBehaviorMode(response.behaviorMode, out BotBehaviorMode mappedMode))
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[BOT DECISION] Unknown API behavior mode '{response.behaviorMode}' for {GetBotDebugName(bot)}. Using local fallback.", this);
            }

            ApplyLocalDecision(bot);
            return;
        }

        bool modeChanged = botMovement.CurrentMode != mappedMode;
        if (modeChanged)
        {
            botMovement.SetMode(mappedMode);
        }
        else if (debugLogs)
        {
            Debug.Log($"[BOT DECISION] {GetBotDebugName(bot)} already in mode {mappedMode}.", this);
        }

        float nextDecisionDelay = response.nextDecisionInSeconds > 0
            ? Mathf.Clamp(response.nextDecisionInSeconds, 3, 15)
            : decisionTickInterval;

        nextDecisionTimeByPlayerId[bot.playerId] = Time.time + nextDecisionDelay;

        TraceDecision(bot, mappedMode, modeChanged);
    }

    void ApplyLocalDecision(PlayerIdentity bot)
    {
        if (!IsValidDecisionBot(bot))
        {
            if (bot != null)
            {
                nextDecisionTimeByPlayerId.Remove(bot.playerId);
            }

            return;
        }

        BotMovement botMovement = bot.GetComponent<BotMovement>();
        if (botMovement == null)
        {
            nextDecisionTimeByPlayerId[bot.playerId] = Time.time + decisionTickInterval;
            return;
        }

        BotBehaviorMode mode = GetLocalModeForCurrentState();
        bool modeChanged = botMovement.CurrentMode != mode;

        if (modeChanged)
        {
            botMovement.SetMode(mode);
        }

        nextDecisionTimeByPlayerId[bot.playerId] = Time.time + decisionTickInterval;

        if (debugLogs)
        {
            Debug.Log($"[BOT DECISION] Local fallback decision for {GetBotDebugName(bot)}: {mode}", this);
        }

        TraceDecision(bot, mode, modeChanged);
    }

    BotBehaviorMode GetLocalModeForCurrentState()
    {
        if (IsFinalHuntActive())
        {
            return BotBehaviorMode.FinalHunt;
        }

        int wave = GetCurrentWave();
        if (wave <= 1)
        {
            return BotBehaviorMode.FakeTask;
        }

        if (wave == 2)
        {
            return BotBehaviorMode.Stalk;
        }

        return BotBehaviorMode.AggressiveChase;
    }

    bool IsValidDecisionBot(PlayerIdentity bot)
    {
        if (bot == null || !bot.isAlive || !bot.isInfected || !bot.isAIControlled || bot.isFrozen)
        {
            return false;
        }

        BotMovement botMovement = bot.GetComponent<BotMovement>();
        return botMovement != null && botMovement.enabled;
    }

    void TraceDecision(PlayerIdentity bot, BotBehaviorMode mode, bool changed)
    {
        if (!traceDecisions || !changed)
        {
            return;
        }

        AgentTracePanel.Trace("AI", $"{GetDisplayName(bot)} decision: {mode}");
    }

    bool IsFinalHuntActive()
    {
        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>();
        return finalHuntManager != null && finalHuntManager.IsFinalHuntActive;
    }

    string GetCurrentPhaseName()
    {
        return GameManager.CurrentPhase.ToString();
    }

    int GetCurrentWave()
    {
        return Mathf.Max(1, GameManager.CurrentWave);
    }

    int GetCurrentCycle()
    {
        return 1;
    }

    int GetTaskProgress()
    {
        return TaskManager.Instance != null ? TaskManager.Instance.CompletedTasks : 0;
    }

    string[] GetAlivePlayerApiIds()
    {
        List<string> ids = new List<string>();
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return ids.ToArray();
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player != null && player.isAlive)
            {
                ids.Add(GetApiPlayerId(player));
            }
        }

        return ids.ToArray();
    }

    string[] GetHumanPlayerApiIds()
    {
        List<string> ids = new List<string>();
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return ids.ToArray();
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player != null && player.isAlive && !player.isInfected)
            {
                ids.Add(GetApiPlayerId(player));
            }
        }

        return ids.ToArray();
    }

    string[] GetInfectedPlayerApiIds()
    {
        List<string> ids = new List<string>();
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return ids.ToArray();
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player != null && player.isInfected)
            {
                ids.Add(GetApiPlayerId(player));
            }
        }

        return ids.ToArray();
    }

    PlayerIdentity FindNearestHuman(PlayerIdentity bot)
    {
        if (bot == null)
        {
            return null;
        }

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null || allPlayers.Length == 0)
        {
            return null;
        }

        Vector2 botPosition = bot.transform.position;
        PlayerIdentity nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity candidate = allPlayers[i];
            if (candidate == null || !candidate.isAlive || candidate.isInfected)
            {
                continue;
            }

            float distance = Vector2.Distance(botPosition, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    string GetBotDebugName(PlayerIdentity bot)
    {
        return bot != null ? GetDisplayName(bot) : "NONE";
    }

    static string GetApiPlayerId(PlayerIdentity player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        return "player_" + player.playerId;
    }

    static string GetDisplayName(PlayerIdentity player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(player.playerName))
        {
            return player.playerName;
        }

        return "Player " + player.playerId;
    }

    static bool TryMapApiBehaviorMode(string apiMode, out BotBehaviorMode mode)
    {
        mode = BotBehaviorMode.FakeTask;
        if (string.IsNullOrWhiteSpace(apiMode))
        {
            return false;
        }

        switch (apiMode)
        {
            case "stealth_fake_task":
            case "fake_task":
                mode = BotBehaviorMode.FakeTask;
                return true;
            case "stalk":
                mode = BotBehaviorMode.Stalk;
                return true;
            case "aggressive_chase":
                mode = BotBehaviorMode.AggressiveChase;
                return true;
            case "final_hunt":
                mode = BotBehaviorMode.FinalHunt;
                return true;
            default:
                return false;
        }
    }
}