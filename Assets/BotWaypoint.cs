using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BotWaypoint : MonoBehaviour
{
    [SerializeField] public List<BotWaypoint> neighbors = new List<BotWaypoint>();
    [SerializeField] public bool isTaskLikePoint = true;
    [SerializeField] public bool isHallwayPoint = true;
    [SerializeField] public float gizmoRadius = 0.18f;

    public Vector2 Position => transform.position;

    public void AddNeighbor(BotWaypoint other)
    {
        if (other == null || other == this)
        {
            return;
        }

        if (neighbors == null)
        {
            neighbors = new List<BotWaypoint>();
        }

        if (!neighbors.Contains(other))
        {
            neighbors.Add(other);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = isTaskLikePoint ? new Color(0.45f, 0.9f, 0.55f, 0.9f) : new Color(0.5f, 0.75f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);

        if (neighbors == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < neighbors.Count; i++)
        {
            BotWaypoint neighbor = neighbors[i];
            if (neighbor == null)
            {
                continue;
            }

            Gizmos.DrawLine(transform.position, neighbor.transform.position);
        }
    }
}