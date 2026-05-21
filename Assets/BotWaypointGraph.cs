using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BotWaypointGraph : MonoBehaviour
{
    public static BotWaypointGraph Instance { get; private set; }

    [SerializeField] bool autoFindWaypointsOnAwake = true;
    [SerializeField] bool autoConnectOnAwake = true;
    [SerializeField] float autoConnectDistance = 5f;
    [SerializeField] LayerMask obstacleMask;
    [SerializeField] bool debugLogs = true;

    readonly List<BotWaypoint> waypoints = new List<BotWaypoint>();
    bool warnedNoWaypoints;
    bool warnedEmptyObstacleMask;

    public IReadOnlyList<BotWaypoint> Waypoints => waypoints;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Instance.isActiveAndEnabled)
            {
                Debug.LogWarning("[BOT NAV] Duplicate BotWaypointGraph detected. Disabling this instance.", this);
                enabled = false;
                return;
            }
        }

        Instance = this;

        if (autoFindWaypointsOnAwake)
        {
            RefreshWaypoints();
        }

        if (autoConnectOnAwake)
        {
            AutoConnectVisibleNearbyWaypoints();
        }

        if (debugLogs)
        {
            Debug.Log($"[BOT NAV] Waypoints found: {waypoints.Count}", this);
        }

        if (waypoints.Count == 0 && !warnedNoWaypoints)
        {
            warnedNoWaypoints = true;
            Debug.LogWarning("[BOT NAV] No BotWaypoint objects found.", this);
        }

        if (obstacleMask.value == 0 && !warnedEmptyObstacleMask)
        {
            warnedEmptyObstacleMask = true;
            Debug.LogWarning("[BOT NAV] obstacleMask is empty. Line of sight may ignore walls.", this);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RefreshWaypoints()
    {
        waypoints.Clear();

        BotWaypoint[] found = FindObjectsByType<BotWaypoint>(FindObjectsInactive.Exclude);
        if (found == null || found.Length == 0)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[BOT NAV] RefreshWaypoints found no active BotWaypoint objects.", this);
            }

            return;
        }

        for (int i = 0; i < found.Length; i++)
        {
            BotWaypoint waypoint = found[i];
            if (waypoint != null && !waypoints.Contains(waypoint))
            {
                waypoints.Add(waypoint);
            }
        }
    }

    public BotWaypoint FindNearestWaypoint(Vector2 position)
    {
        BotWaypoint nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            BotWaypoint waypoint = waypoints[i];
            if (waypoint == null)
            {
                continue;
            }

            float distance = Vector2.Distance(position, waypoint.Position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = waypoint;
            }
        }

        return nearest;
    }

    public BotWaypoint FindNearestVisibleWaypoint(Vector2 position)
    {
        BotWaypoint nearestVisible = null;
        float nearestVisibleDistance = float.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            BotWaypoint waypoint = waypoints[i];
            if (waypoint == null)
            {
                continue;
            }

            if (!HasLineOfSight(position, waypoint.Position))
            {
                continue;
            }

            float distance = Vector2.Distance(position, waypoint.Position);
            if (distance < nearestVisibleDistance)
            {
                nearestVisibleDistance = distance;
                nearestVisible = waypoint;
            }
        }

        if (nearestVisible != null)
        {
            return nearestVisible;
        }

        return FindNearestWaypoint(position);
    }

    public bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        if (obstacleMask.value == 0)
        {
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

    public List<BotWaypoint> FindPath(Vector2 from, Vector2 to)
    {
        List<BotWaypoint> path = new List<BotWaypoint>();

        BotWaypoint start = FindNearestVisibleWaypoint(from);
        BotWaypoint goal = FindNearestVisibleWaypoint(to);
        if (start == null || goal == null)
        {
            return path;
        }

        if (start == goal)
        {
            path.Add(start);
            return path;
        }

        List<BotWaypoint> openSet = new List<BotWaypoint> { start };
        HashSet<BotWaypoint> closedSet = new HashSet<BotWaypoint>();
        Dictionary<BotWaypoint, BotWaypoint> cameFrom = new Dictionary<BotWaypoint, BotWaypoint>();
        Dictionary<BotWaypoint, float> gScore = new Dictionary<BotWaypoint, float>
        {
            [start] = 0f
        };
        Dictionary<BotWaypoint, float> fScore = new Dictionary<BotWaypoint, float>
        {
            [start] = Vector2.Distance(start.Position, goal.Position)
        };

        while (openSet.Count > 0)
        {
            BotWaypoint current = GetLowestCostWaypoint(openSet, fScore);
            if (current == goal)
            {
                ReconstructPath(cameFrom, current, path);
                return path;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            List<BotWaypoint> neighbors = current.neighbors;
            for (int i = 0; i < neighbors.Count; i++)
            {
                BotWaypoint neighbor = neighbors[i];
                if (neighbor == null || closedSet.Contains(neighbor))
                {
                    continue;
                }

                float tentativeG = GetValue(gScore, current) + Vector2.Distance(current.Position, neighbor.Position);
                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (tentativeG >= GetValue(gScore, neighbor))
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + Vector2.Distance(neighbor.Position, goal.Position);
            }
        }

        if (start != null)
        {
            path.Add(start);
        }

        return path;
    }

    public BotWaypoint GetRandomTaskLikeWaypoint()
    {
        List<BotWaypoint> taskLike = new List<BotWaypoint>();
        for (int i = 0; i < waypoints.Count; i++)
        {
            BotWaypoint waypoint = waypoints[i];
            if (waypoint != null && waypoint.isTaskLikePoint)
            {
                taskLike.Add(waypoint);
            }
        }

        if (taskLike.Count > 0)
        {
            return taskLike[Random.Range(0, taskLike.Count)];
        }

        return GetRandomWaypoint();
    }

    public BotWaypoint GetRandomWaypoint()
    {
        if (waypoints.Count == 0)
        {
            return null;
        }

        return waypoints[Random.Range(0, waypoints.Count)];
    }

    void AutoConnectVisibleNearbyWaypoints()
    {
        if (waypoints.Count < 2)
        {
            return;
        }

        for (int i = 0; i < waypoints.Count; i++)
        {
            BotWaypoint a = waypoints[i];
            if (a == null)
            {
                continue;
            }

            for (int j = i + 1; j < waypoints.Count; j++)
            {
                BotWaypoint b = waypoints[j];
                if (b == null)
                {
                    continue;
                }

                if (Vector2.Distance(a.Position, b.Position) > autoConnectDistance)
                {
                    continue;
                }

                if (!HasLineOfSight(a.Position, b.Position))
                {
                    continue;
                }

                a.AddNeighbor(b);
                b.AddNeighbor(a);
            }
        }
    }

    static BotWaypoint GetLowestCostWaypoint(List<BotWaypoint> openSet, Dictionary<BotWaypoint, float> fScore)
    {
        BotWaypoint best = openSet[0];
        float bestScore = GetValue(fScore, best);

        for (int i = 1; i < openSet.Count; i++)
        {
            BotWaypoint candidate = openSet[i];
            float score = GetValue(fScore, candidate);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    static float GetValue(Dictionary<BotWaypoint, float> dictionary, BotWaypoint key)
    {
        return dictionary.TryGetValue(key, out float value) ? value : float.MaxValue;
    }

    static void ReconstructPath(Dictionary<BotWaypoint, BotWaypoint> cameFrom, BotWaypoint current, List<BotWaypoint> path)
    {
        Stack<BotWaypoint> stack = new Stack<BotWaypoint>();
        stack.Push(current);

        while (cameFrom.TryGetValue(current, out BotWaypoint previous))
        {
            current = previous;
            stack.Push(current);
        }

        while (stack.Count > 0)
        {
            path.Add(stack.Pop());
        }
    }
}