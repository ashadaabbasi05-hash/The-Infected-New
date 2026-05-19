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

    [Header("Visuals")]
    [Tooltip("If true, the SpriteRenderer's color on Awake becomes the normalColor. Otherwise, normalColor is white.")]
    [SerializeField] bool useOriginalColorAsNormal = true;

    [Tooltip("Base color used when the player is healthy.")]
    [SerializeField] Color normalColor = Color.white;

    [Tooltip("Subtle blood-rust tint used while infected. Matches #8B1A1A for stealth gameplay.")]
    [SerializeField] Color infectedColor = new Color32(139, 26, 26, 255);

    PlayerMovement playerMovement;
    SpriteRenderer spriteRenderer;
    Color originalSpriteColor;
    bool hasOriginalSpriteColor;

    const string InfectLogPrefix = "[INFECT DEBUG]";

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
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

    void ApplyControlState()
    {
        SetMovementEnabled(isAlive && isLocalPlayer && !isAIControlled && !isFrozen);
        SetBotMovementEnabled(isAlive && isAIControlled && !isFrozen);
    }

    public void FreezePlayer()
    {
        if (isFrozen)
        {
            return;
        }

        isFrozen = true;
        ApplyControlState();
        Debug.Log($"[ANTIDOTE] {GetDisplayName()} frozen.", this);
    }

    public void UnfreezePlayer()
    {
        if (!isFrozen)
        {
            return;
        }

        isFrozen = false;
        ApplyControlState();
        Debug.Log($"[ANTIDOTE] {GetDisplayName()} unfrozen.", this);
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
    }
}
