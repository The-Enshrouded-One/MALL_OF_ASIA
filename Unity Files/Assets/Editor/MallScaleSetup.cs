using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Applies MallWorldScale.HorizontalScale to the scene: widens the mall model and the pathfinding
/// grids horizontally (heights unchanged), keeps the store banners at their natural size, and points
/// every floor's A* grid at the refined walkable map built from the real 3D walls.
/// Idempotent: it detects an already-scaled scene. Run automatically by the Windows build.
/// </summary>
public static class MallScaleSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string MallRootName = "Pheonix Mall of Asia";
    const string MapRootName = "Floor_A*";
    const string BannerRootName = "Store Banners";
    const string WalkableFolder = "Assets/Textures/NavRefined";
    const float ModelBaseScale = 100f;      // the floor models' original uniform scale
    const float BannerEnlargement = 1.6f;   // signage reads better in the wider concourses
    const float NodeRadius = 0.25f;         // 0.5 m pathfinding cells

    [MenuItem("Tools/Mall/Apply Mall Scale")]
    public static void ApplyMenu() => Apply();

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath);

        Transform mall = GameObject.Find(MallRootName)?.transform;
        Transform map = GameObject.Find(MapRootName)?.transform;
        if (mall == null || map == null)
        {
            Debug.LogError("Mall model or map root missing; scale not applied.");
            return;
        }

        float k = MallWorldScale.HorizontalScale;
        bool alreadyScaled = mall.localScale.x > ModelBaseScale * 1.5f;
        if (!alreadyScaled)
        {
            DetachBanners(mall, out List<Transform> banners);

            mall.position = Horizontal(mall.position, k);
            mall.localScale = new Vector3(mall.localScale.x * k, mall.localScale.y, mall.localScale.z * k);
            foreach (Transform banner in banners)
            {
                banner.position = Horizontal(banner.position, k);
                banner.localScale *= BannerEnlargement;
            }

            map.position = Horizontal(map.position, k);
            foreach (GridFromTexture grid in Object.FindObjectsByType<GridFromTexture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                grid.gridWorldSize *= k;
                grid.mirrorOffsetX *= k;
                EditorUtility.SetDirty(grid);
            }
            foreach (WalkableNodeGenerator generator in Object.FindObjectsByType<WalkableNodeGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                generator.worldWidth *= k;
                generator.worldHeight *= k;
                EditorUtility.SetDirty(generator);
            }
            EditorUtility.SetDirty(mall);
            EditorUtility.SetDirty(map);
            Debug.Log($"Mall footprint scaled {k}x horizontally.");
        }

        AssignWalkableMaps();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static Vector3 Horizontal(Vector3 v, float k) => new Vector3(v.x * k, v.y, v.z * k);

    /// <summary>Banners are children of the floor models; move them to their own root so the
    /// non-uniform model scale does not stretch them.</summary>
    static void DetachBanners(Transform mall, out List<Transform> banners)
    {
        foreach (Transform floorModel in mall)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(floorModel.gameObject))
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(floorModel.gameObject);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            }
        }

        GameObject bannerRoot = GameObject.Find(BannerRootName) ?? new GameObject(BannerRootName);
        banners = new List<Transform>();
        foreach (Transform t in mall.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Banner_"))
                banners.Add(t);
        foreach (Transform banner in banners)
            banner.SetParent(bannerRoot.transform, true);
    }

    static void AssignWalkableMaps()
    {
        InterFloorController floors = Object.FindFirstObjectByType<InterFloorController>(FindObjectsInactive.Include);
        if (floors == null || floors.floorAStars == null)
            return;

        for (int floor = 0; floor < floors.floorAStars.Length; floor++)
        {
            GameObject floorObject = floors.floorAStars[floor];
            GridFromTexture grid = floorObject != null ? floorObject.GetComponent<GridFromTexture>() : null;
            if (grid == null)
                continue;

            string path = $"{WalkableFolder}/floor_{floor}_walkable.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"Missing {path}. Run 'Floor Plans Image Processing/build_unity_walkable_maps.py'.");
                continue;
            }
            if (!importer.isReadable || importer.mipmapEnabled || importer.npotScale != TextureImporterNPOTScale.None ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize < 4096)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }

            grid.walkableMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            grid.nodeRadius = NodeRadius;
            grid.walkableThreshold = 0.5f;
            grid.displayGridGizmos = false;
            EditorUtility.SetDirty(grid);
        }
    }
}
