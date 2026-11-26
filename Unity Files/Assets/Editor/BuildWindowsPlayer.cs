using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

public static class BuildWindowsPlayer
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PipelineAssetPath = "Assets/Settings/UniversalRenderPipelineAsset.asset";

    public static void Build()
    {
        XRProjectSetup.Configure();
        EnsureProjectSettings();
        URPMaterialFixer.ConvertAll();
        MallScaleSetup.Apply();
        MallVisualSetup.Apply();

        var outputPath = IOPath.GetFullPath(
            IOPath.Combine(Application.dataPath, "../Builds/Windows/MallOfAsia.exe"));
        IODirectory.CreateDirectory(IOPath.GetDirectoryName(outputPath));

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Windows player build failed: {report.summary.result}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"Windows player built at {outputPath}");
        EditorApplication.Exit(0);
    }

    private static void EnsureProjectSettings()
    {
        PlayerSettings.productName = "Mall of Asia Navigation";

        // The default (automatic) Windows API is Direct3D 12, which crashed the player intermittently
        // on exit (access violation during graphics shutdown). Direct3D 11 is stable here and is the
        // best supported API for OpenXR headsets on PC.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new System.IO.FileNotFoundException(
                "The configured startup scene is missing.", ScenePath);

        var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
            {
                pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
                pipeline.LoadBuiltinRendererData();
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
    }
}
