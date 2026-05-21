using UnityEngine;

[DisallowMultipleComponent]
public sealed class MultiplayerClientConfig : MonoBehaviour
{
    [SerializeField] string localPlayerId = "player_1";
    [SerializeField] string displayName = "Player 1";
    [SerializeField] string roomCode = "ROOM123";
    [SerializeField] bool isHostClient = true;
    [SerializeField] bool debugLogs = true;

    public string LocalPlayerId => localPlayerId;
    public string DisplayName => displayName;
    public string RoomCode => roomCode;
    public bool IsHostClient => isHostClient;

    void Awake()
    {
        LoadFromPlayerPrefs();
    }

    void LoadFromPlayerPrefs()
    {
        if (PlayerPrefs.HasKey("V5_LocalPlayerId")) localPlayerId = PlayerPrefs.GetString("V5_LocalPlayerId", localPlayerId);
        if (PlayerPrefs.HasKey("V5_DisplayName")) displayName = PlayerPrefs.GetString("V5_DisplayName", displayName);
        if (PlayerPrefs.HasKey("V5_RoomCode")) roomCode = PlayerPrefs.GetString("V5_RoomCode", roomCode);
        if (PlayerPrefs.HasKey("V5_IsHostClient")) isHostClient = PlayerPrefs.GetInt("V5_IsHostClient", isHostClient ? 1 : 0) != 0;
    }

    public void UsePlayer1Host()
    {
        localPlayerId = "player_1";
        displayName = "Player 1";
        roomCode = "ROOM123";
        isHostClient = true;
        SaveToPlayerPrefs();
        LogApplied();
    }

    public void UsePlayer2Client()
    {
        localPlayerId = "player_2";
        displayName = "Player 2";
        roomCode = "ROOM123";
        isHostClient = false;
        SaveToPlayerPrefs();
        LogApplied();
    }

    public void ApplyToFirebaseAndFlow()
    {
        SaveToPlayerPrefs();
        FirebaseMultiplayerClient firebase = FirebaseMultiplayerClient.TryGetActiveClient();
        if (firebase != null)
        {
            firebase.SetLocalPlayerId(localPlayerId);
            firebase.SetDisplayName(displayName);
            firebase.SetRoomCode(roomCode);
            firebase.SetHost(isHostClient);
        }

        GameFlowManager flow = GameFlowManager.Instance != null ? GameFlowManager.Instance : FindAnyObjectByType<GameFlowManager>();
        if (flow != null)
        {
            flow.defaultPlayerId = localPlayerId;
            flow.defaultPlayerName = displayName;
            flow.defaultRoomCode = roomCode;
        }

        LogApplied();
    }

    void SaveToPlayerPrefs()
    {
        PlayerPrefs.SetString("V5_LocalPlayerId", localPlayerId);
        PlayerPrefs.SetString("V5_DisplayName", displayName);
        PlayerPrefs.SetString("V5_RoomCode", roomCode);
        PlayerPrefs.SetInt("V5_IsHostClient", isHostClient ? 1 : 0);
        PlayerPrefs.Save();
    }

    void LogApplied()
    {
        if (!debugLogs)
        {
            return;
        }

        Debug.Log($"[CLIENT CONFIG] Applied localPlayerId={localPlayerId} displayName={displayName} room={roomCode} host={isHostClient}");
    }

#if UNITY_EDITOR
    [ContextMenu("Use Player 1 Host")]
    public void ContextUsePlayer1Host()
    {
        UsePlayer1Host();
    }

    [ContextMenu("Use Player 2 Client")]
    public void ContextUsePlayer2Client()
    {
        UsePlayer2Client();
    }

    [ContextMenu("Apply Config")]
    public void ContextApplyConfig()
    {
        ApplyToFirebaseAndFlow();
    }
#else
    [ContextMenu("Use Player 1 Host")]
    public void ContextUsePlayer1Host() => UsePlayer1Host();
    [ContextMenu("Use Player 2 Client")]
    public void ContextUsePlayer2Client() => UsePlayer2Client();
    [ContextMenu("Apply Config")]
    public void ContextApplyConfig() => ApplyToFirebaseAndFlow();
#endif
}
