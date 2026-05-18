using System;
using UnityEngine;

public enum GamePhase
{
    Exploration = 0,
    GasWave = 1,
    Meeting = 2,
    Voting = 3
}

[Serializable]
public struct GameStateSnapshot
{
    public GamePhase phase;
    public int wave;
    public float phaseTimer;
}

[DefaultExecutionOrder(-100)]
public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public static GamePhase CurrentPhase => Instance != null ? Instance.currentPhase : GamePhase.Exploration;
    public static int CurrentWave => Instance != null ? Instance.currentWave : 0;
    public static int MaxWaves => Instance != null ? Instance.maxWaves : 3;
    public static float PhaseTimer => Instance != null ? Instance.phaseTimer : 0f;
    public static GameStateSnapshot Snapshot => Instance != null ? Instance.GetSnapshot() : default;

    [Header("Wave Settings")]
    [Tooltip("Seconds between gas waves while in Exploration.")]
    [Min(1f)]
    [SerializeField] float gasWaveInterval = 120f;

    [Tooltip("How long the GasWave phase lasts before moving to Meeting.")]
    [Min(0.1f)]
    [SerializeField] float gasWaveDuration = 25f;

    [Tooltip("How long the Meeting phase lasts before moving to Voting.")]
    [Min(0.1f)]
    [SerializeField] float meetingDuration = 20f;

    [Tooltip("How long the Voting phase lasts before returning to Exploration.")]
    [Min(0.1f)]
    [SerializeField] float votingDuration = 20f;

    [Tooltip("Total number of gas waves to trigger in the match.")]
    [Min(1)]
    [SerializeField] int maxWaves = 3;

    [Header("Startup")]
    [Tooltip("If enabled, the game starts in Exploration immediately on play.")]
    [SerializeField] bool autoStart = true;

    [SerializeField] GamePhase currentPhase = GamePhase.Exploration;
    [SerializeField] int currentWave;
    [SerializeField] float phaseTimer;

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int> OnWaveChanged;
    public event Action OnExplorationStarted;
    public event Action OnGasWaveStarted;
    public event Action OnGasWaveEnded;
    public event Action OnMeetingStarted;
    public event Action OnVotingStarted;
    public event Action<GameStateSnapshot> OnStateChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (autoStart)
        {
            StartMatch();
        }
    }

    void Update()
    {
        phaseTimer += Time.deltaTime;

        switch (currentPhase)
        {
            case GamePhase.Exploration:
                if (currentWave < maxWaves && phaseTimer >= gasWaveInterval)
                {
                    EnterGasWave();
                }
                break;

            case GamePhase.GasWave:
                if (phaseTimer >= gasWaveDuration)
                {
                    EnterMeeting();
                }
                break;

            case GamePhase.Meeting:
                if (phaseTimer >= meetingDuration)
                {
                    EnterVoting();
                }
                break;

            case GamePhase.Voting:
                if (phaseTimer >= votingDuration)
                {
                    EnterExploration();
                }
                break;
        }
    }

    public void StartMatch()
    {
        currentWave = 0;
        EnterExploration();
    }

    public GameStateSnapshot GetSnapshot()
    {
        return new GameStateSnapshot
        {
            phase = currentPhase,
            wave = currentWave,
            phaseTimer = phaseTimer
        };
    }

    public void ApplySnapshot(GameStateSnapshot snapshot)
    {
        currentPhase = snapshot.phase;
        currentWave = snapshot.wave;
        phaseTimer = snapshot.phaseTimer;

        OnPhaseChanged?.Invoke(currentPhase);
        OnWaveChanged?.Invoke(currentWave);
        RaiseStateChanged();
    }

    public void SetPhase(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Exploration:
                EnterExploration();
                break;
            case GamePhase.GasWave:
                EnterGasWave();
                break;
            case GamePhase.Meeting:
                EnterMeeting();
                break;
            case GamePhase.Voting:
                EnterVoting();
                break;
        }
    }

    public bool CanTriggerGasWave()
    {
        return currentWave < maxWaves && currentPhase == GamePhase.Exploration;
    }

    public void EnterExploration()
    {
        SetPhaseInternal(GamePhase.Exploration);
    }

    public void EnterGasWave()
    {
        if (currentWave >= maxWaves)
        {
            EnterExploration();
            return;
        }

        currentWave++;
        OnWaveChanged?.Invoke(currentWave);
        SetPhaseInternal(GamePhase.GasWave);
    }

    public void EnterMeeting()
    {
        SetPhaseInternal(GamePhase.Meeting);
    }

    public void EnterVoting()
    {
        SetPhaseInternal(GamePhase.Voting);
    }

    public void AddPhaseTime(float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        phaseTimer += seconds;
    }

    public void ForceReset()
    {
        currentWave = 0;
        EnterExploration();
    }

    void SetPhaseInternal(GamePhase nextPhase)
    {
        if (currentPhase == nextPhase && phaseTimer <= 0f)
        {
            return;
        }

        GamePhase previousPhase = currentPhase;
        currentPhase = nextPhase;
        phaseTimer = 0f;
        OnPhaseChanged?.Invoke(currentPhase);

        switch (currentPhase)
        {
            case GamePhase.Exploration:
                OnExplorationStarted?.Invoke();
                if (previousPhase == GamePhase.GasWave)
                {
                    OnGasWaveEnded?.Invoke();
                }
                break;

            case GamePhase.GasWave:
                OnGasWaveStarted?.Invoke();
                break;

            case GamePhase.Meeting:
                OnMeetingStarted?.Invoke();
                if (previousPhase == GamePhase.GasWave)
                {
                    OnGasWaveEnded?.Invoke();
                }
                break;

            case GamePhase.Voting:
                OnVotingStarted?.Invoke();
                break;
        }

        RaiseStateChanged();
    }

    void RaiseStateChanged()
    {
        OnStateChanged?.Invoke(GetSnapshot());
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void OnValidate()
    {
        if (gasWaveInterval < 1f) gasWaveInterval = 1f;
        if (gasWaveDuration < 0.1f) gasWaveDuration = 0.1f;
        if (meetingDuration < 0.1f) meetingDuration = 0.1f;
        if (votingDuration < 0.1f) votingDuration = 0.1f;
        if (maxWaves < 1) maxWaves = 1;
    }
}
