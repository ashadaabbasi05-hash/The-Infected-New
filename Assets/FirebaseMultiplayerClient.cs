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

    [SerializeField] bool enableFirebase = false;
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool autoJoinOnStart = false;
    [SerializeField] string roomCode = "ROOM123";
    [SerializeField] string localPlayerId = "player_1";
    [SerializeField] string databaseUrl = "https://the-infected-hackathon-default-rtdb.asia-southeast1.firebasedatabase.app";
    [SerializeField, Min(0.01f)] float positionSyncInterval = 0.15f;
    [SerializeField] Transform localPlayerTransform;
    [SerializeField] PlayerIdentity localPlayerIdentity;
    [SerializeField] GameObject remotePlayerPrefab;
    [SerializeField] Transform remotePlayersParent;

    public bool IsHost { get; private set; }
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
            pendingPlayerId = NormalizePlayerId(localPlayerId);
            Debug.Log($"[FIREBASE] CreateRoom queued until Firebase ready. room={pendingRoomCode} player={pendingPlayerId}", this);
            return;
        }

        // Proceed with creating and joining the room now that Firebase is ready.
        joined = true;
        IsHost = true;
        cachedWinner = string.Empty;
        cachedFinalHuntActive = false;

        if (!TryEnableFirebaseSession())
        {
            LogFallback();
            return;
        }

#if FIREBASE_ENABLED
        WriteRoomMeta(true);
        PublishLocalPlayerState();
        PublishMatchState();
        Debug.Log($"[FIREBASE] Joined room {roomCode} as {localPlayerId}.", this);
#endif
    }

    public void JoinRoom(string code, string playerId)
    {
        roomCode = NormalizeRoomCode(code);
        localPlayerId = NormalizePlayerId(playerId);

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
            pendingPlayerId = NormalizePlayerId(playerId);
            Debug.Log($"[FIREBASE] JoinRoom queued until Firebase ready. room={pendingRoomCode} player={pendingPlayerId}", this);
            return;
        }

        // Proceed with join now that Firebase is ready.
        joined = true;
        IsHost = false;

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

        roomRootRef = rootRef.Child("matches").Child(roomCode);
        playersRef = roomRootRef.Child("players");
        stateRef = roomRootRef.Child("state");

        Debug.Log($"[FIREBASE] Joined room {roomCode} as {localPlayerId}.", this);

        WriteRoomMeta(false);
        PublishLocalPlayerState();
        PublishMatchState();
#endif
    }

    public void LeaveRoom()
    {
        initialized = false;
        joined = false;
        IsHost = false;

        ClearRemotePlayers();

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

        playersRef.Child(playerState.playerId).SetRawJsonValueAsync(JsonUtility.ToJson(playerState)).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("[FIREBASE] Failed to publish player state: " + task.Exception, this);
                return;
            }

            if (task.IsCanceled)
            {
                Debug.LogError("[FIREBASE] Failed to publish player state: canceled.", this);
                return;
            }

            Debug.Log("[FIREBASE] Published local player state.", this);
        });
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
            TryInitializeFirebaseBindings();
            WriteRoomMeta(IsHost);
            PublishLocalPlayerState();
            PublishMatchState();
            return;
        }
#endif

        if (debugLogs)
        {
            Debug.Log("[FIREBASE] Firebase enabled, but room is not initialized yet.", this);
        }
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
        foreach (DataSnapshot child in args.Snapshot.Children)
        {
            FirebasePlayerState state = DeserializePlayerState(child);
            if (state != null)
            {
                states.Add(state);
            }
        }

        EnqueueMainThreadAction(() => ApplyRemotePlayerStates(states));
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

    void WriteRoomMeta(bool host)
    {
        if (metaRef == null)
        {
            return;
        }

        long timestamp = GetTimestampUtc();
        FirebaseRoomMeta meta = new FirebaseRoomMeta
        {
            roomCode = roomCode,
            hostPlayerId = localPlayerId,
            open = true,
            createdAt = timestamp,
            updatedAt = timestamp
        };

        metaRef.SetRawJsonValueAsync(JsonUtility.ToJson(meta));

        if (debugLogs)
        {
            Debug.Log($"[FIREBASE] Room {(host ? "created" : "joined")}: {roomCode}", this);
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

        if (string.IsNullOrWhiteSpace(localPlayerId))
        {
            localPlayerId = "player_1";
        }

        Vector3 worldPosition = localPlayerTransform != null ? localPlayerTransform.position : Vector3.zero;
        PlayerIdentity identity = localPlayerIdentity;

        playerState = new FirebasePlayerState
        {
            playerId = localPlayerId,
            displayName = identity != null && !string.IsNullOrWhiteSpace(identity.playerName) ? identity.playerName : localPlayerId,
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
            phase = GameManager.CurrentPhase.ToString(),
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

    static string NormalizePlayerId(string playerId)
    {
        return string.IsNullOrWhiteSpace(playerId) ? "player_1" : playerId.Trim();
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
    public string phase;
    public int wave;
    public float taskProgress;
    public int totalTasks;
    public bool finalHuntActive;
    public string winner;
    public long updatedAt;
}