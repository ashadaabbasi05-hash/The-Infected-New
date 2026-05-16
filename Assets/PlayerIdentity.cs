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

    PlayerMovement playerMovement;
    SpriteRenderer spriteRenderer;

    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
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
        SetInfected(true);
    }

    public void Cure()
    {
        SetInfected(false);
    }

    public void SetInfected(bool value)
    {
        if (isInfected == value)
        {
            if (value)
            {
                ApplyInfectionVisual(true);
            }

            return;
        }

        isInfected = value;
        isAIControlled = value;

        ApplyInfectionVisual(value);

        if (value)
        {
            LogInfection($"{GetDisplayName().ToUpperInvariant()} HAS BEEN INFECTED");
        }
    }

    public void KillPlayer()
    {
        if (!isAlive)
        {
            return;
        }

        isAlive = false;
        SetMovementEnabled(false);
    }

    public void RevivePlayer()
    {
        if (isAlive)
        {
            SetMovementEnabled(true);
            return;
        }

        isAlive = true;
        SetMovementEnabled(true);
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

    string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(playerName) ? gameObject.name : playerName;
    }

    void ApplyInfectionVisual(bool infected)
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = infected ? Color.red : Color.white;
        }
    }

    void LogInfection(string message)
    {
        Debug.Log($"<color=red>{message}</color>", this);
    }
}
