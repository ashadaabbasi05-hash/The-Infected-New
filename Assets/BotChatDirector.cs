using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BotChatDirector : MonoBehaviour
{
    [Serializable]
    public class ChatMessageRuntime
    {
        public string senderId;
        public string senderName;
        public string text;
        public float time;
    }

    public static BotChatDirector Instance { get; private set; }

    [Header("Behavior")]
    [SerializeField] bool enableChat = true;
    [SerializeField] bool useTeamARespondApi = false;
    [SerializeField] bool allowLocalFallbackBotResponses = true;
    [SerializeField] float botResponseDelayMin = 1.0f;
    [SerializeField] float botResponseDelayMax = 2.5f;
    [SerializeField] float apiResponseTimeoutSeconds = 5f;
    [SerializeField] int maxRecentMessagesForApi = 8;
    [SerializeField] int maxMessagesOnScreen = 12;
    [SerializeField] string matchId = "ROOM123";
    [SerializeField] TMP_InputField chatInputField;
    [SerializeField] Button sendButton;
    [SerializeField] TMP_Text chatLogText;
    [SerializeField] ScrollRect chatScrollRect;
    [SerializeField] GameObject chatPanelRoot;
    [SerializeField] CanvasGroup chatCanvasGroup;
    [SerializeField] bool debugLogs = true;

    readonly List<ChatMessageRuntime> recentMessages = new List<ChatMessageRuntime>(32);
    bool responseInProgress;

    readonly string[] localFallbackLines = new string[]
    {
        "I was doing tasks.",
        "I did not see anything.",
        "Stay focused on tasks.",
        "Who was near the last gas wave?",
        "We should use antidote carefully.",
        "I think we need more info."
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (debugLogs) Debug.LogWarning("[CHAT] Duplicate BotChatDirector found. Disabling duplicate.");
            gameObject.SetActive(false);
            return;
        }

        Instance = this;
        AutoFindUiIfMissing();

        if (sendButton != null)
        {
            sendButton.onClick.RemoveAllListeners();
            sendButton.onClick.AddListener(SendLocalPlayerMessage);
        }

        if (chatPanelRoot != null)
        {
            HideChat();
        }
    }

    void AutoFindUiIfMissing()
    {
        if (chatInputField == null)
        {
            GameObject go = GameObject.Find("ChatInputField");
            if (go != null) chatInputField = go.GetComponent<TMP_InputField>();
        }

        if (sendButton == null)
        {
            GameObject go = GameObject.Find("ChatSendButton");
            if (go != null) sendButton = go.GetComponent<Button>();
        }

        if (chatLogText == null)
        {
            GameObject go = GameObject.Find("ChatLogText");
            if (go != null) chatLogText = go.GetComponent<TMP_Text>();
        }

        if (chatScrollRect == null)
        {
            GameObject go = GameObject.Find("ChatScrollView");
            if (go != null) chatScrollRect = go.GetComponent<ScrollRect>();
        }

        if (chatPanelRoot == null)
        {
            GameObject go = GameObject.Find("ChatPanel");
            if (go != null) chatPanelRoot = go;
        }

        if (chatCanvasGroup == null && chatPanelRoot != null)
        {
            chatCanvasGroup = chatPanelRoot.GetComponent<CanvasGroup>();
        }
    }

    public void ShowChat()
    {
        if (!enableChat) return;
        AutoFindUiIfMissing();
        if (chatPanelRoot != null)
        {
            chatPanelRoot.SetActive(true);
            if (chatCanvasGroup != null)
            {
                chatCanvasGroup.blocksRaycasts = true;
                chatCanvasGroup.interactable = true;
                chatCanvasGroup.alpha = 1f;
            }
        }
    }

    public void HideChat()
    {
        AutoFindUiIfMissing();
        if (chatPanelRoot != null)
        {
            if (chatCanvasGroup != null)
            {
                chatCanvasGroup.blocksRaycasts = false;
                chatCanvasGroup.interactable = false;
                chatCanvasGroup.alpha = 0f;
            }
            chatPanelRoot.SetActive(false);
        }
    }

    public void SendLocalPlayerMessage()
    {
        if (!enableChat) return;
        AutoFindUiIfMissing();
        if (chatInputField == null)
        {
            if (debugLogs) Debug.LogWarning("[CHAT] No input field assigned.");
            return;
        }

        string text = chatInputField.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        PlayerIdentity local = GetLocalPlayerStrictSafe();
        string senderId = local != null ? InfectionSystem.GetApiPlayerId(local) : "player_1";
        string senderName = local != null ? InfectionSystem.GetDisplayName(local) : "Player 1";

        AddMessage(senderId, senderName, text);
        chatInputField.text = string.Empty;

        AgentTracePanel.Trace("CHAT", $"{senderName}: {text}");

        RequestBotResponses(text);
    }

    PlayerIdentity GetLocalPlayerStrictSafe()
    {
        try
        {
            PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
            for (int i = 0; i < allPlayers.Length; i++)
            {
                var p = allPlayers[i];
                if (p != null && p.IsLocalPlayer) return p;
            }
            return FindPlayerByIdSafe(1);
        }
        catch { return null; }
    }

    PlayerIdentity FindPlayerByIdSafe(int id)
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p != null && p.playerId == id) return p;
        }
        return null;
    }

    public void AddMessage(string senderId, string senderName, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ChatMessageRuntime msg = new ChatMessageRuntime
        {
            senderId = senderId ?? string.Empty,
            senderName = senderName ?? string.Empty,
            text = text,
            time = Time.time
        };

        recentMessages.Add(msg);
        // trim
        if (recentMessages.Count > maxMessagesOnScreen)
        {
            int remove = recentMessages.Count - maxMessagesOnScreen;
            recentMessages.RemoveRange(0, remove);
        }

        RefreshChatLog();
    }

    void RefreshChatLog()
    {
        if (chatLogText == null) return;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int start = Math.Max(0, recentMessages.Count - maxMessagesOnScreen);
        for (int i = start; i < recentMessages.Count; i++)
        {
            var m = recentMessages[i];
            sb.AppendLine($"{m.senderName}: {m.text}");
        }
        chatLogText.text = sb.ToString();

        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    public ChatMessageDto[] BuildRecentChatDto()
    {
        int count = Math.Min(recentMessages.Count, maxRecentMessagesForApi);
        if (count == 0) return new ChatMessageDto[0];
        ChatMessageDto[] arr = new ChatMessageDto[count];
        int start = Math.Max(0, recentMessages.Count - count);
        for (int i = 0; i < count; i++)
        {
            var r = recentMessages[start + i];
            arr[i] = new ChatMessageDto { sender = r.senderId, senderName = r.senderName, text = r.text };
        }
        return arr;
    }

    public void RequestBotResponses(string playerMessage)
    {
        if (!enableChat) return;
        if (responseInProgress)
        {
            if (debugLogs) Debug.Log("[CHAT] Response already in progress; ignoring new request.");
            return;
        }

        StartCoroutine(RequestBotResponsesRoutine(playerMessage));
    }

    IEnumerator RequestBotResponsesRoutine(string playerMessage)
    {
        responseInProgress = true;

        // capture snapshot
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        List<PlayerIdentity> bots = new List<PlayerIdentity>();
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p == null) continue;
            if (!p.isAlive || p.isFrozen) continue;
            if (p.IsLocalPlayer) continue;
            if (p.isAIControlled || !p.IsLocalPlayer)
            {
                bots.Add(p);
            }
        }

        for (int bi = 0; bi < bots.Count; bi++)
        {
            var bot = bots[bi];

            float delay = UnityEngine.Random.Range(botResponseDelayMin, botResponseDelayMax);
            yield return new WaitForSeconds(delay);

            if (!enableChat) break;

            // API path
            if (useTeamARespondApi && TeamAApiClient.Instance != null && TeamAApiClient.Instance.EnableApiCalls)
            {
                RespondRequest req = new RespondRequest
                {
                    matchId = matchId,
                    phase = GameManager.CurrentPhase.ToString(),
                    wave = GameManager.CurrentWave,
                    cycle = 1,
                    botId = InfectionSystem.GetApiPlayerId(bot),
                    botName = InfectionSystem.GetDisplayName(bot),
                    personality = string.Empty,
                    message = playerMessage,
                    latestMessage = recentMessages.Count > 0 ? new ChatMessageDto { sender = recentMessages[recentMessages.Count - 1].senderId, senderName = recentMessages[recentMessages.Count - 1].senderName, text = recentMessages[recentMessages.Count - 1].text } : null,
                    recentChat = BuildRecentChatDto(),
                    alivePlayers = GetAlivePlayerApiIds(),
                    humanPlayers = GetHumanPlayerApiIds(),
                    infectedPlayers = GetInfectedPlayerApiIds()
                };

                bool callbackInvoked = false;
                bool ok = false;
                RespondResponse resp = null;

                StartCoroutine(TeamAApiClient.Instance.Respond(req, (s, r) => { callbackInvoked = true; ok = s; resp = r; }));

                float waited = 0f;
                while (!callbackInvoked && waited < apiResponseTimeoutSeconds)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!callbackInvoked)
                {
                    if (debugLogs) Debug.Log($"[CHAT] Team A /respond timeout for {InfectionSystem.GetDisplayName(bot)}. Falling back.");
                    AgentTracePanel.Trace("CHAT", $"{InfectionSystem.GetDisplayName(bot)} respond fallback.");
                    // fallback below
                }
                else if (ok && resp != null && resp.respond && resp.messages != null && resp.messages.Length > 0)
                {
                    // show messages with typing delays
                    if (resp.typingDelaySeconds > 0f)
                        yield return new WaitForSeconds(resp.typingDelaySeconds);

                    for (int m = 0; m < resp.messages.Length; m++)
                    {
                        AddMessage(InfectionSystem.GetApiPlayerId(bot), InfectionSystem.GetDisplayName(bot), resp.messages[m]);
                        AgentTracePanel.Trace("CHAT", $"{InfectionSystem.GetDisplayName(bot)}: {resp.messages[m]}");
                        if (m == 0 && resp.secondMessageDelaySeconds > 0f)
                        {
                            yield return new WaitForSeconds(resp.secondMessageDelaySeconds);
                        }
                    }

                    continue; // proceed to next bot
                }
                else
                {
                    // API responded but chosen not to respond; trace and possibly fallback
                    AgentTracePanel.Trace("CHAT", $"{InfectionSystem.GetDisplayName(bot)} respond fallback.");
                }
            }

            // local fallback
            if (!allowLocalFallbackBotResponses)
            {
                if (debugLogs) Debug.Log($"[CHAT] Local fallback responses disabled for {InfectionSystem.GetDisplayName(bot)}.");
                continue;
            }

            // small chance to respond
            float chance = UnityEngine.Random.value;
            if (chance <= 0.35f)
            {
                int idx = UnityEngine.Random.Range(0, localFallbackLines.Length);
                string text = localFallbackLines[idx];
                AddMessage(InfectionSystem.GetApiPlayerId(bot), InfectionSystem.GetDisplayName(bot), text);
                AgentTracePanel.Trace("CHAT", $"{InfectionSystem.GetDisplayName(bot)} fallback response.");
            }
        }

        responseInProgress = false;
    }

    string[] GetAlivePlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isAlive) ids.Add(InfectionSystem.GetApiPlayerId(p));
        }
        return ids.ToArray();
    }

    string[] GetHumanPlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isAlive && !p.isInfected) ids.Add(InfectionSystem.GetApiPlayerId(p));
        }
        return ids.ToArray();
    }

    string[] GetInfectedPlayerApiIds()
    {
        PlayerIdentity[] all = PlayerIdentity.GetAllPlayers();
        if (all == null) return new string[0];
        List<string> ids = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            PlayerIdentity p = all[i];
            if (p == null) continue;
            if (p.isInfected) ids.Add(InfectionSystem.GetApiPlayerId(p));
        }
        return ids.ToArray();
    }
}
