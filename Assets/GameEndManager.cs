using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public sealed class GameEndManager : MonoBehaviour
{
    public static GameEndManager Instance { get; private set; }

    public bool IsGameEnded => gameEnded;

    [Header("UI")]
    [SerializeField] GameObject winPanel;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] TMP_Text winResultText;
    [SerializeField] TMP_Text gameOverResultText;

    [Header("Runtime")]
    [SerializeField, Min(0.1f)] float checkInterval = 0.5f;

    bool gameEnded;
    float checkTimer;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HideEndPanels();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Start()
    {
        HideEndPanels();
    }

    void Update()
    {
        if (gameEnded)
        {
            return;
        }

        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
        {
            return;
        }

        checkTimer = 0f;
        CheckLoseConditions();
    }

    public void TriggerHumanWin()
    {
        if (gameEnded)
        {
            Debug.Log("[GAME END] Human win ignored: game already ended.", this);
            return;
        }

        gameEnded = true;
        ShowPanel(winPanel);
        HidePanel(gameOverPanel);

        if (winResultText != null)
        {
            winResultText.text = "HUMANS ESCAPED";
        }

        DisablePlayerMovement();
        Debug.Log("Humans escaped. You win.", this);
    }

    public void ResetGameEndForDemo()
    {
        gameEnded = false;
        checkTimer = 0f;
        HideEndPanels();
    }

    public void CheckLoseConditions()
    {
        if (gameEnded)
        {
            return;
        }

        PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
        if (players == null || players.Length == 0)
        {
            TriggerGameOver("No players remain.");
            return;
        }

        bool hasAlivePlayer = false;
        bool hasAliveHuman = false;
        bool hasAliveInfected = false;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerIdentity player = players[i];
            if (player == null || !player.isAlive)
            {
                continue;
            }

            hasAlivePlayer = true;

            if (player.isInfected)
            {
                hasAliveInfected = true;
            }
            else
            {
                hasAliveHuman = true;
            }
        }

        if (!hasAlivePlayer)
        {
            TriggerGameOver("All players are dead.");
            return;
        }

        if (!hasAliveHuman)
        {
            TriggerGameOver("No alive human players remain.");
            return;
        }

        if (!hasAliveInfected)
        {
            return;
        }
    }

    public void TriggerGameOver(string reason)
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;
        ShowPanel(gameOverPanel);
        HidePanel(winPanel);

        if (gameOverResultText != null)
        {
            gameOverResultText.text = $"GAME OVER\n{reason}";
        }

        DisablePlayerMovement();
        Debug.Log($"GAME OVER: {reason}", this);
    }

    void DisablePlayerMovement()
    {
        PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
        if (players == null)
        {
            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            PlayerIdentity player = players[i];
            if (player == null)
            {
                continue;
            }

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.enabled = false;
            }
        }
    }

    void HideEndPanels()
    {
        HidePanel(winPanel);
        HidePanel(gameOverPanel);

        if (winResultText != null)
        {
            winResultText.text = string.Empty;
        }

        if (gameOverResultText != null)
        {
            gameOverResultText.text = string.Empty;
        }
    }

    void ShowPanel(GameObject panel)
    {
        if (panel != null)
        {
            panel.SetActive(true);
        }
    }

    void HidePanel(GameObject panel)
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    void SetResultText(string message)
    {
        if (winResultText != null)
        {
            winResultText.text = message;
        }

        if (gameOverResultText != null)
        {
            gameOverResultText.text = message;
        }
    }
}
