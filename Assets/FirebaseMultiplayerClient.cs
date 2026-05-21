using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

#if FIREBASE_ENABLED
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
#endif

[DisallowMultipleComponent]
public sealed class FirebaseMultiplayerClient : MonoBehaviour
{
    public static FirebaseMultiplayerClient Instance { get; private set; }

    public event Action<List<LobbyPlayerData>> OnLobbyPlayersChanged;
    public event Action<string> OnRoomJoined;
    public event Action<string> OnRoomLeft;
    public event Action<string> OnMatchStartedFromFirebase;

    [SerializeField] bool enableFirebase = false;
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool autoJoinOnStart = false;
    [SerializeField] string roomCode = "ROOM123";
    [SerializeField] string localPlayerId = "player_1";
    [SerializeField] string displayName = "Player 1";
    [SerializeField] string databaseUrl = "https://the-infected-hackathon-default-rtdb.asia-southeast1.firebasedatabase.app";
    [SerializeField, Min(0.01f)] float positionSyncInterval = 0.15f;
    [SerializeField] Transform localPlayerTransform;
    [SerializeField] PlayerIdentity localPlayerIdentity;
    [SerializeField] GameObject remotePlayerPrefab;
    [SerializeField] Transform remotePlayersParent;

    public bool IsHost { get; private set; }
    public bool FirebaseEnabled => enableFirebase;
    public string CurrentRoomCode => roomCode;
    public string LocalPlayerId => localPlayerId;
    public string DisplayName => displayName;
    public readonly Dictionary<string, RemotePlayerView> remotePlayers = new Dictionary<string, RemotePlayerView>();

    public bool IsOnline
    {
        get
        {
#if FIREBASE_ENABLED
            return enableFirebase && initialized;
#else
            return false;
#endif
        }
    }

    // Pending room request state (queued if Create/Join called before Firebase initialized)
    bool pendingCreateRoom;
    bool pendingJoinRoom;
    string pendingRoomCode;
    string pendingPlayerId;
    bool localReady;
    bool matchStartEventRaised;
    bool matchHasStartedLocally;

    public bool IsFirebaseReady
    {
        get
        {
#if FIREBASE_ENABLED
            return enableFirebase && initialized;
#else
            return false;
#endif
        }
    }

    public bool IsJoined => joined;

    bool initialized;
    bool joined;
    float nextPositionSyncTime;
    bool announcedLocalFallback;
    bool cachedFinalHuntActive;
    string cachedWinner = string.Empty;
    readonly Queue<Action> mainThreadActions = new Queue<Action>();

#if FIREBASE_ENABLED
    FirebaseApp app;
    FirebaseDatabase database;
    DatabaseReference rootRef;
    DatabaseReference roomRootRef;
    DatabaseReference metaRef;
    DatabaseReference playersRef;
    DatabaseReference stateRef;
    bool listenersAttached;
#endif

    [Serializable]
    sealed class FirebaseRoomMeta
    {
        public string roomCode;
        public string hostPlayerId;
        public bool open = true;
        public long createdAt;
        public long updatedAt;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheLocalReferences();

        if (!enableFirebase)
        {
            LogLocalFallbackMode();
        }

#if FIREBASE_ENABLED
        if (enableFirebase)
        {
            TryInitializeFirebaseBindings();
        }
#endif
    }

    void Start()
    {
        Debug.Log("[FIREBASE] FirebaseMultiplayerClient started. enableFirebase=" + enableFirebase, this);

        if (!enableFirebase)
        {
            Debug.Log("[FIREBASE] Local fallback mode active.", this);
            return;
        }

#if FIREBASE_ENABLED
        Debug.Log("[FIREBASE] FIREBASE_ENABLED active. Initializing Firebase...", this);

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[FIREBASE] Dependency check failed: " + task.Exception, this);
                initialized = false;
                return;
            }

            DependencyStatus dependencyStatus = task.Result;
            Debug.Log("[FIREBASE] Dependency status: " + dependencyStatus, this);

            if (dependencyStatus != DependencyStatus.Available)
            {
                Debug.LogError("[FIREBASE] Could not resolve Firebase dependencies: " + dependencyStatus, this);
                initialized = false;
                return;
            }

            app = FirebaseApp.DefaultInstance;

            if (!string.IsNullOrWhiteSpace(databaseUrl))
            {
                database = FirebaseDatabase.GetInstance(app, databaseUrl);
            }
            else
            {
                database = FirebaseDatabase.DefaultInstance;
            }

            rootRef = database.RootReference;
            initialized = true;

            Debug.Log("[FIREBASE] Firebase ready. databaseUrl=" + databaseUrl, this);
            // Process any pending Create/Join requests queued before Firebase was ready.
            ProcessPendingRoomRequest();

            if (autoJoinOnStart)
            {
                JoinRoom(roomCode, localPlayerId);
            }
        });
#else
        Debug.LogWarning("[FIREBASE] FIREBASE_ENABLED define missing or Firebase SDK not active.", this);
#endif
    }

    void OnEnable()
    {
        CacheLocalReferences();
    }

    void Update()
    {
        DrainMainThreadActions();

        if (!enableFirebase || !initialized || !joined)
        {
            return;
        }

        // Only publish position when match is active or in lobby
        bool shouldPublish = false;
        if (GameFlowManager.Instance != null)
        {
            shouldPublish = GameFlowManager.Instance.IsMatchActive || GameFlowManager.Instance.CurrentState == AppFlowState.Lobby;
        }
        else
        {
            shouldPublish = true; // fallback if GameFlowManager not present
        }

        if (!shouldPublish)
        {
            return;
        }

        if (Time.unscaledTime < nextPositionSyncTime)
        {
            return;
        }

        nextPositionSyncTime = Time.unscaledTime + positionSyncInterval;
        PublishLocalPlayerState();
    }

    void OnDestroy()
    {
        LeaveRoom();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static FirebaseMultiplayerClient TryGetActiveClient()
    {
        if (Instance != null)
        {
            return Instance;
        }

        return FindAnyObjectByType<FirebaseMultiplayerClient>();
    }

    public void CreateRoom(string code)
    {
        roomCode = NormalizeRoomCode(code);
        localPlayerId = NormalizeFirebasePlayerId(localPlayerId);
        displayName = NormalizeDisplayName(displayName, localPlayerId);
        localReady = false;
        matchStartEventRaised = false;
        matchHasStartedLocally = false;

        CacheLocalReferences();

        if (!enableFirebase)
        {
            LogFallback();
            return;
        }

        // If Firebase hasn't finished initializing, queue the create request.
        if (!initialized)
        {
            pendingCreateRoom = true;
            pendingJoinRoom = false;
            pendingRoomCode = roomCode;
            pendingPlayerId = NormalizeFirebasePlayerId(localPlayerId);
            Debug.Log($"[FIREBASE] CreateRoom queued until Firebase ready. room={pendingRoomCode} player={pendingPlayerId}", this);
            return;
        }

        if (!TryEnableFirebaseSession())
        {
            LogFallback();
            return;
        }

#if FIREBASE_ENABLED
        IsHost = true;
        joined = true;
        cachedWinner = string.Empty;
        cachedFinalHuntActive = false;

        SetupFirebaseRoomReferences();
        WriteRoomMeta(true);
        WriteLobbyState();
        AttachFirebaseListeners();
        PublishLocalPlayerState();
    EmitLocalLobbyPlayersChanged();
        OnRoomJoined?.Invoke(roomCode);
        Debug.Log($"[FIREBASE] Joined room {roomCode} as {localPlayerId}.", this);
        Debug.Log($"[FIREBASE] Room created {roomCode} host={localPlayerId}", this);
#endif
    }

    public void JoinRoom(string code, string playerId)
    {
        roomCode = NormalizeRoomCode(code);
        localPlayerId = NormalizeFirebasePlayerId(playerId);
        displayName = NormalizeDisplayName(displayName, localPlayerId);
        localReady = false;
        matchStartEventRaised = false;
        matchHasStartedLocally = false;

        CacheLocalReferences();

        if (!enableFirebase)
        {
            LogFallback();
            return;
        }

        // If Firebase hasn't finished initializing, queue the join request.
        if (!initialized)
        {
            pendingJoinRoom = true;
            pendingCreateRoom = false;
            pendingRoomCode = roomCode;
            pendingPlayerId = NormalizeFirebasePlayerId(playerId);
            Debug.Log($"[FIREBASE] JoinRoom queued until Firebase ready. room={pendingRoomCode} player={pendingPlayerId}", this);
            return;
        }

        if (!TryEnableFirebaseSession())
        {
            LogFallback();
            return;
        }

#if FIREBASE_ENABLED
        if (rootRef == null)
        {
            return;
        }

        SetupFirebaseRoomReferences();

        if (stateRef == null)
        {
            FinalizeJoinRoom(false);
            return;
        }

        stateRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            bool roomAlreadyStarted = false;

            if (!task.IsFaulted && !task.IsCanceled && task.Result != null && task.Result.Exists)
            {
                FirebaseMatchState existingState = DeserializeMatchState(task.Result);
                roomAlreadyStarted = IsMatchStartedState(existingState);
            }

            FinalizeJoinRoom(roomAlreadyStarted);
        });
#endif
    }

    public void LeaveRoom()
    {
        string previousRoomCode = roomCode;
        initialized = false;
        joined = false;
        IsHost = false;
        localReady = false;
        matchStartEventRaised = false;
        matchHasStartedLocally = false;

        ClearRemotePlayers();

        if (!string.IsNullOrWhiteSpace(previousRoomCode))
        {
            OnRoomLeft?.Invoke(previousRoomCode);
        }

#if FIREBASE_ENABLED
        DetachFirebaseListeners();
        roomRootRef = null;
        metaRef = null;
        playersRef = null;
        stateRef = null;
        database = null;
#endif
    }

    public void SetHost(bool host)
    {
        IsHost = host;

#if FIREBASE_ENABLED
        if (IsOnline)
        {
            WriteRoomMeta(host);
        }
#endif
    }

    public void PublishLocalPlayerState()
    {
        if (!IsOnline)
        {
            LogFallback();
            return;
        }

        CacheLocalReferences();

        if (!TryBuildPlayerState(out FirebasePlayerState playerState))
        {
            return;
        }

#if FIREBASE_ENABLED
        if (playersRef == null)
        {
            return;
        }

        string playerKey = NormalizeFirebasePlayerId(playerState.playerId);
        DatabaseReference playerRef = playersRef.Child(playerKey);
        playerRef.Child("playerId").SetValueAsync(playerState.playerId);
        playerRef.Child("displayName").SetValueAsync(playerState.displayName);
        playerRef.Child("x").SetValueAsync(playerState.x);
        playerRef.Child("y").SetValueAsync(playerState.y);
        playerRef.Child("alive").SetValueAsync(playerState.alive);
        playerRef.Child("infected").SetValueAsync(playerState.infected);
        playerRef.Child("aiControlled").SetValueAsync(playerState.aiControlled);
        playerRef.Child("frozen").SetValueAsync(playerState.frozen);

        WriteLocalLobbyPlayerData();

        Debug.Log("[FIREBASE] Published local player state.", this);
#endif
    }

    public void PublishMatchState()
    {
        if (!IsOnline)
        {
            LogFallback();
            return;
        }

        if (!TryBuildMatchState(out FirebaseMatchState matchState))
        {
            return;
        }

#if FIREBASE_ENABLED
        WriteMatchState(matchState);
#endif
    }

    public void PublishTaskState()
    {
        PublishMatchState();
    }

    public void PublishFinalHuntState(bool active)
    {
        cachedFinalHuntActive = active;
        PublishMatchState();
    }

    public void PublishWinner(string winner)
    {
        cachedWinner = winner ?? string.Empty;
        PublishMatchState();
    }

    public void SetFirebaseEnabled(bool enabled)
    {
        if (enableFirebase == enabled)
        {
            if (!enabled)
            {
                LogLocalFallbackMode();
            }

            return;
        }

        enableFirebase = enabled;

        if (!enableFirebase)
        {
            LeaveRoom();
            LogLocalFallbackMode();
            return;
        }

        CacheLocalReferences();

#if FIREBASE_ENABLED
        if (initialized)
        {
            SetupFirebaseRoomReferences();
            AttachFirebaseListeners();
            WriteRoomMeta(IsHost);
            PublishLocalPlayerState();
            WriteLobbyState();
            return;
        }
#endif

        if (debugLogs)
        {
            Debug.Log("[FIREBASE] Firebase enabled, but room is not initialized yet.", this);
        }
    }

    public void SetLocalReady(bool ready)
    {
        localReady = ready;

        if (!enableFirebase)
        {
            if (debugLogs)
            {
                Debug.Log($"[FIREBASE] Ready state updated: {ready}", this);
            }

            return;
        }

        if (!IsOnline)
        {
            if (debugLogs)
            {
                Debug.Log($"[FIREBASE] Ready state updated: {ready}", this);
            }

            return;
        }

        WriteLobbyPlayerField("isReady", ready);
        EmitLocalLobbyPlayersChanged();
        if (debugLogs)
        {
            Debug.Log($"[FIREBASE] Ready state updated: {ready}", this);
        }
    }

    public void StartMatch()
    {
        if (!enableFirebase)
        {
            Debug.Log("[FIREBASE] Firebase disabled. StartMatch ignored.", this);
            return;
        }

        Debug.Log($"[FIREBASE] StartMatch requested. joined={IsJoined} isHost={IsHost} room={roomCode} localPlayerId={localPlayerId}", this);

        if (!IsJoined)
        {
            Debug.LogWarning("[FIREBASE] StartMatch blocked: not joined.", this);
            return;
        }

        if (!IsHost)
        {
            Debug.Log("[FIREBASE] Non-host attempted StartMatch.", this);
            return;
        }

        if (!IsOnline)
        {
            Debug.Log("[FIREBASE] Firebase not ready. StartMatch ignored.", this);
            return;
        }

        matchHasStartedLocally = true;

#if FIREBASE_ENABLED
        if (stateRef == null || metaRef == null)
        {
            TryInitializeFirebaseBindings();
        }

        List<System.Threading.Tasks.Task> writeTasks = new List<System.Threading.Tasks.Task>();

        if (stateRef != null)
        {
            Dictionary<string, object> stateUpdate = new Dictionary<string, object>
            {
                { "gameStarted", true },
                { "phase", "ExplorationA" },
                { "status", "in_game" },
                { "phaseStartedAt", Firebase.Database.ServerValue.Timestamp }
            };

            writeTasks.Add(stateRef.UpdateChildrenAsync(stateUpdate));
        }

        if (metaRef != null)
        {
            writeTasks.Add(metaRef.Child("status").SetValueAsync("in_game"));
        }

        if (writeTasks.Count == 0)
        {
            Debug.LogWarning("[FIREBASE] StartMatch write skipped: no Firebase references available.", this);
            return;
        }

        System.Threading.Tasks.Task.WhenAll(writeTasks).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[FIREBASE] Match start write failed: " + (task.Exception != null ? task.Exception.ToString() : "canceled"), this);
                return;
            }

            Debug.Log($"[FIREBASE] Match start written for {roomCode}.", this);
        });
#else
        Debug.Log($"[FIREBASE] Match start written for {roomCode}.", this);
#endif
    }

    void CacheLocalReferences()
    {
        if (localPlayerIdentity == null)
        {
            PlayerIdentity[] players = PlayerIdentity.GetAllPlayers();
            if (players != null)
            {
                for (int i = 0; i < players.Length; i++)
                {
                    PlayerIdentity candidate = players[i];
                    if (candidate != null && candidate.IsLocalPlayer)
                    {
                        localPlayerIdentity = candidate;
                        break;
                    }
                }
            }
        }

        if (localPlayerTransform == null && localPlayerIdentity != null)
        {
            localPlayerTransform = localPlayerIdentity.transform;
        }
    }

    bool TryEnableFirebaseSession()
    {
#if FIREBASE_ENABLED
        if (!enableFirebase)
        {
            return false;
        }

        return initialized && enableFirebase;
#else
        return false;
#endif
    }

#if FIREBASE_ENABLED
    void TryInitializeFirebaseBindings()
    {
        SetupFirebaseRoomReferences();
        AttachFirebaseListeners();
    }

    void SetupFirebaseRoomReferences()
    {
        if (!enableFirebase || !initialized)
        {
            return;
        }

        if (rootRef == null)
        {
            return;
        }

        roomRootRef = rootRef.Child("matches").Child(roomCode);
        metaRef = roomRootRef.Child("meta");
        playersRef = roomRootRef.Child("players");
        stateRef = roomRootRef.Child("state");
    }

    void AttachFirebaseListeners()
    {
        if (!enableFirebase || !initialized || playersRef == null || stateRef == null)
        {
            return;
        }

        if (!listenersAttached)
        {
            playersRef.ValueChanged += HandlePlayersValueChanged;
            stateRef.ValueChanged += HandleMatchStateValueChanged;
            listenersAttached = true;
        }
    }

    void ProcessPendingRoomRequest()
    {
        if (!enableFirebase || !initialized)
        {
            return;
        }

        if (pendingCreateRoom)
        {
            string code = pendingRoomCode;
            string player = pendingPlayerId;
            pendingCreateRoom = false;
            pendingJoinRoom = false;
            if (!string.IsNullOrWhiteSpace(player))
            {
                localPlayerId = player;
            }
            Debug.Log("[FIREBASE] Processing queued CreateRoom. room=" + code + " player=" + player, this);
            CreateRoom(code);
            return;
        }

        if (pendingJoinRoom)
        {
            string code = pendingRoomCode;
            string player = pendingPlayerId;
            pendingCreateRoom = false;
            pendingJoinRoom = false;
            if (!string.IsNullOrWhiteSpace(player))
            {
                localPlayerId = player;
            }
            Debug.Log("[FIREBASE] Processing queued JoinRoom. room=" + code + " player=" + player, this);
            JoinRoom(code, player);
            return;
        }
    }

    void DetachFirebaseListeners()
    {
        if (!listenersAttached)
        {
            return;
        }

        if (playersRef != null)
        {
            playersRef.ValueChanged -= HandlePlayersValueChanged;
        }

        if (stateRef != null)
        {
            stateRef.ValueChanged -= HandleMatchStateValueChanged;
        }

        listenersAttached = false;
    }

    void HandlePlayersValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (!enableFirebase || !initialized || args == null || args.DatabaseError != null || args.Snapshot == null)
        {
            return;
        }

        List<FirebasePlayerState> states = new List<FirebasePlayerState>();
        List<LobbyPlayerData> lobbyPlayers = new List<LobbyPlayerData>();
        foreach (DataSnapshot child in args.Snapshot.Children)
        {
            FirebasePlayerState state = DeserializePlayerState(child);
            if (state != null)
            {
                states.Add(state);
            }

            LobbyPlayerData lobbyPlayer = DeserializeLobbyPlayerData(child);
            if (lobbyPlayer != null)
            {
                lobbyPlayers.Add(lobbyPlayer);
            }
        }

        EnqueueMainThreadAction(() => ApplyRemotePlayerStates(states));
        EnqueueMainThreadAction(() =>
        {
            OnLobbyPlayersChanged?.Invoke(lobbyPlayers);
            if (debugLogs)
            {
                Debug.Log($"[FIREBASE] Lobby players changed count={lobbyPlayers.Count}", this);
            }
        });
    }

    void HandleMatchStateValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (!enableFirebase || !initialized || args == null || args.DatabaseError != null || args.Snapshot == null)
        {
            return;
        }

        FirebaseMatchState state = DeserializeMatchState(args.Snapshot);
        if (state == null)
        {
            return;
        }

        EnqueueMainThreadAction(() => ApplyRemoteMatchState(state));
        EnqueueMainThreadAction(() =>
        {
            if (ShouldAnnounceMatchStart(state))
            {
                Debug.Log($"[FIREBASE] Match start detected from Firebase. phase={state.phase} gameStarted={state.gameStarted} status={state.status}", this);
                OnMatchStartedFromFirebase?.Invoke(state.phase);
            }
        });
    }

    void FinalizeJoinRoom(bool roomAlreadyStarted)
    {
        joined = true;
        IsHost = false;

        if (!roomAlreadyStarted)
        {
            WriteRoomMeta(false);
            WriteLobbyState();
        }

        AttachFirebaseListeners();
        PublishLocalPlayerState();
        EmitLocalLobbyPlayersChanged();
        OnRoomJoined?.Invoke(roomCode);

        if (roomAlreadyStarted)
        {
            CheckCurrentMatchStateOnce();
        }

        Debug.Log($"[FIREBASE] Joined room {roomCode} as {localPlayerId}.", this);
    }

    static bool IsMatchStartedState(FirebaseMatchState state)
    {
        if (state == null)
        {
            return false;
        }

        return state.gameStarted
            || string.Equals(state.status, "in_game", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.phase, "ExplorationA", StringComparison.OrdinalIgnoreCase);
    }

    void CheckCurrentMatchStateOnce()
    {
        if (!enableFirebase || !initialized || stateRef == null)
        {
            return;
        }

#if FIREBASE_ENABLED
        stateRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                if (debugLogs)
                {
                    Debug.LogWarning($"[FIREBASE] Existing match state snapshot failed: {task.Exception}", this);
                }
                return;
            }

            DataSnapshot snapshot = task.Result;
            if (snapshot == null || !snapshot.Exists)
            {
                return;
            }

            FirebaseMatchState state = DeserializeMatchState(snapshot);
            if (state == null)
            {
                return;
            }

            if (IsMatchStartedState(state))
            {
                Debug.Log($"[FIREBASE] Existing match already started. Entering game. phase={state.phase}", this);

                if (!matchStartEventRaised)
                {
                    matchStartEventRaised = true;
                    OnMatchStartedFromFirebase?.Invoke(state.phase);
                }
            }
        });
#endif
    }

    FirebasePlayerState DeserializePlayerState(DataSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        string json = snapshot.GetRawJsonValue();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        FirebasePlayerState state = JsonUtility.FromJson<FirebasePlayerState>(json);
        if (state == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(state.playerId))
        {
            state.playerId = snapshot.Key;
        }

        return state;
    }

    FirebaseMatchState DeserializeMatchState(DataSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        string json = snapshot.GetRawJsonValue();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonUtility.FromJson<FirebaseMatchState>(json);
    }

    LobbyPlayerData DeserializeLobbyPlayerData(DataSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        LobbyPlayerData player = new LobbyPlayerData();
        player.playerId = GetSnapshotString(snapshot, "playerId", snapshot.Key);
        player.displayName = GetSnapshotString(snapshot, "displayName", player.playerId);
        player.isHost = GetSnapshotBool(snapshot, "isHost", false);
        player.isReady = GetSnapshotBool(snapshot, "isReady", false);
        player.isAlive = GetSnapshotBool(snapshot, "isAlive", true);
        player.isBot = GetSnapshotBool(snapshot, "isBot", false);
        player.x = GetSnapshotFloat(snapshot, "x", 0f);
        player.y = GetSnapshotFloat(snapshot, "y", 0f);
        player.z = GetSnapshotFloat(snapshot, "z", 0f);
        player.lastSeenAt = GetSnapshotLong(snapshot, "lastSeenAt", 0L);
        return player;
    }

    static string GetSnapshotString(DataSnapshot snapshot, string childName, string fallback)
    {
        if (snapshot == null)
        {
            return fallback;
        }

        DataSnapshot child = snapshot.Child(childName);
        if (child != null && child.Value != null)
        {
            return child.Value.ToString();
        }

        return fallback;
    }

    static bool GetSnapshotBool(DataSnapshot snapshot, string childName, bool fallback)
    {
        if (snapshot == null)
        {
            return fallback;
        }

        DataSnapshot child = snapshot.Child(childName);
        if (child != null && child.Value != null)
        {
            if (child.Value is bool boolValue)
            {
                return boolValue;
            }

            if (bool.TryParse(child.Value.ToString(), out bool parsedBool))
            {
                return parsedBool;
            }
        }

        return fallback;
    }

    static long GetSnapshotLong(DataSnapshot snapshot, string childName, long fallback)
    {
        if (snapshot == null)
        {
            return fallback;
        }

        DataSnapshot child = snapshot.Child(childName);
        if (child != null && child.Value != null)
        {
            if (child.Value is long longValue)
            {
                return longValue;
            }

            if (long.TryParse(child.Value.ToString(), out long parsedLong))
            {
                return parsedLong;
            }
        }

        return fallback;
    }

    static float GetSnapshotFloat(DataSnapshot snapshot, string childName, float fallback)
    {
        if (snapshot == null)
        {
            return fallback;
        }

        DataSnapshot child = snapshot.Child(childName);
        if (child != null && child.Value != null)
        {
            if (child.Value is float floatValue)
            {
                return floatValue;
            }

            if (child.Value is double doubleValue)
            {
                return (float)doubleValue;
            }

            if (float.TryParse(child.Value.ToString(), out float parsedFloat))
            {
                return parsedFloat;
            }
        }

        return fallback;
    }

    bool ShouldAnnounceMatchStart(FirebaseMatchState state)
    {
        if (state == null)
        {
            return false;
        }

        bool started = state.gameStarted
            || string.Equals(state.status, "in_game", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.phase, "ExplorationA", StringComparison.OrdinalIgnoreCase);
        if (!started)
        {
            return false;
        }

        if (matchStartEventRaised)
        {
            return false;
        }

        matchStartEventRaised = true;
        return true;
    }

    void WriteRoomMeta(bool host)
    {
        if (metaRef == null)
        {
            return;
        }

        long timestamp = GetTimestampUtc();
        metaRef.Child("roomCode").SetValueAsync(roomCode);
        metaRef.Child("hostPlayerId").SetValueAsync(localPlayerId);
        metaRef.Child("status").SetValueAsync(host ? "lobby" : "lobby");
        metaRef.Child("maxPlayers").SetValueAsync(4);
        metaRef.Child("contractVersion").SetValueAsync("v5");
        metaRef.Child("open").SetValueAsync(true);
        metaRef.Child("createdAt").SetValueAsync(timestamp);
        metaRef.Child("updatedAt").SetValueAsync(timestamp);
    }

    void WriteLobbyState()
    {
        if (stateRef == null)
        {
            return;
        }

        stateRef.Child("gameStarted").SetValueAsync(false);
        stateRef.Child("phase").SetValueAsync("Lobby");
        stateRef.Child("status").SetValueAsync("lobby");
        stateRef.Child("phaseStartedAt").SetValueAsync(GetTimestampUtc());
    }

    void WriteLocalLobbyPlayerData()
    {
        if (!IsOnline || playersRef == null)
        {
            return;
        }

        string playerKey = NormalizeFirebasePlayerId(localPlayerId);
        if (string.IsNullOrWhiteSpace(playerKey))
        {
            playerKey = "player_1";
        }

        string displayName = GetDisplayName(localPlayerIdentity);
        if (!string.IsNullOrWhiteSpace(this.displayName))
        {
            displayName = this.displayName;
        }
        object lastSeenAtValue = GetTimestampUtc();

        WriteLobbyPlayerField("playerId", playerKey);
        WriteLobbyPlayerField("displayName", displayName);
        WriteLobbyPlayerField("isHost", IsHost);
        WriteLobbyPlayerField("isReady", localReady);
        WriteLobbyPlayerField("isAlive", localPlayerIdentity == null || localPlayerIdentity.isAlive);
        WriteLobbyPlayerField("isBot", localPlayerIdentity != null && localPlayerIdentity.isAIControlled);
        WriteLobbyPlayerField("x", localPlayerTransform != null ? localPlayerTransform.position.x : 0f);
        WriteLobbyPlayerField("y", localPlayerTransform != null ? localPlayerTransform.position.y : 0f);
        WriteLobbyPlayerField("z", localPlayerTransform != null ? localPlayerTransform.position.z : 0f);
        WriteLobbyPlayerField("lastSeenAt", lastSeenAtValue);
    }

    void WriteLobbyPlayerField(string fieldName, object value)
    {
        if (playersRef == null)
        {
            return;
        }

        string playerKey = NormalizeFirebasePlayerId(localPlayerId);
        if (string.IsNullOrWhiteSpace(playerKey))
        {
            playerKey = "player_1";
        }

#if FIREBASE_ENABLED
        playersRef.Child(playerKey).Child(fieldName).SetValueAsync(value);
#endif
    }

    void EmitLocalLobbyPlayersChanged()
    {
        List<LobbyPlayerData> lobbyPlayers = new List<LobbyPlayerData>
        {
            new LobbyPlayerData
            {
                playerId = NormalizeFirebasePlayerId(localPlayerId),
                displayName = GetLocalDisplayName(),
                isHost = IsHost,
                isReady = localReady,
                isAlive = localPlayerIdentity == null || localPlayerIdentity.isAlive,
                isBot = localPlayerIdentity != null && localPlayerIdentity.isAIControlled,
                x = localPlayerTransform != null ? localPlayerTransform.position.x : 0f,
                y = localPlayerTransform != null ? localPlayerTransform.position.y : 0f,
                z = localPlayerTransform != null ? localPlayerTransform.position.z : 0f,
                lastSeenAt = GetTimestampUtc()
            }
        };

        OnLobbyPlayersChanged?.Invoke(lobbyPlayers);
        if (debugLogs)
        {
            Debug.Log($"[FIREBASE] Lobby players changed count={lobbyPlayers.Count}", this);
        }
    }

    void WriteMatchState(FirebaseMatchState matchState)
    {
        if (stateRef == null)
        {
            return;
        }

        stateRef.SetRawJsonValueAsync(JsonUtility.ToJson(matchState));
    }
#endif

    bool TryBuildPlayerState(out FirebasePlayerState playerState)
    {
        playerState = null;

        if (localPlayerIdentity == null && localPlayerTransform == null)
        {
            CacheLocalReferences();
        }

        localPlayerId = NormalizeFirebasePlayerId(localPlayerId);

        Vector3 worldPosition = localPlayerTransform != null ? localPlayerTransform.position : Vector3.zero;
        PlayerIdentity identity = localPlayerIdentity;

        playerState = new FirebasePlayerState
        {
            playerId = localPlayerId,
            displayName = GetLocalDisplayName(),
            x = worldPosition.x,
            y = worldPosition.y,
            alive = identity == null || identity.isAlive,
            infected = identity != null && identity.isInfected,
            aiControlled = identity != null && identity.isAIControlled,
            frozen = identity != null && identity.isFrozen,
            updatedAt = GetTimestampUtc()
        };

        return true;
    }

    bool TryBuildMatchState(out FirebaseMatchState matchState)
    {
        matchState = new FirebaseMatchState
        {
            gameStarted = matchHasStartedLocally,
            phase = GetPublishedPhaseName(),
            status = matchHasStartedLocally ? "in_game" : "lobby",
            wave = GameManager.CurrentWave,
            taskProgress = TaskManager.Instance != null ? TaskManager.Instance.taskProgress : 0f,
            totalTasks = TaskManager.Instance != null ? TaskManager.Instance.TotalTasks : 0,
            finalHuntActive = cachedFinalHuntActive,
            winner = cachedWinner,
            updatedAt = GetTimestampUtc()
        };

        FinalHuntManager finalHuntManager = FindAnyObjectByType<FinalHuntManager>();
        if (finalHuntManager != null)
        {
            matchState.finalHuntActive = finalHuntManager.IsFinalHuntActive;
        }

        return true;
    }

    string GetPublishedPhaseName()
    {
        if (!matchHasStartedLocally)
        {
            return "Lobby";
        }

        string phaseName = GameManager.CurrentPhase.ToString();
        if (string.Equals(phaseName, "Exploration", StringComparison.OrdinalIgnoreCase))
        {
            return "ExplorationA";
        }

        return phaseName;
    }

    void ApplyRemoteMatchState(FirebaseMatchState state)
    {
        if (state == null)
        {
            return;
        }

        cachedFinalHuntActive = state.finalHuntActive;
        cachedWinner = state.winner ?? string.Empty;

        if (debugLogs)
        {
            Debug.Log($"[FIREBASE] Match state synced: phase={state.phase} wave={state.wave} tasks={state.taskProgress}/{state.totalTasks} finalHunt={state.finalHuntActive} winner={state.winner}", this);
        }
    }

    void ApplyRemotePlayerStates(List<FirebasePlayerState> states)
    {
        if (states == null)
        {
            return;
        }

        HashSet<string> seenPlayerIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < states.Count; i++)
        {
            FirebasePlayerState state = states[i];
            if (state == null || string.IsNullOrWhiteSpace(state.playerId))
            {
                continue;
            }

            if (string.Equals(state.playerId, localPlayerId, StringComparison.Ordinal))
            {
                continue;
            }

            seenPlayerIds.Add(state.playerId);

            if (!remotePlayers.TryGetValue(state.playerId, out RemotePlayerView remoteView) || remoteView == null)
            {
                remoteView = SpawnRemotePlayer(state.playerId);
                if (remoteView == null)
                {
                    continue;
                }

                remotePlayers[state.playerId] = remoteView;
            }

            remoteView.ApplyState(state);
        }

        List<string> stalePlayerIds = new List<string>();
        foreach (KeyValuePair<string, RemotePlayerView> entry in remotePlayers)
        {
            if (!seenPlayerIds.Contains(entry.Key))
            {
                stalePlayerIds.Add(entry.Key);
            }
        }

        for (int i = 0; i < stalePlayerIds.Count; i++)
        {
            string playerId = stalePlayerIds[i];
            if (remotePlayers.TryGetValue(playerId, out RemotePlayerView view) && view != null)
            {
                Destroy(view.gameObject);
            }

            remotePlayers.Remove(playerId);
        }
    }

    RemotePlayerView SpawnRemotePlayer(string playerId)
    {
        GameObject playerObject = remotePlayerPrefab != null ? Instantiate(remotePlayerPrefab) : CreateRuntimeRemotePlayerObject(playerId);
        Transform parent = remotePlayersParent != null ? remotePlayersParent : transform;
        playerObject.transform.SetParent(parent, false);

        RemotePlayerView view = playerObject.GetComponent<RemotePlayerView>();
        if (view == null)
        {
            view = playerObject.AddComponent<RemotePlayerView>();
        }

        view.Initialize(playerId);
        return view;
    }

    GameObject CreateRuntimeRemotePlayerObject(string playerId)
    {
        GameObject remoteObject = new GameObject($"RemotePlayer_{playerId}");
        remoteObject.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        SpriteRenderer spriteRenderer = remoteObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        spriteRenderer.sortingOrder = 100;

        GameObject labelObject = new GameObject("NameLabel");
        labelObject.transform.SetParent(remoteObject.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0.75f, 0f);

        TextMeshPro textMesh = labelObject.AddComponent<TextMeshPro>();
        textMesh.alignment = TextAlignmentOptions.Center;
        textMesh.fontSize = 2.5f;
        textMesh.text = playerId;

        return remoteObject;
    }

    void ClearRemotePlayers()
    {
        foreach (KeyValuePair<string, RemotePlayerView> entry in remotePlayers)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value.gameObject);
            }
        }

        remotePlayers.Clear();
    }

    void EnqueueMainThreadAction(Action action)
    {
        if (action == null)
        {
            return;
        }

        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    void DrainMainThreadActions()
    {
        while (true)
        {
            Action action = null;

            lock (mainThreadActions)
            {
                if (mainThreadActions.Count > 0)
                {
                    action = mainThreadActions.Dequeue();
                }
            }

            if (action == null)
            {
                break;
            }

            action.Invoke();
        }
    }

    void LogFallback()
    {
        Debug.Log("[FIREBASE] Firebase SDK not installed/enabled. Running local fallback.", this);
    }

    void LogLocalFallbackMode()
    {
        if (announcedLocalFallback)
        {
            return;
        }

        announcedLocalFallback = true;
        Debug.Log("[FIREBASE] Local fallback mode active.", this);
    }

    static string NormalizeRoomCode(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? "ROOM123" : code.Trim().ToUpperInvariant();
    }

    static string NormalizeFirebasePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return "player_1";
        }

        string trimmed = playerId.Trim();
        if (int.TryParse(trimmed, out int numericId))
        {
            return "player_" + numericId;
        }

        if (trimmed.StartsWith("player-", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = trimmed.Substring("player-".Length);
            if (int.TryParse(suffix, out int playerNumber))
            {
                return "player_" + playerNumber;
            }
        }

        return trimmed;
    }

    static string NormalizeDisplayName(string name, string fallbackPlayerId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.IsNullOrWhiteSpace(fallbackPlayerId) ? "Player 1" : fallbackPlayerId;
        }

        return name.Trim();
    }

    string GetLocalDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        if (localPlayerIdentity != null && !string.IsNullOrWhiteSpace(localPlayerIdentity.playerName))
        {
            return localPlayerIdentity.playerName;
        }

        return localPlayerId;
    }

    public void SetLocalPlayerId(string id)
    {
        localPlayerId = NormalizeFirebasePlayerId(id);
    }

    public void SetDisplayName(string name)
    {
        displayName = NormalizeDisplayName(name, localPlayerId);
    }

    public void SetRoomCode(string code)
    {
        roomCode = NormalizeRoomCode(code);
    }

    static string GetDisplayName(PlayerIdentity player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(player.playerName))
        {
            return player.playerName;
        }

        return player.gameObject != null ? player.gameObject.name : "Player";
    }

    static long GetTimestampUtc()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

[Serializable]
public sealed class FirebasePlayerState
{
    public string playerId;
    public string displayName;
    public float x;
    public float y;
    public bool alive;
    public bool infected;
    public bool aiControlled;
    public bool frozen;
    public long updatedAt;
}

[Serializable]
public sealed class FirebaseMatchState
{
    public bool gameStarted;
    public string phase;
    public string status;
    public int wave;
    public float taskProgress;
    public int totalTasks;
    public bool finalHuntActive;
    public string winner;
    public long updatedAt;
}