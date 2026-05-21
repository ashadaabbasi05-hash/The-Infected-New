using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Manages remote player spawning, tracking, and cleanup for Firebase multiplayer.
/// Handles runtime placeholder generation if no prefab is assigned.
/// </summary>
[DisallowMultipleComponent]
public sealed class FirebaseRemotePlayerManager : MonoBehaviour
{
    public static FirebaseRemotePlayerManager Instance { get; private set; }

    [SerializeField] public FirebaseMultiplayerClient firebaseClient;
    [SerializeField] public GameObject remotePlayerPrefab;
    [SerializeField] public Transform remotePlayersParent;
    [SerializeField] public bool debugLogs = true;

    readonly Dictionary<string, GameObject> spawnedRemotes = new Dictionary<string, GameObject>(StringComparer.Ordinal);

    // Simulated remote data for testing without Firebase
    Vector3 simulatedRemotePosition;
    string simulatedRemoteId;
    GameObject simulatedRemoteObject;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ResolveReferences();
    }

    void Update()
    {
        // If we have a simulated remote, update its position to follow a test pattern
        if (simulatedRemoteObject != null)
        {
            simulatedRemotePosition += new Vector3(Mathf.Sin(Time.time) * 0.02f, Mathf.Cos(Time.time * 0.7f) * 0.01f, 0f);
            RemotePlayerView view = simulatedRemoteObject.GetComponent<RemotePlayerView>();
            if (view != null)
            {
                view.SetTargetPosition(simulatedRemotePosition);
            }
        }
    }

    void ResolveReferences()
    {
        if (firebaseClient == null)
        {
            firebaseClient = FindAnyObjectByType<FirebaseMultiplayerClient>();
        }
        if (remotePlayersParent == null)
        {
            remotePlayersParent = transform;
        }
    }

    /// <summary>
    /// Gets or creates a remote player GameObject for the given player ID.
    /// Ensures collision prevention and ownership tagging.
    /// </summary>
    public GameObject GetOrCreateRemotePlayer(string playerId, Vector3 worldPosition, string displayName = null)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return null;

        // Return existing
        if (spawnedRemotes.TryGetValue(playerId, out GameObject existing) && existing != null)
        {
            return existing;
        }

        // Clean up stale entries
        if (existing == null)
        {
            spawnedRemotes.Remove(playerId);
        }

        // Create new
        GameObject remoteObj;
        if (remotePlayerPrefab != null)
        {
            remoteObj = Instantiate(remotePlayerPrefab, remotePlayersParent);
            remoteObj.name = $"Remote_{playerId}";
        }
        else
        {
            remoteObj = CreateRuntimeRemotePlayer(playerId);
        }

        // Ensure proper parent
        remoteObj.transform.SetParent(remotePlayersParent, false);
        remoteObj.transform.position = worldPosition;

        // Add or configure RemotePlayerView
        RemotePlayerView view = remoteObj.GetComponent<RemotePlayerView>();
        if (view == null)
            view = remoteObj.AddComponent<RemotePlayerView>();
        view.Initialize(playerId);
        if (!string.IsNullOrEmpty(displayName))
        {
            TMP_Text label = remoteObj.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = displayName;
        }

        // Tag with ownership
        PlayerOwnership ownership = remoteObj.GetComponent<PlayerOwnership>();
        if (ownership == null)
            ownership = remoteObj.AddComponent<PlayerOwnership>();
        ownership.Configure(playerId, false, true, false);

        // Prevent remote from pushing local player
        PreventRemoteCollision(remoteObj);

        spawnedRemotes[playerId] = remoteObj;

        if (debugLogs)
            Debug.Log($"[REMOTE] Created remote player {playerId}");

        return remoteObj;
    }

    /// <summary>
    /// Removes a remote player by ID.
    /// </summary>
    public void RemoveRemotePlayer(string playerId)
    {
        if (spawnedRemotes.TryGetValue(playerId, out GameObject obj) && obj != null)
        {
            Destroy(obj);
        }
        spawnedRemotes.Remove(playerId);
        if (debugLogs)
            Debug.Log($"[REMOTE] Removed remote player {playerId}");
    }

    /// <summary>
    /// Clears all tracked remote players.
    /// </summary>
    public void ClearAllRemotes()
    {
        foreach (GameObject obj in spawnedRemotes.Values)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawnedRemotes.Clear();
        if (debugLogs)
            Debug.Log("[REMOTE] Cleared all remote players");
    }

    /// <summary>
    /// Ensures a remote player GameObject cannot push local players.
    /// Sets Rigidbody2D to kinematic and colliders to trigger.
    /// </summary>
    static void PreventRemoteCollision(GameObject remoteObj)
    {
        if (remoteObj == null) return;

        // Disable PlayerMovement
        PlayerMovement pm = remoteObj.GetComponent<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        // Disable BotMovement
        BotMovement bm = remoteObj.GetComponent<BotMovement>();
        if (bm != null) bm.enabled = false;

        // Rigidbody2D -> kinematic
        Rigidbody2D rb = remoteObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
        }

        // All Collider2D -> trigger
        Collider2D[] colliders = remoteObj.GetComponents<Collider2D>();
        foreach (Collider2D col in colliders)
        {
            col.isTrigger = true;
        }

        // Also check children
        Collider2D[] childColliders = remoteObj.GetComponentsInChildren<Collider2D>();
        foreach (Collider2D col in childColliders)
        {
            col.isTrigger = true;
        }
    }

    /// <summary>
    /// Creates a runtime placeholder remote player with sprite, label, and view.
    /// </summary>
    GameObject CreateRuntimeRemotePlayer(string playerId)
    {
        GameObject obj = new GameObject($"Remote_{playerId}");
        obj.transform.localScale = new Vector3(0.85f, 0.85f, 1f);

        // Cyan/blue sprite
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = CreateWhiteCircleSprite();
        sr.color = new Color32(100, 200, 220, 220);
        sr.sortingOrder = 100;

        // Name label
        GameObject labelObj = new GameObject("NameLabel");
        labelObj.transform.SetParent(obj.transform, false);
        labelObj.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = playerId;
        tmp.fontSize = 2.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color32(200, 240, 255, 255);

        // Collision safety - kinematic rigidbody
        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        // Add view
        obj.AddComponent<RemotePlayerView>();

        if (debugLogs)
            Debug.Log("[REMOTE] Using generated remote player placeholder.");

        return obj;
    }

    static Sprite CreateWhiteCircleSprite()
    {
        Texture2D tex = new Texture2D(16, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float dx = (x - 7.5f) / 7.5f;
                float dy = (y - 7.5f) / 7.5f;
                bool inside = (dx * dx + dy * dy) <= 1f;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
    }

    // ========================================================================
    //  TEST HELPERS
    // ========================================================================

    [ContextMenu("Simulate Remote Player 2")]
    public void SimulateRemotePlayer2()
    {
        // Clean previous sim
        ClearSimulatedRemotes();

        simulatedRemoteId = "player_2";
        simulatedRemotePosition = new Vector3(5f, 0f, 0f);

        simulatedRemoteObject = GetOrCreateRemotePlayer(simulatedRemoteId, simulatedRemotePosition, "Player 2 (sim)");

        Debug.Log("[REMOTE TEST] Simulated remote player_2.");
    }

    [ContextMenu("Clear Simulated Remotes")]
    public void ClearSimulatedRemotes()
    {
        if (simulatedRemoteObject != null)
        {
            Destroy(simulatedRemoteObject);
            simulatedRemoteObject = null;
        }
        if (!string.IsNullOrEmpty(simulatedRemoteId))
        {
            spawnedRemotes.Remove(simulatedRemoteId);
            simulatedRemoteId = null;
        }
        Debug.Log("[REMOTE TEST] Simulated remotes cleared.");
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}