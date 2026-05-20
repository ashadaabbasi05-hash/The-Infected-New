using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FinalHuntManager : MonoBehaviour
{
    [Header("Auto Check")]
    [SerializeField] bool autoCheckFinalHunt = true;
    [SerializeField, Min(0.1f)] float checkInterval = 0.5f;

    [Header("UI")]
    [SerializeField] GameObject finalHuntPanel;
    [SerializeField] CanvasGroup finalHuntCanvasGroup;
    [SerializeField] TMP_Text finalHuntText;

    [Header("Warning Fade")]
    [SerializeField, Min(0f)] float warningShowDuration = 2f;
    [SerializeField, Min(0f)] float warningFadeDuration = 1.5f;
    [SerializeField] bool hideFinalHuntPanelAfterWarning = true;

    [Header("Audio")]
    [SerializeField] AudioSource finalHuntAudioSource;
    [SerializeField] AudioClip finalHuntClip;

    [Header("References")]
    [SerializeField] MeetingController meetingController;
    [SerializeField] GasWaveEffectsController gasWaveEffectsController;
    [SerializeField] GameManager gameManager;
    [SerializeField] GameEndManager gameEndManager;

    [Header("Debug")]
    [SerializeField] bool enableDebugHotkeys = true;
    [SerializeField] bool verboseFinalHuntLogs = false;

    public bool IsFinalHuntActive { get; private set; }

    public event Action OnFinalHuntStarted;

    float checkTimer;
    Coroutine finalHuntWarningRoutine;
    int lastLoggedAliveInfected = -1;
    int lastLoggedAliveHumans = -1;
    bool warningUiConfigured;
    PlayerIdentity trackedLastHuman;
    bool meetingControllerWasEnabledBeforeFinalHunt = true;
    bool meetingControllerWasActiveBeforeFinalHunt = true;
    bool gasWaveEffectsControllerWasEnabledBeforeFinalHunt = true;
    bool gameManagerWasEnabledBeforeFinalHunt = true;

    void Awake()
    {
        AutoAssignReferences();
        EnsureFinalHuntWarningUiConfigured();
        HideFinalHuntWarningImmediate();

        if (finalHuntPanel != null)
        {
            finalHuntPanel.SetActive(false);
        }
    }

    void OnEnable()
    {
        AutoAssignReferences();

        if (gameManager != null)
        {
            gameManager.OnPhaseChanged -= HandleGamePhaseChanged;
            gameManager.OnPhaseChanged += HandleGamePhaseChanged;
        }
    }

    void OnDisable()
    {
        StopFinalHuntWarningRoutine();

        if (gameManager != null)
        {
            gameManager.OnPhaseChanged -= HandleGamePhaseChanged;
        }
    }

    void Update()
    {
        HandleDebugHotkeys();

        if (!autoCheckFinalHunt || IsFinalHuntActive)
        {
            return;
        }

        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
        {
            return;
        }

        checkTimer = 0f;
        if (CheckFinalHuntCondition())
        {
            StartFinalHunt();
        }
    }

    public bool CheckFinalHuntCondition()
    {
        if (!TryGetFinalHuntCounts(out int aliveInfected, out int aliveHumans))
        {
            return false;
        }

        if (verboseFinalHuntLogs || aliveInfected != lastLoggedAliveInfected || aliveHumans != lastLoggedAliveHumans)
        {
            lastLoggedAliveInfected = aliveInfected;
            lastLoggedAliveHumans = aliveHumans;
            Debug.Log($"[FINAL HUNT] Check: infected={aliveInfected} humans={aliveHumans}", this);
        }

        return aliveInfected == 3 && aliveHumans == 1;
    }

    public void StartFinalHunt()
    {
        StartFinalHunt(false);
    }

    void StartFinalHunt(bool bypassConditionCheck)
    {
        if (IsFinalHuntActive)
        {
            return;
        }

        if (!HasValidFinalHuntState(out int infectedCount, out int humanCount, out PlayerIdentity lastHuman))
        {
            Debug.LogWarning($"[FINAL HUNT] Start blocked. Need infected=3 humans=1, found infected={infectedCount} humans={humanCount}", this);
            return;
        }

        IsFinalHuntActive = true;
        AgentTracePanel.Trace("FINAL HUNT", "Final Hunt started: 3 infected vs 1 human.");
        Debug.Log("[FINAL HUNT] STARTED", this);

        StopFinalHuntWarningRoutine();

        if (finalHuntPanel != null)
        {
            finalHuntPanel.SetActive(true);
        }

        if (finalHuntText != null)
        {
            finalHuntText.text = "FINAL HUNT\nLAST HUMAN SURVIVE";
            finalHuntText.raycastTarget = false;
        }

        EnsureFinalHuntWarningUiConfigured();
        ShowThenFadeFinalHuntWarning();

        if (finalHuntAudioSource != null && finalHuntClip != null)
        {
            finalHuntAudioSource.Stop();
            finalHuntAudioSource.PlayOneShot(finalHuntClip);
        }

        StopNormalPhaseSystemsForFinalHunt();
        ApplyFinalHuntBotModeToAllInfected();

        if (lastHuman.isInfected || lastHuman.isAIControlled)
        {
            lastHuman.Cure();
        }

        PlayerMovement humanMovement = lastHuman.GetComponent<PlayerMovement>();
        if (humanMovement != null)
        {
            humanMovement.enabled = lastHuman.isLocalPlayer;
        }

        BotMovement humanBot = lastHuman.GetComponent<BotMovement>();
        if (humanBot != null)
        {
            humanBot.enabled = false;
        }

        AgentTracePanel.Trace("FINAL HUNT", $"Last human: {lastHuman.playerName}");
        Debug.Log($"[FINAL HUNT] Last human is {GetDebugPlayerLabel(lastHuman)}.", lastHuman);
        trackedLastHuman = lastHuman;

        OnFinalHuntStarted?.Invoke();
        Debug.Log("[FINAL HUNT] Objective HUD notified.", this);
    }

    public void ResetFinalHuntForDemo()
    {
        StopFinalHuntWarningRoutine();

        IsFinalHuntActive = false;
        trackedLastHuman = null;

        HideFinalHuntWarningImmediate();

        if (finalHuntText != null)
        {
            finalHuntText.text = string.Empty;
            finalHuntText.gameObject.SetActive(false);
        }

        if (finalHuntPanel != null)
        {
            finalHuntPanel.SetActive(false);
        }

        if (meetingController != null)
        {
            meetingController.gameObject.SetActive(meetingControllerWasActiveBeforeFinalHunt);
            meetingController.enabled = meetingControllerWasEnabledBeforeFinalHunt;
        }

        if (gasWaveEffectsController != null)
        {
            gasWaveEffectsController.enabled = gasWaveEffectsControllerWasEnabledBeforeFinalHunt;
        }

        if (gameManager != null)
        {
            gameManager.enabled = gameManagerWasEnabledBeforeFinalHunt;
        }

        Debug.Log("[FINAL HUNT] Reset for demo.", this);
    }

    public void ForceStartFinalHuntDebug()
    {
        if (CheckFinalHuntCondition())
        {
            StartFinalHunt();
        }
        else
        {
            Debug.LogWarning("[FINAL HUNT DEBUG] Cannot force start: need 3 infected and 1 human. Press K to create debug state.", this);
        }
    }

    public PlayerIdentity FindLastHuman()
    {
        if (HasValidFinalHuntState(out _, out _, out PlayerIdentity lastHuman))
        {
            return lastHuman;
        }

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        int aliveHumans = 0;

        if (allPlayers != null)
        {
            for (int i = 0; i < allPlayers.Length; i++)
            {
                PlayerIdentity player = allPlayers[i];
                if (player != null && player.isAlive && !player.isInfected)
                {
                    aliveHumans++;
                }
            }
        }

        Debug.LogWarning($"[FINAL HUNT] Cannot find a unique last human. Alive humans={aliveHumans}.", this);
        return null;
    }

    public void ApplyFinalHuntBotModeToAllInfected()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive || !player.isInfected)
            {
                continue;
            }

            // Ensure infected players remain AI-controlled during final hunt.
            if (!player.isAIControlled)
            {
                player.Infect();
            }

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.enabled = false;
            }

            BotMovement botMovement = player.GetComponent<BotMovement>();
            if (botMovement == null)
            {
                string missingBotName = GetDebugPlayerLabel(player);
                Debug.LogWarning($"[FINAL HUNT] BotMovement missing on {missingBotName}. Cannot move as bot.", player);
                continue;
            }

            botMovement.enabled = true;
            botMovement.SetMode(BotBehaviorMode.FinalHunt);
            botMovement.ResumeBot();

            AgentTracePanel.Trace("BOT", $"{GetDebugPlayerLabel(player)} assigned FinalHunt chase behavior.");
            Debug.Log($"[FINAL HUNT] {GetDebugPlayerLabel(player)} set to FinalHunt bot.", player);
        }
    }

    public void StopNormalPhaseSystemsForFinalHunt()
    {
        if (meetingController != null)
        {
            meetingControllerWasActiveBeforeFinalHunt = meetingController.gameObject.activeSelf;
            meetingControllerWasEnabledBeforeFinalHunt = meetingController.enabled;
            // Disable meeting system component and object to stop updates and hide UI.
            meetingController.enabled = false;
            meetingController.gameObject.SetActive(false);
        }

        if (gasWaveEffectsController != null)
        {
            gasWaveEffectsControllerWasEnabledBeforeFinalHunt = gasWaveEffectsController.enabled;
            gasWaveEffectsController.ForceStopGasEffects();
            gasWaveEffectsController.enabled = false;
        }

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager != null)
        {
            gameManagerWasEnabledBeforeFinalHunt = gameManager.enabled;
            // Keep current phase in Exploration and stop phase loop updates.
            if (GameManager.CurrentPhase != GamePhase.Exploration)
            {
                gameManager.EnterExploration();
            }

            gameManager.enabled = false;
            Debug.Log("[FINAL HUNT] GameManager phase loop disabled for Final Hunt.", gameManager);
        }
        else
        {
            Debug.LogWarning("[FINAL HUNT] GameManager does not expose pause/lock method. Continuing with FinalHuntManager active.", this);
        }

        if (gameEndManager == null)
        {
            gameEndManager = GameEndManager.Instance;
        }
    }

    void HandleDebugHotkeys()
    {
        if (!enableDebugHotkeys)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            if (CheckFinalHuntCondition())
            {
                StartFinalHunt();
            }
            else
            {
                Debug.LogWarning("[FINAL HUNT DEBUG] Cannot force start: need 3 infected and 1 human. Press K to create debug state.", this);
            }
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            CreateFinalHuntDebugState();
        }
    }

    // Public demo API for mobile Final Hunt button.
    public void StartFinalHuntDemo()
    {
        AgentTracePanel.Trace("DEMO", "Final Hunt demo button pressed.");
        CreateFinalHuntDebugState();
    }

    void CreateFinalHuntDebugState()
    {
        if (IsFinalHuntActive)
        {
            return;
        }

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null || allPlayers.Length == 0)
        {
            return;
        }

        PlayerIdentity playerOne = FindPlayerById(1, allPlayers);
        PlayerIdentity playerTwo = FindPlayerById(2, allPlayers);
        PlayerIdentity playerThree = FindPlayerById(3, allPlayers);
        PlayerIdentity playerFour = FindPlayerById(4, allPlayers);

        if (playerOne == null || playerTwo == null || playerThree == null || playerFour == null)
        {
            Debug.LogWarning("[FINAL HUNT DEBUG] Cannot create debug state: Player 1-4 must all exist.", this);
            return;
        }

        SetDebugHumanState(playerOne);
        SetDebugInfectedState(playerTwo);
        SetDebugInfectedState(playerThree);
        SetDebugInfectedState(playerFour);

        if (!HasValidFinalHuntState(out int aliveInfected, out int aliveHumans, out PlayerIdentity lastHuman))
        {
            Debug.LogWarning($"[FINAL HUNT DEBUG] Debug state invalid after setup: infected={aliveInfected} humans={aliveHumans}", this);
            return;
        }

        if (lastHuman == null || lastHuman.playerId != 1)
        {
            Debug.LogWarning("[FINAL HUNT DEBUG] Cannot create debug state: Player 1 must be the only alive human.", this);
            return;
        }

        Debug.Log("[FINAL HUNT DEBUG] Created debug state: infected=3 humans=1 lastHuman=Player 1", this);
        StartFinalHunt(true);
    }

    void SetDebugHumanState(PlayerIdentity player)
    {
        if (player == null)
        {
            return;
        }

        player.RevivePlayer();
        if (player.isInfected || player.isAIControlled)
        {
            player.Cure();
        }

        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = true;
        }

        BotMovement botMovement = player.GetComponent<BotMovement>();
        if (botMovement != null)
        {
            botMovement.enabled = false;
        }
    }

    void SetDebugInfectedState(PlayerIdentity player)
    {
        if (player == null)
        {
            return;
        }

        player.RevivePlayer();
        if (!player.isInfected || !player.isAIControlled)
        {
            player.Infect();
        }

        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        BotMovement botMovement = player.GetComponent<BotMovement>();
        if (botMovement == null)
        {
            Debug.LogWarning($"[FINAL HUNT DEBUG] Missing BotMovement on {GetDebugPlayerLabel(player)}.", player);
            return;
        }

        botMovement.enabled = true;
        botMovement.SetMode(BotBehaviorMode.FinalHunt);
        botMovement.ResumeBot();
    }

    bool HasValidFinalHuntState(out int infectedCount, out int humanCount, out PlayerIdentity lastHuman)
    {
        infectedCount = 0;
        humanCount = 0;
        lastHuman = null;

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return false;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            if (player.isInfected)
            {
                infectedCount++;
                continue;
            }

            humanCount++;
            if (lastHuman == null)
            {
                lastHuman = player;
            }
        }

        return infectedCount == 3 && humanCount == 1 && lastHuman != null;
    }

    PlayerIdentity FindPlayerById(int playerId, PlayerIdentity[] allPlayers = null)
    {
        PlayerIdentity[] players = allPlayers ?? PlayerIdentity.GetAllPlayers();
        if (players == null)
        {
            return null;
        }

        for (int i = 0; i < players.Length; i++)
        {
            PlayerIdentity player = players[i];
            if (player != null && player.playerId == playerId)
            {
                return player;
            }
        }

        return null;
    }

    bool TryGetFinalHuntCounts(out int aliveInfected, out int aliveHumans)
    {
        aliveInfected = 0;
        aliveHumans = 0;

        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null)
        {
            return false;
        }

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity player = allPlayers[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            if (player.isInfected)
            {
                aliveInfected++;
            }
            else
            {
                aliveHumans++;
            }
        }

        return true;
    }

    void GetFinalHuntCounts(out int aliveInfected, out int aliveHumans)
    {
        TryGetFinalHuntCounts(out aliveInfected, out aliveHumans);
    }

    string GetDebugPlayerLabel(PlayerIdentity player)
    {
        if (player == null)
        {
            return "Unknown";
        }

        if (player.playerId > 0)
        {
            return $"Player {player.playerId}";
        }

        return string.IsNullOrWhiteSpace(player.playerName) ? player.gameObject.name : player.playerName;
    }

    void EnsureFinalHuntWarningUiConfigured()
    {
        if (finalHuntPanel == null)
        {
            return;
        }

        if (finalHuntCanvasGroup == null)
        {
            finalHuntCanvasGroup = finalHuntPanel.GetComponent<CanvasGroup>();
            if (finalHuntCanvasGroup == null)
            {
                finalHuntCanvasGroup = finalHuntPanel.AddComponent<CanvasGroup>();
            }
        }

        if (finalHuntCanvasGroup != null)
        {
            finalHuntCanvasGroup.alpha = 0f;
            finalHuntCanvasGroup.blocksRaycasts = false;
            finalHuntCanvasGroup.interactable = false;
        }

        Image panelImage = finalHuntPanel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.raycastTarget = false;
        }

        if (finalHuntText != null)
        {
            finalHuntText.raycastTarget = false;
        }

        if (!warningUiConfigured)
        {
            warningUiConfigured = true;
            Debug.Log("[FINAL HUNT] Warning UI configured as non-blocking.", this);
        }
    }

    void ShowThenFadeFinalHuntWarning()
    {
        StopFinalHuntWarningRoutine();
        finalHuntWarningRoutine = StartCoroutine(ShowThenFadeFinalHuntWarningRoutine());
    }

    IEnumerator ShowThenFadeFinalHuntWarningRoutine()
    {
        if (finalHuntPanel == null)
        {
            finalHuntWarningRoutine = null;
            yield break;
        }

        EnsureFinalHuntWarningUiConfigured();
        finalHuntPanel.SetActive(true);

        if (finalHuntCanvasGroup != null)
        {
            finalHuntCanvasGroup.alpha = 1f;
            finalHuntCanvasGroup.blocksRaycasts = false;
            finalHuntCanvasGroup.interactable = false;
        }

        if (warningShowDuration > 0f)
        {
            float showTimer = 0f;
            while (showTimer < warningShowDuration)
            {
                showTimer += Time.unscaledDeltaTime;
                if (finalHuntCanvasGroup != null)
                {
                    finalHuntCanvasGroup.blocksRaycasts = false;
                    finalHuntCanvasGroup.interactable = false;
                }

                yield return null;
            }
        }

        if (finalHuntCanvasGroup == null || warningFadeDuration <= 0f)
        {
            if (finalHuntCanvasGroup != null)
            {
                finalHuntCanvasGroup.alpha = 0f;
            }

            if (hideFinalHuntPanelAfterWarning)
            {
                finalHuntPanel.SetActive(false);
            }

            finalHuntWarningRoutine = null;
            yield break;
        }

        float fadeTimer = 0f;
        while (fadeTimer < warningFadeDuration)
        {
            fadeTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeTimer / warningFadeDuration);
            finalHuntCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            finalHuntCanvasGroup.blocksRaycasts = false;
            finalHuntCanvasGroup.interactable = false;
            yield return null;
        }

        finalHuntCanvasGroup.alpha = 0f;
        finalHuntCanvasGroup.blocksRaycasts = false;
        finalHuntCanvasGroup.interactable = false;

        if (hideFinalHuntPanelAfterWarning)
        {
            finalHuntPanel.SetActive(false);
        }

        finalHuntWarningRoutine = null;
    }

    public void HideFinalHuntWarningImmediate()
    {
        StopFinalHuntWarningRoutine();

        if (finalHuntCanvasGroup != null)
        {
            finalHuntCanvasGroup.alpha = 0f;
            finalHuntCanvasGroup.blocksRaycasts = false;
            finalHuntCanvasGroup.interactable = false;
        }

        if (finalHuntPanel != null)
        {
            finalHuntPanel.SetActive(false);
        }
    }

    void StopFinalHuntWarningRoutine()
    {
        if (finalHuntWarningRoutine != null)
        {
            StopCoroutine(finalHuntWarningRoutine);
            finalHuntWarningRoutine = null;
        }
    }

    void HandleGamePhaseChanged(GamePhase phase)
    {
        if (!IsFinalHuntActive)
        {
            return;
        }

        // Safety: if something else re-enabled GameManager and changed phase,
        // force Exploration to keep Final Hunt uninterrupted.
        if (phase != GamePhase.Exploration && gameManager != null)
        {
            gameManager.EnterExploration();
        }
    }

    void AutoAssignReferences()
    {
        if (meetingController == null)
        {
            meetingController = FindAnyObjectByType<MeetingController>(FindObjectsInactive.Include);
        }

        if (gasWaveEffectsController == null)
        {
            gasWaveEffectsController = FindAnyObjectByType<GasWaveEffectsController>(FindObjectsInactive.Include);
        }

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
            if (gameManager == null)
            {
                gameManager = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            }
        }

        if (gameEndManager == null)
        {
            gameEndManager = GameEndManager.Instance;
            if (gameEndManager == null)
            {
                gameEndManager = FindAnyObjectByType<GameEndManager>(FindObjectsInactive.Include);
            }
        }

        if (finalHuntAudioSource == null)
        {
            finalHuntAudioSource = GetComponent<AudioSource>();
        }
    }
}
