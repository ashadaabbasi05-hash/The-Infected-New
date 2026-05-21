using System.Collections;
using System.Collections.Generic;
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

    [Header("Waypoint Navigation")]
    [SerializeField] bool useWaypointNavigation = true;
    [SerializeField] float waypointReachDistance = 0.25f;
    [SerializeField] float repathInterval = 0.75f;
    [SerializeField] float directLineOfSightDistance = 2.5f;
    [SerializeField] LayerMask obstacleMask;
    [SerializeField] bool navDebugLogs = false;

    Rigidbody2D rb;
    Transform currentTarget;
    int currentWaypointIndex;
    bool waiting;
    Coroutine waitRoutine;
    bool warnedNoWaypoints;
    bool warnedEmptyObstacleMask;
    PlayerIdentity selfIdentity;

    List<BotWaypoint> currentPath = new List<BotWaypoint>();
    int currentPathIndex;
    float repathTimer;
    Vector2 currentMoveTarget;
    Transform currentChaseTarget;
    BotWaypoint currentFakeTaskTarget;
    Vector2 lastNavigationPosition;
    float stuckTimer;
    bool hasLastNavigationPosition;

    bool stalkIsFollowing;
    float stalkModeTimer;

    public BotBehaviorMode CurrentMode => currentMode;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfIdentity = GetComponent<PlayerIdentity>();

        if (obstacleMask.value == 0)
        {
            warnedEmptyObstacleMask = true;
            Debug.LogWarning("[BOT NAV] obstacleMask is empty. Line of sight may ignore walls.", this);
        }

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
        ResetNavigationState();
        ChooseRandomStartingWaypoint();
        ResetStalkState();
        warnedNoWaypoints = false;
    }

    void OnDisable()
    {
        StopWaitRoutine();
        ResetNavigationState();

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

        if (selfIdentity == null || !selfIdentity.isAIControlled)
        {
            StopMoving();
            return;
        }

        if (selfIdentity.isFrozen)
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

        BotWaypointGraph graph = BotWaypointGraph.Instance;
        if (useWaypointNavigation && graph != null && graph.Waypoints.Count > 0)
        {
            UpdateGraphFakeTaskMovement(graph, speed);
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
        UpdateStuckTracking(currentPosition, true, targetPosition);
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

        currentChaseTarget = target;
        MoveTowardsTransform(target, aggressiveSpeed);
    }

    void UpdateFinalHuntBehavior()
    {
        waiting = false;
        StopWaitRoutine();

        Transform target = FindPreferredFinalHuntTarget();
        if (target == null)
        {
            StopMoving();
            return;
        }

        currentChaseTarget = target;

        Vector2 currentPosition = GetCurrentPosition();
        Vector2 targetPosition = target.position;
        currentMoveTarget = targetPosition;

        if (HasLineOfSight(currentPosition, targetPosition))
        {
            MoveDirectToTarget(targetPosition, finalHuntSpeed);
            UpdateStuckTracking(currentPosition, true, targetPosition);
            return;
        }

        MoveTowardsTransform(target, finalHuntSpeed, true);
    }

    void MoveTowardsTransform(Transform target, float speed)
    {
        MoveTowardsTransform(target, speed, false);
    }

    void MoveTowardsTransform(Transform target, float speed, bool forceDirectWhenVisible)
    {
        if (target == null)
        {
            StopMoving();
            return;
        }

        Vector2 currentPosition = GetCurrentPosition();
        Vector2 targetPosition = target.position;
        currentChaseTarget = target;
        currentMoveTarget = targetPosition;

        if (forceDirectWhenVisible && HasLineOfSight(currentPosition, targetPosition))
        {
            MoveDirectToTarget(targetPosition, speed);
            UpdateStuckTracking(currentPosition, true, targetPosition);
            return;
        }

        if (!forceDirectWhenVisible && HasLineOfSight(currentPosition, targetPosition) && Vector2.Distance(currentPosition, targetPosition) <= directLineOfSightDistance)
        {
            MoveDirectToTarget(targetPosition, speed);
            UpdateStuckTracking(currentPosition, true, targetPosition);
            return;
        }

        if (useWaypointNavigation && BotWaypointGraph.Instance != null && BotWaypointGraph.Instance.Waypoints.Count > 0)
        {
            NavigateToTarget(targetPosition, speed, false);
            return;
        }

        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, targetPosition, speed * Time.fixedDeltaTime);
        MoveTo(nextPosition);
        UpdateStuckTracking(currentPosition, true, targetPosition);
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
        ResetNavigationState();
        if (currentTarget == null && HasValidWaypoints())
        {
            ChooseRandomStartingWaypoint();
        }

        if (currentFakeTaskTarget == null)
        {
            currentFakeTaskTarget = GetRandomTaskLikeWaypoint();
        }
    }

    public void SetWaypoints(Transform[] newWaypoints)
    {
        waypoints = newWaypoints;
        warnedNoWaypoints = false;
        ResetNavigationState();

        if (!HasValidWaypoints())
        {
            currentTarget = null;
            return;
        }

        ChooseRandomStartingWaypoint();
    }

    void ChooseRandomStartingWaypoint()
    {
        if (useWaypointNavigation && BotWaypointGraph.Instance != null && BotWaypointGraph.Instance.Waypoints.Count > 0)
        {
            currentFakeTaskTarget = GetRandomTaskLikeWaypoint();
            currentTarget = null;
            currentWaypointIndex = 0;
            return;
        }

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
        if (useWaypointNavigation && BotWaypointGraph.Instance != null && BotWaypointGraph.Instance.Waypoints.Count > 0)
        {
            currentFakeTaskTarget = GetRandomTaskLikeWaypoint();
            if (currentFakeTaskTarget != null)
            {
                TraceFakeTaskWaypoint(currentFakeTaskTarget);
            }

            return;
        }

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
        BotWaypointGraph graph = BotWaypointGraph.Instance;
        if (useWaypointNavigation && graph != null && graph.Waypoints.Count > 0)
        {
            return true;
        }

        return waypoints != null && waypoints.Length > 0;
    }

    void ResetNavigationState()
    {
        if (currentPath == null)
        {
            currentPath = new List<BotWaypoint>();
        }

        currentPath.Clear();
        currentPathIndex = 0;
        repathTimer = 0f;
        currentMoveTarget = GetCurrentPosition();
        currentChaseTarget = null;
        currentFakeTaskTarget = null;
        stuckTimer = 0f;
        hasLastNavigationPosition = false;
        lastNavigationPosition = GetCurrentPosition();
    }

    void UpdateGraphFakeTaskMovement(BotWaypointGraph graph, float speed)
    {
        Vector2 currentPosition = GetCurrentPosition();

        if (currentFakeTaskTarget == null)
        {
            currentFakeTaskTarget = graph != null ? graph.GetRandomTaskLikeWaypoint() : null;
            if (currentFakeTaskTarget != null)
            {
                TraceFakeTaskWaypoint(currentFakeTaskTarget);
            }
        }

        if (currentFakeTaskTarget == null)
        {
            StopMoving();
            return;
        }

        Vector2 targetPosition = currentFakeTaskTarget.Position;
        currentMoveTarget = targetPosition;

        if (Vector2.Distance(currentPosition, targetPosition) <= arriveDistance)
        {
            StartWaitThenPickNext();
            UpdateStuckTracking(currentPosition, false, targetPosition);
            return;
        }

        NavigateToTarget(targetPosition, speed, true);
    }

    void NavigateToTarget(Vector2 finalTarget, float speed, bool allowWaitAtTarget)
    {
        Vector2 currentPosition = GetCurrentPosition();
        currentMoveTarget = finalTarget;

        if (allowWaitAtTarget && Vector2.Distance(currentPosition, finalTarget) <= arriveDistance)
        {
            StartWaitThenPickNext();
            UpdateStuckTracking(currentPosition, false, finalTarget);
            return;
        }

        Vector2 nextNavigationTarget = GetNextNavigationTarget(finalTarget);
        currentMoveTarget = nextNavigationTarget;

        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, nextNavigationTarget, speed * Time.fixedDeltaTime);
        MoveTo(nextPosition);
        UpdateStuckTracking(currentPosition, true, finalTarget);
    }

    Vector2 GetNextNavigationTarget(Vector2 finalTarget)
    {
        BotWaypointGraph graph = BotWaypointGraph.Instance;
        if (!useWaypointNavigation || graph == null)
        {
            return finalTarget;
        }

        Vector2 currentPosition = GetCurrentPosition();
        if (graph.HasLineOfSight(currentPosition, finalTarget) && Vector2.Distance(currentPosition, finalTarget) <= directLineOfSightDistance)
        {
            return finalTarget;
        }

        repathTimer -= Time.fixedDeltaTime;
        if (currentPath == null)
        {
            currentPath = new List<BotWaypoint>();
        }

        if (currentPath.Count == 0 || repathTimer <= 0f)
        {
            currentPath = graph.FindPath(currentPosition, finalTarget);
            currentPathIndex = 0;
            repathTimer = repathInterval;
        }

        if (currentPath != null && currentPath.Count > 0 && currentPathIndex < currentPath.Count)
        {
            BotWaypoint node = currentPath[currentPathIndex];
            if (node != null)
            {
                if (Vector2.Distance(currentPosition, node.Position) <= waypointReachDistance)
                {
                    currentPathIndex++;
                }

                if (currentPathIndex < currentPath.Count && currentPath[currentPathIndex] != null)
                {
                    return currentPath[currentPathIndex].Position;
                }
            }
        }

        return finalTarget;
    }

    void MoveDirectToTarget(Vector2 targetPosition, float speed)
    {
        Vector2 currentPosition = GetCurrentPosition();
        Vector2 nextPosition = Vector2.MoveTowards(currentPosition, targetPosition, speed * Time.fixedDeltaTime);
        MoveTo(nextPosition);
    }

    void UpdateStuckTracking(Vector2 currentPosition, bool isTryingToMove, Vector2 finalTarget)
    {
        if (!isTryingToMove)
        {
            stuckTimer = 0f;
            lastNavigationPosition = currentPosition;
            hasLastNavigationPosition = true;
            return;
        }

        if (!hasLastNavigationPosition)
        {
            hasLastNavigationPosition = true;
            lastNavigationPosition = currentPosition;
            return;
        }

        float movedDistance = Vector2.Distance(currentPosition, lastNavigationPosition);
        if (movedDistance < 0.05f)
        {
            stuckTimer += Time.fixedDeltaTime;
        }
        else
        {
            stuckTimer = 0f;
        }

        lastNavigationPosition = currentPosition;

        if (stuckTimer < 1.5f)
        {
            return;
        }

        stuckTimer = 0f;
        RepathDueToStuck(finalTarget);
    }

    void RepathDueToStuck(Vector2 finalTarget)
    {
        if (currentPath == null)
        {
            currentPath = new List<BotWaypoint>();
        }

        currentPath.Clear();
        currentPathIndex = 0;
        repathTimer = 0f;

        if (currentMode == BotBehaviorMode.FakeTask && useWaypointNavigation && BotWaypointGraph.Instance != null && BotWaypointGraph.Instance.Waypoints.Count > 0)
        {
            currentFakeTaskTarget = GetRandomTaskLikeWaypoint();
        }

        if (navDebugLogs)
        {
            Debug.Log($"[BOT NAV] Repath due to stuck. {gameObject.name} target={finalTarget}", this);
        }
    }

    void TraceFakeTaskWaypoint(BotWaypoint waypoint)
    {
        if (waypoint == null)
        {
            return;
        }

        string playerName = selfIdentity != null && !string.IsNullOrWhiteSpace(selfIdentity.playerName)
            ? selfIdentity.playerName
            : gameObject.name;

        AgentTracePanel.Trace("BOT", $"{playerName} moving to fake task waypoint.");

        if (navDebugLogs)
        {
            Debug.Log($"[BOT NAV] {playerName} targeting waypoint {waypoint.name}.", this);
        }
    }

    BotWaypoint GetRandomTaskLikeWaypoint()
    {
        BotWaypointGraph graph = BotWaypointGraph.Instance;
        if (graph != null)
        {
            return graph.GetRandomTaskLikeWaypoint();
        }

        if (waypoints != null && waypoints.Length > 0)
        {
            List<Transform> candidates = new List<Transform>();
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform waypoint = waypoints[i];
                if (waypoint != null)
                {
                    candidates.Add(waypoint);
                }
            }

            if (candidates.Count > 0)
            {
                Transform chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                BotWaypoint fallback = chosen.GetComponent<BotWaypoint>();
                if (fallback != null)
                {
                    return fallback;
                }
            }
        }

        return null;
    }

    Transform FindPreferredFinalHuntTarget()
    {
        PlayerIdentity[] allPlayers = PlayerIdentity.GetAllPlayers();
        if (allPlayers == null || allPlayers.Length == 0)
        {
            return null;
        }

        PlayerIdentity playerOne = null;
        PlayerIdentity localHuman = null;
        PlayerIdentity nearestHuman = null;
        float nearestDistance = float.MaxValue;
        Vector2 myPosition = GetCurrentPosition();

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerIdentity candidate = allPlayers[i];
            if (candidate == null || !candidate.isAlive || candidate.isInfected || candidate.isFrozen)
            {
                continue;
            }

            if (selfIdentity != null && candidate == selfIdentity)
            {
                continue;
            }

            if (candidate.playerId == 1)
            {
                playerOne = candidate;
            }

            if (candidate.IsLocalPlayer)
            {
                localHuman = candidate;
            }

            float distance = Vector2.Distance(myPosition, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestHuman = candidate;
            }
        }

        if (playerOne != null)
        {
            return playerOne.transform;
        }

        if (localHuman != null)
        {
            return localHuman.transform;
        }

        return nearestHuman != null ? nearestHuman.transform : null;
    }

    bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        BotWaypointGraph graph = BotWaypointGraph.Instance;
        if (graph != null)
        {
            return graph.HasLineOfSight(from, to);
        }

        if (obstacleMask.value == 0)
        {
            if (!warnedEmptyObstacleMask)
            {
                warnedEmptyObstacleMask = true;
                Debug.LogWarning("[BOT NAV] obstacleMask is empty. Line of sight may ignore walls.", this);
            }

            RaycastHit2D hit = Physics2D.Linecast(from, to);
            if (hit.collider == null)
            {
                return true;
            }

            if (hit.collider.isTrigger)
            {
                RaycastHit2D[] hits = Physics2D.LinecastAll(from, to);
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider2D collider = hits[i].collider;
                    if (collider != null && !collider.isTrigger)
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        RaycastHit2D maskHit = Physics2D.Linecast(from, to, obstacleMask);
        if (maskHit.collider == null)
        {
            return true;
        }

        if (maskHit.collider.isTrigger)
        {
            RaycastHit2D[] hits = Physics2D.LinecastAll(from, to, obstacleMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i].collider;
                if (collider != null && !collider.isTrigger)
                {
                    return false;
                }
            }

            return true;
        }

        return false;
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
