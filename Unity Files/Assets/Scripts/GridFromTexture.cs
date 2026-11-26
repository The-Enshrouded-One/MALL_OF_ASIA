using UnityEngine;
using System.Collections.Generic;

// Generating walkable grid map using navigable bit map of the floor
public class GridFromTexture : MonoBehaviour
{
    [Header("Walkability Texture (White = Walkable)")]
    public Texture2D walkableMap; // Navigable bitmap of the floor

    [Header("World Grid")]
    public Vector2 gridWorldSize; // Size of the grid in world units
    public float nodeRadius;          // Radius of each node

    [Header("Pixel → Walkable Threshold")]
    [Range(0, 1)]
    public float walkableThreshold = 0.9f; // Pixel brightness threshold to consider walkable

    [Header("X Mirror Offset")]
    public float mirrorOffsetX = 79.375f;

    // [Header("Player Reference")]
    // public Transform player; // Reference to player transform
     
    Node[,] grid;
    float nodeDiameter;
    int gridSizeX, gridSizeY;
    public bool displayGridGizmos = true;

    public int MaxSize {
        get {
            return gridSizeX * gridSizeY;
        }
    }
    

    void Awake()
    {
        if (walkableMap == null)
        {
            Debug.LogError("No walkableMap assigned!");
            return;
        }

        nodeDiameter = nodeRadius * 2f;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
        // transform.rotation = Quaternion.Euler(0, -180f, 0);

        CreateGrid();
    }

    Color32[] mapPixels;

    // Reads from a cached pixel array (GetPixel per sample is far too slow for the full-size grid).
    Color AverageRegion(Texture2D tex, int centerX, int centerY, int halfSize)
    {
        if (mapPixels == null)
            mapPixels = tex.GetPixels32();

        int r = 0, g = 0, b = 0;
        int count = 0;
        int width = tex.width, height = tex.height;

        for (int dx = -halfSize; dx <= halfSize; dx++)
        {
            int x = Mathf.Clamp(centerX + dx, 0, width - 1);
            for (int dy = -halfSize; dy <= halfSize; dy++)
            {
                int y = Mathf.Clamp(centerY + dy, 0, height - 1);
                Color32 c = mapPixels[y * width + x];
                r += c.r; g += c.g; b += c.b;
                count++;
            }
        }

        float scale = 1f / (255f * count);
        return new Color(r * scale, g * scale, b * scale);
    }


    void CreateGrid()
    { 
        // Force the grid object to match the required orientation & position
        // transform.position = Vector3.zero;
        // transform.rotation = Quaternion.Euler(180f, 0f, 0f);

        grid = new Node[gridSizeX, gridSizeY];

        // bottom-left corner of your grid in world space
        Vector3 worldBottomLeft =
            transform.position
            - Vector3.right * gridWorldSize.x / 2f
            - Vector3.forward * gridWorldSize.y / 2f;

        int texWidth = walkableMap.width;
        int texHeight = walkableMap.height;

        float pixelPerGridX = texWidth / (float)gridSizeX;
        float pixelPerGridY = texHeight / (float)gridSizeY;

        Debug.Log($"Node Radius: {nodeRadius}, Grid World Size: {gridWorldSize.x} x {gridWorldSize.y}");
        Debug.Log($"Creating grid of size {gridSizeX} x {gridSizeY} from texture of size {texWidth} x {texHeight}");

        int numWalkable = 0, numBlocked = 0;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                // center pixel of the region
                int px = Mathf.FloorToInt(x * pixelPerGridX);
                // int py = Mathf.FloorToInt(y * pixelPerGridY);

                // Flip vertically (rotate 180° relative to current grid)
                int py = walkableMap.height - 1 - Mathf.FloorToInt(y * pixelPerGridY);
                
                // Color pixel = walkableMap.GetPixel(px, py);

                // Take the average color of the region for better results
                Color pixel = AverageRegion(walkableMap, px, py, Mathf.CeilToInt(Mathf.Max(pixelPerGridX, pixelPerGridY) / 2f));

                bool walkable =
                    pixel.r > walkableThreshold &&
                    pixel.g > walkableThreshold &&
                    pixel.b > walkableThreshold;

                Vector3 worldPoint =
                    worldBottomLeft +
                    Vector3.right * (x * nodeDiameter + nodeRadius) +
                    Vector3.forward * (y * nodeDiameter + nodeRadius);
                
                // worldPoint = new Vector3(-worldPoint.x, worldPoint.y, worldPoint.z);
                worldPoint = new Vector3(-(worldPoint.x + mirrorOffsetX), worldPoint.y, worldPoint.z);


                grid[x, y] = new Node(walkable, worldPoint, x, y);

                if (walkable) numWalkable++;
                else numBlocked++;
            }
        }

        Debug.Log($"Grid done. Walkable: {numWalkable} | Blocked: {numBlocked}");
        mapPixels = null;
        ComputeClearance();
    }

    public float NodeDiameter => nodeDiameter;

    /// <summary>Breadth-first distance (8-connected, in cells) from every node to the nearest blocked node.</summary>
    public void ComputeClearance()
    {
        if (grid == null)
            return;

        var queue = new Queue<Node>();
        foreach (Node node in grid)
        {
            node.clearance = node.walkable ? int.MaxValue : 0;
            if (!node.walkable)
                queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            Node current = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = current.gridX + dx, y = current.gridY + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= gridSizeX || y >= gridSizeY)
                        continue;
                    Node next = grid[x, y];
                    if (next.clearance > current.clearance + 1)
                    {
                        next.clearance = current.clearance + 1;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        // Cells at the edge of the map with no blocked cell anywhere nearby.
        foreach (Node node in grid)
            if (node.clearance == int.MaxValue)
                node.clearance = 255;
    }

    /// <summary>Marks a circular area as blocked (used for lift shafts) and refreshes clearance.</summary>
    public void BlockCircle(Vector3 worldCenter, float radius)
    {
        if (grid == null)
            return;

        foreach (Node node in grid)
        {
            Vector3 offset = node.worldPosition - worldCenter;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radius * radius)
                node.walkable = false;
        }
        ComputeClearance();
    }

    /// <summary>Marks an oriented rectangle as blocked (benches, planters).</summary>
    public void BlockBox(Vector3 center, Vector3 axis, float halfLength, float halfWidth, bool recomputeClearance = true)
    {
        if (grid == null)
            return;

        axis.y = 0f;
        axis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        foreach (Node node in grid)
        {
            Vector3 offset = node.worldPosition - center;
            offset.y = 0f;
            if (Mathf.Abs(Vector3.Dot(offset, axis)) <= halfLength && Mathf.Abs(Vector3.Dot(offset, side)) <= halfWidth)
                node.walkable = false;
        }
        if (recomputeClearance)
            ComputeClearance();
    }

    /// <summary>
    /// True when every cell along the straight segment is walkable with at least the given clearance.
    /// </summary>
    public bool HasClearLine(Vector3 from, Vector3 to, int minClearance)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        float length = delta.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / (nodeRadius * 0.75f)));
        for (int i = 0; i <= steps; i++)
        {
            Node node = NodeFromWorldPoint(from + delta * (i / (float)steps));
            if (node == null || !node.walkable || node.clearance < minClearance)
                return false;
        }
        return true;
    }

    public List<Node> GetNeighbors(Node node)
    {
        List<Node> neighbors = new List<Node>();

        // Check all nodes in a 3 x 3 area around the given node
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                // Skip the current node
                if (x == 0 && y == 0)
                    continue;

                // Uncomment below to restrict to 4-directional movement
                // if (Mathf.Abs(x) == Mathf.Abs(y))
                //     continue;

                int checkX = node.gridX + x;
                int checkY = node.gridY + y;

                if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY)
                {
                    bool diagonal = x != 0 && y != 0;
                    if (diagonal && (!grid[node.gridX + x, node.gridY].walkable || !grid[node.gridX, node.gridY + y].walkable))
                        continue;

                    neighbors.Add(grid[checkX, checkY]);
                }
            }
        }

        return neighbors;
    }

    // public Node NodeFromWorldPoint(Vector3 worldPosition)
    // {
    //     // Recompute bottom-left corner exactly like in CreateGrid
    //     Vector3 worldBottomLeft =
    //         transform.position
    //         - Vector3.right * gridWorldSize.x / 2f
    //         - Vector3.forward * gridWorldSize.y / 2f;

    //     // float mirroredX = -(worldPosition.x) - 0.79375f;

    //     float percentX = (worldPosition.x - worldBottomLeft.x) / gridWorldSize.x;
    //     float percentY = (worldPosition.z - worldBottomLeft.z) / gridWorldSize.y;

    //     // float percentX = (mirroredX + gridWorldSize.x / 2f) / gridWorldSize.x;
    //     // float percentY = (worldPosition.z + gridWorldSize.y / 2f) / gridWorldSize.y;


    //     percentX = Mathf.Clamp01(percentX);
    //     percentY = Mathf.Clamp01(percentY);

    //     int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
    //     int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);

    //     return grid[x, y];
    // }

    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        if (grid == null || gridSizeX == 0 || gridSizeY == 0)
            return null;

        // Recompute bottom-left corner exactly like in CreateGrid
        Vector3 worldBottomLeft =
            transform.position
            - Vector3.right * gridWorldSize.x / 2f
            - Vector3.forward * gridWorldSize.y / 2f;

        // Undo the X mirroring + offset to get the "base" X used before mirroring
        float baseX = -worldPosition.x - mirrorOffsetX;

        // How far along the grid (0..1) from the left/bottom edges?
        float percentX = (baseX - worldBottomLeft.x) / gridWorldSize.x;
        float percentY = (worldPosition.z - worldBottomLeft.z) / gridWorldSize.y;

        if (percentX < 0f || percentX > 1f || percentY < 0f || percentY > 1f)
            return null;

        int x = Mathf.Clamp(Mathf.RoundToInt((gridSizeX - 1) * percentX), 0, gridSizeX - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt((gridSizeY - 1) * percentY), 0, gridSizeY - 1);

        return grid[x, y];
    }

    public Node FindNearestWalkableNode(Vector3 worldPosition, int maxRadius = 20)
    {
        Node origin = NodeFromWorldPoint(worldPosition);
        if (origin == null)
            return null;

        if (origin.walkable)
            return origin;

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Node best = null;
            int bestDistance = int.MaxValue;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius)
                        continue;

                    int gridX = origin.gridX + x;
                    int gridY = origin.gridY + y;
                    if (gridX < 0 || gridX >= gridSizeX || gridY < 0 || gridY >= gridSizeY)
                        continue;

                    Node candidate = grid[gridX, gridY];
                    int distance = x * x + y * y;
                    if (candidate.walkable && distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }

            if (best != null)
                return best;
        }

        return null;
    }

    public void ResetSearchState()
    {
        if (grid == null)
            return;

        foreach (Node node in grid)
        {
            node.gCost = int.MaxValue;
            node.hCost = 0;
            node.parent = null;
        }
    }

    public List<Node> path;

    void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 0.01f, gridWorldSize.y));

        if (grid != null && displayGridGizmos)
        {
            // Node playerNode = NodeFromWorldPoint(player.position);
            foreach (Node n in grid)
            {
                Gizmos.color = n.walkable ? Color.white : Color.red;

                // if (n == playerNode)
                // {
                //     Gizmos.color = Color.cyan; // Highlight player's current node (Only for testing)
                // }
                // if (path != null && path.Contains(n))
                // {
                //     // Gizmos.color = Color.green; // Highlight path nodes
                //     if (path.Contains(n))
                //     {
                //         Gizmos.color = Color.black;
                //     }
                // }

                Gizmos.DrawCube(n.worldPosition, Vector3.one * (nodeDiameter - 0.001f));
            }
        }
    }
}








// using UnityEngine;
// using System.Collections.Generic;

// public class Grid : MonoBehaviour {

//     public LayerMask unwalkableMask;
//     public Vector2 gridWorldSize;
//     public float nodeRadius;

//     Node[,] grid;

//     float nodeDiameter;
//     int gridSizeX, gridSizeY;
//     int num_white = 0, num_red = 0; 

//     void Start() {
//         nodeDiameter = nodeRadius * 2;
//         gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
//         gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
//         Debug.Log("Grid Size X: " + gridSizeX + ", Y: " + gridSizeY);
//         CreateGrid();
//     }

//     void CreateGrid() {
//         grid = new Node[gridSizeX, gridSizeY];
//         Vector3 worldBottomLeft = transform.position - Vector3.right * gridWorldSize.x / 2 - Vector3.forward * gridWorldSize.y / 2;

//         for (int x = 0; x < gridSizeX; x++) {
//             for (int y = 0; y < gridSizeY; y++) {
//                 Vector3 worldPoint = worldBottomLeft + Vector3.right * (x * nodeDiameter + nodeRadius) + Vector3.forward * (y * nodeDiameter + nodeRadius);
//                 bool walkable = !(Physics.CheckSphere(worldPoint, nodeRadius, unwalkableMask));
//                 grid[x, y] = new Node(walkable, worldPoint);

//                 if (walkable) {
//                     num_white += 1;
//                 } 
//                 else {
//                     num_red += 1;
//                 }
//             }
//         }

//         Debug.Log("Grid created with " + (gridSizeX * gridSizeY) + " nodes.");
//         Debug.Log("Number of walkable nodes: " + num_white + ", Number of unwalkable nodes: " + num_red);

//     }

//     void OnDrawGizmos() {
//         Gizmos.DrawWireCube(transform.position, new Vector3(gridWorldSize.x, 0.01f, gridWorldSize.y));

//         if (grid != null) {
//             foreach (Node n in grid) {
                
//                 Gizmos.color = (n.walkable) ? Color.white : Color.red;
//                 Gizmos.DrawCube(n.worldPosition, Vector3.one * (nodeDiameter - 0.001f));
                
//             }
//         }

//         // Debug.Log("Number of walkable nodes: " + num_white + ", Number of unwalkable nodes: " + num_red);
//     }
// }
