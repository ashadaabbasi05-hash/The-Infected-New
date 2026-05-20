using System.Collections;
using UnityEngine;

public enum BotBehaviorMode
{
    FakeTask = 0,
    Stalk = 1,
    AggressiveChase = 2,
    FinalHunt = 3
}

[DisallowMultipleComponent]
public sealed class BotMovement : MonoBehaviour
{
    [Header("Behavior")]
    [SerializeField] BotBehaviorMode currentMode = BotBehaviorMode.FakeTask;
    [SerializeField] float stalkSpeed = 2.0f;
    [SerializeField] float aggressiveSpeed = 2.7f;
    [SerializeField] float finalHuntSpeed = 3.2f;
    [SerializeField] float stalkFollowDurationMin = 3f;
    [SerializeField] float stalkFollowDurationMax = 6f;
    [SerializeField] float stalkFakeTaskDurationMin = 4f;
    [SerializeField] float stalkFakeTaskDurationMax = 8f;
    [SerializeField] bool enableDebugHotkeys = true;

    [Header("Waypoints")]
    [SerializeField] Transform[] waypoints;
    [SerializeField] float moveSpeed = 2.3f;
    [SerializeField] float waitAtWaypointMin = 3f;
    [SerializeField] float waitAtWaypointMax = 8f;
    [SerializeField] float arriveDistance = 0.15f;
    [SerializeField] bool pauseDuringMeetingAndVoting = true;

    Rigidbody2D rb;
    Transform currentTarget;
    int currentWaypointIndex;
    bool waiting;
    Coroutine waitRoutine;
    bool warnedNoWaypoints;
    PlayerIdentity selfIdentity;

    bool stalkIsFollowing;
    float stalkModeTimer;

    public BotBehaviorMode CurrentMode => currentMode;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfIdentity = GetComponent<PlayerIdentity>();

        // Keep 2D top-down movement stable.
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }
    }

    void OnEnable()
    {
        Debug.Log($"[BOT] BotMovement enabled on {gameObject.name}", this);
        ChooseRandomStartingWaypoint();
        ResetStalkState();
        warnedNoWaypoints = false;
    }

    void OnDisable()
    {
        StopWaitRoutine();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    void FixedUpdate()
    {
        if (selfIdentity == null)
        {
            selfIdentity = GetComponent<PlayerIdentity>();
        }

        if (selfIdentity != null && selfIdentity.isFrozen)
        {
            StopMoving();
            return;
        }

        if (pauseDuringMeetingAndVoting && IsMeetingOrVotingActive())
        {
            StopMoving();
            return;
        }

        switch (currentMode)
        {
            case BotBehaviorMode.FakeTask:
                UpdateFakeTaskMovement(moveSpeed);
                break;

            case BotBehaviorMode.Stalk:
                UpdateStalkBehavior();
                break;

            case BotBehaviorMode.AggressiveChase:
                UpdateAggressiveChaseBehavior();
                break;

            case BotBehaviorMode.FinalHunt:
                UpdateFinalHuntBehavior();
                break;
        }
    }

    void Update()
    {
        if (!enableDebugHotkeys || !enabled)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            SetMode(BotBehaviorMode.FakeTask);
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            SetMode(BotBehaviorMode.Stalk);
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            SetMode(BotBehaviorMode.AggressiveChase);
        }

        if (Input.GetKeyDown(KeyCode.H))
        {
            SetMode(BotBehaviorMode.FinalHunt);
        }
    }

    public void SetMode(BotBehaviorMode mode)
    {
        BotBehaviorMode previousMode = currentMode;
        currentMode = mode;

        StopWaitRoutine();
        waiting = false;
        ResetStalkState();

        if (previousMode != mode)
        {
            string playerName = selfIdentity != null && !string.IsNullOrWhiteSpace(selfIdentity.playerName)
                ? selfIdentity.playerName
                : gameObject.name;

            AgentTracePanel.Trace("BOT", $"{playerName} mode changed to {mode}");
            Debug.Log($"[BOT] {playerName} mode changed to {mode}", this);
        }
    }

    void UpdateFakeTaskMovement(float speed)
    {
        if (waiting)
        {
            return;
        }

        if (!HasValidWaypoints())
        {
            if (!warnedNoWaypoints)
            {
                warnedNoWaypoints = true;
                Debug.LogWarning($"[BOT] No waypoints assigned on {gameObject.name}. BotMovement idle.", this);
            }

            StopMoving();
            return;
        }

        if (currentTarget == null)
        {
            ChooseRandomStartingWaypoint();
            if (currentTarget == null)
            {
                return;
            }
        }

        Vector2 currentPosition = GetCurrentPosition();
        Vector2 targetPosition = currentTarget.position;
        float distanceToTarget = Vector2.Distance(currentPosition, targetPosition);

        if (distanceToTarget <= arriveDistance)
        {
            StartWaitThenPickNext();
            return;
        }

        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, targetPosition, speed * Time.fixedDeltaTime);
        MoveTo(nextPosition);
    }

    void UpdateStalkBehavior()
    {
        if (stalkModeTimer <= 0f)
        {
            if (stalkIsFollowing)
            {
                stalkIsFollowing = false;
                stalkingTimerReset(stalkFakeTaskDurationMin, stalkFakeTaskDurationMax);
            }
            else
            {
                stalkIsFollowing = true;
                stalkingTimerReset(stalkFollowDurationMin, stalkFollowDurationMax);
            }
        }

        stalkModeTimer -= Time.fixedDeltaTime;

        if (stalkIsFollowing)
        {
            Transform target = FindNearestHumanTarget();
            if (target != null)
            {
                waiting = false;
                StopWaitRoutine();
                MoveTowardsTransform(target, stalkSpeed);
                return;
            }
        }

        UpdateFakeTaskMovement(moveSpeed);
    }

    void stalkingTimerReset(float minDuration, float maxDuration)
    {
        float min = Mathf.Min(minDuration, maxDuration);
        float max = Mathf.Max(minDuration, maxDuration);
        stalkModeTimer = UnityEngine.Random.Range(min, max);
    }

    void UpdateAggressiveChaseBehavior()
    {
        waiting = false;
        StopWaitRoutine();

        Transform target = FindNearestHumanTarget();
        if (target == null)
        {
            UpdateFakeTaskMovement(moveSpeed);
            return;
        }

        MoveTowardsTransform(target, aggressiveSpeed);
    }

    void UpdateFinalHuntBehavior()
    {
        waiting = false;
        StopWaitRoutine();

        Transform target = FindNearestHumanTarget();
        if (target == null)
        {
            StopMoving();
            return;
        }

        MoveTowardsTransform(target, finalHuntSpeed);
    }

    void MoveTowardsTransform(Transform target, float speed)
    {
        if (target == null)
        {
            StopMoving();
            return;
        }

        Vector2 currentPosition = GetCurrentPosition();
        Vector2 targetPosition = target.position;
        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, targetPosition, speed * Time.fixedDeltaTime);
        MoveTo(nextPosition);
    }

    void ResetStalkState()
    {
        stalkIsFollowing = false;
        stalkingTimerReset(stalkFakeTaskDurationMin, stalkFakeTaskDurationMax);
    }

    public Transform FindNearestHumanTarget()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null || allPlayers.Length == 0)
        {
            return null;
        }

        if (selfIdentity == null)
        {
            selfIdentity = GetComponent<PlayerIdentity>();
        }

        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        Vector2 myPosition = GetCurrentPosition();

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity candidate = allPlayers[i];
            if (candidate == null)
            {
                continue;
            }

            if (!candidate.isAlive || candidate.isInfected || candidate.isFrozen)
            {
                continue;
            }

            if (selfIdentity != null && candidate == selfIdentity)
            {
                continue;
            }

            float distance = Vector2.Distance(myPosition, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate.transform;
            }
        }

        return nearest;
    }

    public void PauseBot()
    {
        waiting = true;
        StopMoving();
        StopWaitRoutine();
    }

    public void ResumeBot()
    {
        waiting = false;
        ResetStalkState();
        if (currentTarget == null && HasValidWaypoints())
        {
            ChooseRandomStartingWaypoint();
        }
    }

    public void SetWaypoints(Transform[] newWaypoints)
    {
        waypoints = newWaypoints;
        warnedNoWaypoints = false;

        if (!HasValidWaypoints())
        {
            currentTarget = null;
            return;
        }

        ChooseRandomStartingWaypoint();
    }

    void ChooseRandomStartingWaypoint()
    {
        if (!HasValidWaypoints())
        {
            currentTarget = null;
            return;
        }

        currentWaypointIndex = UnityEngine.Random.Range(0, waypoints.Length);
        currentTarget = waypoints[currentWaypointIndex];
    }

    void StartWaitThenPickNext()
    {
        if (waiting)
        {
            return;
        }

        StopWaitRoutine();
        waitRoutine = StartCoroutine(WaitThenPickNext());
    }

    IEnumerator WaitThenPickNext()
    {
        waiting = true;
        StopMoving();

        float minWait = Mathf.Min(waitAtWaypointMin, waitAtWaypointMax);
        float maxWait = Mathf.Max(waitAtWaypointMin, waitAtWaypointMax);
        float waitTime = UnityEngine.Random.Range(minWait, maxWait);
        waitTime = Mathf.Min(waitTime, waitAtWaypointMax);

        yield return new WaitForSeconds(waitTime);

        waiting = false;
        waitRoutine = null;

        PickNextWaypoint();
    }

    void PickNextWaypoint()
    {
        if (!HasValidWaypoints())
        {
            currentTarget = null;
            return;
        }

        if (waypoints.Length == 1)
        {
            currentWaypointIndex = 0;
            currentTarget = waypoints[0];
            return;
        }

        int nextIndex = currentWaypointIndex;
        int safetyCounter = 0;

        while (nextIndex == currentWaypointIndex && safetyCounter < 32)
        {
            nextIndex = UnityEngine.Random.Range(0, waypoints.Length);
            safetyCounter++;
        }

        currentWaypointIndex = nextIndex;
        currentTarget = waypoints[currentWaypointIndex];
    }

    void StopWaitRoutine()
    {
        if (waitRoutine != null)
        {
            StopCoroutine(waitRoutine);
            waitRoutine = null;
        }
    }

    void StopMoving()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    void MoveTo(Vector2 nextPosition)
    {
        if (rb != null)
        {
            rb.MovePosition(nextPosition);
            return;
        }

        transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
    }

    Vector2 GetCurrentPosition()
    {
        if (rb != null)
        {
            return rb.position;
        }

        return transform.position;
    }

    bool HasValidWaypoints()
    {
        return waypoints != null && waypoints.Length > 0;
    }

    bool IsMeetingOrVotingActive()
    {
        if (GameManager.Instance == null)
        {
            return false;
        }

        return GameManager.CurrentPhase == GamePhase.Meeting || GameManager.CurrentPhase == GamePhase.Voting;
    }
}
