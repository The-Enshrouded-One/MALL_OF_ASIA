using UnityEngine;

/// <summary>
/// Draws the route on the floor as a flowing ribbon of chevrons that fades out behind the visitor,
/// plus a pulsing beacon (light column and floor halo) at the lift or the destination.
/// Materials come from Resources (created by MallVisualSetup) so their shaders are in the build.
/// </summary>
public class RouteGuide : MonoBehaviour
{
    public static readonly Color DestinationColour = new Color(0.10f, 0.85f, 1.00f);
    public static readonly Color LiftColour = new Color(1.00f, 0.74f, 0.25f);

    const float RibbonWidth = 0.42f;
    const float RibbonHeight = 0.035f;

    LineRenderer ribbon;
    Transform beacon;
    Renderer beaconColumn;
    Renderer beaconRing;
    Material ribbonMaterial;
    Material columnMaterial;
    Material ringMaterial;
    float progress;
    float arrivedTime = -1f;

    public bool RouteVisible => ribbon != null && ribbon.enabled;
    public int RoutePointCount => ribbon != null ? ribbon.positionCount : 0;

    public static RouteGuide Create()
    {
        var root = new GameObject("Route Guide");
        return root.AddComponent<RouteGuide>();
    }

    void Awake()
    {
        ribbonMaterial = LoadMaterial("Effects_Route");
        columnMaterial = LoadMaterial("Effects_Beacon");
        ringMaterial = LoadMaterial("Effects_Ring");

        // Ribbon lies flat on the floor: the object faces up and the line aligns to its Z axis.
        var ribbonObject = new GameObject("Route Ribbon");
        ribbonObject.transform.SetParent(transform, false);
        ribbonObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ribbon = ribbonObject.AddComponent<LineRenderer>();
        ribbon.useWorldSpace = true;
        ribbon.alignment = LineAlignment.TransformZ;
        ribbon.widthMultiplier = RibbonWidth;
        ribbon.textureMode = LineTextureMode.Tile;
        ribbon.numCornerVertices = 4;
        ribbon.numCapVertices = 6;
        ribbon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ribbon.receiveShadows = false;
        ribbon.sharedMaterial = ribbonMaterial;
        ribbon.enabled = false;

        beacon = new GameObject("Destination Beacon").transform;
        beacon.SetParent(transform, false);

        var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        column.name = "Light Column";
        Destroy(column.GetComponent<Collider>());
        column.transform.SetParent(beacon, false);
        column.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        column.transform.localScale = new Vector3(0.32f, 1.3f, 0.32f);
        beaconColumn = column.GetComponent<Renderer>();
        beaconColumn.sharedMaterial = columnMaterial != null ? new Material(columnMaterial) : null;
        beaconColumn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ring.name = "Floor Halo";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(beacon, false);
        ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
        beaconRing = ring.GetComponent<Renderer>();
        beaconRing.sharedMaterial = ringMaterial != null ? new Material(ringMaterial) : null;
        beaconRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        beacon.gameObject.SetActive(false);
    }

    static Material LoadMaterial(string name)
    {
        Material material = Resources.Load<Material>(name);
        if (material == null)
            Debug.LogWarning($"Route guide material '{name}' is missing from Resources; run Tools > Mall > Apply Mall Visuals.");
        return material;
    }

    public void ShowRoute(Vector3[] points, float floorY)
    {
        if (points == null || points.Length < 2)
        {
            ribbon.enabled = false;
            return;
        }

        var lifted = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
            lifted[i] = new Vector3(points[i].x, floorY + RibbonHeight, points[i].z);

        ribbon.positionCount = lifted.Length;
        ribbon.SetPositions(lifted);
        ribbon.enabled = true;
        arrivedTime = -1f;
        SetProgress(0f);
    }

    /// <summary>Fades the part of the route already walked (0 = start, 1 = arrived).</summary>
    public void SetProgress(float fraction)
    {
        progress = Mathf.Clamp01(fraction);
        var gradient = new Gradient();
        float fadeStart = Mathf.Clamp01(progress - 0.01f);
        float fadeEnd = Mathf.Clamp01(progress + 0.04f);
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0f, fadeStart),
                new GradientAlphaKey(1f, fadeEnd),
                new GradientAlphaKey(1f, 1f)
            });
        ribbon.colorGradient = gradient;
    }

    public void ShowBeacon(Vector3 position, Color colour)
    {
        beacon.position = position;
        beacon.gameObject.SetActive(true);
        if (beaconColumn.sharedMaterial != null)
            beaconColumn.sharedMaterial.SetColor("_Color", colour);
        if (beaconRing.sharedMaterial != null)
            beaconRing.sharedMaterial.SetColor("_Color", colour);
        beacon.localScale = Vector3.one;
    }

    public void ShowArrived()
    {
        ribbon.enabled = false;
        arrivedTime = Time.time;
    }

    public void Clear()
    {
        ribbon.enabled = false;
        ribbon.positionCount = 0;
        beacon.gameObject.SetActive(false);
        arrivedTime = -1f;
    }

    void Update()
    {
        if (!beacon.gameObject.activeSelf)
            return;

        // After arrival the beacon shrinks away gracefully.
        if (arrivedTime >= 0f)
        {
            float t = (Time.time - arrivedTime) / 2.5f;
            if (t >= 1f)
            {
                beacon.gameObject.SetActive(false);
                arrivedTime = -1f;
                return;
            }
            float scale = 1f - t * t * (3f - 2f * t);
            beacon.localScale = new Vector3(1f, scale, 1f);
        }
    }
}
