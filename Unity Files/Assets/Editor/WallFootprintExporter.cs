using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Exports the vertical wall triangles of the mall model to
/// "Final Datasets/Wall_Mesh_Footprint/wall_triangles.csv" (x,y,z per vertex, in the original
/// plan coordinates, i.e. with MallWorldScale.HorizontalScale removed). The CSV feeds
/// "Floor Plans Image Processing/build_unity_walkable_maps.py".
/// </summary>
public static class WallFootprintExporter
{
    [MenuItem("Tools/Mall/Export Wall Footprint")]
    public static void Export()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        GameObject mall = GameObject.Find("Pheonix Mall of Asia");
        if (mall == null)
        {
            Debug.LogError("Mall model not found.");
            return;
        }

        float k = mall.transform.localScale.x > 150f ? MallWorldScale.HorizontalScale : 1f;
        var csv = new StringBuilder();
        foreach (MeshFilter filter in mall.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.name == "Plane" || filter.name.StartsWith("Banner"))
                continue;
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Transform t = filter.transform;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(vertices[triangles[i]]);
                Vector3 b = t.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = t.TransformPoint(vertices[triangles[i + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f || Mathf.Abs(normal.normalized.y) > 0.3f)
                    continue; // only vertical faces are walls
                Append(csv, a, k); csv.Append(',');
                Append(csv, b, k); csv.Append(',');
                Append(csv, c, k); csv.AppendLine();
            }
        }

        string path = IOPath.GetFullPath(IOPath.Combine(Application.dataPath, "../../Final Datasets/Wall_Mesh_Footprint/wall_triangles.csv"));
        Directory.CreateDirectory(IOPath.GetDirectoryName(path));
        File.WriteAllText(path, csv.ToString());
        Debug.Log("Wall footprint exported to " + path);
    }

    static void Append(StringBuilder csv, Vector3 v, float k)
    {
        csv.Append((v.x / k).ToString(CultureInfo.InvariantCulture)).Append(',')
           .Append(v.y.ToString(CultureInfo.InvariantCulture)).Append(',')
           .Append((v.z / k).ToString(CultureInfo.InvariantCulture));
    }
}
