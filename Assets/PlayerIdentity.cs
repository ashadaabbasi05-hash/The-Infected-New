using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerIdentity : MonoBehaviour
{
    [field: Header("Identity")]
    [field: Tooltip("Unique identifier for this player within the current match or scene.")]
    [field: SerializeField]
    public int playerId { get; private set; }

    [field: Tooltip("Display name shown in UI, logs, and gameplay systems.")]
    [field: SerializeField]
    public string playerName { get; private set; } = "Player";

    [field: Tooltip("True when this object belongs to the local player.")]
    [field: SerializeField]
    public bool isLocalPlayer { get; private set; }
    public bool IsLocalPlayer => isLocalPlayer;

    [field: Header("State")]
    [field: Tooltip("True when the player is alive and able to act.")]
    [field: SerializeField]
    public bool isAlive { get; private set; } = true;

    [field: Tooltip("True when the player is infected.")]
    [field: SerializeField]
    public bool isInfected { get; private set; }

    [field: Tooltip("True when AI should control this player instead of manual input.")]
    [field: SerializeField]
    public bool isAIControlled { get; private set; }

    [field: Tooltip("True when the player is frozen by antidote.")]
    [field: SerializeField]
    public bool isFrozen { get; private set; }

    [Header("Antidote Freeze Visual")]
    [SerializeField] bool useFreezeTint = true;
    [SerializeField] Color freezeColor = new Color32(99, 190, 210, 255);

    [Header("Visuals")]
    [Tooltip("If true, the SpriteRenderer's color on Awake becomes the normalColor. Otherwise, normalColor is white.")]
    [SerializeField] bool useOriginalColorAsNormal = true;

    [Tooltip("Base color used when the player is healthy.")]
    [SerializeField] Color normalColor = Color.white;

    [Tooltip("Subtle blood-rust tint used while infected. Matches #8B1A1A for stealth gameplay.")]
    [SerializeField] Color infectedColor = new Color32(139, 26, 26, 255);

    PlayerMovement playerMovement;
    Rigidbody2D rb;
    SpriteRenderer spriteRenderer;
    Color originalSpriteColor;
    bool hasOriginalSpriteColor;

    const string InfectLogPrefix = "[INFECT DEBUG]";

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Capture the artist-authored sprite color once so Cure() can restore it.
        // If useOriginalColorAsNormal is true, the original sprite color becomes the healing target.
        if (spriteRenderer != null)
        {
            originalSpriteColor = spriteRenderer.color;
            if (useOriginalColorAsNormal)
            {
                normalColor = originalSpriteColor;
            }
            hasOriginalSpriteColor = true;
        }
    }

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = gameObject.name;
        }
    }

    public void Infect()
    {
        isInfected = true;
        isAIControlled = true;

        ApplyControlState();
        ApplyInfectionVisual(true);

        Debug.Log($"{InfectLogPrefix} INFECTED: {GetDisplayName()}", this);
    }

    public void Cure()
    {
        isInfected = false;
        isAIControlled = false;

        ApplyControlState();
        ApplyInfectionVisual(false);

        Debug.Log($"{InfectLogPrefix} CURED: {GetDisplayName()}", this);
    }

    public void SetInfected(bool value)
    {
        if (value)
        {
            Infect();
        }
        else
        {
            Cure();
        }
    }

    public void KillPlayer()
    {
        if (!isAlive)
        {
            return;
        }

        isAlive = false;
        ApplyControlState();
    }

    public void RevivePlayer()
    {
        if (isAlive)
        {
            ApplyControlState();
            return;
        }

        isAlive = true;
        ApplyControlState();
        ApplyInfectionVisual(isInfected);
    }

    public static PlayerIdentity[] GetAllPlayers()
    {
        var players = FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Include);
        return players;
    }

    void SetMovementEnabled(bool enabled)
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (playerMovement != null)
        {
            playerMovement.enabled = enabled;
        }
    }

    void SetBotMovementEnabled(bool enabled)
    {
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null && behaviour.GetType().Name == "BotMovement")
            {
                behaviour.enabled = enabled;
            }
        }
    }

    public void ApplyControlState()
    {
        if (!isAlive)
        {
            SetMovementEnabled(false);
            SetBotMovementEnabled(false);
            StopRigidbody();
            return;
        }

        if (isFrozen)
        {
            SetMovementEnabled(false);
            SetBotMovementEnabled(false);
            StopRigidbody();
            return;
        }

        if (isInfected || isAIControlled)
        {
            SetMovementEnabled(false);
            SetBotMovementEnabled(true);
            return;
        }

        if (isLocalPlayer)
        {
            SetMovementEnabled(true);
            SetBotMovementEnabled(false);
            return;
        }

        SetMovementEnabled(false);
        SetBotMovementEnabled(false);
    }

    void StopRigidbody()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>Prototype helper so MeetingController can enforce a single local player.</summary>
    public void SetLocalPlayerForPrototype(bool value)
    {
        isLocalPlayer = value;
    }

    public void FreezePlayer()
    {
        FreezePlayer("Unknown");
    }

    public void FreezePlayer(string reason)
    {
        if (isFrozen)
        {
            return;
        }

        isFrozen = true;

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        ApplyControlState();
        ApplyFreezeVisual(true);
        Debug.Log($"[FREEZE DEBUG] FreezePlayer called on {GetDisplayName()} by {reason}", this);
        Debug.Log($"[FREEZE DEBUG] Local player frozen? {isLocalPlayer} reason={reason}", this);
        Debug.Log($"[ANTIDOTE] {GetDisplayName()} frozen.", this);
    }

    public void UnfreezePlayer()
    {
        if (!isFrozen)
        {
            return;
        }

        isFrozen = false;
        RefreshControlState();
        ApplyFreezeVisual(false);
        Debug.Log($"[ANTIDOTE] {GetDisplayName()} unfrozen.", this);
    }

    public void RefreshControlState()
    {
        ApplyControlState();
    }

    string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(playerName) ? gameObject.name : playerName;
    }

    public void RefreshInfectionVisual(bool useObviousDebugColor = false)
    {
        ApplyInfectionVisual(isInfected, useObviousDebugColor);
    }

    void ApplyInfectionVisual(bool infected, bool useObviousDebugColor = false)
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // No renderer means nothing to color; skip safely.
        if (spriteRenderer == null)
        {
            return;
        }

        if (!hasOriginalSpriteColor)
        {
            originalSpriteColor = spriteRenderer.color;
            normalColor = originalSpriteColor;
            hasOriginalSpriteColor = true;
        }

        // Use a subtle rust tint for infection; restore original tone when cured.
        spriteRenderer.color = infected
            ? (useObviousDebugColor ? Color.red : infectedColor)
            : originalSpriteColor;

        if (isFrozen)
        {
            ApplyFreezeVisual(true);
        }
    }

    void ApplyFreezeVisual(bool frozen)
    {
        if (!useFreezeTint)
        {
            return;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer == null)
        {
            return;
        }

        if (frozen)
        {
            spriteRenderer.color = freezeColor;
            return;
        }

        ApplyInfectionVisual(isInfected);
    }
}
