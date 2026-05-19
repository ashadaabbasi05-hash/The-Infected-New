using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Safe Team A API client for Unity 6.
/// 
/// IMPORTANT: 
/// - enableApiCalls is FALSE by default. API calls will not execute.
/// - All network calls are non-blocking coroutines.
/// - Backend failure is non-fatal. Local gameplay continues.
/// - No crashes on network error, timeout, or invalid JSON.
/// </summary>
public class TeamAApiClient : MonoBehaviour
{
    public static TeamAApiClient Instance { get; private set; }

    [SerializeField] private string baseUrl = "http://localhost:8000";
    [SerializeField] private float requestTimeoutSeconds = 8f;
    [SerializeField] private bool enableApiCalls = false;
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool traceToAgentPanel = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] Duplicate TeamAApiClient found. Disabling duplicate.");
            gameObject.SetActive(false);
            return;
        }

        Instance = this;

        // Trim trailing slash from baseUrl
        if (baseUrl.EndsWith("/"))
        {
            baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
        }

        if (debugLogs) Debug.Log("[TEAM A API] Client ready. enableApiCalls=" + enableApiCalls.ToString());
    }

    /// <summary>
    /// Check backend health. GET /health
    /// </summary>
    public IEnumerator Health(System.Action<bool, HealthResponse> callback)
    {
        if (IsApiDisabled(callback))
            yield break;

        yield return SendGet<HealthResponse>("/health", callback);
    }

    /// <summary>
    /// Register a bot. POST /register_bot
    /// </summary>
    public IEnumerator RegisterBot(RegisterBotRequest request, System.Action<bool, RegisterBotResponse> callback)
    {
        if (request == null)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] RegisterBot request is null. Aborting.");
            callback?.Invoke(false, null);
            yield break;
        }

        if (IsApiDisabled(callback))
            yield break;

        yield return SendPost("/register_bot", request, callback);
    }

    /// <summary>
    /// Unregister a bot. POST /unregister_bot
    /// </summary>
    public IEnumerator UnregisterBot(UnregisterBotRequest request, System.Action<bool, UnregisterBotResponse> callback)
    {
        if (request == null)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] UnregisterBot request is null. Aborting.");
            callback?.Invoke(false, null);
            yield break;
        }

        if (IsApiDisabled(callback))
            yield break;

        yield return SendPost("/unregister_bot", request, callback);
    }

    /// <summary>
    /// Get bot decision. POST /decide_action
    /// </summary>
    public IEnumerator DecideAction(DecideActionRequest request, System.Action<bool, DecideActionResponse> callback)
    {
        if (request == null)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] DecideAction request is null. Aborting.");
            callback?.Invoke(false, null);
            yield break;
        }

        if (IsApiDisabled(callback))
            yield break;

        yield return SendPost("/decide_action", request, callback);
    }

    /// <summary>
    /// Get bot response message. POST /respond
    /// </summary>
    public IEnumerator Respond(RespondRequest request, System.Action<bool, RespondResponse> callback)
    {
        if (request == null)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] Respond request is null. Aborting.");
            callback?.Invoke(false, null);
            yield break;
        }

        if (IsApiDisabled(callback))
            yield break;

        yield return SendPost("/respond", request, callback);
    }

    /// <summary>
    /// Get bot vote choice. POST /vote
    /// </summary>
    public IEnumerator Vote(VoteRequest request, System.Action<bool, VoteResponse> callback)
    {
        if (request == null)
        {
            if (debugLogs) Debug.LogWarning("[TEAM A API] Vote request is null. Aborting.");
            callback?.Invoke(false, null);
            yield break;
        }

        if (IsApiDisabled(callback))
            yield break;

        yield return SendPost("/vote", request, callback);
    }

    /// <summary>
    /// Build full URL from path
    /// </summary>
    private string BuildUrl(string path)
    {
        if (!path.StartsWith("/"))
            path = "/" + path;
        return baseUrl + path;
    }

    /// <summary>
    /// Check if API is disabled and invoke fallback if so.
    /// Returns true if disabled, false if enabled.
    /// </summary>
    private bool IsApiDisabled<T>(System.Action<bool, T> callback) where T : class
    {
        if (!enableApiCalls)
        {
            if (debugLogs) Debug.Log("[TEAM A API] API calls disabled. Using local fallback.");
            callback?.Invoke(false, null);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Generic GET request handler
    /// </summary>
    private IEnumerator SendGet<TResponse>(string path, System.Action<bool, TResponse> callback) where TResponse : class
    {
        string url = BuildUrl(path);

        if (debugLogs) Debug.Log("[TEAM A API] GET " + path);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (HandleWebRequestResult(request, path))
            {
                try
                {
                    string json = request.downloadHandler.text;
                    TResponse response = JsonUtility.FromJson<TResponse>(json);
                    if (debugLogs) Debug.Log("[TEAM A API] " + path + " success");
                    callback?.Invoke(true, response);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[TEAM A API] JSON parse error on " + path + ": " + ex.Message);
                    callback?.Invoke(false, null);
                }
            }
            else
            {
                callback?.Invoke(false, null);
            }
        }
    }

    /// <summary>
    /// Generic POST request handler
    /// </summary>
    private IEnumerator SendPost<TRequest, TResponse>(string path, TRequest request, System.Action<bool, TResponse> callback)
        where TResponse : class
    {
        string url = BuildUrl(path);
        string json = JsonUtility.ToJson(request);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        if (debugLogs) Debug.Log("[TEAM A API] POST " + path);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Accept", "application/json");
            webRequest.timeout = Mathf.CeilToInt(requestTimeoutSeconds);

            yield return webRequest.SendWebRequest();

            if (HandleWebRequestResult(webRequest, path))
            {
                try
                {
                    string responseJson = webRequest.downloadHandler.text;

                    if (string.IsNullOrEmpty(responseJson))
                    {
                        Debug.LogWarning("[TEAM A API] Empty response from " + path);
                        callback?.Invoke(false, null);
                        yield break;
                    }

                    TResponse response = JsonUtility.FromJson<TResponse>(responseJson);

                    // Trace if available and enabled
                    if (traceToAgentPanel && response is RegisterBotResponse regResponse && !string.IsNullOrEmpty(regResponse.trace))
                    {
                        AgentTracePanel.Trace("API", regResponse.trace);
                    }
                    else if (traceToAgentPanel && response is DecideActionResponse decResponse && !string.IsNullOrEmpty(decResponse.trace))
                    {
                        AgentTracePanel.Trace("API", decResponse.trace);
                    }
                    else if (traceToAgentPanel && response is RespondResponse respResponse && !string.IsNullOrEmpty(respResponse.trace))
                    {
                        AgentTracePanel.Trace("API", respResponse.trace);
                    }
                    else if (traceToAgentPanel && response is VoteResponse voteResponse && !string.IsNullOrEmpty(voteResponse.trace))
                    {
                        AgentTracePanel.Trace("API", voteResponse.trace);
                    }
                    else if (traceToAgentPanel && response is UnregisterBotResponse unregResponse && !string.IsNullOrEmpty(unregResponse.trace))
                    {
                        AgentTracePanel.Trace("API", unregResponse.trace);
                    }

                    if (debugLogs) Debug.Log("[TEAM A API] " + path + " success");
                    callback?.Invoke(true, response);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[TEAM A API] JSON parse error on " + path + ": " + ex.Message);
                    callback?.Invoke(false, null);
                }
            }
            else
            {
                callback?.Invoke(false, null);
            }
        }
    }

    /// <summary>
    /// Handle UnityWebRequest result. Returns true if successful, false if error.
    /// Logs warnings but never crashes.
    /// </summary>
    private bool HandleWebRequestResult(UnityWebRequest request, string path)
    {
        // Unity 6 uses request.result
        if (request.result == UnityWebRequest.Result.Success)
        {
            return true;
        }

        // Network error
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.LogWarning("[TEAM A API] Connection error on " + path + ": " + request.error);
            return false;
        }

        // Protocol error (HTTP error codes)
        if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogWarning("[TEAM A API] HTTP " + request.responseCode + " on " + path + ": " + request.error);
            return false;
        }

        // Timeout
        if (request.result == UnityWebRequest.Result.DataProcessingError)
        {
            Debug.LogWarning("[TEAM A API] Data processing error on " + path + ": " + request.error);
            return false;
        }

        Debug.LogWarning("[TEAM A API] Unknown error on " + path + ": " + request.error);
        return false;
    }

    /// <summary>
    /// Local fallback: Determine bot behavior by wave
    /// </summary>
    public static string LocalBehaviorForWave(int wave, bool isFinalChase)
    {
        if (isFinalChase) return "final_hunt";
        if (wave <= 1) return "stealth_fake_task";
        if (wave == 2) return "stalk";
        return "aggressive_chase";
    }

    /// <summary>
    /// Local fallback: Random vote target from humans
    /// </summary>
    public static string LocalRandomVoteTarget(string[] humanPlayers)
    {
        if (humanPlayers == null || humanPlayers.Length == 0) return "";
        int index = Random.Range(0, humanPlayers.Length);
        return humanPlayers[index];
    }

    /// <summary>
    /// Local fallback: Silent bot response
    /// </summary>
    public static RespondResponse LocalSilentRespond(string botId)
    {
        return new RespondResponse
        {
            botId = botId,
            respond = false,
            messages = new string[0],
            typingDelaySeconds = 0f,
            secondMessageDelaySeconds = 0f,
            trace = "Local fallback: bot stayed silent."
        };
    }
}
