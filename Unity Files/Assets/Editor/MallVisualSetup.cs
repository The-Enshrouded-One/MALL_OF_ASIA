using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sets up the look of the reconstructed mall: plan-driven floor, shopfront and ceiling materials,
/// ceiling geometry for every storey, and indoor ambient lighting. Safe to run repeatedly; the
/// Windows build runs it automatically (see BuildWindowsPlayer).
/// </summary>
public static class MallVisualSetup
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string ShaderName = "Mall/Plan Surface";
    const string PlanFolder = "Assets/Textures/MallPlan";
    const string FloorMaterialPath = "Assets/Materials/Floor_Mat.mat";
    const string WallMaterialPath = "Assets/Materials/Mall_Mat.mat";
    const string CeilingMaterialPath = "Assets/Materials/Ceiling_Mat.mat";
    const string CeilingRootName = "Mall Ceilings";
    const int FloorCount = 4;
    const float CeilingHeight = 4.6f;

    [MenuItem("Tools/Mall/Apply Mall Visuals")]
    public static void ApplyMenu() => Apply();

    public static void Apply()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{ShaderName}' was not found; mall visuals were not applied.");
            return;
        }

        Texture2D[] plans = ConfigurePlanTextures();
        if (plans == null)
            return;

        ConfigureMaterial(FloorMaterialPath, shader, plans, surface: 0, cull: CullMode.Back);
        ConfigureMaterial(WallMaterialPath, shader, plans, surface: 1, cull: CullMode.Off);
        Material ceiling = ConfigureMaterial(CeilingMaterialPath, shader, plans, surface: 2, cull: CullMode.Off);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath);

        BuildCeilings(ceiling);
        ConfigureLighting();
        ConfigureEffectMaterials();
        ConfigurePostProcessing();
        ConfigureCameraQuality();
        ConfigureSpawn();
        ConfigureDecor();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Mall visuals applied.");
    }

    static Texture2D[] ConfigurePlanTextures()
    {
        var plans = new Texture2D[FloorCount];
        for (int floor = 0; floor < FloorCount; floor++)
        {
            string path = $"{PlanFolder}/floor_{floor}_plan.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"Missing {path}. Run 'Floor Plans Image Processing/build_unity_floor_textures.py' first.");
                return null;
            }

            bool changed = importer.textureType != TextureImporterType.Default ||
                           importer.npotScale != TextureImporterNPOTScale.None ||
                           importer.mipmapEnabled ||
                           importer.wrapMode != TextureWrapMode.Clamp ||
                           importer.maxTextureSize != 4096 ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed ||
                           importer.alphaSource != TextureImporterAlphaSource.FromInput;
            if (changed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false; // sampled at LOD 0; region codes live in alpha
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 4096;
                // Uncompressed keeps the region codes in alpha exact (block compression blurs them).
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            plans[floor] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return plans;
    }

    static Material ConfigureMaterial(string path, Shader shader, Texture2D[] plans, int surface, CullMode cull)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetFloat("_Surface", surface);
        material.SetFloat("_Cull", (float)cull);
        for (int floor = 0; floor < FloorCount; floor++)
            material.SetTexture("_Plan" + floor, plans[floor]);
        material.SetTexture("_AtriumMask", ConfigureAtriumMask());
        material.SetVector("_PlanWorldSize", new Vector4(
            MallWorldScale.PlanWidthPixels * MallWorldScale.WorldUnitsPerPixel,
            MallWorldScale.PlanHeightPixels * MallWorldScale.WorldUnitsPerPixel, 0f, 0f));
        material.SetFloat("_LightPanelSpacing", 4f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void BuildCeilings(Material ceilingMaterial)
    {
        GameObject root = GameObject.Find(CeilingRootName);
        if (root == null)
            root = new GameObject(CeilingRootName);

        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        // Same footprint as the plan textures: 3000 x 1385 px at MallWorldScale.WorldUnitsPerPixel.
        float width = 3000f * MallWorldScale.WorldUnitsPerPixel;
        float depth = 1385f * MallWorldScale.WorldUnitsPerPixel;

        for (int floor = 0; floor < FloorCount; floor++)
        {
            GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ceiling.name = $"Ceiling_F{floor}";
            Object.DestroyImmediate(ceiling.GetComponent<Collider>());
            ceiling.transform.SetParent(root.transform, false);
            ceiling.transform.position = new Vector3(-width / 2f, floor * MallWorldScale.FloorHeight + CeilingHeight, depth / 2f);
            ceiling.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // face down
            ceiling.transform.localScale = new Vector3(width, depth, 1f);

            var renderer = ceiling.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ceilingMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static void ConfigureLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.42f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.36f, 0.35f, 0.34f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
        RenderSettings.ambientIntensity = 1f;

        // Light haze gives the long corridors depth without hiding the far end.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.86f, 0.87f, 0.89f);
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 280f;
    }

    // ------------------------------------------------------------------ navigation effects

    const string EffectsShaderName = "Mall/Effects";

    /// <summary>Materials loaded at runtime by RouteGuide and LiftShaft (kept in Resources so they ship in the build).</summary>
    static void ConfigureEffectMaterials()
    {
        Shader effects = Shader.Find(EffectsShaderName);
        if (effects == null)
        {
            Debug.LogError($"Shader '{EffectsShaderName}' was not found.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        EffectMaterial("Effects_Glass", effects, 0, new Color(0.55f, 0.75f, 0.85f), 0.35f, 1f, 1f);
        EffectMaterial("Effects_Route", effects, 1, new Color(0.10f, 0.85f, 1.00f), 0.85f, 1.1f, 1.8f);
        EffectMaterial("Effects_Beacon", effects, 2, new Color(0.10f, 0.85f, 1.00f), 0.55f, 1f, 2.2f);
        EffectMaterial("Effects_Ring", effects, 3, new Color(0.10f, 0.85f, 1.00f), 0.9f, 1f, 2.0f);
        EffectMaterial("Effects_Glow", effects, 4, new Color(1.00f, 0.96f, 0.88f), 1f, 1f, 1.6f);

        const string metalPath = "Assets/Resources/LiftMetal.mat";
        Material metal = AssetDatabase.LoadAssetAtPath<Material>(metalPath);
        if (metal == null)
        {
            metal = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(metal, metalPath);
        }
        metal.SetColor("_BaseColor", new Color(0.78f, 0.79f, 0.80f));
        metal.SetFloat("_Metallic", 0.9f);
        metal.SetFloat("_Smoothness", 0.75f);
        EditorUtility.SetDirty(metal);
        AssetDatabase.SaveAssets();
    }

    static Texture2D ConfigureAtriumMask()
    {
        const string path = PlanFolder + "/atrium_mask.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"Missing {path}; run build_unity_decor_layout.py. The atrium will stay closed.");
            return null;
        }
        if (importer.sRGBTexture || importer.mipmapEnabled || importer.npotScale != TextureImporterNPOTScale.None ||
            importer.textureCompression != TextureImporterCompression.Uncompressed || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void DecorMaterial(string name, Color colour, float smoothness, float metallic = 0f)
    {
        string path = $"Assets/Resources/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    /// <summary>Materials for MallDecor and the scene object that builds the furniture and atrium edges.</summary>
    static void ConfigureDecor()
    {
        DecorMaterial("DecorPlaster", new Color(0.95f, 0.94f, 0.91f), 0.35f);
        DecorMaterial("DecorStone", new Color(0.30f, 0.31f, 0.33f), 0.45f);
        DecorMaterial("DecorWood", new Color(0.62f, 0.42f, 0.25f), 0.4f);
        DecorMaterial("DecorTrunk", new Color(0.33f, 0.24f, 0.17f), 0.15f);
        DecorMaterial("DecorSoil", new Color(0.18f, 0.13f, 0.10f), 0.05f);
        DecorMaterial("DecorLeafDark", new Color(0.16f, 0.40f, 0.20f), 0.25f);
        DecorMaterial("DecorLeafLight", new Color(0.33f, 0.58f, 0.25f), 0.25f);
        AssetDatabase.SaveAssets();

        GameObject decor = GameObject.Find("Mall Decor");
        if (decor == null)
            decor = new GameObject("Mall Decor");
        if (decor.GetComponent<MallDecor>() == null)
            decor.AddComponent<MallDecor>();
    }

    static void EffectMaterial(string name, Shader shader, int mode, Color colour, float opacity, float flowSpeed, float intensity)
    {
        string path = $"Assets/Resources/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetFloat("_Mode", mode);
        material.SetColor("_Color", colour);
        material.SetFloat("_Opacity", opacity);
        material.SetFloat("_FlowSpeed", flowSpeed);
        material.SetFloat("_Intensity", intensity);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    // ------------------------------------------------------------------ polish

    const string PostProfilePath = "Assets/Settings/MallPostProcessing.asset";

    /// <summary>Soft bloom on the light panels and route, filmic tonemapping, gentle contrast and vignette.</summary>
    static void ConfigurePostProcessing()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");

        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);
        }

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.55f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(true);

        Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        ColorAdjustments colour = GetOrAdd<ColorAdjustments>(profile);
        colour.postExposure.Override(0.15f);
        colour.contrast.Override(8f);
        colour.saturation.Override(6f);

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.45f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        GameObject volumeObject = GameObject.Find("Mall Post Processing");
        if (volumeObject == null)
            volumeObject = new GameObject("Mall Post Processing");
        Volume volume = volumeObject.GetComponent<Volume>();
        if (volume == null)
            volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        component.active = true;
        return component;
    }

    static void ConfigureCameraQuality()
    {
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.stopNaN = true; // a stray NaN pixel would otherwise smear across the screen through bloom
            EditorUtility.SetDirty(data);
        }

        // Smooth edges on the thin shopfront frames and lift glass.
        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset pipeline)
        {
            pipeline.msaaSampleCount = 4;
            EditorUtility.SetDirty(pipeline);
        }
        QualitySettings.vSyncCount = 1;
    }

    /// <summary>Start just inside the main entrance, looking down the ground-floor concourse.</summary>
    static void ConfigureSpawn()
    {
        Unit visitor = Object.FindFirstObjectByType<Unit>(FindObjectsInactive.Include);
        if (visitor == null)
            return;
        // Widest point of the west ground-floor concourse (plan pixel 899, 773), looking east along it.
        float s = MallWorldScale.WorldUnitsPerPixel;
        visitor.transform.SetPositionAndRotation(new Vector3(-899f * s, 0f, 773f * s), Quaternion.Euler(0f, 270f, 0f));
        visitor.currentFloor = 0;
        visitor.speed = 2.4f;
        visitor.acceleration = 1.2f;
        visitor.rotationSpeed = 3f;
        visitor.lookAhead = 3.5f;
        EditorUtility.SetDirty(visitor.transform);
        EditorUtility.SetDirty(visitor);
    }
}
