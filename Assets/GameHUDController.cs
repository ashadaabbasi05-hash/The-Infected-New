using TMPro;
using UnityEngine;

public sealed class GameHUDController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TMP_Text phaseText;
    [SerializeField] TMP_Text waveText;
    [SerializeField] TMP_Text timerText;

    [Header("Formatting")]
    [SerializeField] string phasePrefix = "PHASE: ";
    [SerializeField] string waveFormat = "WAVE: {0}/{1}";

    static readonly Color32 PhaseColor = new Color32(242, 253, 255, 255);
    static readonly Color32 WaveColor = new Color32(169, 214, 221, 255);
    static readonly Color32 TimerColor = new Color32(169, 214, 221, 255);
    static readonly Color32 TimerWarningColor = new Color32(255, 90, 95, 255);

    GameManager gameManager;
    int lastDisplayedSecond = int.MinValue;

    static readonly string[] PhaseLabels =
    {
        "Exploration",
        "GasWave",
        "Meeting",
        "Voting"
    };

    void Awake()
    {
        TryCacheGameManager();
    }

    public void SetPhaseText(TMP_Text text)
    {
        phaseText = text;
        RefreshAll();
    }

    public void SetWaveText(TMP_Text text)
    {
        waveText = text;
        RefreshAll();
    }

    public void SetTimerText(TMP_Text text)
    {
        timerText = text;
        RefreshAll();
    }

    void OnEnable()
    {
        if (GameManager.Instance == null) return;

        // Prevent duplicate subscriptions
        GameManager.Instance.OnPhaseChanged -= UpdatePhase;
        GameManager.Instance.OnWaveChanged -= UpdateWave;

        GameManager.Instance.OnPhaseChanged += UpdatePhase;
        GameManager.Instance.OnWaveChanged += UpdateWave;
    }

    void Start()
    {
        if (GameManager.Instance != null)
        {
            UpdatePhase(GameManager.CurrentPhase);
            UpdateWave(GameManager.CurrentWave);
            int sec = Mathf.FloorToInt(GameManager.PhaseTimer);
            lastDisplayedSecond = sec;
            UpdateTimer(sec);
        }
    }

    void Update()
    {
        if (gameManager == null)
        {
            TryCacheGameManager();
        }

        int sec = Mathf.FloorToInt(GameManager.PhaseTimer);
        if (sec != lastDisplayedSecond)
        {
            lastDisplayedSecond = sec;
            UpdateTimer(sec);
        }
    }

    void OnDisable()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.OnPhaseChanged -= UpdatePhase;
        GameManager.Instance.OnWaveChanged -= UpdateWave;
    }

    void TryCacheGameManager()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    

    void RefreshAll()
    {
        if (gameManager == null)
        {
            if (phaseText != null)
            {
                phaseText.text = "PHASE: --";
                phaseText.color = PhaseColor;
            }

            if (waveText != null)
            {
                waveText.SetText(waveFormat, 0, 3);
                waveText.color = WaveColor;
            }

            if (timerText != null)
            {
                timerText.text = "TIME: 0";
                timerText.color = TimerColor;
            }

            return;
        }

        UpdatePhase(GameManager.CurrentPhase);
        UpdateWave(GameManager.CurrentWave);
        int sec = Mathf.FloorToInt(GameManager.PhaseTimer);
        lastDisplayedSecond = sec;
        UpdateTimer(sec);
    }

    void UpdatePhase(GamePhase phase)
    {
        if (phaseText == null) return;

        phaseText.text = string.Concat(phasePrefix, GetPhaseLabel(phase));
        phaseText.color = PhaseColor;
        phaseText.fontStyle = FontStyles.Bold;
    }

    void UpdateWave(int wave)
    {
        if (waveText == null) return;

        int max = GameManager.MaxWaves;
        waveText.SetText(waveFormat, Mathf.Clamp(wave, 0, max), max);
        waveText.color = WaveColor;
        waveText.fontStyle = FontStyles.Bold;
    }

    void UpdateTimer(int seconds)
    {
        if (timerText == null) return;

        int safeSeconds = Mathf.Max(0, seconds);
        timerText.SetText("TIME: {0}", safeSeconds);
        timerText.color = safeSeconds <= 10 ? TimerWarningColor : TimerColor;
        timerText.fontStyle = FontStyles.Bold;
    }

    string GetPhaseLabel(GamePhase phase)
    {
        int index = (int)phase;
        if (index >= 0 && index < PhaseLabels.Length)
        {
            return PhaseLabels[index].ToUpperInvariant();
        }

        return phase.ToString().ToUpperInvariant();
    }
}
