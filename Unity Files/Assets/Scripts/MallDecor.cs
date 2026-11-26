using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Furnishes the concourses from Resources/MallDecorLayout.json (built by
/// "Floor Plans Image Processing/build_unity_decor_layout.py"): glass balustrades around the atrium
/// voids, and seating clusters / planters with trees in the widest parts of the concourses.
/// Every decor footprint is blocked in that floor's A* grid so routes flow around it.
/// </summary>
public class MallDecor : MonoBehaviour
{
    [Serializable]
    class Layout
    {
        public Outline[] atria;
        public Spot[] decor;
    }

    [Serializable]
    class Outline
    {
        public int floor;
        public float[] points; // x0, z0, x1, z1, ...
    }

    [Serializable]
    class Spot
    {
        public int floor;
        public float x;
        public float z;
        public float yaw;
        public string kind;
    }

    const float BalustradeHeight = 1.05f;
    const float SlabDepth = 0.4f; // matches the ceiling height of 4.6 m below a 5 m storey

    Material glass, metal, plaster, stone, wood, trunk, soil;
    Material[] leaves;
    System.Random random = new System.Random(20261126);

    void Start()
    {
        TextAsset json = Resources.Load<TextAsset>("MallDecorLayout");
        if (json == null)
        {
            Debug.LogWarning("MallDecorLayout.json is missing from Resources; the concourses stay unfurnished.");
            return;
        }

        Layout layout = JsonUtility.FromJson<Layout>(json.text);
        LoadMaterials();

        if (layout.atria != null)
            foreach (Outline outline in layout.atria)
                BuildAtriumEdge(outline);

        var blocked = new Dictionary<int, List<(Vector3, Vector3, float, float)>>();
        if (layout.decor != null)
        {
            foreach (Spot spot in layout.decor)
            {
                float y = spot.floor * MallWorldScale.FloorHeight;
                var root = new GameObject(spot.kind == "cluster" ? "Seating Cluster" : "Planter").transform;
                root.SetParent(transform, false);
                root.SetPositionAndRotation(new Vector3(spot.x, y, spot.z), Quaternion.Euler(0f, spot.yaw, 0f));

                float halfLength;
                if (spot.kind == "cluster")
                {
                    BuildPlanter(root, Vector3.zero, 0.8f);
                    BuildBench(root, new Vector3(0f, 0f, 2.05f));
                    BuildBench(root, new Vector3(0f, 0f, -2.05f));
                    halfLength = 3.1f;
                }
                else
                {
                    BuildPlanter(root, Vector3.zero, 0.8f);
                    halfLength = 1.1f;
                }

                if (!blocked.TryGetValue(spot.floor, out var list))
                    blocked[spot.floor] = list = new List<(Vector3, Vector3, float, float)>();
                list.Add((root.position, root.forward, halfLength, 1.1f));
            }
        }

        BlockInGrids(blocked);
    }

    void LoadMaterials()
    {
        glass = Resources.Load<Material>("Effects_Glass");
        metal = Resources.Load<Material>("LiftMetal");
        plaster = Resources.Load<Material>("DecorPlaster");
        stone = Resources.Load<Material>("DecorStone");
        wood = Resources.Load<Material>("DecorWood");
        trunk = Resources.Load<Material>("DecorTrunk");
        soil = Resources.Load<Material>("DecorSoil");
        leaves = new[] { Resources.Load<Material>("DecorLeafDark"), Resources.Load<Material>("DecorLeafLight") };
    }

    // ---------------------------------------------------------------- atrium

    void BuildAtriumEdge(Outline outline)
    {
        if (outline.points == null || outline.points.Length < 6)
            return;

        var points = new List<Vector3>();
        float y = outline.floor * MallWorldScale.FloorHeight;
        for (int i = 0; i + 1 < outline.points.Length; i += 2)
            points.Add(new Vector3(outline.points[i], y, outline.points[i + 1]));

        var root = new GameObject($"Atrium Edge (Floor {outline.floor})").transform;
        root.SetParent(transform, false);

        // Slab edge fascia, glass balustrade, kick plate and handrail.
        Strip(root, "Slab Edge", points, -SlabDepth, 0f, plaster, 0f);
        Strip(root, "Glass Balustrade", points, 0.06f, BalustradeHeight, glass, 0f);
        Strip(root, "Kick Plate", points, 0f, 0.08f, metal, 0.02f);
        Strip(root, "Handrail", points, BalustradeHeight - 0.02f, BalustradeHeight + 0.05f, metal, 0.03f);
        Band(root, "Handrail Top", points, BalustradeHeight + 0.05f, 0.09f, metal);
        Band(root, "Slab Edge Underside", points, -SlabDepth, 0.25f, plaster);
    }

    /// <summary>Vertical double-sided band along a closed outline, from y0 to y1 above the floor.</summary>
    static void Strip(Transform parent, string name, List<Vector3> outline, float y0, float y1, Material material, float inset)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outline[i];
            Vector3 b = outline[(i + 1) % n];
            if ((b - a).sqrMagnitude < 1e-6f)
                continue;
            AddDoubleSidedQuad(vertices, triangles,
                a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1);
        }
        CreateMesh(parent, name, vertices, triangles, material);
    }

    /// <summary>Horizontal band of the given width around a closed outline (handrail top, slab underside).</summary>
    static void Band(Transform parent, string name, List<Vector3> outline, float y, float width, Material material)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outline[i];
            Vector3 b = outline[(i + 1) % n];
            if ((b - a).sqrMagnitude < 1e-6f)
                continue;
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * (width * 0.5f);
            AddDoubleSidedQuad(vertices, triangles,
                a - side + Vector3.up * y, b - side + Vector3.up * y, b + side + Vector3.up * y, a + side + Vector3.up * y);
        }
        CreateMesh(parent, name, vertices, triangles, material);
    }

    // Front and back faces get their own vertices: shared vertices would average opposite normals
    // to zero, and a zero normal turns into NaN in lighting (which blows up bloom across the screen).
    static void AddDoubleSidedQuad(List<Vector3> vertices, List<int> triangles, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        int front = vertices.Count;
        vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
        triangles.AddRange(new[] { front, front + 1, front + 2, front, front + 2, front + 3 });
        int back = vertices.Count;
        vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
        triangles.AddRange(new[] { back, back + 2, back + 1, back, back + 3, back + 2 });
    }

    static void CreateMesh(Transform parent, string name, List<Vector3> vertices, List<int> triangles, Material material)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------------------------------------------------------- furniture

    void BuildPlanter(Transform root, Vector3 local, float radius)
    {
        Part(root, "Planter", PrimitiveType.Cylinder, stone, local + Vector3.up * 0.28f, Quaternion.identity,
            new Vector3(radius * 2f, 0.28f, radius * 2f));
        Part(root, "Planter Rim", PrimitiveType.Cylinder, wood, local + Vector3.up * 0.57f, Quaternion.identity,
            new Vector3(radius * 2.08f, 0.025f, radius * 2.08f));
        Part(root, "Soil", PrimitiveType.Cylinder, soil, local + Vector3.up * 0.58f, Quaternion.identity,
            new Vector3(radius * 1.85f, 0.01f, radius * 1.85f));

        // A tree: slim trunk and a layered canopy of overlapping foliage clumps.
        float height = 2.4f + (float)random.NextDouble() * 0.8f;
        Part(root, "Trunk", PrimitiveType.Cylinder, trunk, local + Vector3.up * (0.58f + height / 2f), Quaternion.identity,
            new Vector3(0.12f, height / 2f, 0.12f));
        int clumps = 6;
        for (int i = 0; i < clumps; i++)
        {
            float angle = i * (360f / clumps) + (float)random.NextDouble() * 30f;
            float spread = 0.35f + (float)random.NextDouble() * 0.25f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spread;
            float size = 0.9f + (float)random.NextDouble() * 0.5f;
            Part(root, "Foliage", PrimitiveType.Sphere, leaves[i % leaves.Length],
                local + offset + Vector3.up * (0.58f + height + (float)random.NextDouble() * 0.5f - 0.2f), Quaternion.identity,
                new Vector3(size, size * 0.8f, size));
        }
        Part(root, "Foliage Crown", PrimitiveType.Sphere, leaves[0], local + Vector3.up * (0.58f + height + 0.55f), Quaternion.identity,
            new Vector3(1.1f, 0.9f, 1.1f));

        // Low ground cover around the trunk.
        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * (radius * 0.6f);
            Part(root, "Shrub", PrimitiveType.Sphere, leaves[(i + 1) % leaves.Length], local + offset + Vector3.up * 0.66f,
                Quaternion.identity, new Vector3(0.42f, 0.3f, 0.42f));
        }
    }

    void BuildBench(Transform root, Vector3 local)
    {
        // Bench along the concourse axis (local Z), timber slats on a slim steel frame.
        for (int slat = 0; slat < 4; slat++)
        {
            float x = -0.18f + slat * 0.12f;
            Part(root, "Seat Slat", PrimitiveType.Cube, wood, local + new Vector3(x, 0.45f, 0f), Quaternion.identity,
                new Vector3(0.1f, 0.04f, 1.9f));
        }
        foreach (float z in new[] { -0.8f, 0.8f })
        {
            Part(root, "Bench Leg", PrimitiveType.Cube, metal, local + new Vector3(0f, 0.22f, z), Quaternion.identity,
                new Vector3(0.48f, 0.44f, 0.05f));
        }
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Material material, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = scale;
        var renderer = part.GetComponent<Renderer>();
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return part;
    }

    // ---------------------------------------------------------------- navigation

    static void BlockInGrids(Dictionary<int, List<(Vector3 center, Vector3 axis, float halfLength, float halfWidth)>> blocked)
    {
        InterFloorController floors = InterFloorController.Instance != null
            ? InterFloorController.Instance
            : FindFirstObjectByType<InterFloorController>();
        if (floors == null || floors.floorAStars == null)
            return;

        foreach (var entry in blocked)
        {
            if (entry.Key < 0 || entry.Key >= floors.floorAStars.Length || floors.floorAStars[entry.Key] == null)
                continue;
            GridFromTexture grid = floors.floorAStars[entry.Key].GetComponent<GridFromTexture>();
            if (grid == null)
                continue;
            foreach (var box in entry.Value)
                grid.BlockBox(box.center, box.axis, box.halfLength, box.halfWidth, false);
            grid.ComputeClearance();
        }
    }
}
