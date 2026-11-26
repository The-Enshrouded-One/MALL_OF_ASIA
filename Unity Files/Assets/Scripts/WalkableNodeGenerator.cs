using UnityEngine;

public class WalkableNodeGenerator : MonoBehaviour
{
    [Header("Inputs")]
    public Texture2D walkableMap;       // your Ground Floor BW image
    public Transform planeTransform;    // the plane you used for centroids
    public GameObject navNodePrefab;    // NavNode prefab

    [Header("Sampling")]
    [Tooltip("Pixel step when scanning the image. Smaller = more nodes.")]
    public int pixelStep = 20;          // tweak this for density

    [Tooltip("How bright a pixel must be to count as walkable (0-1).")]
    public float walkableThreshold = 0.9f;

    [Header("Scale (pixel -> world units)")]
    public float mapWidthPixels  = 3000f;
    public float mapHeightPixels = 1385f;
    public float worldWidth      = 79.375f; // your plane width in Unity units
    public float worldHeight     = 36.644791f; // your plane height in Unity units

    [ContextMenu("Generate Nav Nodes")]
    public void GenerateNavNodes()
    {
        if (walkableMap == null || navNodePrefab == null || planeTransform == null)
        {
            Debug.LogError("WalkableNodeGenerator: please assign walkableMap, planeTransform and navNodePrefab.");
            return;
        }

        // Parent object to keep things tidy
        var parent = new GameObject("NavNodes_GF");
        parent.transform.SetParent(planeTransform.parent, worldPositionStays: true);

        float scaleX = worldWidth / mapWidthPixels;
        float scaleZ = worldHeight / mapHeightPixels;

        int width  = walkableMap.width;
        int height = walkableMap.height;

        Debug.Log("Height: " + height + ", Width: " + width);

        for (int y = 0; y < height; y += pixelStep)
        {
            for (int x = 0; x < width; x += pixelStep)
            {
                Color c = walkableMap.GetPixel(x, y);

                // Only place nodes on (almost) white pixels
                if (c.r > walkableThreshold && c.g > walkableThreshold && c.b > walkableThreshold)
                {
                    // Flip Y because texture origin is top-left
                    // float flippedY = height - y;

                    float worldX = x * scaleX;
                    float worldZ = y * scaleZ;

                    Vector3 worldPos = new Vector3(-worldX, 0f, worldZ);

                    GameObject nodeObj = Instantiate(navNodePrefab, worldPos, Quaternion.identity, parent.transform);
                    NavNode node = nodeObj.GetComponent<NavNode>();
                    node.floorNumber = 0; // ground floor
                }
            }
        }

        Debug.Log("WalkableNodeGenerator: Finished generating nodes.");
    }
}
