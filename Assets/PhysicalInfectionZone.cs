using UnityEngine;

// PhysicalInfectionZone
// Attach this to a child object under each player (example: Player -> InfectionZone)
// Requires a CircleCollider2D on the same GameObject (isTrigger = true)
// Uses GetComponentInParent<PlayerIdentity>() to find the owner.
[DisallowMultipleComponent]
public sealed class PhysicalInfectionZone : MonoBehaviour
{
    [Header("Enable")]
    [SerializeField] bool enablePhysicalInfection = true;

    [Header("Debug")]
    [SerializeField] bool debugLogs = true;
    [SerializeField] bool showProgressLogs = false;
    [SerializeField] bool useFastDebugTimers = false;

    [Header("Timers (seconds)")]
    [SerializeField] float fakeTaskMinTime = 7f;
    [SerializeField] float fakeTaskMaxTime = 15f;
    [SerializeField] float stalkMinTime = 5f;
    [SerializeField] float stalkMaxTime = 10f;
    [SerializeField] float aggressiveMinTime = 5f;
    [SerializeField] float aggressiveMaxTime = 10f;
    [SerializeField] float finalHuntMinTime = 4f;

    [SerializeField] float finalHuntMaxTime = 8f;
    [SerializeField] float debugMinTime = 1f;
    [SerializeField] float debugMaxTime = 2f;
    // Runtime state
    PlayerIdentity sourceIdentity;
    BotMovement sourceBotMovement;
    PlayerIdentity currentTarget;
    float currentContactTimer;
    float requiredContactTime;
    bool contactActive;
    System.Collections.Generic.HashSet<Collider2D> touchingColliders = new System.Collections.Generic.HashSet<Collider2D>();

    // Progress logging
    float lastProgressLogTime;
    const float progressLogInterval = 0.5f;

    void Awake()
    {
        sourceIdentity = GetComponentInParent<PlayerIdentity>();
        sourceBotMovement = GetComponentInParent<BotMovement>();

        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            if (debugLogs) Debug.LogWarning("[PHYSICAL INFECTION] InfectionZone missing Collider2D.", this);
        }
        else if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.Log("[PHYSICAL INFECTION] Collider was not trigger. Auto-fixed.", this);
            col.isTrigger = true;
        }

        // start cleared
        ResetContact();
    }

    bool IsSourceValid()
    {
        if (!enablePhysicalInfection) return false;
        if (sourceIdentity == null) return false;
        if (!sourceIdentity.isAlive) return false;
        if (!sourceIdentity.isInfected) return false;
        if (!sourceIdentity.isAIControlled) return false;
        return true;
    }

    bool IsValidTarget(PlayerIdentity target)
    {
        if (target == null) return false;
        if (sourceIdentity != null && target == sourceIdentity) return false;
        if (!target.isAlive) return false;
        if (target.isInfected) return false;
        if (target.isAIControlled) return false;
        return true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsSourceValid()) return;

        PlayerIdentity target = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (!IsValidTarget(target)) return;

        // If already actively contacting the same target, just register collider and continue
        if (contactActive && IsSameTarget(target))
        {
            touchingColliders.Add(other);
            if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] Contact already active with {currentTarget.playerName}. Timer continues.", this);
            return;
        }

        // If contact is active with a different target, ignore new target for now
        if (contactActive && currentTarget != null && !IsSameTarget(target))
        {
            if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] Ignoring new contact from {target.playerName} because {currentTarget.playerName} is already being contacted.", this);
            return;
        }

        // Begin contact with this target
        BeginContact(target, other);
    }

    bool IsSameTarget(PlayerIdentity target)
    {
        return target != null && currentTarget != null && target == currentTarget;
    }

    void BeginContact(PlayerIdentity target, Collider2D collider)
    {
        currentTarget = target;
        currentContactTimer = 0f;
        requiredContactTime = ChooseRequiredContactTime();
        contactActive = true;
        touchingColliders.Clear();
        if (collider != null) touchingColliders.Add(collider);
        lastProgressLogTime = Time.time - progressLogInterval; // allow immediate progress log

        if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] {sourceIdentity.playerName} started infecting {currentTarget.playerName}. RequiredTime={requiredContactTime:0.00}", this);
    }

    void Update()
    {
        if (!contactActive) return;

        // validate source & target still valid
        if (!IsSourceValid() || !IsValidTarget(currentTarget))
        {
            ResetContact();
            return;
        }

        currentContactTimer += Time.deltaTime;

        if (showProgressLogs && Time.time - lastProgressLogTime >= progressLogInterval)
        {
            lastProgressLogTime = Time.time;
            if (sourceBotMovement != null && sourceBotMovement.CurrentMode == BotBehaviorMode.FinalHunt)
            {
                Debug.Log($"[FINAL HUNT] Infection progress {currentTarget.playerName}: {currentContactTimer:0.00}/{requiredContactTime:0.00}", this);
            }
            else if (debugLogs)
            {
                Debug.Log($"[PHYSICAL INFECTION] Progress {currentTarget.playerName}: {currentContactTimer:0.00}/{requiredContactTime:0.00}", this);
            }
        }

        if (currentContactTimer >= requiredContactTime)
        {
            CompletePhysicalInfection();
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        PlayerIdentity leaving = other != null ? other.GetComponentInParent<PlayerIdentity>() : null;
        if (leaving == null) return;

        // Remove collider from tracking
        if (touchingColliders.Contains(other))
        {
            touchingColliders.Remove(other);
        }

        // Only reset if the leaving identity is the current target and no colliders remain for them
        if (currentTarget == leaving)
        {
            if (touchingColliders.Count == 0)
            {
                if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] {currentTarget.playerName} escaped infection contact. Timer reset.", this);
                ResetContact();
            }
            else
            {
                if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] Collider for {currentTarget.playerName} left but other colliders remain. Timer continues.", this);
            }
        }
    }


    float ChooseRequiredContactTime()
    {
        if (useFastDebugTimers)
        {
            return UnityEngine.Random.Range(debugMinTime, debugMaxTime);
        }
        BotBehaviorMode mode = sourceBotMovement != null ? sourceBotMovement.CurrentMode : BotBehaviorMode.FakeTask;
        switch (mode)
        {
            case BotBehaviorMode.Stalk:
                return UnityEngine.Random.Range(stalkMinTime, stalkMaxTime);
            case BotBehaviorMode.AggressiveChase:
                return UnityEngine.Random.Range(aggressiveMinTime, aggressiveMaxTime);
            case BotBehaviorMode.FinalHunt:
                return UnityEngine.Random.Range(finalHuntMinTime, finalHuntMaxTime);
            case BotBehaviorMode.FakeTask:
            default:
                return UnityEngine.Random.Range(fakeTaskMinTime, fakeTaskMaxTime);
        }
    }

    void CompletePhysicalInfection()
    {
        if (currentTarget == null || sourceIdentity == null)
        {
            ResetContact();
            return;
        }

        // Safety checks: do not infect dead/already infected/self
        if (!IsValidTarget(currentTarget) || !IsSourceValid())
        {
            ResetContact();
            return;
        }

        PlayerIdentity target = currentTarget;

        // Call Infect() to flip internal state and AI control
        try
        {
            target.Infect();
        }
        catch
        {
            // If Infect isn't available or throws, continue cautiously
            if (debugLogs) Debug.LogWarning($"[PHYSICAL INFECTION] Failed to call Infect() on {target.playerName}.", this);
        }

        // Disable player movement
        PlayerMovement pm = target.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            pm.enabled = false;
        }

        // Enable bot movement and set mode
        BotMovement bm = target.GetComponent<BotMovement>();
        if (bm != null)
        {
            bm.enabled = true;

            BotBehaviorMode sourceMode = sourceBotMovement != null ? sourceBotMovement.CurrentMode : BotBehaviorMode.FakeTask;

            // If FinalHunt is active globally, force FinalHunt mode
            FinalHuntManager fhm = FindAnyObjectByType<FinalHuntManager>(FindObjectsInactive.Include);
            if (fhm != null && fhm.IsFinalHuntActive)
            {
                bm.SetMode(BotBehaviorMode.FinalHunt);
            }
            else
            {
                // Map source mode to new bot mode
                switch (sourceMode)
                {
                    case BotBehaviorMode.FinalHunt:
                        bm.SetMode(BotBehaviorMode.FinalHunt);
                        break;
                    case BotBehaviorMode.AggressiveChase:
                        bm.SetMode(BotBehaviorMode.AggressiveChase);
                        break;
                    case BotBehaviorMode.Stalk:
                        bm.SetMode(BotBehaviorMode.Stalk);
                        break;
                    case BotBehaviorMode.FakeTask:
                    default:
                        bm.SetMode(BotBehaviorMode.FakeTask);
                        break;
                }
            }

            bm.ResumeBot();
        }
        else
        {
            if (debugLogs) Debug.LogWarning($"[PHYSICAL INFECTION] BotMovement missing on infected target {target.playerName}.", this);
        }

        if (debugLogs) Debug.Log($"[PHYSICAL INFECTION] {target.playerName} was infected by {sourceIdentity.playerName}.", this);

        // Check lose conditions
        GameEndManager ge = GameEndManager.Instance != null ? GameEndManager.Instance : FindAnyObjectByType<GameEndManager>(FindObjectsInactive.Include);
        if (ge != null)
        {
            ge.CheckLoseConditions();
        }

        ResetContact();
    }

    // Public helpers
    public void ResetContact()
    {
        contactActive = false;
        currentTarget = null;
        currentContactTimer = 0f;
        requiredContactTime = 0f;
        lastProgressLogTime = 0f;
        touchingColliders.Clear();
    }

    public bool HasValidTarget()
    {
        return currentTarget != null && IsValidTarget(currentTarget);
    }


    public float GetProgress01()
    {
        if (!contactActive || requiredContactTime <= 0f) return 0f;
        return Mathf.Clamp01(currentContactTimer / requiredContactTime);
    }

    public string GetDebugStatus()
    {
        string sourceName = sourceIdentity != null ? sourceIdentity.playerName : "Unknown";
        string targetName = currentTarget != null ? currentTarget.playerName : "None";
        string progress = $"{currentContactTimer:0.0}/{requiredContactTime:0.0}";
        return $"Source={sourceName} Target={targetName} Progress={progress} Active={contactActive}";
    }
}
