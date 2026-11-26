using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PathFinding : MonoBehaviour
{
    PathRequestManager requestManager;
    GridFromTexture grid;

    void Awake()
    {
        requestManager = GetComponent<PathRequestManager>();
        grid = GetComponent<GridFromTexture>();
    }

    IEnumerator FindPath(Vector3 startPosition, Vector3 targetPosition)
    {
        Vector3[] waypoints = new Vector3[0];
        bool pathSuccess = false;

        if (grid == null || requestManager == null)
        {
            Debug.LogError("Pathfinding needs both GridFromTexture and PathRequestManager on the same floor.", this);
            requestManager?.FinishedProcessingPath(waypoints, false);
            yield break;
        }

        grid.ResetSearchState();

        Node startNode = grid.FindNearestWalkableNode(startPosition);
        Node requestedTargetNode = grid.NodeFromWorldPoint(targetPosition);
        if (startNode == null || requestedTargetNode == null)
        {
            Debug.LogWarning("Path request starts or ends outside the walkable map bounds.", this);
            requestManager.FinishedProcessingPath(waypoints, false);
            yield break;
        }

        bool targetIsWalkable = requestedTargetNode.walkable;
        Node targetNode = targetIsWalkable ? requestedTargetNode : null;

        startNode.gCost = 0;
        startNode.hCost = GetDistance(startNode, requestedTargetNode);

        Node closestReachableNode = startNode;
        Heap<Node> openSet = new Heap<Node>(grid.MaxSize);
        HashSet<Node> closedSet = new HashSet<Node>();
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            Node currentNode = openSet.RemoveFirst();
            closedSet.Add(currentNode);

            int currentDistance = GetDistance(currentNode, requestedTargetNode);
            int bestDistance = GetDistance(closestReachableNode, requestedTargetNode);
            if (currentDistance < bestDistance ||
                (currentDistance == bestDistance && currentNode.gCost < closestReachableNode.gCost))
            {
                closestReachableNode = currentNode;
            }

            if (targetIsWalkable && currentNode == targetNode)
            {
                pathSuccess = true;
                break;
            }

            foreach (Node neighbor in grid.GetNeighbors(currentNode))
            {
                if (!neighbor.walkable || closedSet.Contains(neighbor))
                    continue;

                int movementCost = currentNode.gCost + GetDistance(currentNode, neighbor) + ClearancePenalty(neighbor);
                if (movementCost >= neighbor.gCost)
                    continue;

                neighbor.gCost = movementCost;
                neighbor.hCost = GetDistance(neighbor, requestedTargetNode);
                neighbor.parent = currentNode;

                if (!openSet.Contains(neighbor))
                    openSet.Add(neighbor);
                else
                    openSet.UpdateItem(neighbor);
            }
        }

        if (!pathSuccess && !targetIsWalkable && closestReachableNode != null)
        {
            // Store centroids normally fall inside the shop footprint. Route to the closest
            // reachable corridor cell instead of pretending the blocked centroid is walkable.
            targetNode = closestReachableNode;
            pathSuccess = true;
        }

        if (pathSuccess && targetNode != null)
            waypoints = RetracePath(startNode, targetNode);

        yield return null;
        requestManager.FinishedProcessingPath(waypoints, pathSuccess);
    }

    // Routes should run down the middle of corridors, not scrape along shopfronts.
    const int PreferredClearance = 6;   // cells (0.5 m each) -> about 3 m from walls
    const int ClearancePenaltyPerCell = 7;

    static int ClearancePenalty(Node node)
    {
        return node.clearance >= PreferredClearance ? 0 : (PreferredClearance - node.clearance) * ClearancePenaltyPerCell;
    }

    Vector3[] RetracePath(Node startNode, Node endNode)
    {
        var nodes = new List<Node>();
        Node currentNode = endNode;
        while (currentNode != startNode)
        {
            if (currentNode == null || currentNode.parent == null)
                return new Vector3[0];
            nodes.Add(currentNode);
            currentNode = currentNode.parent;
        }
        nodes.Add(startNode);
        nodes.Reverse();

        if (nodes.Count == 1)
            return new[] { startNode.worldPosition };

        List<Vector3> corners = StringPull(nodes);
        return Smooth(corners).ToArray();
    }

    /// <summary>
    /// Replaces the grid staircase with straight segments: from each corner, jump to the farthest
    /// later node that is still in clear line of sight (keeping the clearance the A* path had).
    /// </summary>
    List<Vector3> StringPull(List<Node> nodes)
    {
        var corners = new List<Vector3> { nodes[0].worldPosition };
        int anchor = 0;
        while (anchor < nodes.Count - 1)
        {
            int best = anchor + 1;
            for (int candidate = anchor + 2; candidate < nodes.Count; candidate++)
            {
                // A shortcut must stay as clear of walls as both of its ends (up to 2 m), so it can
                // never slide along a wall just because the route squeezes through a doorway elsewhere.
                int required = Mathf.Clamp(Mathf.Min(Mathf.Min(nodes[anchor].clearance, nodes[candidate].clearance), 4), 1, 4);
                if (grid.HasClearLine(nodes[anchor].worldPosition, nodes[candidate].worldPosition, required))
                    best = candidate;
                else if (candidate - best > 25)
                    break; // far past the last visible node; stop scanning
            }
            corners.Add(nodes[best].worldPosition);
            anchor = best;
        }
        return corners;
    }

    /// <summary>
    /// Rounds the corners with quadratic curves and resamples the route evenly, so the walker
    /// glides around bends instead of pivoting on the spot.
    /// </summary>
    List<Vector3> Smooth(List<Vector3> corners)
    {
        const float spacing = 0.4f;
        const float cornerRadius = 3.5f;
        var result = new List<Vector3> { corners[0] };
        if (corners.Count < 3)
        {
            AppendLine(result, corners[0], corners[corners.Count - 1], spacing);
            return result;
        }

        Vector3 segmentStart = corners[0];
        for (int i = 1; i < corners.Count - 1; i++)
        {
            Vector3 corner = corners[i];
            Vector3 inDir = corner - corners[i - 1];
            Vector3 outDir = corners[i + 1] - corner;
            float cut = Mathf.Min(cornerRadius, inDir.magnitude * 0.45f, outDir.magnitude * 0.45f);
            Vector3 entry = corner - inDir.normalized * cut;
            Vector3 exit = corner + outDir.normalized * cut;

            AppendLine(result, segmentStart, entry, spacing);

            // Quadratic Bezier around the corner. A curve cuts inside the corner, so it is used only
            // where it keeps clear of walls; otherwise the route keeps the original sharp corner.
            int steps = Mathf.Max(2, Mathf.CeilToInt(cut * 2f / spacing));
            var curve = new List<Vector3>(steps);
            bool curveClear = true;
            for (int s = 1; s <= steps && curveClear; s++)
            {
                float t = s / (float)steps;
                Vector3 point = (1 - t) * (1 - t) * entry + 2 * (1 - t) * t * corner + t * t * exit;
                Node node = grid.NodeFromWorldPoint(point);
                curveClear = node != null && node.walkable && node.clearance >= 2 &&
                             grid.HasClearLine(s == 1 ? entry : curve[curve.Count - 1], point, 2);
                curve.Add(point);
            }
            if (curveClear)
                result.AddRange(curve);
            else
            {
                AppendLine(result, entry, corner, spacing);
                AppendLine(result, corner, exit, spacing);
            }
            segmentStart = exit;
        }
        AppendLine(result, segmentStart, corners[corners.Count - 1], spacing);
        return result;
    }

    static void AppendLine(List<Vector3> points, Vector3 from, Vector3 to, float spacing)
    {
        float length = Vector3.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
        for (int i = 1; i <= steps; i++)
            points.Add(Vector3.Lerp(from, to, i / (float)steps));
    }

    int GetDistance(Node nodeA, Node nodeB)
    {
        int dstX = Mathf.Abs(nodeA.gridX - nodeB.gridX);
        int dstY = Mathf.Abs(nodeA.gridY - nodeB.gridY);
        int diagonal = Mathf.Min(dstX, dstY);
        int straight = Mathf.Max(dstX, dstY) - diagonal;
        return 14 * diagonal + 10 * straight;
    }

    public void StartFindPath(Vector3 startPosition, Vector3 targetPosition)
    {
        StartCoroutine(FindPath(startPosition, targetPosition));
    }
}
