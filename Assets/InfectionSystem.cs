using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class InfectionSystem : MonoBehaviour
{
    public static InfectionSystem Instance { get; private set; }

    [SerializeField]
    string matchId = "ROOM123";

    public string MatchId => matchId;

    readonly HashSet<string> registeredBotApiIds = new HashSet<string>();

    [SerializeField, Min(0f)]
    float safePhaseDuration = 10f;

    [SerializeField, Min(0f)]
    float gasWaveDuration = 5f;

    [SerializeField, Min(0f)]
    float antidoteFreezeDuration = 10f;

    public float AntidoteFreezeDuration => antidoteFreezeDuration;

    [SerializeField]
    AudioSource alarmSource;

    [SerializeField]
    AudioSource ambientSource;

    [SerializeField]
    AudioClip alarmClip;

    [SerializeField]
    AudioClip ambientClip;

    [SerializeField, Min(0f)]
    float ambientGasVolume = 0.25f;

    [SerializeField]
    Image gasOverlay;

    [SerializeField, Min(0f)]
    float overlayFadeSpeed = 1.5f;

    [SerializeField]
    bool enableDebugHotkeys = true;

    [SerializeField]
    bool useObviousDebugInfectedColor = false;

    float timer;
    bool isGasWaveActive;
    PlayerIdentity currentInfectedPlayer;
    int gasWaveCount;
    int lastWaveInfectionAttempted = -1;
    bool isSubscribedToGameManagerEvents;
    float ambientSafeVolume = 1f;

    readonly Dictionary<PlayerIdentity, Coroutine> frozenPlayers = new Dictionary<PlayerIdentity, Coroutine>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void OnEnable()
    {
        TrySubscribeToGameManagerEvents();
    }

    void Start()
    {
        InitializePresentation();
        TrySubscribeToGameManagerEvents();

        if (GameManager.Instance != null)
        {
            SyncToGameManagerState();
        }
        else
        {
            StartSafePhase(true);
        }
    }

    void OnDisable()
    {
        TryUnsubscribeFromGameManagerEvents();

        foreach (Coroutine runningCoroutine in frozenPlayers.Values)
        {
            if (runningCoroutine != null)
            {
                StopCoroutine(runningCoroutine);
            }
        }

        frozenPlayers.Clear();
    }

    void Update()
    {
        TrySubscribeToGameManagerEvents();

        if (enableDebugHotkeys)
        {
            HandleDebugHotkeys();
        }

        if (GameManager.Instance != null)
        {
            UpdateGasOverlay();
            return;
        }

        if (timer > 0f)
        {
            timer -= Time.deltaTime;
        }

        if (timer <= 0f)
        {
            if (isGasWaveActive)
            {
                StartSafePhase();
            }
            else
            {
                StartGasWave();
            }
        }

        UpdateGasOverlay();
    }

    public void OnGasWaveStarted(int wave)
    {
        Debug.Log($"[INFECT DEBUG] Gas wave start received. Wave={wave}", this);

        isGasWaveActive = true;
        ApplyGasWaveAudio();

        if (wave == lastWaveInfectionAttempted)
        {
            Debug.Log($"[INFECT DEBUG] Infection already attempted for wave {wave}. Skipping duplicate.", this);
            return;
        }

        lastWaveInfectionAttempted = wave;

        bool success = TryInfectRandomHuman(wave, false, "GAS_WAVE");
        Debug.Log($"[INFECT DEBUG] Infection attempt completed. Success={success}", this);
    }

    public void InfectRandomPlayer()
    {
        int wave = GameManager.Instance != null ? GameManager.CurrentWave : gasWaveCount;
        TryInfectRandomHuman(wave, true, "DEBUG_FORCE_RANDOM");
    }

    public bool TryInfectRandomHuman(int wave, bool forceIgnoreCoinFlip, string reason)
    {
        AgentTracePanel.Trace("INFECTION", "Gas wave infection check started.");

        PlayerIdentity[] allPlayers = GetAllPlayers();
        int playersFound = allPlayers != null ? allPlayers.Length : 0;

        CollectInfectionStats(allPlayers, out int aliveHumansBefore, out int infectedBefore, out _);

        List<PlayerIdentity> eligiblePlayers = new List<PlayerIdentity>();

        if (allPlayers != null)
        {
            for (int index = 0; index < allPlayers.Length; index++)
            {
                PlayerIdentity player = allPlayers[index];

                if (player == null)
                {
                    continue;
                }

                if (!player.isAlive || player.isInfected)
                {
                    continue;
                }

                eligiblePlayers.Add(player);
            }
        }

        if (eligiblePlayers.Count == 0)
        {
            Debug.Log("[INFECT DEBUG] No valid human candidates available.", this);
            PrintInfectionSummary(wave, null, false, playersFound, aliveHumansBefore, infectedBefore, aliveHumansBefore, infectedBefore);
            return false;
        }

        if (wave == 1 && !forceIgnoreCoinFlip)
        {
            bool infectionHappens = UnityEngine.Random.Range(0, 2) == 1;
            Debug.Log(infectionHappens
                ? "[INFECT DEBUG] Wave 1 coin flip result: infection happens."
                : "[INFECT DEBUG] Wave 1 coin flip result: infection skipped.", this);

            if (!infectionHappens)
            {
                AgentTracePanel.Trace("INFECTION", "Wave 1 coin flip skipped infection.");
                PrintInfectionSummary(wave, null, false, playersFound, aliveHumansBefore, infectedBefore, aliveHumansBefore, infectedBefore);
                return false;
            }
        }

        int selectedIndex = UnityEngine.Random.Range(0, eligiblePlayers.Count);
        PlayerIdentity selectedPlayer = eligiblePlayers[selectedIndex];

        ApplyInfection(selectedPlayer, wave, reason);

        CollectInfectionStats(allPlayers, out int aliveHumansAfter, out int infectedAfter, out _);
        PrintInfectionSummary(wave, selectedPlayer, true, playersFound, aliveHumansBefore, infectedBefore, aliveHumansAfter, infectedAfter);
        return true;
    }

    public bool TryInfectPlayerById(int playerId, int wave, string reason)
    {
        PlayerIdentity[] allPlayers = GetAllPlayers();
        int playersFound = allPlayers != null ? allPlayers.Length : 0;

        CollectInfectionStats(allPlayers, out int aliveHumansBefore, out int infectedBefore, out _);

        PlayerIdentity target = FindPlayerById(allPlayers, playerId);
        if (target == null)
        {
            Debug.Log($"[INFECT DEBUG] PlayerId={playerId} not found.", this);
            PrintInfectionSummary(wave, null, false, playersFound, aliveHumansBefore, infectedBefore, aliveHumansBefore, infectedBefore);
            return false;
        }

        if (!target.isAlive || target.isInfected)
        {
            Debug.Log($"[INFECT DEBUG] PlayerId={playerId} is not a valid infection target. Alive={target.isAlive} Infected={target.isInfected}", target);
            PrintInfectionSummary(wave, target, false, playersFound, aliveHumansBefore, infectedBefore, aliveHumansBefore, infectedBefore);
            return false;
        }

        ApplyInfection(target, wave, reason);

        CollectInfectionStats(allPlayers, out int aliveHumansAfter, out int infectedAfter, out _);
        PrintInfectionSummary(wave, target, true, playersFound, aliveHumansBefore, infectedBefore, aliveHumansAfter, infectedAfter);
        return true;
    }

    public void DebugPrintAllPlayers()
    {
        PlayerIdentity[] allPlayers = GetAllPlayers();
        int totalPlayersFound = allPlayers != null ? allPlayers.Length : 0;
        CollectInfectionStats(allPlayers, out int aliveHumans, out int infectedCount, out int validHumanCandidatesCount);

        Debug.Log($"[INFECT DEBUG] total players found={totalPlayersFound}", this);
        Debug.Log($"[INFECT DEBUG] valid human infection candidates count={validHumanCandidatesCount}", this);
        Debug.Log($"[INFECT DEBUG] infected count={infectedCount}", this);
        Debug.Log($"[INFECT DEBUG] alive human count={aliveHumans}", this);

        if (allPlayers == null)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null)
            {
                continue;
            }

            bool hasSprite = player.GetComponent<SpriteRenderer>() != null;
            bool hasPlayerMovement = player.GetComponent<PlayerMovement>() != null;
            bool hasBotMovement = HasBotMovement(player);

            Debug.Log($"[INFECT DEBUG] PlayerId={player.playerId} Name={GetPlayerName(player)} Alive={player.isAlive} Infected={player.isInfected} AI={player.isAIControlled} Local={player.isLocalPlayer} HasSprite={hasSprite} HasPlayerMovement={hasPlayerMovement} HasBotMovement={hasBotMovement}", player);
        }
    }

    /// <summary>
    /// Silent meeting-vote cure. Does not freeze, log role outcome, or start legacy freeze coroutines.
    /// MeetingController owns the public antidote freeze sequence.
    /// </summary>
    public void ApplyVoteCure(PlayerIdentity target)
    {
        if (target == null || !target.isAlive || !target.isInfected)
        {
            return;
        }

        if (target == currentInfectedPlayer)
        {
            currentInfectedPlayer = null;
        }

        CancelAntidoteFreezeCoroutine(target);

        target.Cure();
        target.RefreshInfectionVisual(useObviousDebugInfectedColor);
        DisableBotMovement(target);
        target.RefreshControlState();

        string botId = GetApiPlayerId(target);
        RemoveRegisteredBotApiId(botId);

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }
    }

    /// <summary>
    /// Legacy/debug antidote path. Prefer MeetingController vote flow + ApplyVoteCure for meetings.
    /// </summary>
    public void ApplyAntidote(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        if (!target.isAlive)
        {
            Debug.Log($"[INFECT DEBUG] Antidote target {GetPlayerName(target)} is dead. Skipped.", target);
            return;
        }

        if (!target.isInfected)
        {
            Debug.Log($"[INFECT DEBUG] Antidote skipped for {GetPlayerName(target)} (not infected).", target);
            return;
        }

        ApplyVoteCure(target);

        if (frozenPlayers.TryGetValue(target, out Coroutine runningCoroutine) && runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
        }

        frozenPlayers[target] = StartCoroutine(FreezeMovementAfterAntidote(target));
    }

    /// <summary>
    /// Stops legacy InfectionSystem antidote freeze coroutines so MeetingController owns meeting freeze timing.
    /// </summary>
    public void CancelLegacyAntidoteFreeze(PlayerIdentity target)
    {
        CancelAntidoteFreezeCoroutine(target);
    }

    void CancelAntidoteFreezeCoroutine(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        if (frozenPlayers.TryGetValue(target, out Coroutine runningCoroutine) && runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
        }

        frozenPlayers.Remove(target);
    }

    void HandleDebugHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            DebugPrintAllPlayers();
        }

        if (Input.GetKeyDown(KeyCode.I))
        {
            int wave = GameManager.Instance != null ? GameManager.CurrentWave : GetDebugWaveNumber();
            TryInfectRandomHuman(wave, true, "DEBUG_FORCE_RANDOM");
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            TryInfectPlayerById(1, GetDebugWaveNumber(), "DEBUG_FORCE_PLAYER_1");
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            TryInfectPlayerById(2, GetDebugWaveNumber(), "DEBUG_FORCE_PLAYER_2");
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            TryInfectPlayerById(3, GetDebugWaveNumber(), "DEBUG_FORCE_PLAYER_3");
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            TryInfectPlayerById(4, GetDebugWaveNumber(), "DEBUG_FORCE_PLAYER_4");
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("[INFECT DEBUG] Debug hotkey C pressed. Curing infected players only. This is debug only.", this);
            CureAllInfectedPlayers();
        }
    }

    void CureAllInfectedPlayers()
    {
        PlayerIdentity[] allPlayers = GetAllPlayers();
        if (allPlayers == null)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isInfected)
            {
                continue;
            }

            player.Cure();
        }

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }
    }

    void ApplyInfection(PlayerIdentity target, int wave, string reason)
    {
        if (target == null)
        {
            Debug.Log("[INFECT DEBUG] ApplyInfection received a null target.", this);
            return;
        }

        if (!target.isAlive)
        {
            Debug.Log($"[INFECT DEBUG] Cannot infect {GetPlayerName(target)} because the player is dead.", target);
            return;
        }

        if (target.isInfected)
        {
            Debug.Log($"[INFECT DEBUG] {GetPlayerName(target)} is already infected.", target);
            return;
        }
        // Primary infection call - this should set isInfected and isAIControlled.
        target.Infect();
        target.RefreshInfectionVisual(useObviousDebugInfectedColor);

        AgentTracePanel.Trace("INFECTION", $"Wave {wave}: {GetPlayerName(target)} infected. Reason: {reason}");

        // Ensure the infection flags are set. PlayerIdentity controls the actual fields,
        // so we verify rather than assign directly (private setters).
        if (!target.isInfected || !target.isAIControlled)
        {
            Debug.LogWarning($"[INFECT DEBUG] Infection flags not set by Infect() for {GetPlayerName(target)}. Re-invoking Infect().", target);
            target.Infect();
            target.RefreshInfectionVisual(useObviousDebugInfectedColor);
        }

        ApplyBotTakeover(target, wave);
        UpdateAllBotModesForWave(wave);

        if (BotDecisionDirector.Instance != null)
        {
            BotDecisionDirector.Instance.RequestImmediateDecision(target);
        }

        currentInfectedPlayer = target;

        Debug.Log($"[INFECT DEBUG] INFECTED: {GetPlayerName(target)} on wave {wave}. Reason: {reason}", target);

        // Final verification log for takeover state.
        PlayerMovement movement = target.GetComponent<PlayerMovement>();
        BotMovement botMovement = target.GetComponent<BotMovement>();
        bool playerMovementEnabled = movement != null && movement.enabled;
        bool botMovementEnabled = botMovement != null && botMovement.enabled;
        string botModeName = botMovement != null ? botMovement.CurrentMode.ToString() : "NONE";
        Debug.Log($"[INFECT DEBUG] TAKEOVER RESULT: {GetPlayerName(target)} infected={target.isInfected} ai={target.isAIControlled} botMode={botModeName} playerMovementEnabled={playerMovementEnabled} botMovementEnabled={botMovementEnabled}", target);

        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.CheckLoseConditions();
        }

        // Non-blocking: attempt to register this new bot with Team A backend.
        TryRegisterBotWithTeamA(target, wave, reason);
    }

    void TryRegisterBotWithTeamA(PlayerIdentity target, int wave, string reason)
    {
        if (target == null) return;

        if (TeamAApiClient.Instance == null)
        {
            Debug.Log("[TEAM A API] RegisterBot skipped: TeamAApiClient missing.");
            return;
        }

        string botId = GetApiPlayerId(target);
        if (string.IsNullOrWhiteSpace(botId)) return;

        // Prevent duplicate register attempts for same bot during its infected lifetime.
        if (registeredBotApiIds.Contains(botId) && target.isInfected)
        {
            Debug.Log($"[TEAM A API] RegisterBot skipped: already registered {botId}.");
            return;
        }

        registeredBotApiIds.Add(botId);

        RegisterBotRequest request = new RegisterBotRequest
        {
            matchId = matchId,
            botId = botId,
            botName = GetDisplayName(target),
            wave = wave,
            cycle = GetCurrentCycle(),
            phase = GetCurrentPhaseName(),
            alivePlayers = GetAlivePlayerApiIds(),
            humanPlayers = GetHumanPlayerApiIds(),
            infectedPlayers = GetInfectedPlayerApiIds(),
            taskProgress = GetCurrentTaskProgress()
        };

        StartCoroutine(TeamAApiClient.Instance.RegisterBot(request, (ok, response) =>
        {
            string playerName = GetDisplayName(target);
            if (ok && response != null && response.ok)
            {
                string personality = string.IsNullOrWhiteSpace(response.personality) ? "(none)" : response.personality;
                string behavior = string.IsNullOrWhiteSpace(response.behaviorMode) ? "(none)" : response.behaviorMode;
                Debug.Log($"[TEAM A API] RegisterBot success for {playerName} personality={personality} mode={behavior}");
                AgentTracePanel.Trace("API", $"Registered {playerName} as Team A bot.");

                // Map and apply behavior mode if available
                if (!string.IsNullOrWhiteSpace(response.behaviorMode) && TryMapApiBehaviorMode(response.behaviorMode, out BotBehaviorMode mappedMode))
                {
                    BotMovement botMovement = target.GetComponent<BotMovement>();
                    if (botMovement != null)
                    {
                        botMovement.SetMode(mappedMode);
                        botMovement.ResumeBot();
                    }
                }
            }
            else
            {
                Debug.Log($"[TEAM A API] RegisterBot failed/skipped for {playerName}. Local fallback remains active.");
                AgentTracePanel.Trace("API", $"RegisterBot fallback for {playerName}.");
            }
        }));
    }

    BotBehaviorMode GetBotModeForWave(int wave)
    {
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

    void ApplyBotTakeover(PlayerIdentity target, int wave)
    {
        if (target == null)
        {
            return;
        }

        // Human input must be disabled when infection takeover starts.
        PlayerMovement movement = target.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
            Debug.Log($"[INFECT DEBUG] Disabled PlayerMovement on {GetPlayerName(target)}", target);
        }

        BotMovement botMovement = target.GetComponent<BotMovement>();
        if (botMovement == null)
        {
            Debug.LogWarning($"[INFECT DEBUG] BotMovement missing on {GetPlayerName(target)}. Cannot move as bot.", target);
            return;
        }

        BotBehaviorMode mode = GetBotModeForWave(wave);
        botMovement.enabled = true;
        botMovement.ResumeBot();
        botMovement.SetMode(mode);

        AgentTracePanel.Trace("BOT", $"{GetPlayerName(target)} converted to AI-controlled bot.");
        AgentTracePanel.Trace("BOT", $"{GetPlayerName(target)} behavior mode: {mode}");
        Debug.Log($"[INFECT DEBUG] Bot takeover applied to {GetPlayerName(target)}. Mode={mode}", target);
    }

    void UpdateAllBotModesForWave(int wave)
    {
        BotBehaviorMode mode = GetBotModeForWave(wave);
        PlayerIdentity[] allPlayers = GetAllPlayers();
        if (allPlayers == null)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null)
            {
                continue;
            }

            if (!player.isInfected || !player.isAIControlled)
            {
                continue;
            }

            BotMovement botMovement = player.GetComponent<BotMovement>();
            if (botMovement == null)
            {
                continue;
            }

            botMovement.enabled = true;
            botMovement.SetMode(mode);
            botMovement.ResumeBot();
            AgentTracePanel.Trace("BOT", $"{GetPlayerName(player)} behavior mode: {mode}");
        }
    }

    void SyncToGameManagerState()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.CurrentPhase == GamePhase.GasWave)
        {
            OnGasWaveStarted(GameManager.CurrentWave);
        }
        else
        {
            isGasWaveActive = false;
            ApplySafePhaseAudio();
            UpdateGasOverlay();
        }
    }

    void TrySubscribeToGameManagerEvents()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || isSubscribedToGameManagerEvents)
        {
            return;
        }

        gameManager.OnGasWaveStarted -= HandleGasWaveStarted;
        gameManager.OnGasWaveStarted += HandleGasWaveStarted;

        gameManager.OnGasWaveEnded -= HandleGasWaveEnded;
        gameManager.OnGasWaveEnded += HandleGasWaveEnded;

        gameManager.OnExplorationStarted -= HandleExplorationStarted;
        gameManager.OnExplorationStarted += HandleExplorationStarted;

        isSubscribedToGameManagerEvents = true;
    }

    void TryUnsubscribeFromGameManagerEvents()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || !isSubscribedToGameManagerEvents)
        {
            return;
        }

        gameManager.OnGasWaveStarted -= HandleGasWaveStarted;
        gameManager.OnGasWaveEnded -= HandleGasWaveEnded;
        gameManager.OnExplorationStarted -= HandleExplorationStarted;

        isSubscribedToGameManagerEvents = false;
    }

    void HandleGasWaveStarted()
    {
        int wave = GameManager.Instance != null ? GameManager.CurrentWave : gasWaveCount + 1;
        OnGasWaveStarted(wave);
    }

    void HandleGasWaveEnded()
    {
        StartSafePhase();
    }

    void HandleExplorationStarted()
    {
        StartSafePhase();
    }

    void StartSafePhase(bool isInitialStart = false)
    {
        isGasWaveActive = false;
        timer = safePhaseDuration;

        ApplySafePhaseAudio();

        if (isInitialStart)
        {
            return;
        }
    }

    void StartGasWave()
    {
        isGasWaveActive = true;
        timer = gasWaveDuration;
        gasWaveCount++;

        ApplyGasWaveAudio();

        if (GameManager.Instance == null)
        {
            OnGasWaveStarted(gasWaveCount);
        }
    }

    void InitializePresentation()
    {
        if (ambientSource != null)
        {
            ambientSafeVolume = ambientSource.volume;
            ambientSource.loop = true;
        }

        if (alarmSource != null)
        {
            alarmSource.loop = true;
        }

        if (gasOverlay != null)
        {
            SetOverlayAlpha(0f);
        }
    }

    void ApplySafePhaseAudio()
    {
        if (alarmSource != null)
        {
            alarmSource.Stop();
        }

        if (ambientSource != null)
        {
            if (ambientClip != null && ambientSource.clip != ambientClip)
            {
                ambientSource.clip = ambientClip;
            }

            ambientSource.loop = true;
            ambientSource.volume = ambientSafeVolume;

            if (ambientSource.clip != null && !ambientSource.isPlaying)
            {
                ambientSource.Play();
            }
        }
    }

    void ApplyGasWaveAudio()
    {
        if (ambientSource != null)
        {
            if (ambientClip != null && ambientSource.clip != ambientClip)
            {
                ambientSource.clip = ambientClip;
            }

            ambientSource.loop = true;
            ambientSource.volume = ambientGasVolume;

            if (ambientSource.clip != null && !ambientSource.isPlaying)
            {
                ambientSource.Play();
            }
        }

        if (alarmSource != null)
        {
            if (alarmClip != null && alarmSource.clip != alarmClip)
            {
                alarmSource.clip = alarmClip;
            }

            alarmSource.loop = true;

            if (alarmSource.clip != null)
            {
                alarmSource.Stop();
                alarmSource.Play();
            }
        }
    }

    void UpdateGasOverlay()
    {
        if (gasOverlay == null)
        {
            return;
        }

        float targetAlpha = isGasWaveActive ? 0.5f : 0f;
        Color overlayColor = Color.red;
        overlayColor.a = Mathf.MoveTowards(gasOverlay.color.a, targetAlpha, overlayFadeSpeed * Time.deltaTime);
        gasOverlay.color = overlayColor;
    }

    void SetOverlayAlpha(float alpha)
    {
        if (gasOverlay == null)
        {
            return;
        }

        Color overlayColor = Color.red;
        overlayColor.a = alpha;
        gasOverlay.color = overlayColor;
    }

    IEnumerator FreezeMovementAfterAntidote(PlayerIdentity target)
    {
        if (target == null)
        {
            yield break;
        }

        yield return new WaitForSeconds(antidoteFreezeDuration);

        frozenPlayers.Remove(target);

        if (target == null || !target.isAlive)
        {
            yield break;
        }

        SetPlayerMovementEnabled(target, !target.isAIControlled);
    }

    void CollectInfectionStats(PlayerIdentity[] allPlayers, out int aliveHumans, out int infectedCount, out int validHumanCandidatesCount)
    {
        aliveHumans = 0;
        infectedCount = 0;
        validHumanCandidatesCount = 0;

        if (allPlayers == null)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null)
            {
                continue;
            }

            if (player.isInfected)
            {
                infectedCount++;
            }

            if (!player.isAlive)
            {
                continue;
            }

            if (!player.isInfected)
            {
                aliveHumans++;
                validHumanCandidatesCount++;
            }
        }
    }

    void PrintInfectionSummary(int wave, PlayerIdentity target, bool success, int playersFound, int aliveHumansBefore, int infectedBefore, int aliveHumansAfter, int infectedAfter)
    {
        string chosenTarget = target != null ? GetPlayerName(target) : "NONE";
        Debug.Log($"[INFECT DEBUG] SUMMARY:\nWave: {wave}\nPlayers found: {playersFound}\nAlive humans before: {aliveHumansBefore}\nInfected before: {infectedBefore}\nChosen target: {chosenTarget}\nSuccess: {success}\nAlive humans after: {aliveHumansAfter}\nInfected after: {infectedAfter}", this);
    }

    PlayerIdentity[] GetAllPlayers()
    {
        return PlayerIdentity.GetAllPlayers();
    }

    // Public helpers for API integration
    public static string GetApiPlayerId(PlayerIdentity player)
    {
        if (player == null) return string.Empty;
        // PlayerIdentity currently exposes numeric playerId. Compose API id.
        return "player_" + player.playerId;
    }

    public static string GetDisplayName(PlayerIdentity player)
    {
        if (player == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(player.playerName)) return player.playerName;
        return "Player " + player.playerId;
    }

    // Snapshot helpers used for API requests
    string[] GetAlivePlayerApiIds()
    {
        PlayerIdentity[] all = GetAllPlayers();
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
        PlayerIdentity[] all = GetAllPlayers();
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
        PlayerIdentity[] all = GetAllPlayers();
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

    int GetCurrentTaskProgress()
    {
        if (TaskManager.Instance != null)
        {
            return Mathf.RoundToInt(TaskManager.Instance.taskProgress * 100f);
        }
        return 0;
    }

    string GetCurrentPhaseName()
    {
        return GameManager.CurrentPhase.ToString();
    }

    int GetCurrentWave()
    {
        return GameManager.CurrentWave;
    }

    int GetCurrentCycle()
    {
        // Prototype: cycles not implemented yet
        return 1;
    }

    // Allow external removal when a player is cured/unregistered
    public void RemoveRegisteredBotApiId(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId)) return;
        registeredBotApiIds.Remove(botId);
    }

    // Map API behavior mode strings to local BotBehaviorMode
    bool TryMapApiBehaviorMode(string apiMode, out BotBehaviorMode mode)
    {
        mode = BotBehaviorMode.FakeTask;
        if (string.IsNullOrWhiteSpace(apiMode)) return false;

        switch (apiMode)
        {
            case "stealth_fake_task":
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
            case "frozen":
            case "idle":
                // No direct mapping; keep existing mode
                return false;
            default:
                return false;
        }
    }

    PlayerIdentity FindPlayerById(PlayerIdentity[] allPlayers, int playerId)
    {
        if (allPlayers == null)
        {
            return null;
        }

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

    string GetPlayerName(PlayerIdentity player)
    {
        if (player == null)
        {
            return "NONE";
        }

        return string.IsNullOrWhiteSpace(player.playerName) ? player.gameObject.name : player.playerName;
    }

    bool HasBotMovement(PlayerIdentity player)
    {
        if (player == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour.GetType().Name == "BotMovement")
            {
                return true;
            }
        }

        return false;
    }

    void DisableBotMovement(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour.GetType().Name == "BotMovement")
            {
                behaviour.enabled = false;
            }
        }
    }

    void EnableBotMovement(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour.GetType().Name == "BotMovement")
            {
                behaviour.enabled = true;
            }
        }
    }

    void DisablePlayerMovement(PlayerIdentity target)
    {
        if (target == null)
        {
            return;
        }

        PlayerMovement movement = target.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }
    }

    void SetPlayerMovementEnabled(PlayerIdentity target, bool enabled)
    {
        if (target == null)
        {
            return;
        }

        PlayerMovement movement = target.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = enabled;
        }
    }

    int GetDebugWaveNumber()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.CurrentWave;
        }

        return Mathf.Max(1, gasWaveCount);
    }
}