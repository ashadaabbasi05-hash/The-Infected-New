using UnityEngine;

/// <summary>
/// Optional debug tester for Team A API client.
/// Hotkeys:
/// - F9: Health check
/// - F10: Fake RegisterBot
/// - F11: Fake Respond
/// - F12: Fake Vote
/// 
/// Does not affect gameplay. Attaches to AgentTracePanel for output.
/// </summary>
public class TeamAApiDebugTester : MonoBehaviour
{
    [SerializeField] private TeamAApiClient apiClient;
    [SerializeField] private bool enableHotkeys = true;

    private void Start()
    {
        if (apiClient == null)
        {
            apiClient = TeamAApiClient.Instance;
            if (apiClient == null)
            {
                apiClient = FindAnyObjectByType<TeamAApiClient>();
            }
        }

        if (apiClient == null)
        {
            Debug.LogWarning("[TEAM A API TESTER] No TeamAApiClient found. Disabling tester.");
            enabled = false;
            return;
        }

        Debug.Log("[TEAM A API TESTER] Ready. Press F9-F12 to test.");
    }

    private void Update()
    {
        if (!enableHotkeys || apiClient == null) return;

        if (Input.GetKeyDown(KeyCode.F9))
        {
            TestHealth();
        }
        else if (Input.GetKeyDown(KeyCode.F10))
        {
            TestRegisterBot();
        }
        else if (Input.GetKeyDown(KeyCode.F11))
        {
            TestRespond();
        }
        else if (Input.GetKeyDown(KeyCode.F12))
        {
            TestVote();
        }
    }

    private void TestHealth()
    {
        Debug.Log("[TEAM A API TESTER] F9 pressed. Testing Health...");
        StartCoroutine(apiClient.Health((ok, response) =>
        {
            if (ok && response != null)
            {
                Debug.Log("[TEAM A API TEST] Health OK. contractVersion=" + response.contractVersion);
                AgentTracePanel.Trace("API", "Backend health OK. Version: " + response.contractVersion);
            }
            else
            {
                Debug.LogWarning("[TEAM A API TEST] Health failed or API disabled. Local fallback active.");
                AgentTracePanel.Trace("API", "Backend unavailable. Local fallback active.");
            }
        }));
    }

    private void TestRegisterBot()
    {
        Debug.Log("[TEAM A API TESTER] F10 pressed. Testing RegisterBot...");

        RegisterBotRequest request = new RegisterBotRequest
        {
            matchId = "ROOM123",
            botId = "player_2",
            botName = "Player 2",
            wave = 1,
            cycle = 1,
            phase = "GasWave",
            alivePlayers = new[] { "player_1", "player_2", "player_3", "player_4" },
            humanPlayers = new[] { "player_1", "player_3", "player_4" },
            infectedPlayers = new[] { "player_2" },
            taskProgress = 0
        };

        StartCoroutine(apiClient.RegisterBot(request, (ok, response) =>
        {
            if (ok && response != null)
            {
                Debug.Log("[TEAM A API TEST] RegisterBot OK. botId=" + response.botId + " personality=" + response.personality);
                AgentTracePanel.Trace("API", "Bot registered. Personality: " + response.personality);
            }
            else
            {
                Debug.LogWarning("[TEAM A API TEST] RegisterBot failed or API disabled.");
                AgentTracePanel.Trace("API", "RegisterBot failed. Using local behavior.");
            }
        }));
    }

    private void TestRespond()
    {
        Debug.Log("[TEAM A API TESTER] F11 pressed. Testing Respond...");

        ChatMessageDto accusation = new ChatMessageDto
        {
            sender = "player_1",
            senderName = "Player 1",
            text = "player_2 is sus"
        };

        RespondRequest request = new RespondRequest
        {
            matchId = "ROOM123",
            phase = "Meeting",
            wave = 1,
            cycle = 1,
            botId = "player_2",
            botName = "Player 2",
            personality = "cautious",
            message = "",
            latestMessage = accusation,
            recentChat = new[] { accusation },
            alivePlayers = new[] { "player_1", "player_2", "player_3", "player_4" },
            humanPlayers = new[] { "player_1", "player_3", "player_4" },
            infectedPlayers = new[] { "player_2" }
        };

        StartCoroutine(apiClient.Respond(request, (ok, response) =>
        {
            if (ok && response != null)
            {
                Debug.Log("[TEAM A API TEST] Respond OK. respond=" + response.respond + " messages=" + response.messages.Length);
                if (response.messages.Length > 0)
                {
                    AgentTracePanel.Trace("API", "Bot would say: " + response.messages[0]);
                }
                else
                {
                    AgentTracePanel.Trace("API", "Bot stays silent");
                }
            }
            else
            {
                Debug.LogWarning("[TEAM A API TEST] Respond failed or API disabled.");
                AgentTracePanel.Trace("API", "Respond failed. Bot silent.");
            }
        }));
    }

    private void TestVote()
    {
        Debug.Log("[TEAM A API TESTER] F12 pressed. Testing Vote...");

        RespondRequest mockMessage = new RespondRequest
        {
            matchId = "ROOM123",
            phase = "Meeting",
            wave = 1,
            cycle = 1,
            botId = "player_2",
            botName = "Player 2",
            personality = "cautious",
            message = "",
            latestMessage = null,
            recentChat = new ChatMessageDto[0],
            alivePlayers = new[] { "player_1", "player_2", "player_3", "player_4" },
            humanPlayers = new[] { "player_1", "player_3", "player_4" },
            infectedPlayers = new[] { "player_2" }
        };

        VoteRequest request = new VoteRequest
        {
            matchId = "ROOM123",
            phase = "Meeting",
            wave = 1,
            cycle = 1,
            botId = "player_2",
            botName = "Player 2",
            alivePlayers = new[] { "player_1", "player_2", "player_3", "player_4" },
            humanPlayers = new[] { "player_1", "player_3", "player_4" },
            infectedPlayers = new[] { "player_2" },
            recentChat = new ChatMessageDto[0]
        };

        StartCoroutine(apiClient.Vote(request, (ok, response) =>
        {
            if (ok && response != null)
            {
                Debug.Log("[TEAM A API TEST] Vote OK. voteTarget=" + response.voteTarget + " reason=" + response.reason);
                AgentTracePanel.Trace("API", "Bot votes: " + response.voteTarget);
            }
            else
            {
                Debug.LogWarning("[TEAM A API TEST] Vote failed or API disabled.");
                AgentTracePanel.Trace("API", "Vote failed. Using random fallback.");
            }
        }));
    }
}
