using TMPro;
using UnityEngine;

public sealed class GameHUDController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TMP_Text phaseText;
    [SerializeField] TMP_Text waveText;
    [SerializeField] TMP_Text timerText;

    [Header("Formatting")]
    [SerializeField] string phasePrefix = "Phase: ";
    [SerializeField] string waveFormat = "{0}/{1}";

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
            if (phaseText != null) phaseText.text = "Phase: --";
            if (waveText != null) waveText.SetText(waveFormat, 0, 3);
            if (timerText != null) timerText.text = "0";
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

        phaseText.text = string.Concat(phasePrefix, phase.ToString());
    }

    void UpdateWave(int wave)
    {
        if (waveText == null) return;

        int max = GameManager.MaxWaves;
        waveText.SetText(waveFormat, Mathf.Clamp(wave, 0, max), max);
    }

    void UpdateTimer(int seconds)
    {
        if (timerText == null) return;

        timerText.SetText("{0}s", Mathf.Max(0, seconds));
    }
}
